using LoquendoAI.Core.Models;

namespace LoquendoAI.Infrastructure.Projects;

/// <summary>What a project has to give: its library (sources, folder rules, assets and their tags), voice profiles
/// and characters.</summary>
public sealed record LibrarySnapshot(
    IReadOnlyList<AssetSource> Sources,
    IReadOnlyList<FolderRule> Rules,
    IReadOnlyList<AssetRecord> Assets,
    IReadOnlyList<AssetTag> Tags,
    IReadOnlyList<VoiceProfile> Profiles,
    IReadOnlyList<CharacterDefinition> Characters);

/// <summary>What to bring from the other project.</summary>
public sealed record LibraryTransferOptions(bool Library = true, bool Profiles = false, bool Characters = false);

/// <summary>
/// The writes of a transfer, ready for SqliteProjectRepository.ApplyLibraryTransferAsync. Ids are already
/// those of the receiving project. <see cref="Rules"/> replace the folder rules of each listed source; <see cref="Tags"/>
/// replace the tags of each listed asset.
/// </summary>
public sealed record LibraryTransferPlan(
    IReadOnlyList<AssetSource> Sources,
    IReadOnlyDictionary<Guid, IReadOnlyList<FolderRule>> Rules,
    IReadOnlyList<AssetRecord> Assets,
    IReadOnlyDictionary<Guid, IReadOnlyList<AssetTag>> Tags,
    IReadOnlyList<VoiceProfile> Profiles,
    IReadOnlyList<CharacterDefinition> Characters)
{
    public int NewSources { get; init; }
    public int NewAssets { get; init; }
    public int UpdatedAssets { get; init; }
    public int NewProfiles { get; init; }
    public int NewCharacters { get; init; }
    public bool IsEmpty => Sources.Count == 0 && Assets.Count == 0 && Profiles.Count == 0 && Characters.Count == 0;
}

/// <summary>
/// Brings the library of another project (1.4.8), and optionally its voice profiles and characters, without duplicating
/// anything and without touching the other project:
/// <list type="bullet">
/// <item>A source is the same when it points to the same folder; an asset, when it is the same file of that source. What
/// the receiving project already has keeps its id, so its scenes keep working.</item>
/// <item>What comes from the other project wins for those same files: classification, tags and folder rules (the
/// «biblioteca principal» rules the projects that follow it). Nothing is deleted.</item>
/// <item>Profiles and characters are matched by name: one with the same name is not duplicated; a character that has no
/// voice gets the one it has there.</item>
/// </list>
/// The other project is read from a snapshot (VACUUM INTO), so it can be open in another window (LibraryTransfer.Io.cs).
/// </summary>
public static partial class LibraryTransfer
{
    /// <summary>The writes that bring <paramref name="from"/> into <paramref name="into"/>.</summary>
    public static LibraryTransferPlan Plan(LibrarySnapshot from, LibrarySnapshot into, LibraryTransferOptions options)
    {
        var sources = new List<AssetSource>();
        var rules = new Dictionary<Guid, IReadOnlyList<FolderRule>>();
        var assets = new List<AssetRecord>();
        var tags = new Dictionary<Guid, IReadOnlyList<AssetTag>>();
        int newSources = 0, newAssets = 0, updatedAssets = 0;
        if (options.Library)
        {
            var usedSourceIds = into.Sources.Select(x => x.Id).ToHashSet();
            var sourceMap = new Dictionary<Guid, Guid>();
            foreach (var source in from.Sources)
            {
                var same = into.Sources.FirstOrDefault(x => SameFolder(x.RootPath, source.RootPath));
                if (same is not null)
                {
                    sourceMap[source.Id] = same.Id;
                    // Already there: only remember the newest scan.
                    if (source.LastScanUtc > same.LastScanUtc) sources.Add(same with { LastScanUtc = source.LastScanUtc });
                    continue;
                }
                var id = usedSourceIds.Add(source.Id) ? source.Id : NewId(usedSourceIds);
                sourceMap[source.Id] = id;
                sources.Add(source with { Id = id });
                newSources++;
            }

            var usedRuleIds = into.Rules.Select(x => x.Id).ToHashSet();
            foreach (var (fromId, toId) in sourceMap)
            {
                var incoming = from.Rules.Where(x => x.SourceId == fromId).ToArray();
                if (incoming.Length == 0) continue;
                var merged = into.Rules.Where(x => x.SourceId == toId)
                    .ToDictionary(x => Folder(x.RelativeFolder), StringComparer.OrdinalIgnoreCase);
                foreach (var rule in incoming)
                {
                    var key = Folder(rule.RelativeFolder);
                    var id = merged.TryGetValue(key, out var existing) ? existing.Id
                        : usedRuleIds.Add(rule.Id) ? rule.Id : NewId(usedRuleIds);
                    merged[key] = rule with { Id = id, SourceId = toId };
                }
                rules[toId] = merged.Values.OrderBy(x => x.RelativeFolder, StringComparer.OrdinalIgnoreCase).ToArray();
            }

            var usedAssetIds = into.Assets.Select(x => x.Id).ToHashSet();
            var existingByFile = into.Assets.Where(x => x.SourceId is not null && x.SourceRelativePath is not null)
                .GroupBy(x => (x.SourceId!.Value, FilePath(x.SourceRelativePath!)))
                .ToDictionary(x => x.Key, x => x.First());
            var tagsByAsset = from.Tags.GroupBy(x => x.AssetId).ToDictionary(x => x.Key, x => x.ToArray());
            var tagsHere = into.Tags.GroupBy(x => x.AssetId).ToDictionary(x => x.Key, x => x.Select(Tag).ToHashSet());
            static (string, string, double) Tag(AssetTag tag) => (tag.Key, tag.Value, tag.Confidence);
            bool SameTags(Guid there, Guid here) => !tagsByAsset.TryGetValue(there, out var incoming) ||
                tagsHere.TryGetValue(here, out var existing) && existing.SetEquals(incoming.Select(Tag));
            foreach (var asset in from.Assets)
            {
                // Files of the project itself (imported voices…) are not part of the library.
                if (asset.SourceId is not Guid fromSource || asset.SourceRelativePath is not { } path ||
                    !sourceMap.TryGetValue(fromSource, out var toSource)) continue;
                var known = existingByFile.GetValueOrDefault((toSource, FilePath(path)));
                var id = known?.Id ?? (usedAssetIds.Add(asset.Id) ? asset.Id : NewId(usedAssetIds));
                var relative = known?.SourceRelativePath ?? path;
                var record = asset with
                {
                    Id = id, SourceId = toSource, SourceRelativePath = relative,
                    RelativePath = $"@source/{toSource:D}/{relative}", ImportedUtc = known?.ImportedUtc ?? asset.ImportedUtc
                };
                if (known is not null && known == record && SameTags(asset.Id, known.Id)) continue; // nothing new
                assets.Add(record);
                if (known is null) newAssets++; else updatedAssets++;
                if (tagsByAsset.TryGetValue(asset.Id, out var own))
                    tags[id] = own.Select(x => x with { AssetId = id }).ToArray();
            }
        }

        // Voice profiles and characters, by name.
        var profiles = new List<VoiceProfile>();
        var profileMap = new Dictionary<Guid, Guid>();
        var usedProfileIds = into.Profiles.Select(x => x.Id).ToHashSet();
        foreach (var profile in from.Profiles)
        {
            if (into.Profiles.FirstOrDefault(x => SameName(x.Name, profile.Name)) is { } same)
            {
                profileMap[profile.Id] = same.Id;
                continue;
            }
            if (!options.Profiles) continue;
            var id = usedProfileIds.Add(profile.Id) ? profile.Id : NewId(usedProfileIds);
            profileMap[profile.Id] = id;
            profiles.Add(profile with { Id = id });
        }
        var characters = new List<CharacterDefinition>();
        var newCharacters = 0;
        if (options.Characters)
        {
            var usedCharacterIds = into.Characters.Select(x => x.Id).ToHashSet();
            foreach (var character in from.Characters)
            {
                Guid? voice = character.DefaultVoiceProfileId is Guid v && profileMap.TryGetValue(v, out var mapped) ? mapped : null;
                if (into.Characters.FirstOrDefault(x => SameName(x.Name, character.Name)) is { } same)
                {
                    if (same.DefaultVoiceProfileId is null && voice is not null) characters.Add(same with { DefaultVoiceProfileId = voice });
                    continue;
                }
                var id = usedCharacterIds.Add(character.Id) ? character.Id : NewId(usedCharacterIds);
                characters.Add(character with { Id = id, DefaultVoiceProfileId = voice });
                newCharacters++;
            }
        }
        return new LibraryTransferPlan(sources, rules, assets, tags, profiles, characters)
        {
            NewSources = newSources, NewAssets = newAssets, UpdatedAssets = updatedAssets,
            NewProfiles = profiles.Count, NewCharacters = newCharacters
        };
    }

    /// <summary>The same folder on this PC: full path, either slash, any case, with or without the final slash.</summary>
    public static bool SameFolder(string left, string right) =>
        string.Equals(FolderPath(left), FolderPath(right), StringComparison.OrdinalIgnoreCase);

    private static string FolderPath(string path)
    {
        var normal = path.Trim().Replace('/', '\\').TrimEnd('\\');
        return normal.Length == 2 && normal[1] == ':' ? normal + "\\" : normal; // «C:» is the drive root
    }

    private static string Folder(string relative) => relative.Trim().Replace('\\', '/').Trim('/');
    /// <summary>A file of a source as a key: either slash, any case (Windows paths).</summary>
    private static string FilePath(string relative) => relative.Trim().Replace('\\', '/').ToUpperInvariant();
    private static bool SameName(string left, string right) => string.Equals(left.Trim(), right.Trim(), StringComparison.CurrentCultureIgnoreCase);

    private static Guid NewId(HashSet<Guid> used)
    {
        Guid id;
        do id = Guid.NewGuid(); while (!used.Add(id));
        return id;
    }
}
