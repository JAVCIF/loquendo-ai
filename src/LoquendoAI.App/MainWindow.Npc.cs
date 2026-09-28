using System.IO;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Persistence;
using LoquendoAI.Infrastructure.Tts;

namespace LoquendoAI.App;

/// <summary>
/// The «NPC» voice profile (1.2.0). Every project has it (it is created when the project opens) and Voice Lab
/// shows it like any profile, but it cannot be deleted. It is the voice of anyone without a profile of their own
/// (NPCs, the narrator, characters created as labels), so a line never fails with «no tiene perfil de voz»; and a
/// line can give it any voice of the selector (the block's direct voice: TTS7, SAPI4, SAPI5 since 1.4.0). Its default
/// voice is Jorge (Loquendo TTS7). Since 1.4.0 the Editor treats NPC as WHO speaks, not as a voice: a new line
/// without a character shows «NPC» as the speaker and the NPC's voice («TTS7 · Jorge») as its voice.
/// </summary>
public partial class MainWindow
{
    /// <summary>Same id in every project, so the profile is recognised without a special column.</summary>
    internal static readonly Guid NpcProfileId = new("4e50c000-0000-4000-8000-00000000c0de");
    internal const string NpcProfileName = "NPC";
    internal const string NpcDefaultVoice = "Jorge";

    private static bool IsNpcProfile(Guid? id) => id == NpcProfileId;

    /// <summary>«NPC: hola» in the Director language: a dialogue line without a registered character (1.4.0), unless a
    /// character is really called NPC (then it is that character).</summary>
    private static bool IsNpcLine(LoquendoAI.Infrastructure.Director.DirectorSpec spec) =>
        spec.Kind == ScriptBlockKind.Dialogue && SameDirectorName(spec.CharacterName, NpcProfileName);

    /// <summary>«[MOSTRAR] NPC | render» / «[OCULTAR] NPC | render» (1.4.1): a render without a registered character
    /// (an extra), unless a character is really called NPC.</summary>
    private static bool IsNpcRender(LoquendoAI.Infrastructure.Director.DirectorSpec spec) =>
        spec.Kind is ScriptBlockKind.CharacterShow or ScriptBlockKind.CharacterHide && SameDirectorName(spec.CharacterName, NpcProfileName);

    /// <summary>
    /// Director checks of an NPC render line (1.4.1). «Mostrar» must name its render (the usual search then finds it,
    /// without character). «Ocultar» finds the asset of the render to hide: first among the NPC renders shown before
    /// (by ID or by name), then in the library; a render that was not shown before is only a warning.
    /// </summary>
    private async Task<(string? Status, AssetRecord? Asset, string? Note)> CheckNpcRenderAsync(SqliteProjectRepository repository,
        IReadOnlyList<AssetSource> sources, LoquendoAI.Infrastructure.Director.DirectorSpec spec, IEnumerable<SceneScriptBlock> before)
    {
        var query = spec.ResourceQuery.Trim();
        if (spec.Kind == ScriptBlockKind.CharacterShow)
            return query.Length == 0 || SameDirectorName(query, NpcProfileName)
                ? ("Falta el render: [MOSTRAR] NPC | <render>", null, null) : (null, null, null);
        if (query.Length == 0) return ("Falta el render: [OCULTAR] NPC | <el render que mostraste>", null, null);
        var shown = before.Where(x => x.Kind == ScriptBlockKind.CharacterShow && x.CharacterId is null && x.AssetId is not null)
            .Select(x => x.AssetId!.Value).Distinct().ToArray();
        var known = shown.Select(id => _scriptAssetCache.GetValueOrDefault(id)).OfType<AssetRecord>().ToArray();
        var asset = Guid.TryParse(query, out var id) && shown.Contains(id) ? known.FirstOrDefault(x => x.Id == id)
            : known.LastOrDefault(x => SameDirectorName(x.DisplayName, query) ||
                SameDirectorName(Path.GetFileNameWithoutExtension(DirectorAssetLabel(x)), Path.GetFileNameWithoutExtension(query)) ||
                DirectorAssetLabel(x).Equals(query.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
        if (asset is not null) return (null, asset, null);
        var match = await FindDirectorAssetAsync(repository, sources, ScriptBlockKind.CharacterShow, query, null);
        if (match.Asset is null) return (match.Issue ?? "Render sin catalogar", null, null);
        _scriptAssetCache[match.Asset.Id] = match.Asset;
        var withCharacter = before.Any(x => x.Kind == ScriptBlockKind.CharacterShow && x.CharacterId is not null && x.AssetId == match.Asset.Id);
        return (null, match.Asset, shown.Contains(match.Asset.Id) ? null
            : withCharacter ? "Aviso: ese render se mostró con personaje; se ocultará a ese personaje"
            : "Aviso: ese render no se mostró antes como NPC; ocultarlo no cambiará nada");
    }

    /// <summary>Creates the NPC profile when the project does not have it yet.</summary>
    private static async Task EnsureNpcProfileAsync(SqliteProjectRepository repository)
    {
        var profiles = await repository.GetVoiceProfilesAsync();
        if (profiles.Any(x => x.Id == NpcProfileId)) return;
        await repository.UpsertVoiceProfileAsync(new VoiceProfile(NpcProfileId, NpcProfileName, DirectTts7Provider,
            NpcDefaultVoice, Pitch: 50, Speed: null, Volume: 100, SampleRate: 32000));
    }

    /// <summary>Once the TTS7 catalog is known: an untouched NPC profile takes the catalog's spelling of «Jorge»
    /// (e.g. «Jorge (es-ES)») so the bridge finds it.</summary>
    private async Task MatchNpcVoiceToCatalogAsync()
    {
        if (_currentRepository is not { } repository || _directTts7Voices.Count == 0) return;
        if (_voiceProfiles.FirstOrDefault(x => x.Id == NpcProfileId) is not { ProviderKey: DirectTts7Provider } npc) return;
        if (_directTts7Voices.Any(x => string.Equals(x, npc.VoiceId, StringComparison.OrdinalIgnoreCase))) return;
        if (!string.Equals(npc.VoiceId, NpcDefaultVoice, StringComparison.OrdinalIgnoreCase)) return;
        var jorge = _directTts7Voices.FirstOrDefault(x => x.StartsWith(NpcDefaultVoice, StringComparison.OrdinalIgnoreCase));
        if (jorge is null) return;
        await repository.UpsertVoiceProfileAsync(npc with { VoiceId = jorge });
        if (_currentRepository == repository) _voiceProfiles = await repository.GetVoiceProfilesAsync();
    }

    /// <summary>
    /// The NPC profile as a line uses it: with the line's own voice when it has one. The result has an empty
    /// ConfigJson like <see cref="DirectProfile"/>, so the same voice and prosody share the voice cache.
    /// </summary>
    private static VoiceProfile NpcVoice(VoiceProfile npc, DirectVoiceRef? directVoice) =>
        directVoice is null ? npc with { ConfigJson = "{}" }
        : string.Equals(VoiceSelectorSettings.Canonical(npc.ProviderKey), directVoice.Provider, StringComparison.OrdinalIgnoreCase)
            ? npc with { VoiceId = directVoice.Voice, ConfigJson = "{}" }
        // Another engine (SAPI5 −10…10 against TTS7/SAPI4 0–100) cannot borrow the NPC's prosody: its neutral one.
        : DirectProfile(directVoice) with { Id = npc.Id, Name = npc.Name };

    /// <summary>A profile chosen for a line: the NPC one takes the line's direct voice, the others are as saved.</summary>
    private static VoiceProfile LineProfile(VoiceProfile profile, DirectVoiceRef? directVoice) =>
        profile.Id == NpcProfileId ? NpcVoice(profile, directVoice) : profile;
}
