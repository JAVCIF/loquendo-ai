using LoquendoAI.Core.Models;

namespace LoquendoAI.Core.Abstractions;

public interface IProjectRepository : IAsyncDisposable
{
    string ProjectRoot { get; }
    ProjectManifest Manifest { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AssetRecord>> GetAssetsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AssetFolder>> GetSpriteFoldersForCharacterAsync(string characterName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> GetAssetSourceIdsByKindsAsync(IReadOnlyCollection<AssetKind> kinds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetAssetSubfoldersAsync(IReadOnlyCollection<AssetKind> kinds, AssetFolder parent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AssetRecord>> SearchAssetsInFolderAsync(IReadOnlyCollection<AssetKind> kinds, AssetFolder folder, bool includeSubfolders, string search, int limit, CancellationToken cancellationToken = default);
    Task<AssetRecord?> GetAssetBySourcePathAsync(Guid sourceId, string sourceRelativePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AssetRecord>> GetAssetsByIdsAsync(IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AssetRecord>> GetAssetsForSourceAsync(Guid sourceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<Guid>> GetAssetIdsWithTagAsync(Guid sourceId, string key, CancellationToken cancellationToken = default);
    Task UpsertAssetAsync(AssetRecord asset, CancellationToken cancellationToken = default);
    Task DeleteAssetAsync(Guid assetId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AssetTag>> GetAssetTagsAsync(Guid? assetId = null, CancellationToken cancellationToken = default);
    Task ReplaceDirectorAssetTagsAsync(Guid assetId, string prefix, IEnumerable<AssetTag> tags, CancellationToken cancellationToken = default);
    /// <summary>IDs of assets that scans must not delete: referenced by script blocks or scenes,
    /// or carrying manual catalog/director edits.</summary>
    Task<IReadOnlySet<Guid>> GetProtectedAssetIdsAsync(CancellationToken cancellationToken = default);
    Task MarkAssetsMissingAsync(Guid sourceId, IReadOnlySet<string> presentRelativePaths, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AssetSource>> GetAssetSourcesAsync(CancellationToken cancellationToken = default);
    Task UpsertAssetSourceAsync(AssetSource source, CancellationToken cancellationToken = default);
    Task DeleteAssetSourceAsync(Guid sourceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FolderRule>> GetFolderRulesAsync(Guid sourceId, CancellationToken cancellationToken = default);
    Task ReplaceFolderRulesAsync(Guid sourceId, IEnumerable<FolderRule> rules, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VoiceProfile>> GetVoiceProfilesAsync(CancellationToken cancellationToken = default);
    Task UpsertVoiceProfileAsync(VoiceProfile profile, CancellationToken cancellationToken = default);
    Task DeleteVoiceProfileAsync(Guid profileId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterDefinition>> GetCharactersAsync(CancellationToken cancellationToken = default);
    Task UpsertCharacterAsync(CharacterDefinition character, CancellationToken cancellationToken = default);
    Task DeleteCharacterAsync(Guid characterId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Episode>> GetEpisodesAsync(CancellationToken cancellationToken = default);
    Task UpsertEpisodeAsync(Episode episode, CancellationToken cancellationToken = default);
    Task DeleteEpisodeAsync(Guid episodeId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Scene>> GetScenesAsync(Guid episodeId, CancellationToken cancellationToken = default);
    Task UpsertSceneAsync(Scene scene, CancellationToken cancellationToken = default);
    Task DeleteSceneAsync(Guid sceneId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SceneScriptBlock>> GetSceneScriptBlocksAsync(Guid sceneId, CancellationToken cancellationToken = default);
    Task UpsertSceneScriptBlockAsync(SceneScriptBlock block, CancellationToken cancellationToken = default);
    Task ReplaceSceneScriptBlocksAsync(Guid sceneId, IEnumerable<SceneScriptBlock> blocks, CancellationToken cancellationToken = default);
}
