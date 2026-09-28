namespace LoquendoAI.Infrastructure.Projects;

public sealed record ScriptAudioReference(string? GeneratedAudioPath, string? GeneratedAudioHash, string ParametersJson);

public sealed record CleanupItem(string Path, long Bytes, string Category);

/// <summary>
/// "Limpiar caché": finds regenerable files the project no longer uses. It never touches assets,
/// imported recordings (generated/imported_voices), VEGAS exports (a saved .veg may point at them),
/// exports or backups. Voices are only removed when no block in any scene mentions them, and the
/// content cache keeps recent lines so AI redrafts can still reuse them.
/// </summary>
public static class ProjectMaintenance
{
    public const string OldPreviews = "Previews antiguas";
    public const string UnusedVoices = "Voces TTS sin uso";
    public const string OldVoiceCache = "Caché de voces sin usar";
    public const string Leftovers = "Temporales";

    public static readonly TimeSpan VoiceCacheAge = TimeSpan.FromDays(30);
    public static readonly TimeSpan LeftoverAge = TimeSpan.FromHours(6);

    public static IReadOnlyList<CleanupItem> Plan(string projectRoot, IReadOnlyCollection<ScriptAudioReference> references,
        IEnumerable<string> keep, DateTime utcNow)
    {
        var root = Path.GetFullPath(projectRoot);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var protectedPaths = keep.Where(x => !string.IsNullOrWhiteSpace(x)).Select(Path.GetFullPath).ToHashSet(comparer);
        var items = new List<CleanupItem>();
        void Add(FileInfo file, string category)
        {
            if (!protectedPaths.Contains(file.FullName)) items.Add(new CleanupItem(file.FullName, file.Length, category));
        }
        IEnumerable<FileInfo> Files(string relative, string pattern, SearchOption option = SearchOption.TopDirectoryOnly)
        {
            var folder = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(folder)) return [];
            try { return new DirectoryInfo(folder).EnumerateFiles(pattern, option).ToArray(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
        }

        // Previews: one MP4 per distinct composition (scene_<id>_<fingerprint>.mp4). Keep the newest per scene.
        foreach (var scene in Files("generated/previews", "*.mp4")
                     .GroupBy(file => file.Name.Split('_') is [var prefix, var id, ..] && prefix == "scene" ? id : file.Name, comparer))
            foreach (var old in scene.OrderByDescending(x => x.LastWriteTimeUtc).Skip(1)) Add(old, OldPreviews);

        // Voice file names mentioned anywhere in the script (path, or a kept recording inside parameters).
        var mentioned = references.Select(x => x.GeneratedAudioPath).OfType<string>()
            .Select(x => x.Replace('\\', '/').Split('/').Last()).ToHashSet(comparer);
        var parameters = string.Join("\n", references.Select(x => x.ParametersJson).Where(x => x.Contains(".wav", StringComparison.OrdinalIgnoreCase)));
        var hashes = references.Select(x => x.GeneratedAudioHash).OfType<string>().ToHashSet(StringComparer.Ordinal);
        bool Mentioned(FileInfo file) => mentioned.Contains(file.Name) ||
            parameters.Contains(file.Name, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

        // Synthesized lines: generated/voices/episode_###/scene_###/*.wav. The same line stays in the content cache.
        foreach (var file in Files("generated/voices", "*.wav", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(Path.Combine(root, "generated", "voices"), file.FullName).Replace('\\', '/');
            if (!relative.StartsWith("episode_", StringComparison.Ordinal) || Mentioned(file)) continue;
            Add(file, UnusedVoices);
        }
        // Content cache: <hash>.wav. Unused and not touched for a month.
        foreach (var file in Files("generated/voices/_cache", "*.wav"))
            if (!hashes.Contains(Path.GetFileNameWithoutExtension(file.Name)) && !Mentioned(file) &&
                utcNow - file.LastWriteTimeUtc > VoiceCacheAge)
                Add(file, OldVoiceCache);
        // Interrupted writes: parallel TTS staging, cache temp files, half-written previews.
        foreach (var file in Files("generated/voices/_cache/_staging", "*")
                     .Concat(Files("generated/voices/_cache", "*.tmp"))
                     .Concat(Files("generated/previews", "*.tmp"))
                     .Concat(Files("generated/png_compatibles", ".*.png")))
            if (utcNow - file.LastWriteTimeUtc > LeftoverAge) Add(file, Leftovers);
        return items;
    }

    /// <summary>Deletes planned files. Only paths inside the project's generated folder are accepted.</summary>
    public static (int Deleted, long Bytes, int Failed) Delete(string projectRoot, IEnumerable<CleanupItem> items)
    {
        var generated = Path.GetFullPath(Path.Combine(projectRoot, "generated")) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        int deleted = 0, failed = 0;
        long bytes = 0;
        foreach (var item in items)
        {
            var full = Path.GetFullPath(item.Path);
            if (!full.StartsWith(generated, comparison) ||
                full.Contains(Path.DirectorySeparatorChar + "imported_voices" + Path.DirectorySeparatorChar, comparison) ||
                full.Contains(Path.DirectorySeparatorChar + "vegas" + Path.DirectorySeparatorChar, comparison))
            {
                failed++;
                continue;
            }
            try
            {
                if (!File.Exists(full)) continue;
                File.Delete(full);
                deleted++;
                bytes += item.Bytes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed++; }
        }
        return (deleted, bytes, failed);
    }

    /// <summary>Size of VEGAS export folders, reported but never deleted automatically.</summary>
    public static long VegasExportBytes(string projectRoot)
    {
        var folder = Path.Combine(projectRoot, "generated", "vegas");
        if (!Directory.Exists(folder)) return 0;
        try { return new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(x => x.Length); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }
}
