using LoquendoAI.Core.Abstractions;
using LoquendoAI.Core.Models;

namespace LoquendoAI.Infrastructure.Projects;

public sealed class AssetLibraryService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff"
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".mp3", ".ogg", ".flac", ".m4a", ".aac", ".wma", ".opus"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".mpg", ".mpeg", ".m4v", ".ts", ".m2ts"
    };

    private static readonly HashSet<string> FontExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ttf", ".otf", ".woff", ".woff2"
    };

    public async Task<AssetImportSummary> ScanSourceAsync(
        IProjectRepository repository,
        AssetSource source,
        IReadOnlyList<FolderRule> rules,
        IProgress<(int Current, string Message)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(source.RootPath))
            throw new DirectoryNotFoundException($"La fuente ya no existe: {source.RootPath}");

        var existing = (await repository.GetAssetsForSourceAsync(source.Id, cancellationToken))
            .Where(a => !string.IsNullOrWhiteSpace(a.SourceRelativePath))
            .ToDictionary(a => NormalizeRelativePath(a.SourceRelativePath!), StringComparer.OrdinalIgnoreCase);
        var manualKind = (await repository.GetAssetIdsWithTagAsync(source.Id, "catalog.manual.kind", cancellationToken)).ToHashSet();
        var manualSubject = (await repository.GetAssetIdsWithTagAsync(source.Id, "catalog.manual.subject", cancellationToken)).ToHashSet();

        var normalizedRules = rules
            .Select(r => r with { RelativeFolder = NormalizeRelativePath(r.RelativeFolder) })
            .OrderByDescending(r => FolderDepth(r.RelativeFolder))
            .ToArray();

        var files = new List<string>();
        var enumerationErrors = new List<string>();
        CollectFilesSafe(source.RootPath, files, enumerationErrors, cancellationToken);

        // Assets referenced by scripts/scenes or carrying manual edits must never be deleted by
        // a scan: deleting them nulled script references (ON DELETE SET NULL) and cascaded the
        // user's manual descriptions away.
        var protectedIds = await repository.GetProtectedAssetIdsAsync(cancellationToken);

        // Move/rename detection: a catalogued file that disappeared from its old path and shows
        // up elsewhere in the same source with identical bytes keeps its asset ID, so scripts,
        // tags and descriptions follow it.
        var onDisk = new HashSet<string>(files
            .Where(f => IsSupported(Path.GetExtension(f)))
            .Select(f => NormalizeRelativePath(Path.GetRelativePath(source.RootPath, f))), StringComparer.OrdinalIgnoreCase);
        var vanishedBySize = enumerationErrors.Count > 0
            ? new Dictionary<long, List<AssetRecord>>()
            : existing.Where(pair => !onDisk.Contains(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value.Sha256))
                .GroupBy(pair => pair.Value.FileSize)
                .ToDictionary(group => group.Key, group => group.Select(pair => pair.Value).ToList());
        var moved = 0;

        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var imported = 0;
        var updated = 0;
        var skipped = 0;
        var needsCutout = 0;
        var errors = new List<string>(enumerationErrors);
        var seen = 0;

        foreach (var fullPath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            seen++;
            // Reporting every single one of 60k+ files floods the WPF dispatcher and can
            // make an otherwise-background scan look frozen.  A few updates per second /
            // every couple hundred files are enough for human-visible progress.
            if (seen == 1 || seen % 250 == 0)
                progress?.Report((seen, Path.GetFileName(fullPath) ?? fullPath));

            try
            {
                var extension = Path.GetExtension(fullPath).ToLowerInvariant();
                if (!IsSupported(extension))
                    continue;

                var sourceRelativePath = NormalizeRelativePath(Path.GetRelativePath(source.RootPath, fullPath));

                var folder = NormalizeRelativePath(Path.GetDirectoryName(sourceRelativePath) ?? string.Empty);
                var rule = ResolveRule(folder, normalizedRules);
                if (rule?.Classification == FolderClassification.Ignore)
                {
                    // Not added to `present`: a protected ignored asset stays catalogued but hidden
                    // (is_missing = 1), so pickers skip it while scripts keep their reference.
                    if (existing.TryGetValue(sourceRelativePath, out var ignoredExisting))
                    {
                        if (!protectedIds.Contains(ignoredExisting.Id))
                            await repository.DeleteAssetAsync(ignoredExisting.Id, cancellationToken);
                        else if (!ignoredExisting.IsMissing)
                            await repository.UpsertAssetAsync(ignoredExisting with { IsMissing = true }, cancellationToken);
                    }
                    continue;
                }
                present.Add(sourceRelativePath);

                existing.TryGetValue(sourceRelativePath, out var old);
                string? knownSha = null;
                if (old is null && vanishedBySize.Count > 0)
                {
                    var length = new FileInfo(fullPath).Length;
                    if (vanishedBySize.TryGetValue(length, out var sameSize) && sameSize.Count > 0)
                    {
                        knownSha = await AssetHasher.Sha256Async(fullPath, cancellationToken);
                        var match = sameSize.FirstOrDefault(candidate =>
                            candidate.Sha256 == knownSha &&
                            string.Equals(candidate.Extension, extension, StringComparison.OrdinalIgnoreCase));
                        if (match is not null)
                        {
                            sameSize.Remove(match);
                            old = match;
                            moved++;
                        }
                    }
                }
                var kind = old is not null && manualKind.Contains(old.Id) ? old.Kind :
                    ResolveKind(extension, rule?.Classification ?? FolderClassification.Auto, folder);
                var cutout = ResolveCutoutStatus(fullPath, extension, kind, rule?.DefaultCutoutStatus ?? CutoutStatus.Unknown);
                if (cutout == CutoutStatus.NeedsCutout)
                    needsCutout++;

                var info = new FileInfo(fullPath);
                var lastWrite = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);

                var unchangedBytes = old is not null &&
                    old.FileSize == info.Length &&
                    old.LastWriteUtc?.UtcDateTime == lastWrite.UtcDateTime &&
                    !string.IsNullOrWhiteSpace(old.Sha256);

                var sha = unchangedBytes
                    ? old!.Sha256
                    : knownSha ?? await AssetHasher.Sha256Async(fullPath, cancellationToken);

                var subject = old is not null && manualSubject.Contains(old.Id) ? old.SubjectName :
                    CleanOptional(rule?.SubjectName);
                var collection = CleanOptional(rule?.CollectionName);
                var internalPath = $"@source/{source.Id:D}/{sourceRelativePath}";
                var record = new AssetRecord(
                    old?.Id ?? Guid.NewGuid(),
                    internalPath,
                    kind,
                    sha,
                    Path.GetFileNameWithoutExtension(fullPath) ?? Path.GetFileName(fullPath) ?? fullPath,
                    old?.ImportedUtc ?? DateTimeOffset.UtcNow,
                    source.Id,
                    sourceRelativePath,
                    info.Length,
                    lastWrite,
                    extension,
                    cutout,
                    subject,
                    collection,
                    false);

                if (old is not null && !old.IsMissing && AssetEquivalent(old, record))
                {
                    skipped++;
                    continue;
                }

                await repository.UpsertAssetAsync(record, cancellationToken);
                if (old is null)
                    imported++;
                else
                    updated++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                errors.Add($"{fullPath}: {ex.Message}");
            }
        }

        if (enumerationErrors.Count == 0)
            await repository.MarkAssetsMissingAsync(source.Id, present, cancellationToken);

        var after = await repository.GetAssetsForSourceAsync(source.Id, cancellationToken);
        var missing = enumerationErrors.Count == 0
            ? after.Count(a => !present.Contains(NormalizeRelativePath(a.SourceRelativePath ?? string.Empty)))
            : after.Count(a => a.IsMissing);

        await repository.UpsertAssetSourceAsync(source with { LastScanUtc = DateTimeOffset.UtcNow }, cancellationToken);
        progress?.Report((seen, "finalizando catálogo"));

        return new AssetImportSummary(
            seen,
            imported,
            updated,
            skipped,
            missing,
            needsCutout,
            errors.Count,
            errors,
            moved);
    }

    private static FolderRule? ResolveRule(string folder, IReadOnlyList<FolderRule> rules)
    {
        foreach (var rule in rules)
        {
            if (string.Equals(folder, rule.RelativeFolder, StringComparison.OrdinalIgnoreCase))
                return rule;

            if (!rule.IncludeSubfolders)
                continue;

            if (string.IsNullOrEmpty(rule.RelativeFolder))
                return rule;

            if (folder.StartsWith(rule.RelativeFolder + "/", StringComparison.OrdinalIgnoreCase))
                return rule;
        }
        return null;
    }

    private static AssetKind ResolveKind(string extension, FolderClassification classification, string folder)
    {
        return classification switch
        {
            FolderClassification.CharacterRenders when ImageExtensions.Contains(extension) => AssetKind.CharacterSprite,
            FolderClassification.Backgrounds when ImageExtensions.Contains(extension) || VideoExtensions.Contains(extension) => AssetKind.Background,
            FolderClassification.SoundEffects when AudioExtensions.Contains(extension) => AssetKind.SoundEffect,
            FolderClassification.Music when AudioExtensions.Contains(extension) => AssetKind.Music,
            FolderClassification.Videos when VideoExtensions.Contains(extension) => AssetKind.Video,
            FolderClassification.VisualEffects when ImageExtensions.Contains(extension) || VideoExtensions.Contains(extension) => AssetKind.VisualEffect,
            FolderClassification.Memes when ImageExtensions.Contains(extension) || VideoExtensions.Contains(extension) => AssetKind.Meme,
            FolderClassification.Props when ImageExtensions.Contains(extension) => AssetKind.Prop,
            FolderClassification.Mixed => KindFromMixedFolder(extension, folder),
            FolderClassification.Auto => KindFromAutoContext(extension, folder),
            _ => AssetKind.Unknown
        };
    }

    // Auto is intentionally conservative: it trusts explicit folder-name context,
    // but leaves ambiguous images/audio undefined. Mixed is different: the user has
    // explicitly told us that several media types coexist here, so every supported
    // file should receive the best non-destructive type we can infer.
    private static AssetKind KindFromAutoContext(string extension, string folder)
    {
        var contextual = KindFromFolderContext(extension, folder);
        if (contextual != AssetKind.Unknown)
            return contextual;

        if (VideoExtensions.Contains(extension))
            return AssetKind.Video;
        if (FontExtensions.Contains(extension))
            return AssetKind.Font;
        return AssetKind.Unknown;
    }

    private static AssetKind KindFromMixedFolder(string extension, string folder)
    {
        var contextual = KindFromFolderContext(extension, folder);
        if (contextual != AssetKind.Unknown)
            return contextual;

        // GIF is treated as an animated prop/object by default. A more specific rule
        // (VFX, meme, background, render, etc.) still wins before reaching this fallback.
        if (extension.Equals(".gif", StringComparison.OrdinalIgnoreCase))
            return AssetKind.Prop;

        if (AudioExtensions.Contains(extension))
            return AssetKind.Audio;
        if (VideoExtensions.Contains(extension))
            return AssetKind.Video;
        if (FontExtensions.Contains(extension))
            return AssetKind.Font;
        if (ImageExtensions.Contains(extension))
            return AssetKind.CharacterSprite;

        return AssetKind.Unknown;
    }

    private static AssetKind KindFromFolderContext(string extension, string folder)
    {
        var suggested = FolderRuleSuggestions.Suggest(folder);
        if (suggested == FolderClassification.Auto || suggested == FolderClassification.Mixed)
            return AssetKind.Unknown;

        // A path hint only counts if the hinted category can actually consume this
        // extension. Example: a .mp3 under "renders/sonidos" should become SFX rather
        // than Unknown merely because another path segment says "renders".
        return ResolveKind(extension, suggested, string.Empty);
    }

    private static CutoutStatus ResolveCutoutStatus(
        string path,
        string extension,
        AssetKind kind,
        CutoutStatus ruleDefault)
    {
        if (kind != AssetKind.CharacterSprite)
            return CutoutStatus.NotApplicable;

        if (ruleDefault is CutoutStatus.Ready or CutoutStatus.NeedsCutout or CutoutStatus.IntentionallyOpaque)
            return ruleDefault;

        if (extension is ".jpg" or ".jpeg" or ".bmp")
            return CutoutStatus.NeedsCutout;

        if (extension == ".png")
            return PngHasAlphaChannel(path) ? CutoutStatus.Ready : CutoutStatus.NeedsCutout;

        return CutoutStatus.Unknown;
    }

    internal static bool PngHasAlphaChannel(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return PngHasAlphaChannel(stream);
    }

    /// <summary>True for RGBA/grey+alpha PNGs and for palette/grey/RGB PNGs with a tRNS chunk.
    /// Indexed sprites ripped from games usually carry transparency in tRNS (colour type 3);
    /// checking only the colour type marked them as NeedsCutout.</summary>
    internal static bool PngHasAlphaChannel(Stream stream)
    {
        Span<byte> header = stackalloc byte[26];
        if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
            return false;

        ReadOnlySpan<byte> signature = stackalloc byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (!header[..8].SequenceEqual(signature))
            return false;

        var colorType = header[25];
        if (colorType is 4 or 6)
            return true;

        // Walk the chunks after IHDR (8 signature + 8 chunk header + 13 data + 4 CRC = 33 bytes).
        // tRNS must appear before the first IDAT.
        stream.Position = 33;
        Span<byte> chunk = stackalloc byte[8];
        for (var i = 0; i < 64; i++)
        {
            if (stream.ReadAtLeast(chunk, chunk.Length, throwOnEndOfStream: false) < chunk.Length)
                return false;
            var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(chunk[..4]);
            var type = System.Text.Encoding.ASCII.GetString(chunk[4..]);
            if (type == "tRNS") return true;
            if (type is "IDAT" or "IEND") return false;
            var next = stream.Position + length + 4L; // data + CRC
            if (length > int.MaxValue || next > stream.Length) return false;
            stream.Position = next;
        }
        return false;
    }

    private static bool AssetEquivalent(AssetRecord a, AssetRecord b) =>
        a.Kind == b.Kind &&
        a.Sha256 == b.Sha256 &&
        a.DisplayName == b.DisplayName &&
        a.SourceId == b.SourceId &&
        string.Equals(a.SourceRelativePath, b.SourceRelativePath, StringComparison.OrdinalIgnoreCase) &&
        a.FileSize == b.FileSize &&
        a.LastWriteUtc?.UtcDateTime == b.LastWriteUtc?.UtcDateTime &&
        a.Extension == b.Extension &&
        a.CutoutStatus == b.CutoutStatus &&
        a.SubjectName == b.SubjectName &&
        a.CollectionName == b.CollectionName &&
        !a.IsMissing;

    private static string? CleanOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsSupported(string extension) =>
        ImageExtensions.Contains(extension) || AudioExtensions.Contains(extension) ||
        VideoExtensions.Contains(extension) || FontExtensions.Contains(extension);

    private static int FolderDepth(string path) => string.IsNullOrEmpty(path) ? 0 : path.Count(c => c == '/') + 1;

    public static string NormalizeRelativePath(string path) => path.Replace('\\', '/').Trim('/');

    private static void CollectFilesSafe(
        string root,
        ICollection<string> files,
        ICollection<string> errors,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();

            try
            {
                foreach (var file in Directory.EnumerateFiles(current))
                    files.Add(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                errors.Add($"{current}: {ex.Message}");
                continue;
            }

            try
            {
                foreach (var directory in Directory.EnumerateDirectories(current))
                    pending.Push(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                errors.Add($"{current}: {ex.Message}");
            }
        }
    }
}
