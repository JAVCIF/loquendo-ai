using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using LoquendoAI.Infrastructure.Composition;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Tts;

namespace LoquendoAI.App;

public partial class MainWindow
{
    private static readonly ScriptBlockTypeChoice[] ScriptBlockTypes =
    [
        new(ScriptBlockKind.Dialogue, "Diálogo"),
        new(ScriptBlockKind.Narration, "Narración"),
        new(ScriptBlockKind.Background, "Fondo"),
        new(ScriptBlockKind.CharacterShow, "Mostrar personaje"),
        new(ScriptBlockKind.CharacterHide, "Ocultar personaje"),
        new(ScriptBlockKind.Image, "Imagen / prop"),
        new(ScriptBlockKind.SoundEffect, "SFX"),
        new(ScriptBlockKind.Music, "Música"),
        new(ScriptBlockKind.Video, "Video"),
        new(ScriptBlockKind.Pause, "Pausa"),
        new(ScriptBlockKind.TextOverlay, "Texto en pantalla"),
        new(ScriptBlockKind.Transition, "Transición"),
        new(ScriptBlockKind.Camera, "Cámara"),
        new(ScriptBlockKind.Cinema, "Cine (barras negras)"),
        new(ScriptBlockKind.Gesture, "Gesto (balanceo / rebote)"),
        new(ScriptBlockKind.Blur, "Desenfoque"),
        new(ScriptBlockKind.Comment, "Comentario")
    ];

    private readonly MediaPlayer _scriptAudioPlayer = new();
    private IReadOnlyList<Episode> _episodes = Array.Empty<Episode>();
    private IReadOnlyList<Scene> _scenes = Array.Empty<Scene>();
    private IReadOnlyList<SceneScriptBlock> _scriptBlocks = Array.Empty<SceneScriptBlock>();
    private readonly Dictionary<Guid, AssetRecord> _scriptAssetCache = new();
    private readonly SemaphoreSlim _scriptAssetLoadLock = new(1, 1);
    private int _scriptAssetCacheRevision;
    private Guid? _editingScriptBlockId;
    private bool _loadingScriptUi;
    private bool _scriptUiReady;
    private CancellationTokenSource? _sceneVoiceGenerationCancellation;
    private CancellationTokenSource? _sceneRenderCancellation;
    private readonly DispatcherTimer _scenePreviewTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private string? _scenePreviewPath;
    private bool _scenePreviewPlaying;

    private void InitializeScriptEditor()
    {
        ScriptBlockTypeCombo.ItemsSource = ScriptBlockTypes;
        QuickBlockTypeCombo.ItemsSource = ScriptBlockTypes.Where(x => x.Kind != ScriptBlockKind.Dialogue).ToArray();
        ScriptBlockTypeCombo.SelectedItem = ScriptBlockTypes[0];
        QuickBlockTypeCombo.SelectedItem = ScriptBlockTypes.First(x => x.Kind == ScriptBlockKind.SoundEffect);
        BlockPauseBox.Text = "0";
        VisualDurationBox.Text = string.Empty;
        ResourceVolumeBox.Text = "100";
        VideoLayerCombo.SelectedIndex = 1;
        VideoGreenScreenCheck.IsChecked = false;
        VideoKeyColorBox.Text = "00FF00";
        VideoKeyToleranceBox.Text = "0.30";
        VegasPluginCombo.ItemsSource = VegasTransitionCatalog.All;
        ResetVisualTransformControls(ScriptBlockKind.Dialogue);
        AudioDurationModeCombo.SelectedIndex = 0;
        ResetEffectControls();
        _scenePreviewTimer.Tick += (_, _) => UpdateScenePreviewTime();
        _scenePreviewTimer.Start();
        _assetSearchTimer.Tick += AssetSearchTimer_Tick;
        _scriptUiReady = true;
        UpdateScriptBlockEditorState();
    }

    private async Task RefreshScriptAsync(Guid? selectEpisodeId = null, Guid? selectSceneId = null, Guid? selectBlockId = null)
    {
        if (_currentRepository is null)
            return;

        _loadingScriptUi = true;
        try
        {
            _episodes = await _currentRepository.GetEpisodesAsync();
            _characters = await _currentRepository.GetCharactersAsync();
            _voiceProfiles = await _currentRepository.GetVoiceProfilesAsync();

            EpisodesList.ItemsSource = _episodes.Select(x => new EpisodeScriptRow(x)).ToArray();
            ScriptCharacterCombo.ItemsSource = BuildCharacterChoices();
            ScriptVoiceProfileCombo.ItemsSource = BuildVoiceChoices();

            var episodeId = selectEpisodeId
                ?? (EpisodesList.SelectedItem as EpisodeScriptRow)?.Episode.Id
                ?? _episodes.FirstOrDefault()?.Id;
            EpisodesList.SelectedItem = EpisodesList.Items.Cast<EpisodeScriptRow>()
                .FirstOrDefault(x => x.Episode.Id == episodeId);
        }
        finally
        {
            _loadingScriptUi = false;
        }

        if (EpisodesList.SelectedItem is EpisodeScriptRow episodeRow)
        {
            LoadEpisodeForm(episodeRow.Episode);
            await LoadScenesAsync(episodeRow.Episode.Id, selectSceneId, selectBlockId);
        }
        else
        {
            ClearEpisodeForm();
            ScenesList.ItemsSource = Array.Empty<SceneScriptRow>();
            ClearSceneForm();
            await LoadBlocksAsync(null);
        }
    }

    private void ResetScriptAssetCache()
    {
        // A scan can add assets or change their kind without changing projects. A kind
        // that was queried while empty must not remain permanently cached as empty.
        _scriptAssetCacheRevision++;
        _scriptAssetCache.Clear();
        _folderChoicesByCharacter.Clear();
        _activeAssetPage = [];
        _activeAssetQueryKey = null;
        _assetPageHasMore = false;
        _assetSearchTimer.Stop();
    }

    private async Task RefreshScriptAssetsAfterCatalogChangeAsync()
    {
        var selectedAssetId = (ScriptAssetCombo.SelectedItem as AssetChoice)?.Id;
        var editingAssetId = _scriptBlocks.FirstOrDefault(x => x.Id == _editingScriptBlockId)?.AssetId;
        var selectedFolder = CharacterFolderCombo.SelectedItem as CharacterFolderChoice;
        ResetScriptAssetCache();

        if (_currentRepository is null || ScriptBlockTypeCombo.SelectedItem is not ScriptBlockTypeChoice choice)
            return;

        var referencedIds = _scriptBlocks.Where(x => x.AssetId.HasValue).Select(x => x.AssetId!.Value).Distinct().ToArray();
        if (referencedIds.Length > 0)
        {
            var referenced = await _currentRepository.GetAssetsByIdsAsync(referencedIds);
            foreach (var asset in referenced) _scriptAssetCache[asset.Id] = asset;
        }
        await LoadAssetFoldersForKindAsync(choice.Kind, selectedFolder);
        await EnsureAssetsForKindAsync(choice.Kind);
        // Never bind a completed query for an old kind over a newer user selection.
        if (ScriptBlockTypeCombo.SelectedItem is ScriptBlockTypeChoice current && current.Kind == choice.Kind)
            RefreshAssetChoices(choice.Kind, selectedAssetId ?? editingAssetId);
    }

    private async void ScriptAssetsRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is null || ScriptBlockTypeCombo.SelectedItem is not ScriptBlockTypeChoice choice)
            return;

        ScriptAssetsRefreshButton.IsEnabled = false;
        try
        {
            await RefreshScriptAssetsAfterCatalogChangeAsync();
            ScriptStatusText.Text = $"Catálogo actualizado: {ScriptAssetCombo.Items.Count - 1:N0} assets disponibles para {choice.Name}.";
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            ScriptAssetsRefreshButton.IsEnabled = true;
        }
    }

    private void RefreshScriptReferenceChoices()
    {
        if (ScriptCharacterCombo is null || ScriptVoiceProfileCombo is null)
            return;
        var characterId = (ScriptCharacterCombo.SelectedItem as CharacterChoice)?.Id;
        var renderAssetId = (ScriptCharacterCombo.SelectedItem as CharacterChoice)?.RenderAssetId;
        var selectedVoice = ScriptVoiceProfileCombo.SelectedItem as VoiceProfileChoice;
        _folderChoicesByCharacter.Clear();
        ScriptCharacterCombo.ItemsSource = BuildCharacterChoices();
        ScriptVoiceProfileCombo.ItemsSource = BuildVoiceChoices(characterId, speakerKnown: true);
        SelectCharacterChoice(characterId, renderAssetId);
        SelectVoiceChoice(ScriptVoiceProfileCombo, selectedVoice?.Id, selectedVoice?.Direct);
        RefreshCinemaCharacterChoices();
        RefreshDialogueChoices();
        if (ScriptBlocksGrid?.ItemsSource is not null)
            BindScriptBlockGrid((ScriptBlocksGrid.SelectedItem as ScriptBlockRow)?.Block.Id);
    }

    /// <summary>Who speaks without a registered character: «NPC» in speech blocks (1.4.0), «—» (nobody) elsewhere.</summary>
    private const string NpcCharacterLabel = "NPC";

    private CharacterChoice[] BuildCharacterChoices()
    {
        var kind = CurrentEditorKind;
        _characterChoicesForHide = kind == ScriptBlockKind.CharacterHide;
        _characterChoicesBlockId = _editingScriptBlockId;
        _characterChoicesBlockCount = _scriptBlocks.Count;
        return [new CharacterChoice(null, IsSpeechBlock(kind) ? NpcCharacterLabel : "—"),
            .. _characters.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).Select(x => new CharacterChoice(x.Id, x.Name)),
            .. _characterChoicesForHide ? NpcRenderChoices() : []];
    }

    /// <summary>True when the character combo was built for «Ocultar personaje» (it then also lists NPC renders), and
    /// for which block: the NPC renders offered depend on the block's place in the scene.</summary>
    private bool _characterChoicesForHide;
    private Guid? _characterChoicesBlockId;
    private int _characterChoicesBlockCount;

    /// <summary>
    /// «Ocultar personaje» (1.4.1) also lists the renders shown WITHOUT a character before this block (NPCs, extras),
    /// by their resource name. Once such a render gets a character, it leaves this list: the character hides it.
    /// </summary>
    private IEnumerable<CharacterChoice> NpcRenderChoices()
    {
        var editing = _editingScriptBlockId is Guid id ? _scriptBlocks.FirstOrDefault(x => x.Id == id) : null;
        var before = editing?.OrderIndex ?? int.MaxValue;
        var assets = _scriptBlocks.Where(x => x.OrderIndex < before && x.Kind == ScriptBlockKind.CharacterShow &&
                x.CharacterId is null && x.AssetId is not null)
            .OrderBy(x => x.OrderIndex).Select(x => x.AssetId!.Value).Distinct().ToList();
        // The render an existing hide points at stays selectable even if its «Mostrar» moved or was removed.
        if (editing is { Kind: ScriptBlockKind.CharacterHide, CharacterId: null, AssetId: Guid own } && !assets.Contains(own))
            assets.Add(own);
        return assets.Select(asset => new CharacterChoice(null,
            "NPC · " + (_scriptAssetCache.TryGetValue(asset, out var record) ? record.DisplayName : "render"), asset));
    }

    /// <summary>Selects a character, or an NPC render of «Ocultar personaje» by its asset.</summary>
    private void SelectCharacterChoice(Guid? characterId, Guid? renderAssetId)
    {
        if (characterId is null && renderAssetId is Guid asset &&
            ScriptCharacterCombo.Items.OfType<CharacterChoice>().FirstOrDefault(x => x.RenderAssetId == asset) is { } render)
            ScriptCharacterCombo.SelectedItem = render;
        else SelectChoiceById(ScriptCharacterCombo, characterId);
    }

    private ScriptBlockKind CurrentEditorKind =>
        (ScriptBlockTypeCombo?.SelectedItem as ScriptBlockTypeChoice)?.Kind ?? ScriptBlockKind.Dialogue;

    /// <summary>The first entry of the character combo says «NPC» or «—» depending on the block type, and «Ocultar
    /// personaje» adds the NPC renders.</summary>
    private void UpdateCharacterNoneLabel()
    {
        var label = IsSpeechBlock(CurrentEditorKind) ? NpcCharacterLabel : "—";
        var hide = CurrentEditorKind == ScriptBlockKind.CharacterHide;
        if (ScriptCharacterCombo.Items.Count > 0 && ScriptCharacterCombo.Items[0] is CharacterChoice { Id: null } first && first.Name == label &&
            _characterChoicesForHide == hide &&
            (!hide || _characterChoicesBlockId == _editingScriptBlockId && _characterChoicesBlockCount == _scriptBlocks.Count))
            return;
        var selected = ScriptCharacterCombo.SelectedItem as CharacterChoice;
        var wasLoading = _loadingScriptUi;
        _loadingScriptUi = true;
        try
        {
            ScriptCharacterCombo.ItemsSource = BuildCharacterChoices();
            SelectCharacterChoice(selected?.Id, selected?.RenderAssetId);
        }
        finally { _loadingScriptUi = wasLoading; }
    }

    /// <summary>
    /// The first choice of «Voz» (1.4.0): the voice of whoever speaks, named with the TTS voice it really is. A character
    /// with its own profile → «Su perfil · Bart»; NPC or a character without a profile → «TTS7 · Jorge (voz NPC)», the NPC
    /// profile's voice. The line keeps following that profile: changing it in Voice Lab changes these lines too.
    /// </summary>
    private string InheritVoiceLabel(Guid? speakerId)
    {
        var character = speakerId is Guid id ? _characters.FirstOrDefault(x => x.Id == id) : null;
        if (character?.DefaultVoiceProfileId is Guid own && !IsNpcProfile(own) && _voiceProfiles.FirstOrDefault(x => x.Id == own) is { } profile)
            return "Su perfil · " + profile.Name;
        return NpcVoiceLabel();
    }

    /// <summary>«TTS7 · Jorge (voz NPC)» (Editor) or «TTS7 · Jorge» (Diálogos): the NPC profile's voice as the
    /// selectors name it.</summary>
    private string NpcVoiceLabel(bool mark = true)
    {
        var suffix = mark ? " (voz NPC)" : "";
        return NpcLineVoice() is { } voice ? voice.Label + suffix
            : _voiceProfiles.FirstOrDefault(x => IsNpcProfile(x.Id)) is { } npc ? npc.VoiceId + suffix : "Voz NPC";
    }

    /// <summary>
    /// The «Voz» combo (1.4.0). NPC is not a voice: it is who speaks, and any TTS voice can be given to it. So the combo
    /// offers the speaker's own voice (named with the real TTS voice), the saved profiles and the voices of the selector
    /// (TTS7, SAPI4, SAPI5 — Voice Lab edits which). Lines that already use the NPC profile keep a choice of their own
    /// so editing them does not change their voice. <paramref name="speakerId"/>: who speaks (default: the Personaje combo).
    /// </summary>
    private VoiceProfileChoice[] BuildVoiceChoices(Guid? speakerId = null, bool speakerKnown = false)
    {
        if (!speakerKnown) speakerId = (ScriptCharacterCombo?.SelectedItem as CharacterChoice)?.Id;
        var choices = new List<VoiceProfileChoice> { new(null, InheritVoiceLabel(speakerId)) };
        choices.AddRange(_voiceProfiles.Where(x => !IsNpcProfile(x.Id)).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => new VoiceProfileChoice(x.Id, "Perfil · " + x.Name)));
        if (_voiceProfiles.FirstOrDefault(x => IsNpcProfile(x.Id)) is { } npc &&
            _scriptBlocks.Any(x => IsNpcProfile(x.VoiceProfileId) && DirectVoiceOf(x) is null))
            choices.Add(new VoiceProfileChoice(NpcProfileId, $"{NpcProfileName} · {npc.VoiceId} (perfil NPC)"));
        choices.AddRange(_scriptBlocks.Where(x => IsNpcProfile(x.VoiceProfileId)).Select(DirectVoiceOf).OfType<DirectVoiceRef>()
            .DistinctBy(x => (x.Provider, x.Voice.ToLowerInvariant())).OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => VoiceProfileChoice.Of(NpcProfileId, $"{NpcProfileName} · {x.Label}", x)));
        choices.AddRange(DirectVoiceOptions(_scriptBlocks.Select(DirectVoiceOf)).Select(x => VoiceProfileChoice.Of(null, x.Label, x)));
        return choices.ToArray();
    }

    /// <summary>The selector's voices plus the ones already in use (so a hidden voice is not lost when a line is
    /// edited) and the NPC's own voice (the default of a new line).</summary>
    private IReadOnlyList<DirectVoiceRef> DirectVoiceOptions(IEnumerable<DirectVoiceRef?> used)
    {
        var list = SelectorVoices().ToList();
        void Add(DirectVoiceRef? voice)
        {
            if (voice is not null && !list.Any(x => x.Is(voice.Provider, voice.Voice))) list.Add(voice);
        }
        foreach (var voice in used) Add(voice);
        Add(NpcLineVoice());
        var order = VoiceSelectorSettings.Providers.ToList();
        return list.OrderBy(x => order.IndexOf(x.Provider) is var index && index < 0 ? order.Count : index)
            .ThenBy(x => x.Voice, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    /// <summary>The NPC profile's voice (Voice Lab), Jorge (TTS7) by default; null if it is not a direct engine.</summary>
    private DirectVoiceRef? NpcLineVoice()
    {
        var npc = _voiceProfiles.FirstOrDefault(x => IsNpcProfile(x.Id));
        if (npc is null) return new DirectVoiceRef(DirectTts7Provider,
            _directTts7Voices.FirstOrDefault(x => x.StartsWith(NpcDefaultVoice, StringComparison.OrdinalIgnoreCase)) ?? NpcDefaultVoice);
        return VoiceSelectorSettings.IsDirectProvider(npc.ProviderKey) && !string.IsNullOrWhiteSpace(npc.VoiceId)
            ? new DirectVoiceRef(VoiceSelectorSettings.Canonical(npc.ProviderKey), npc.VoiceId) : null;
    }

    /// <summary>New line (1.4.0): the speaker's own voice, shown as the real TTS voice («Su perfil · Bart» or
    /// «TTS7 · Jorge (voz NPC)»), never the bare word «NPC».</summary>
    private void SelectDefaultSpeechVoice()
    {
        ScriptVoiceProfileCombo.ItemsSource = BuildVoiceChoices();
        SelectChoiceById(ScriptVoiceProfileCombo, null);
    }

    /// <summary>Who speaks changed: the first «Voz» choice is renamed after the new speaker, the selection is kept.</summary>
    private void RefreshSpeakerVoiceChoice()
    {
        var selected = ScriptVoiceProfileCombo.SelectedItem as VoiceProfileChoice;
        var wasLoading = _loadingScriptUi;
        _loadingScriptUi = true;
        try
        {
            ScriptVoiceProfileCombo.ItemsSource = BuildVoiceChoices();
            SelectVoiceChoice(ScriptVoiceProfileCombo, selected?.Id, selected?.Direct);
        }
        finally { _loadingScriptUi = wasLoading; }
    }

    private async void EpisodesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingScriptUi)
            return;
        if (EpisodesList.SelectedItem is not EpisodeScriptRow row)
        {
            ClearEpisodeForm();
            await LoadScenesAsync(Guid.Empty);
            return;
        }

        LoadEpisodeForm(row.Episode);
        await LoadScenesAsync(row.Episode.Id);
    }

    private async Task LoadScenesAsync(Guid episodeId, Guid? selectSceneId = null, Guid? selectBlockId = null)
    {
        if (_currentRepository is null || episodeId == Guid.Empty)
        {
            _scenes = Array.Empty<Scene>();
            ScenesList.ItemsSource = Array.Empty<SceneScriptRow>();
            ClearSceneForm();
            await LoadBlocksAsync(null);
            return;
        }

        _scenes = await _currentRepository.GetScenesAsync(episodeId);
        _loadingScriptUi = true;
        try
        {
            ScenesList.ItemsSource = _scenes.Select(x => new SceneScriptRow(x)).ToArray();
            var sceneId = selectSceneId
                ?? (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id
                ?? _scenes.FirstOrDefault()?.Id;
            ScenesList.SelectedItem = ScenesList.Items.Cast<SceneScriptRow>()
                .FirstOrDefault(x => x.Scene.Id == sceneId);
        }
        finally
        {
            _loadingScriptUi = false;
        }

        if (ScenesList.SelectedItem is SceneScriptRow sceneRow)
        {
            LoadSceneForm(sceneRow.Scene);
            await LoadBlocksAsync(sceneRow.Scene.Id, selectBlockId);
        }
        else
        {
            ClearSceneForm();
            await LoadBlocksAsync(null);
        }
        UpdateDirectorDraftTarget();
    }

    private async void ScenesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingScriptUi)
            return;
        UpdateDirectorDraftTarget();
        if (ScenesList.SelectedItem is not SceneScriptRow row)
        {
            ClearSceneForm();
            await LoadBlocksAsync(null);
            return;
        }

        LoadSceneForm(row.Scene);
        await LoadBlocksAsync(row.Scene.Id);
    }

    private async Task LoadBlocksAsync(Guid? sceneId, Guid? selectBlockId = null)
    {
        ClearScenePreview();
        _editingScriptBlockId = null;
        if (_currentRepository is null || sceneId is null)
        {
            _scriptBlocks = Array.Empty<SceneScriptBlock>();
            ScriptBlocksGrid.ItemsSource = Array.Empty<ScriptBlockRow>();
            ClearBlockEditorForm();
            UpdateScriptSceneSummary();
            return;
        }

        _scriptBlocks = await _currentRepository.GetSceneScriptBlocksAsync(sceneId.Value);
        var referencedAssetIds = _scriptBlocks.Where(x => x.AssetId is not null).Select(x => x.AssetId!.Value)
            .Where(id => !_scriptAssetCache.ContainsKey(id)).Distinct().ToArray();
        if (referencedAssetIds.Length > 0)
        {
            var referencedAssets = await _currentRepository.GetAssetsByIdsAsync(referencedAssetIds);
            foreach (var asset in referencedAssets)
                _scriptAssetCache[asset.Id] = asset;
        }
        BindScriptBlockGrid(selectBlockId);
        UpdateScriptSceneSummary();
        if (selectBlockId is null)
            ClearBlockEditorForm();
    }

    private void BindScriptBlockGrid(Guid? selectBlockId = null)
    {
        var characterNames = _characters.ToDictionary(x => x.Id, x => x.Name);
        var characterProfiles = _characters.ToDictionary(x => x.Id, x => x.DefaultVoiceProfileId);
        var assetNames = _scriptAssetCache.ToDictionary(x => x.Key, x => x.Value.DisplayName);
        var profileNames = _voiceProfiles.ToDictionary(x => x.Id, x => x.Name);
        var rows = _scriptBlocks
            .OrderBy(x => x.OrderIndex)
            .Select(block => new ScriptBlockRow(block, ResolveBlockSubject(
                // An «Ocultar» of an NPC render that has a character by now is shown as that character (1.4.1).
                block.Kind == ScriptBlockKind.CharacterHide && block.CharacterId is null &&
                SceneComposer.HideTarget(_scriptBlocks, block).CharacterId is Guid hiddenCharacter
                    ? block with { CharacterId = hiddenCharacter, AssetId = null } : block,
                characterNames, characterProfiles, assetNames, profileNames)))
            .ToArray();
        ScriptBlocksGrid.ItemsSource = rows;
        if (selectBlockId is Guid id)
            ScriptBlocksGrid.SelectedItem = rows.FirstOrDefault(x => x.Block.Id == id);
    }

    private static string ResolveBlockSubject(
        SceneScriptBlock block,
        IReadOnlyDictionary<Guid, string> characterNames,
        IReadOnlyDictionary<Guid, Guid?> characterProfiles,
        IReadOnlyDictionary<Guid, string> assetNames,
        IReadOnlyDictionary<Guid, string> profileNames)
    {
        if (IsImportedVoice(block))
        {
            // Recorded take: who speaks (label) and, only if the take has one, its own TTS voice.
            var speaker = block.CharacterId is Guid speakerId && characterNames.TryGetValue(speakerId, out var speakerName)
                ? speakerName : "Narrador";
            var ownVoice = block.VoiceProfileId is Guid ownId && profileNames.TryGetValue(ownId, out var ownName) ? ownName
                : DirectVoiceOf(block) is { } direct ? direct.Label : null;
            // The Audio column already says "Grabada"; before, this cell showed the voice instead of the speaker.
            return speaker + (ownVoice is null ? "" : " (TTS: " + ownVoice + ")");
        }
        if (block.Kind == ScriptBlockKind.Narration)
        {
            if (block.VoiceProfileId is null && DirectVoiceOf(block) is not null) return string.Empty;
            var effectiveId = block.VoiceProfileId ?? (block.CharacterId is Guid id && characterProfiles.TryGetValue(id, out var inherited)
                ? inherited : null);
            return effectiveId is Guid profileId && profileNames.TryGetValue(profileId, out var profileName)
                ? profileName : string.Empty;
        }
        if (block.CharacterId is Guid characterId && characterNames.TryGetValue(characterId, out var character))
            return character;
        if (block.Kind == ScriptBlockKind.Dialogue && block.CharacterId is null)
            return NpcCharacterLabel;
        if (block.Kind is ScriptBlockKind.CharacterShow or ScriptBlockKind.CharacterHide && block.CharacterId is null &&
            block.AssetId is Guid npcRender && assetNames.TryGetValue(npcRender, out var npcName))
            return NpcCharacterLabel + " · " + npcName;
        if (block.AssetId is Guid assetId && assetNames.TryGetValue(assetId, out var asset))
            return asset;
        return "—";
    }

    private void LoadEpisodeForm(Episode episode)
    {
        EpisodeNumberBox.Text = episode.Number.ToString();
        EpisodeTitleBox.Text = episode.Title;
        EpisodeSynopsisBox.Text = episode.Synopsis ?? string.Empty;
    }

    private void ClearEpisodeForm()
    {
        EpisodeNumberBox.Text = string.Empty;
        EpisodeTitleBox.Text = string.Empty;
        EpisodeSynopsisBox.Text = string.Empty;
    }

    private void LoadSceneForm(Scene scene)
    {
        SceneTitleBox.Text = scene.Title;
        SceneNotesBox.Text = scene.DirectionNotes ?? string.Empty;
    }

    private void ClearSceneForm()
    {
        SceneTitleBox.Text = string.Empty;
        SceneNotesBox.Text = string.Empty;
    }

    private async void NewEpisode_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is null)
            return;
        var nextNumber = _episodes.Count == 0 ? 1 : _episodes.Max(x => x.Number) + 1;
        var episode = new Episode(Guid.NewGuid(), nextNumber, $"Episodio {nextNumber}");
        try
        {
            await _currentRepository.UpsertEpisodeAsync(episode);
            await RefreshScriptAsync(episode.Id);
            ScriptStatusText.Text = $"Creado Episodio {nextNumber}.";
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async void SaveEpisode_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is null || EpisodesList.SelectedItem is not EpisodeScriptRow row)
            return;
        if (!int.TryParse(EpisodeNumberBox.Text.Trim(), out var number) || number <= 0)
        {
            MessageBox.Show(this, "El número de episodio debe ser un entero mayor que cero.", "Guion", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var title = EpisodeTitleBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
            title = $"Episodio {number}";

        try
        {
            var updated = row.Episode with { Number = number, Title = title, Synopsis = NullIfBlank(EpisodeSynopsisBox.Text) };
            await _currentRepository.UpsertEpisodeAsync(updated);
            await RefreshScriptAsync(updated.Id, (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id);
            ScriptStatusText.Text = "Episodio guardado.";
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async void DeleteEpisode_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is null || EpisodesList.SelectedItem is not EpisodeScriptRow row)
            return;
        if (MessageBox.Show(this, $"¿Eliminar '{row.Episode.Title}' y todas sus escenas?", "Guion",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            await _currentRepository.DeleteEpisodeAsync(row.Episode.Id);
            await RefreshScriptAsync();
            ScriptStatusText.Text = "Episodio eliminado.";
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async void NewScene_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is null || EpisodesList.SelectedItem is not EpisodeScriptRow episodeRow)
        {
            MessageBox.Show(this, "Crea o selecciona un episodio primero.", "Guion", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var nextIndex = _scenes.Count == 0 ? 1 : _scenes.Max(x => x.Index) + 1;
        var scene = new Scene(Guid.NewGuid(), episodeRow.Episode.Id, nextIndex, $"Escena {nextIndex}", 0);
        try
        {
            await _currentRepository.UpsertSceneAsync(scene);
            await RefreshScriptAsync(episodeRow.Episode.Id, scene.Id);
            ScriptStatusText.Text = $"Creada Escena {nextIndex}.";
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async void SaveScene_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is null || ScenesList.SelectedItem is not SceneScriptRow row)
            return;
        var title = SceneTitleBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
            title = $"Escena {row.Scene.Index}";
        try
        {
            var updated = row.Scene with { Title = title, DirectionNotes = NullIfBlank(SceneNotesBox.Text) };
            await _currentRepository.UpsertSceneAsync(updated);
            await RefreshScriptAsync(updated.EpisodeId, updated.Id, (ScriptBlocksGrid.SelectedItem as ScriptBlockRow)?.Block.Id);
            ScriptStatusText.Text = "Escena guardada.";
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async void DeleteScene_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is null || ScenesList.SelectedItem is not SceneScriptRow row)
            return;
        if (MessageBox.Show(this, $"¿Eliminar '{row.Scene.Title}' y todos sus bloques?", "Guion",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        try
        {
            var episodeId = row.Scene.EpisodeId;
            await _currentRepository.DeleteSceneAsync(row.Scene.Id);
            await RefreshScriptAsync(episodeId);
            ScriptStatusText.Text = "Escena eliminada.";
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async void AddDialogueBlock_Click(object sender, RoutedEventArgs e) => await BeginNewBlockSafelyAsync(ScriptBlockKind.Dialogue);

    private async void AddTypedBlock_Click(object sender, RoutedEventArgs e)
    {
        if (QuickBlockTypeCombo.SelectedItem is ScriptBlockTypeChoice choice)
            await BeginNewBlockSafelyAsync(choice.Kind);
    }

    private async void ClearBlockEditor_Click(object sender, RoutedEventArgs e) => await BeginNewBlockSafelyAsync(ScriptBlockKind.Dialogue);

    private async Task BeginNewBlockSafelyAsync(ScriptBlockKind kind)
    {
        try { await BeginNewScriptBlockAsync(kind); }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task BeginNewScriptBlockAsync(ScriptBlockKind kind)
    {
        if (ScenesList.SelectedItem is not SceneScriptRow)
        {
            MessageBox.Show(this, "Selecciona una escena primero.", "Guion", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _editingScriptBlockId = null;
        ScriptBlocksGrid.SelectedItem = null;
        ScriptBlockTypeCombo.SelectedItem = ScriptBlockTypes.First(x => x.Kind == kind);
        ScriptBlockTextBox.Text = string.Empty;
        BlockPitchBox.Text = string.Empty;
        BlockSpeedBox.Text = string.Empty;
        BlockVolumeBox.Text = string.Empty;
        BlockPauseBox.Text = "0";
        VisualDurationBox.Text = string.Empty;
        ResourceVolumeBox.Text = SceneComposer.DefaultVolumePercent(kind).ToString(CultureInfo.InvariantCulture);
        VideoLayerCombo.SelectedIndex = 1;
        VideoGreenScreenCheck.IsChecked = false;
        VideoKeyColorBox.Text = "00FF00";
        VideoKeyToleranceBox.Text = "0.30";
        ResetVisualTransformControls(kind);
        TransitionVegasEffectCombo.SelectedIndex = 0;
        VegasPluginCombo.SelectedIndex = -1;
        VegasPresetCombo.ItemsSource = null;
        AudioDurationModeCombo.SelectedIndex = 0;
        SfxWaitCheck.IsChecked = false;
        CharacterPositionCombo.SelectedIndex = kind == ScriptBlockKind.CharacterShow ? 3 : 1;
        CharacterFramingCombo.SelectedIndex = kind == ScriptBlockKind.CharacterShow ? 1 : 0;
        BackgroundTrimBordersCheck.IsChecked = true;
        UpdateCharacterNoneLabel();
        SelectChoiceById(ScriptCharacterCombo, null);
        SelectDefaultSpeechVoice();
        SpriteSubfoldersCheck.IsChecked = false;
        SpriteNameSearchBox.Text = string.Empty;
        ResetEffectControls();
        await LoadAssetFoldersForKindAsync(kind, null);
        await EnsureAssetsForKindAsync(kind);
        RefreshAssetChoices(kind, null);
        UpdateScriptBlockEditorState();
        ScriptBlockTextBox.Focus();
        ScriptStatusText.Text = "Nuevo bloque: completa los datos y pulsa Guardar bloque.";
    }

    private async void ScriptBlocksGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingScriptUi || ScriptBlocksGrid.SelectedItem is not ScriptBlockRow row)
            return;
        try { await LoadBlockIntoEditorAsync(row.Block); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void ScriptBlocksGrid_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Tab || ScriptBlocksGrid.Items.Count == 0) return;
        var backwards = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift);
        var selected = ScriptBlocksGrid.SelectedItems.Cast<ScriptBlockRow>().ToArray();
        if (selected.Length == 0) return;
        var edge = backwards ? selected.Min(x => x.Block.OrderIndex) : selected.Max(x => x.Block.OrderIndex);
        var next = edge + (backwards ? -1 : 1);
        if (next < 0 || next >= ScriptBlocksGrid.Items.Count) return;
        var row = ScriptBlocksGrid.Items.Cast<ScriptBlockRow>()
            .FirstOrDefault(x => x.Block.OrderIndex == next);
        if (row is null) return;
        ScriptBlocksGrid.SelectedItems.Add(row);
        ScriptBlocksGrid.ScrollIntoView(row);
        e.Handled = true;
    }

    private async Task LoadBlockIntoEditorAsync(SceneScriptBlock block)
    {
        _editingScriptBlockId = block.Id;
        _loadingScriptUi = true;
        try
        {
            ScriptBlockTypeCombo.SelectedItem = ScriptBlockTypes.First(x => x.Kind == block.Kind);
            ScriptBlockTextBox.Text = block.Text;
            BlockPitchBox.Text = block.VoicePitchOverride?.ToString() ?? string.Empty;
            BlockSpeedBox.Text = block.VoiceSpeedOverride?.ToString() ?? string.Empty;
            BlockVolumeBox.Text = block.VoiceVolumeOverride?.ToString() ?? string.Empty;
            BlockPauseBox.Text = block.PauseAfterMs.ToString();
            VisualDurationBox.Text = (SceneComposer.VisualDuration(block) ?? SceneComposer.AudioDuration(block))?.ToString() ?? string.Empty;
            ResourceVolumeBox.Text = SceneComposer.VolumePercent(block).ToString(CultureInfo.InvariantCulture);
            var video = SceneComposer.VideoOptions(block);
            VideoLayerCombo.SelectedIndex = video.Layer switch { "fondo" => 0, "sobre" => 2, _ => 1 };
            var knownVideo = block.AssetId is Guid videoId && _scriptAssetCache.TryGetValue(videoId, out var cataloguedVideo)
                ? cataloguedVideo.DisplayName : string.Empty;
            VideoGreenScreenCheck.IsChecked = video.GreenScreen || SceneComposer.AutoGreenScreen(block) &&
                SceneComposer.LooksLikeGreenScreen(knownVideo);
            VideoKeyColorBox.Text = video.KeyColor;
            VideoKeyToleranceBox.Text = video.Tolerance.ToString("0.###", CultureInfo.InvariantCulture);
            var transform = SceneComposer.VisualTransform(block);
            VisualWidthBox.Text = transform.MaxWidth.ToString(CultureInfo.InvariantCulture);
            VisualHeightBox.Text = transform.MaxHeight.ToString(CultureInfo.InvariantCulture);
            SyncBackgroundZoomCombo();
            VisualOffsetXBox.Text = transform.OffsetX.ToString(CultureInfo.InvariantCulture);
            VisualOffsetYBox.Text = transform.OffsetY.ToString(CultureInfo.InvariantCulture);
            VisualFlipHorizontalCheck.IsChecked = transform.FlipHorizontal;
            VisualFlipVerticalCheck.IsChecked = transform.FlipVertical;
            VisualChangeDirectionCheck.IsChecked = transform.ChangeDirection;
            VisualRotationBox.Text = transform.RotationDegrees.ToString("0.###", CultureInfo.InvariantCulture);
            MotionOffsetXBox.Text = transform.MotionOffsetX.ToString(CultureInfo.InvariantCulture);
            MotionOffsetYBox.Text = transform.MotionOffsetY.ToString(CultureInfo.InvariantCulture);
            MotionRotationBox.Text = transform.MotionRotationDegrees.ToString("0.###", CultureInfo.InvariantCulture);
            MotionDurationBox.Text = transform.MotionDurationMs.ToString(CultureInfo.InvariantCulture);
            VisualTransitionOverrideCombo.SelectedIndex = SceneComposer.VisualTransitionMode(block) switch
            {
                "corte" => 1, "fundido" => 2, "disolvente" => 3, "flash" => 4,
                "barrido" => 5, "plugin" => 6, _ => 0
            };
            AudioDurationModeCombo.SelectedIndex = SceneComposer.StretchAudio(block) ? 1 : 0;
            SfxWaitCheck.IsChecked = SceneComposer.WaitForSound(block);
            CharacterPositionCombo.SelectedIndex = SceneComposer.Position(block) switch { "izquierda" => 0, "derecha" => 2, "auto" => 3, _ => 1 };
            CharacterFramingCombo.SelectedIndex = SceneComposer.FramingPreset(block) switch
            {
                "auto" => 1, "entero" => 2, "medio" => 3, "detalle" => 4, _ => 0
            };
            BackgroundTrimBordersCheck.IsChecked = SceneComposer.AutoTrimBorders(block);
            if (block.Kind == ScriptBlockKind.Transition)
            {
                var transition = SceneComposer.TransitionOptions(block);
                TransitionStyleCombo.SelectedIndex = transition.Style switch
                {
                    "salida" => 1, "cambio" => 2, "cruce" => 3, _ => 0
                };
                TransitionVegasEffectCombo.SelectedIndex = SceneComposer.TransitionEffect(block) switch
                {
                    "disolvente" => 1, "flash" => 2, "barrido" => 3, "plugin" => 4, _ => 0
                };
                var targets = SceneComposer.TransitionTargets(block);
                TransitionBackgroundCheck.IsChecked = (targets & SceneComposer.TargetBackground) != 0;
                TransitionCharacterCheck.IsChecked = (targets & SceneComposer.TargetCharacter) != 0;
                TransitionImageCheck.IsChecked = (targets & SceneComposer.TargetImage) != 0;
                TransitionVideoCheck.IsChecked = (targets & SceneComposer.TargetVideo) != 0;
                TransitionDurationBox.Text = (transition.DurationMs > 0 ? transition.DurationMs : 500)
                    .ToString(CultureInfo.InvariantCulture);
            }
            var (pluginId, pluginPreset) = SceneComposer.VegasPlugin(block);
            VegasPluginCombo.SelectedItem = VegasTransitionCatalog.Find(pluginId);
            if (VegasPluginCombo.SelectedItem is VegasTransitionChoice pluginChoice)
            {
                VegasPresetCombo.ItemsSource = pluginChoice.Presets;
                VegasPresetCombo.SelectedItem = pluginPreset;
            }
            ScriptCharacterCombo.ItemsSource = BuildCharacterChoices();
            ScriptVoiceProfileCombo.ItemsSource = BuildVoiceChoices();
            if (block.Kind == ScriptBlockKind.CharacterHide)
            {
                // A hide of an NPC render whose «Mostrar» has a character by now shows (and saves) that character.
                var target = SceneComposer.HideTarget(_scriptBlocks, block);
                SelectCharacterChoice(target.CharacterId, target.CharacterId is null ? block.AssetId : null);
            }
            else SelectChoiceById(ScriptCharacterCombo, block.CharacterId);
            ScriptVoiceProfileCombo.ItemsSource = BuildVoiceChoices(block.CharacterId, speakerKnown: true);
            SelectVoiceChoice(ScriptVoiceProfileCombo, block.VoiceProfileId, DirectVoiceOf(block));
            LoadEffectControls(block);
            if (RelevantAssetKinds(block.Kind).Count > 0)
            {
                SpriteSubfoldersCheck.IsChecked = StoredSpriteSubfolders(block);
                SpriteNameSearchBox.Text = string.Empty;
                await LoadAssetFoldersForKindAsync(block.Kind, StoredSpriteFolder(block, _scriptAssetCache));
            }
            else ClearSpriteFolderSelection();
            await EnsureAssetsForKindAsync(block.Kind);
            RefreshAssetChoices(block.Kind, block.AssetId);
        }
        finally
        {
            _loadingScriptUi = false;
        }
        UpdateScriptBlockEditorState();
        ScriptStatusText.Text = $"Editando bloque #{block.OrderIndex + 1}.";
    }

    private async void ScriptBlockTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingScriptUi || ScriptBlockTypeCombo.SelectedItem is not ScriptBlockTypeChoice choice)
            return;
        if (_scriptUiReady)
        {
            ResetVisualTransformControls(choice.Kind);
            ResourceVolumeBox.Text = SceneComposer.DefaultVolumePercent(choice.Kind).ToString(CultureInfo.InvariantCulture);
            if (IsEffectBlock(choice.Kind)) ResetEffectControls();
        }
        try
        {
            await LoadAssetFoldersForKindAsync(choice.Kind, null);
            await EnsureAssetsForKindAsync(choice.Kind);
            if (ScriptBlockTypeCombo.SelectedItem is not ScriptBlockTypeChoice current || current.Kind != choice.Kind)
                return;
            RefreshAssetChoices(choice.Kind, null);
            UpdateScriptBlockEditorState();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void ScriptCharacterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingScriptUi && IsSpeechBlock(CurrentEditorKind))
            RefreshSpeakerVoiceChoice();
        if (_loadingScriptUi || ScriptBlockTypeCombo.SelectedItem is not ScriptBlockTypeChoice { Kind: ScriptBlockKind.CharacterShow })
            return;
        var characterId = (ScriptCharacterCombo.SelectedItem as CharacterChoice)?.Id;
        ClearSpriteFolderSelection();
        RefreshAssetChoices(ScriptBlockKind.CharacterShow, null);
        UpdateScriptBlockEditorState();
        try
        {
            await LoadSpriteFoldersAsync(characterId, null);
            await EnsureAssetsForKindAsync(ScriptBlockKind.CharacterShow);
            if (ScriptBlockTypeCombo.SelectedItem is ScriptBlockTypeChoice { Kind: ScriptBlockKind.CharacterShow } &&
                (ScriptCharacterCombo.SelectedItem as CharacterChoice)?.Id == characterId)
            {
                RefreshAssetChoices(ScriptBlockKind.CharacterShow, null);
                UpdateScriptBlockEditorState();
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task EnsureAssetsForKindAsync(ScriptBlockKind kind)
    {
        if (_currentRepository is null)
            return;

        // SelectionChanged and New Block can request the same kind concurrently.
        // Serialize SQLite reads on the project connection, and discard a read if a
        // scan invalidated the catalog while it was awaiting the database.
        await _scriptAssetLoadLock.WaitAsync();
        try
        {
            var queryKey = CurrentAssetQueryKey(kind);
            if (queryKey is null || _activeAssetQueryKey == queryKey) return;
            if (CharacterFolderCombo.SelectedItem is not CharacterFolderChoice folder) return;
            var repository = _currentRepository;
            if (repository is null) return;
            var revision = _scriptAssetCacheRevision;
            var kinds = RelevantAssetKinds(kind);
            var assetFolder = new AssetFolder(folder.SourceId, folder.RelativePath);
            var recursive = SpriteSubfoldersCheck.IsChecked == true;
            var search = SpriteNameSearchBox.Text;
            var page = await Task.Run(() => repository.SearchAssetsInFolderAsync(kinds,
                assetFolder, recursive, search, AssetPageSize + 1));
            if (revision != _scriptAssetCacheRevision || repository != _currentRepository || CurrentAssetQueryKey(kind) != queryKey) return;
            var referencedIds = _scriptBlocks.Where(x => x.AssetId.HasValue).Select(x => x.AssetId!.Value).ToHashSet();
            foreach (var oldAsset in _activeAssetPage)
                if (!referencedIds.Contains(oldAsset.Id)) _scriptAssetCache.Remove(oldAsset.Id);
            _assetPageHasMore = page.Count > AssetPageSize;
            _activeAssetPage = page.Take(AssetPageSize).ToArray();
            foreach (var asset in _activeAssetPage) _scriptAssetCache[asset.Id] = asset;
            _activeAssetQueryKey = queryKey;
        }
        finally
        {
            _scriptAssetLoadLock.Release();
        }
    }

    private void RefreshAssetChoices(ScriptBlockKind kind, Guid? preferredId)
    {
        var relevantKinds = RelevantAssetKinds(kind);
        var choices = new List<AssetChoice> { new(null, "—") };
        if (_activeAssetQueryKey == CurrentAssetQueryKey(kind) && relevantKinds.Count > 0)
            choices.AddRange(_activeAssetPage.Select(x => new AssetChoice(x.Id, $"{x.DisplayName}  [{KindLabel(x.Kind)}]")));
        if (_activeAssetQueryKey == CurrentAssetQueryKey(kind) && preferredId is Guid assignedId &&
            !choices.Any(x => x.Id == assignedId) && _scriptAssetCache.TryGetValue(assignedId, out var assigned) &&
            (relevantKinds.Contains(assigned.Kind) && AssetBelongsToSelectedFolder(assigned) ||
             (SceneComposer.IsVisualBlock(kind) || kind is ScriptBlockKind.SoundEffect or ScriptBlockKind.Music) &&
             DirectorAssetCompatible(kind, assigned)))
            choices.Add(new AssetChoice(assignedId, $"{assigned.DisplayName}  [asignado]"));
        ScriptAssetCombo.ItemsSource = choices;
        SelectChoiceById(ScriptAssetCombo, preferredId);

        var available = choices.Count - 1;
        var folderSelected = CharacterFolderCombo.SelectedItem is CharacterFolderChoice { SourceId: var chosenId } && chosenId != Guid.Empty;
        ScriptAssetLabel.Text = relevantKinds.Count > 0 && !folderSelected
            ? "Elige carpeta:" : relevantKinds.Count == 0 ? "Asset:" : $"Asset ({available:N0}{(_assetPageHasMore && _activeAssetQueryKey == CurrentAssetQueryKey(kind) ? "+" : "")}):";
        ScriptAssetCombo.ToolTip = relevantKinds.Count > 0 && !folderSelected
            ? "Elige una carpeta o pulsa Elegir archivo… para seleccionar un asset catalogado directamente."
            : relevantKinds.Count > 0 && available == 0
                ? "No hay assets en esta carpeta con el filtro actual. Prueba Buscar, Incluir subcarpetas o revisa el catálogo."
            : relevantKinds.Count > 0 && _assetPageHasMore && _activeAssetQueryKey == CurrentAssetQueryKey(kind)
                ? "Se muestran los primeros 150. Usa Buscar o Elegir archivo… para encontrar cualquier otro sin cargar toda la biblioteca."
            : relevantKinds.Count == 0
            ? "Este tipo de bloque no requiere asset."
            : available == 0
                ? "No hay assets disponibles en estas categorías. Si acabas de escanear, pulsa ↻."
                : $"{available:N0} assets disponibles para este tipo de bloque.";
    }

    private static HashSet<AssetKind> RelevantAssetKinds(ScriptBlockKind kind) => kind switch
    {
        ScriptBlockKind.Background => [AssetKind.Background],
        ScriptBlockKind.CharacterShow => [AssetKind.CharacterSprite],
        ScriptBlockKind.Image => [AssetKind.Prop, AssetKind.Meme],
        ScriptBlockKind.SoundEffect => [AssetKind.SoundEffect, AssetKind.Audio],
        ScriptBlockKind.Music => [AssetKind.Music, AssetKind.Audio],
        ScriptBlockKind.Video => [AssetKind.Video],
        _ => []
    };

    private void VideoLayerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_scriptUiReady)
            UpdateScriptBlockEditorState();
    }

    private void ResetVisualTransformControls(ScriptBlockKind kind)
    {
        var (width, height) = kind switch
        {
            ScriptBlockKind.Background => (1280, 720),
            ScriptBlockKind.CharacterShow => (1280, 610),
            ScriptBlockKind.Image => (500, 400),
            _ => (960, 700)
        };
        VisualWidthBox.Text = width.ToString(CultureInfo.InvariantCulture);
        VisualHeightBox.Text = height.ToString(CultureInfo.InvariantCulture);
        VisualOffsetXBox.Text = "0";
        VisualOffsetYBox.Text = "0";
        VisualFlipHorizontalCheck.IsChecked = false;
        VisualFlipVerticalCheck.IsChecked = false;
        VisualChangeDirectionCheck.IsChecked = false;
        VisualRotationBox.Text = "0";
        MotionOffsetXBox.Text = MotionOffsetYBox.Text = MotionRotationBox.Text = MotionDurationBox.Text = "0";
        VisualTransitionOverrideCombo.SelectedIndex = 0;
        SyncBackgroundZoomCombo();
    }

    private string SelectedVisualTransitionMode() => VisualTransitionOverrideCombo.SelectedIndex switch
    {
        1 => "corte", 2 => "fundido", 3 => "disolvente", 4 => "flash", 5 => "barrido", 6 => "plugin", _ => "heredar"
    };

    private bool UsesVegasPlugin() => ScriptBlockTypeCombo.SelectedItem is ScriptBlockTypeChoice choice &&
        (choice.Kind == ScriptBlockKind.Transition && TransitionStyleCombo.SelectedIndex == 3 && TransitionVegasEffectCombo.SelectedIndex == 4 ||
         SceneComposer.IsVisualBlock(choice.Kind) && VisualTransitionOverrideCombo.SelectedIndex == 6);

    private (string Id, string Preset) SelectedVegasPlugin()
    {
        if (!UsesVegasPlugin()) return ("", "");
        var selector = VegasPluginCombo.SelectedItem is VegasTransitionChoice choice ? choice.Id : VegasPluginCombo.Text;
        return VegasTransitionCatalog.TryResolve(selector, VegasPresetCombo.Text, out var plugin, out var preset)
            ? (plugin!.Id, preset) : ("", "");
    }

    private void UpdateVegasPluginPanel()
    {
        if (!_scriptUiReady) return;
        VegasPluginPanel.Visibility = UsesVegasPlugin() ? Visibility.Visible : Visibility.Collapsed;
    }

    private void VegasEffectSelection_Changed(object sender, SelectionChangedEventArgs e) => UpdateVegasPluginPanel();

    private void VegasPluginCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VegasPresetCombo is null) return;
        VegasPresetCombo.ItemsSource = (VegasPluginCombo.SelectedItem as VegasTransitionChoice)?.Presets;
        if (!_loadingScriptUi && VegasPresetCombo.Items.Count > 0)
            VegasPresetCombo.SelectedIndex = 0;
    }

    private void TransitionStyleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TransitionVegasEffectCombo is not null)
            TransitionVegasEffectCombo.IsEnabled = TransitionStyleCombo.SelectedIndex == 3;
        if (TransitionTargetsPanel is not null)
            TransitionTargetsPanel.IsEnabled = TransitionStyleCombo.SelectedIndex == 3;
        UpdateVegasPluginPanel();
    }

    private int SelectedTransitionTargets() =>
        (TransitionBackgroundCheck.IsChecked == true ? SceneComposer.TargetBackground : 0) |
        (TransitionCharacterCheck.IsChecked == true ? SceneComposer.TargetCharacter : 0) |
        (TransitionImageCheck.IsChecked == true ? SceneComposer.TargetImage : 0) |
        (TransitionVideoCheck.IsChecked == true ? SceneComposer.TargetVideo : 0);

    private void UpdateScriptBlockEditorState()
    {
        if (ScriptBlockTypeCombo.SelectedItem is not ScriptBlockTypeChoice choice)
            return;
        var kind = choice.Kind;
        var speech = IsSpeechBlock(kind);
        UpdateCharacterNoneLabel();
        // The first «Voz» choice is named after the speaker (e.g. after switching from «Mostrar personaje»).
        if (speech && ScriptVoiceProfileCombo.Items.Count > 0 && ScriptVoiceProfileCombo.Items[0] is VoiceProfileChoice { Id: null } first &&
            first.Direct is null && first.Name != InheritVoiceLabel((ScriptCharacterCombo.SelectedItem as CharacterChoice)?.Id))
            RefreshSpeakerVoiceChoice();
        var usesCharacter = speech || kind is ScriptBlockKind.CharacterShow or ScriptBlockKind.CharacterHide || EffectUsesCharacter(kind);
        var usesAsset = RelevantAssetKinds(kind).Count > 0;

        ScriptCharacterCombo.IsEnabled = usesCharacter;
        ScriptAssetCombo.IsEnabled = usesAsset && CharacterFolderCombo.SelectedItem is CharacterFolderChoice { SourceId: var folderSource } && folderSource != Guid.Empty;
        BrowseCatalogAssetButton.IsEnabled = usesAsset;
        SpriteFolderPanel.Visibility = usesAsset ? Visibility.Visible : Visibility.Collapsed;
        SelectedAssetPreviewPanel.Visibility = kind is ScriptBlockKind.Background or ScriptBlockKind.CharacterShow or ScriptBlockKind.Image
            ? Visibility.Visible : Visibility.Collapsed;
        AssetFolderUpButton.IsEnabled = usesAsset && kind != ScriptBlockKind.CharacterShow;
        ScriptVoiceProfileCombo.IsEnabled = speech;
        BlockPitchBox.IsEnabled = speech;
        BlockSpeedBox.IsEnabled = speech;
        BlockVolumeBox.IsEnabled = speech;
        SfxWaitCheck.IsEnabled = kind == ScriptBlockKind.SoundEffect;
        CharacterPositionCombo.IsEnabled = (kind is ScriptBlockKind.CharacterShow or ScriptBlockKind.Image) ||
            (kind == ScriptBlockKind.Video && VideoLayerCombo.SelectedIndex != 0);
        VisualTimingPanel.Visibility = kind is ScriptBlockKind.Narration or ScriptBlockKind.Background or ScriptBlockKind.CharacterShow or ScriptBlockKind.Image or ScriptBlockKind.Video or ScriptBlockKind.SoundEffect or ScriptBlockKind.Music
            ? Visibility.Visible : Visibility.Collapsed;
        VisualTimingHint.Text = kind switch
        {
            ScriptBlockKind.Video => "Vacío = duración del video; no retrasa la voz",
            ScriptBlockKind.Narration => "Vacío = duración original de la narración",
            ScriptBlockKind.Music => "Vacío = hasta el final de la escena",
            ScriptBlockKind.SoundEffect => "Vacío = duración original del SFX",
            _ => "Vacío = automático; no retrasa la voz"
        };
        AudioOptionsPanel.Visibility = kind is ScriptBlockKind.Narration or ScriptBlockKind.SoundEffect or ScriptBlockKind.Music ? Visibility.Visible : Visibility.Collapsed;
        ResourceVolumePanel.Visibility = kind is ScriptBlockKind.SoundEffect or ScriptBlockKind.Music or ScriptBlockKind.Video
            ? Visibility.Visible : Visibility.Collapsed;
        ResourceVolumeHint.Text = kind == ScriptBlockKind.Video
            ? "0 = video mudo; más de 0 activa su audio si lo tiene" :
            kind == ScriptBlockKind.Music ? "Predeterminado: 25%; 100 = nivel original" :
            "Predeterminado: 100%; 0 = silencio";
        VideoOptionsPanel.Visibility = kind == ScriptBlockKind.Video ? Visibility.Visible : Visibility.Collapsed;
        var visualKind = kind is ScriptBlockKind.Background or ScriptBlockKind.CharacterShow or ScriptBlockKind.Image or ScriptBlockKind.Video;
        VisualTransformPanel.Visibility = visualKind ? Visibility.Visible : Visibility.Collapsed;
        BackgroundZoomPanel.Visibility = kind == ScriptBlockKind.Background ? Visibility.Visible : Visibility.Collapsed;
        VisualMotionPanel.Visibility = visualKind ? Visibility.Visible : Visibility.Collapsed;
        VisualTransitionPanel.Visibility = visualKind ? Visibility.Visible : Visibility.Collapsed;
        UpdateVegasPluginPanel();
        CharacterFramingPanel.Visibility = kind == ScriptBlockKind.CharacterShow ? Visibility.Visible : Visibility.Collapsed;
        BackgroundBordersPanel.Visibility = kind == ScriptBlockKind.Background ? Visibility.Visible : Visibility.Collapsed;
        TransitionOptionsPanel.Visibility = kind == ScriptBlockKind.Transition ? Visibility.Visible : Visibility.Collapsed;
        TransitionTargetsPanel.Visibility = kind == ScriptBlockKind.Transition ? Visibility.Visible : Visibility.Collapsed;
        UpdateEffectPanels(kind);
        TransitionTargetsPanel.IsEnabled = kind == ScriptBlockKind.Transition && TransitionStyleCombo.SelectedIndex == 3;
        if (TransitionVegasEffectCombo is not null)
            TransitionVegasEffectCombo.IsEnabled = kind == ScriptBlockKind.Transition && TransitionStyleCombo.SelectedIndex == 3;
        var editableSize = visualKind && (kind != ScriptBlockKind.Video || VideoLayerCombo.SelectedIndex != 0);
        VisualWidthBox.IsEnabled = VisualHeightBox.IsEnabled = VisualOffsetXBox.IsEnabled = VisualOffsetYBox.IsEnabled = editableSize;
        PreviewAssetButton.IsEnabled = kind is ScriptBlockKind.SoundEffect or ScriptBlockKind.Music;

        ScriptBlockHintText.Text = kind switch
        {
            ScriptBlockKind.Dialogue => "Diálogo: personaje + texto. «NPC» = alguien sin personaje registrado (voz en off); " +
                "«Voz» elige con qué voz TTS habla (la lista se edita en Voice Lab). Overrides vacíos = los de la voz.",
            ScriptBlockKind.Narration => "Narración: voz con duración opcional; el WAV generado se conserva para repetirlo o ajustar su velocidad.",
            ScriptBlockKind.Pause => "Pausa: usa 'Pausa ms' como duración del silencio.",
            ScriptBlockKind.Background => "Fondo: cubre el cuadro; detecta bordes planos integrados en la imagen y permite conservarlos si son parte del dibujo.",
            ScriptBlockKind.CharacterShow => "Mostrar: posición inicial y animación en X/Y/giro; permanece hasta ocultarlo, sustituirlo o agotar su duración.",
            ScriptBlockKind.Image => "Imagen / prop: posición inicial y animación en X/Y/giro; permanece hasta otra imagen o agotar su duración.",
            ScriptBlockKind.CharacterHide => "Ocultar personaje: elige el personaje o un render mostrado sin personaje («NPC · recurso»). " +
                "Si luego ese render recibe personaje, este bloque oculta al personaje.",
            ScriptBlockKind.SoundEffect => "SFX: duración opcional; recorta/repite o ajusta velocidad sin cambiar el tono. Esperar desplaza el bloque siguiente.",
            ScriptBlockKind.Music => "Música: duración opcional; si queda vacía, suena hasta el final de la escena al 25 %.",
            ScriptBlockKind.Video => "Video: tamaño máximo y desplazamiento conservan la proporción; cambiar dirección voltea la imagen en su sitio; invertir horizontal la refleja al otro lado del cuadro. Fondo fijo llena el cuadro.",
            ScriptBlockKind.TextOverlay => "Texto en pantalla: queda guardado en el guion, pero todavía no se dibuja en el MP4.",
            ScriptBlockKind.Transition => "Entrada/salida: fundido a negro. Cambio: corte a mitad. Cruce: solapa solo los tipos marcados; los demás medios nuevos cambian por corte a mitad.",
            ScriptBlockKind.Comment => "Comentario de dirección: queda guardado y no aparece en el MP4.",
            ScriptBlockKind.Cinema => "Cine: barras negras arriba y abajo (en VEGAS, Cortador de galletas). Siguen en las escenas " +
                "siguientes hasta un bloque Cine «Quitar». Con «Personajes elegidos», lo que esté delante de ellos hereda el efecto " +
                "y sin ellos en pantalla no hay barras. Equivale a la línea [CINE] del Director (prompt).",
            ScriptBlockKind.Blur => "Desenfoque: VEGAS «Desenfoque gaussiano» (Suavizar 0,02 · Ligero 0,01 · 0 = nítido), animando " +
                "horizontal y vertical a la vez. Dura hasta otro desenfoque del mismo objetivo. Equivale a [DESENFOQUE] del Director (prompt).",
            ScriptBlockKind.Gesture => "Gesto: balanceo (se inclina y rebota) y/o rebote (se estira) desde el eje, con keyframes «Rápido» " +
                "de Pan/Crop en VEGAS. «Quien habla» lo repite en cada línea hasta «Dejar de gesticular». Equivale a [GESTO] del Director (prompt).",
            ScriptBlockKind.Camera => "Cámara: encuadra al personaje (y lo sigue), a quien habla o un punto; «Plano general» vuelve " +
                "al cuadro completo. Movimiento 0 = corte. Equivale a la línea [CAMARA] del Director (prompt).",
            _ => "Bloque estructurado: el timeline ya conserva su orden y contexto."
        };
    }

    private async void SaveBlock_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is null || ScenesList.SelectedItem is not SceneScriptRow sceneRow)
            return;
        if (ScriptBlockTypeCombo.SelectedItem is not ScriptBlockTypeChoice typeChoice)
            return;

        var kind = typeChoice.Kind;
        var characterId = (ScriptCharacterCombo.SelectedItem as CharacterChoice)?.Id;
        var assetId = (ScriptAssetCombo.SelectedItem as AssetChoice)?.Id;
        if (kind == ScriptBlockKind.CharacterHide)
        {
            // Hide a character, or an NPC render by its asset (1.4.1).
            assetId = characterId is null ? (ScriptCharacterCombo.SelectedItem as CharacterChoice)?.RenderAssetId : null;
            if (characterId is null && assetId is null)
            {
                MessageBox.Show(this, "Elige el personaje o el render NPC que se oculta.", "Guion", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }
        var selectedVoice = ScriptVoiceProfileCombo.SelectedItem as VoiceProfileChoice;
        var voiceProfileId = selectedVoice?.Id;
        var text = ScriptBlockTextBox.Text.Trim();

        var previous = _editingScriptBlockId is Guid editId ? _scriptBlocks.FirstOrDefault(x => x.Id == editId) : null;
        if (IsSpeechBlock(kind) && string.IsNullOrWhiteSpace(text) &&
            !(previous is not null && IsImportedVoice(previous)))
        {
            MessageBox.Show(this, "El bloque de voz necesita texto.", "Guion", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (RelevantAssetKinds(kind).Count > 0 && assetId is null)
        {
            MessageBox.Show(this, "Selecciona un asset para este tipo de bloque.", "Guion", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (SceneComposer.IsVisualBlock(kind) && assetId is Guid selectedAssetId &&
            _scriptAssetCache.TryGetValue(selectedAssetId, out var visualAsset) &&
            !DirectorAssetCompatible(kind, visualAsset))
        {
            MessageBox.Show(this, "El formato de ese archivo no es compatible con el bloque visual elegido.",
                "Guion", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!TryNullableInt(BlockPitchBox.Text, out var pitch) || !TryNullableInt(BlockSpeedBox.Text, out var speed) || !TryNullableInt(BlockVolumeBox.Text, out var volume))
        {
            MessageBox.Show(this, "Pitch, velocidad y volumen deben estar vacíos (heredar) o contener enteros.", "Guion", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!int.TryParse(BlockPauseBox.Text.Trim(), out var pauseMs) || pauseMs < 0)
        {
            MessageBox.Show(this, "La pausa debe ser un entero de 0 ms o más.", "Guion", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (kind == ScriptBlockKind.Pause && pauseMs == 0)
        {
            MessageBox.Show(this, "Pon una duración mayor que 0 ms para la pausa.", "Guion", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (kind == ScriptBlockKind.Transition &&
            (!long.TryParse(TransitionDurationBox.Text.Trim(), out var transitionMs) || transitionMs is < 80 or > 10_000))
        {
            MessageBox.Show(this, "El fundido debe durar entre 80 y 10.000 ms.", "Guion",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (kind == ScriptBlockKind.Transition && TransitionStyleCombo.SelectedIndex == 3 &&
            SelectedTransitionTargets() == 0)
        {
            MessageBox.Show(this, "Selecciona al menos una capa para el cruce.", "Guion",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (kind is (ScriptBlockKind.Narration or ScriptBlockKind.Background or ScriptBlockKind.CharacterShow or ScriptBlockKind.Image or ScriptBlockKind.Video or ScriptBlockKind.SoundEffect or ScriptBlockKind.Music) &&
            !string.IsNullOrWhiteSpace(VisualDurationBox.Text) &&
            (!long.TryParse(VisualDurationBox.Text.Trim(), out var visualDuration) || visualDuration is < 1 or > 86_400_000))
        {
            MessageBox.Show(this, "La duración debe estar vacía (automática) o ser de 1 a 86.400.000 ms.", "Guion", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (kind is (ScriptBlockKind.SoundEffect or ScriptBlockKind.Music or ScriptBlockKind.Video) &&
            (!int.TryParse(ResourceVolumeBox.Text.Trim(), out var resourceVolume) || resourceVolume is < 0 or > 200))
        {
            MessageBox.Show(this, "El volumen del recurso debe ser un entero entre 0 y 200%.",
                "Guion", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var keyColor = VideoKeyColorBox.Text.Trim().TrimStart('#');
        if (kind == ScriptBlockKind.Video && VideoGreenScreenCheck.IsChecked == true &&
            (keyColor.Length != 6 || !keyColor.All(Uri.IsHexDigit) ||
             !double.TryParse(VideoKeyToleranceBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var tolerance) || tolerance is < 0.01 or > 1))
        {
            MessageBox.Show(this, "Pantalla verde: usa un color hexadecimal RRGGBB y una tolerancia entre 0.01 y 1.00 (con punto).", "Guion", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var sizeLimit = BlockDefaults.VisualMaxBox(kind);
        if (kind is (ScriptBlockKind.Background or ScriptBlockKind.CharacterShow or ScriptBlockKind.Image or ScriptBlockKind.Video) &&
            (!int.TryParse(VisualWidthBox.Text.Trim(), out var maxWidth) || maxWidth < 64 || maxWidth > sizeLimit.Width ||
             !int.TryParse(VisualHeightBox.Text.Trim(), out var maxHeight) || maxHeight < 64 || maxHeight > sizeLimit.Height ||
             !int.TryParse(VisualOffsetXBox.Text.Trim(), out var offsetX) || offsetX is < -1280 or > 1280 ||
             !int.TryParse(VisualOffsetYBox.Text.Trim(), out var offsetY) || offsetY is < -720 or > 720 ||
             !double.TryParse(VisualRotationBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var angle) ||
             !double.IsFinite(angle) || angle is < -180 or > 180 ||
             !int.TryParse(MotionOffsetXBox.Text.Trim(), out var moveX) || moveX is < -1280 or > 1280 ||
             !int.TryParse(MotionOffsetYBox.Text.Trim(), out var moveY) || moveY is < -720 or > 720 ||
             !double.TryParse(MotionRotationBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var spin) ||
             !double.IsFinite(spin) || spin is < -720 or > 720 ||
             !long.TryParse(MotionDurationBox.Text.Trim(), out var motionMs) || motionMs is < 0 or > 86_400_000))
        {
            MessageBox.Show(this, $"Visual: tamaño 64–{sizeLimit.Width} × 64–{sizeLimit.Height}" +
                (kind == ScriptBlockKind.Background ? " (fondo con zoom hasta 300 %)" : "") +
                "; X ±1280, Y ±720, rotación ±180°. Animar ΔX ±1280, ΔY ±720, giro ±720° y duración 0–86400000 ms.",
                "Guion", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (UsesVegasPlugin() && SelectedVegasPlugin().Id.Length == 0)
        {
            MessageBox.Show(this, "Elige un plugin y un preset que figuren en el catálogo de transiciones de VEGAS.",
                "Guion", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var effectParameters = "{}";
        if (IsEffectBlock(kind))
        {
            if (!EffectUsesCharacter(kind)) characterId = null;
            if (!TryEffectParameters(kind, previous, characterId, out effectParameters, out var effectError))
            {
                MessageBox.Show(this, effectError ?? "", "Guion", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        var order = previous?.OrderIndex ?? _scriptBlocks.Count;
        var speechChanged = previous is null || previous.Kind != kind || previous.CharacterId != characterId || previous.Text != text ||
                            previous.VoiceProfileId != voiceProfileId || previous.VoicePitchOverride != pitch ||
                            previous.VoiceSpeedOverride != speed || previous.VoiceVolumeOverride != volume ||
                            !SameVoice(previous is null ? null : DirectVoiceOf(previous), selectedVoice?.Direct);
        var retainImported = previous is not null && IsImportedVoice(previous) && IsSpeechBlock(kind);
        // A recorded take keeps its WAV when only its speaker, text or Dialogue/Narration kind change;
        // replacing it with TTS is only done on explicit confirmation below.
        var clearVoice = !retainImported && (previous is not null && previous.Kind != kind || speechChanged);

        var block = new SceneScriptBlock(
            previous?.Id ?? Guid.NewGuid(),
            sceneRow.Scene.Id,
            order,
            kind,
            characterId,
            text,
            assetId,
            voiceProfileId,
            pitch,
            speed,
            volume,
            pauseMs,
            kind == ScriptBlockKind.CharacterShow ? SerializeSpriteParameters()
                : kind == ScriptBlockKind.Transition ? new BlockParameters
                {
                    TransitionStyle = TransitionStyleCombo.SelectedIndex switch
                    {
                        1 => "salida", 2 => "cambio", 3 => "cruce", _ => "entrada"
                    },
                    TransitionDurationMs = long.Parse(TransitionDurationBox.Text.Trim(), CultureInfo.InvariantCulture),
                    TransitionTargets = TransitionStyleCombo.SelectedIndex == 3 ? SelectedTransitionTargets() : SceneComposer.TargetAll,
                    VegasEffect = TransitionStyleCombo.SelectedIndex == 3 ? TransitionVegasEffectCombo.SelectedIndex switch
                    {
                        1 => "disolvente", 2 => "flash", 3 => "barrido", 4 => "plugin", _ => "ninguno"
                    } : "ninguno",
                    VegasPluginId = SelectedVegasPlugin().Id is { Length: > 0 } pluginId ? pluginId : null,
                    VegasPluginPreset = SelectedVegasPlugin().Preset is { Length: > 0 } pluginPreset ? pluginPreset : null
                }.ToJson()
                : IsEffectBlock(kind) ? effectParameters
                : IsSpeechBlock(kind) ? SerializeSpeechParameters(previous, kind)
                : RelevantAssetKinds(kind).Count > 0 ? SerializeResourceParameters(kind)
                : previous?.Kind == kind ? previous.ParametersJson : "{}",
            previous?.StartOffsetMs,
            clearVoice ? null : previous?.GeneratedAudioPath,
            clearVoice ? null : previous?.GeneratedAudioHash,
            clearVoice || kind == ScriptBlockKind.SoundEffect && previous?.AssetId != assetId ? null : previous?.GeneratedDurationMs);

        // A recorded take is never replaced silently. When its text, speaker or voice really
        // changes and the take has its own TTS voice, offer (default: keep the recording).
        var regenerate = false;
        if (retainImported && speechChanged && _ttsBridge.BridgePath is not null &&
            ResolveRecordedTakeVoice(block, _voiceProfiles.ToDictionary(x => x.Id)) is { } ttsProfile &&
            !string.IsNullOrWhiteSpace(text))
            regenerate = MessageBox.Show(this,
                $"Este bloque usa una voz GRABADA y cambiaste su texto, personaje o voz.\n\n" +
                $"¿Regenerarlo con TTS («{ttsProfile.Name}»)?\n\nNo = conservar la grabación y solo guardar los cambios " +
                "(elige No si solo corregiste la transcripción).",
                "Voz grabada", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

        try
        {
            // An NPC render that now gets a character: the «Ocultar» blocks that pointed at it by its resource are
            // tied to the character from now on (1.4.1).
            var rebind = previous is { Kind: ScriptBlockKind.CharacterShow, CharacterId: null } && kind == ScriptBlockKind.CharacterShow &&
                characterId is Guid newOwner
                ? _scriptBlocks.Where(x => x.Kind == ScriptBlockKind.CharacterHide && x.CharacterId is null &&
                    SceneComposer.HideTarget(_scriptBlocks, x).RenderBlockId == previous.Id)
                    .Select(x => x with { CharacterId = newOwner, AssetId = null }).ToArray()
                : [];
            await _currentRepository.UpsertSceneScriptBlockAsync(block);
            foreach (var hide in rebind) await _currentRepository.UpsertSceneScriptBlockAsync(hide);
            _editingScriptBlockId = block.Id;
            await LoadBlocksAsync(sceneRow.Scene.Id, block.Id);
            await RefreshSceneTimingAsync(_scriptBlocks, block.Id);
            ScriptStatusText.Text = $"Bloque #{block.OrderIndex + 1} guardado; tiempos actualizados." +
                (BackgroundZoomAdvice(kind, SceneComposer.VisualTransform(block)) is { } zoomAdvice ? " " + zoomAdvice : "");
            if (regenerate && _scriptBlocks.FirstOrDefault(x => x.Id == block.Id) is { } saved &&
                ScenesList.SelectedItem is SceneScriptRow current && current.Scene.Id == sceneRow.Scene.Id)
                await RegenerateRecordedTakeAsync(current, saved, askFirst: false);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async void DeleteBlock_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is null || ScenesList.SelectedItem is not SceneScriptRow sceneRow)
            return;
        var selectedIds = ScriptBlocksGrid.SelectedItems.Cast<ScriptBlockRow>()
            .Select(row => row.Block.Id).ToHashSet();
        if (selectedIds.Count == 0) return;
        try
        {
            var remaining = _scriptBlocks.Where(x => !selectedIds.Contains(x.Id)).OrderBy(x => x.OrderIndex)
                .Select((x, index) => x with { OrderIndex = index }).ToArray();
            await _currentRepository.ReplaceSceneScriptBlocksAsync(sceneRow.Scene.Id, remaining);
            await LoadBlocksAsync(sceneRow.Scene.Id);
            await RefreshSceneTimingAsync(_scriptBlocks);
            ScriptStatusText.Text = selectedIds.Count == 1 ? "Bloque eliminado." : $"{selectedIds.Count} bloques eliminados.";
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async void MoveBlockUp_Click(object sender, RoutedEventArgs e) => await MoveSelectedBlockAsync(-1);
    private async void MoveBlockDown_Click(object sender, RoutedEventArgs e) => await MoveSelectedBlockAsync(1);

    private async Task MoveSelectedBlockAsync(int delta)
    {
        if (_currentRepository is null || ScenesList.SelectedItem is not SceneScriptRow sceneRow)
            return;
        var selectedIds = ScriptBlocksGrid.SelectedItems.Cast<ScriptBlockRow>()
            .Select(row => row.Block.Id).ToHashSet();
        if (selectedIds.Count == 0) return;
        var list = _scriptBlocks.OrderBy(x => x.OrderIndex).ToList();
        var moved = false;
        if (delta < 0)
        {
            for (var i = 1; i < list.Count; i++)
            {
                if (!selectedIds.Contains(list[i].Id) || selectedIds.Contains(list[i - 1].Id)) continue;
                (list[i], list[i - 1]) = (list[i - 1], list[i]);
                moved = true;
            }
        }
        else
        {
            for (var i = list.Count - 2; i >= 0; i--)
            {
                if (!selectedIds.Contains(list[i].Id) || selectedIds.Contains(list[i + 1].Id)) continue;
                (list[i], list[i + 1]) = (list[i + 1], list[i]);
                moved = true;
            }
        }
        if (!moved) return;
        await ApplyBlockOrderAsync(sceneRow, list, selectedIds, $"{selectedIds.Count} bloque(s) movidos una posición.");
    }

    private async void GenerateSceneVoices_Click(object sender, RoutedEventArgs e) => await GenerateSceneVoicesAsync();

    private async Task<bool> GenerateSceneVoicesAsync()
    {
        if (_currentRepository is null || ScenesList.SelectedItem is not SceneScriptRow sceneRow || EpisodesList.SelectedItem is not EpisodeScriptRow episodeRow)
            return false;
        if (_scriptBlocks.Count == 0)
        {
            MessageBox.Show(this, "La escena no tiene bloques.", "Guion", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (_sceneVoiceGenerationCancellation is not null) return false;
        _sceneVoiceGenerationCancellation = new CancellationTokenSource();
        GenerateSceneVoicesButton.IsEnabled = false;
        ToggleScenePreviewButton.IsEnabled = false;
        var cancellationToken = _sceneVoiceGenerationCancellation.Token;
        var errors = new List<string>();
        var generated = 0;
        var reused = 0;
        long clock = 0;
        SceneTransition? pendingVisual = null;
        var updatedBlocks = new List<SceneScriptBlock>(_scriptBlocks.Count);

        try
        {
            var characterMap = _characters.ToDictionary(x => x.Id);
            var profileMap = _voiceProfiles.ToDictionary(x => x.Id);
            var ordered = _scriptBlocks.OrderBy(x => x.OrderIndex).ToArray();
            var voiceClock = System.Diagnostics.Stopwatch.StartNew();
            var prefetch = await PrefetchSceneVoicesAsync(_currentRepository.ProjectRoot, ordered, characterMap, profileMap,
                new Progress<string>(text => ScriptStatusText.Text = text), cancellationToken);
            var prefetched = new HashSet<string>(prefetch.Generated, StringComparer.Ordinal);
            for (var i = 0; i < ordered.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var start = pendingVisual is not null && SceneComposer.IsVisualBlock(ordered[i].Kind)
                    ? SceneComposer.VisualStart(pendingVisual, ordered[i])
                    : pendingVisual is { Style: "cambio" or "cruce" } && ordered[i].Kind == ScriptBlockKind.CharacterHide
                        ? SceneComposer.HideStart(pendingVisual) : clock;
                var block = ordered[i] with { StartOffsetMs = start };
                ScriptStatusText.Text = $"Procesando bloque {i + 1}/{ordered.Length}…";

                if (IsSpeechBlock(block.Kind) && IsImportedVoice(block))
                {
                    var importedPath = ResolveGeneratedPath(block.GeneratedAudioPath);
                    if (importedPath is null || !IsValidWave(importedPath))
                        errors.Add($"#{i + 1}: falta el WAV importado o no es válido.");
                    else
                    {
                        var duration = GetWaveDurationMs(importedPath);
                        block = block with { GeneratedDurationMs = duration };
                        clock += block.Kind == ScriptBlockKind.Narration ? SceneComposer.AudioDuration(block) ?? duration : duration;
                        reused++;
                    }
                }
                else if (IsSpeechBlock(block.Kind) && !string.IsNullOrWhiteSpace(block.Text))
                {
                    var profile = ResolveVoiceProfile(block, characterMap, profileMap);
                    if (profile is null)
                    {
                        errors.Add($"#{i + 1}: no tiene perfil de voz resoluble.");
                        block = block with { GeneratedAudioPath = null, GeneratedAudioHash = null, GeneratedDurationMs = null };
                    }
                    else
                    {
                        var effectivePitch = block.VoicePitchOverride ?? profile.Pitch;
                        var effectiveSpeed = block.VoiceSpeedOverride ?? profile.Speed;
                        var effectiveVolume = block.VoiceVolumeOverride ?? profile.Volume;
                        var hash = ComputeVoiceCacheHash(profile, block.Text, effectivePitch, effectiveSpeed, effectiveVolume);
                        var existingPath = ResolveGeneratedPath(block.GeneratedAudioPath);
                        string audioPath;

                        if (block.GeneratedAudioHash == hash && existingPath is not null && IsValidWave(existingPath))
                        {
                            audioPath = existingPath;
                            reused++;
                        }
                        else
                        {
                            var characterName = block.CharacterId is Guid characterId && characterMap.TryGetValue(characterId, out var character)
                                ? character.Name
                                : "Narrador";
                            var folder = Path.Combine(_currentRepository.ProjectRoot, "generated", "voices",
                                $"episode_{episodeRow.Episode.Number:000}", $"scene_{sceneRow.Scene.Index:000}");
                            Directory.CreateDirectory(folder);
                            audioPath = Path.Combine(folder, $"{block.OrderIndex + 1:0000}_{SafeFilePart(characterName)}_{hash[..10]}.wav");
                            // Same voice + prosody + text = same audio: reuse it from any scene or
                            // earlier draft instead of calling the TTS bridge again.
                            var cachedVoice = VoiceCache.Find(_currentRepository.ProjectRoot, hash, IsValidWave);
                            if (cachedVoice is not null)
                            {
                                if (!string.Equals(Path.GetFullPath(cachedVoice), Path.GetFullPath(audioPath), StringComparison.OrdinalIgnoreCase))
                                    File.Copy(cachedVoice, audioPath, true);
                                // Synthesized moments ago by the parallel phase: it counts as generated.
                                if (prefetched.Remove(hash)) generated++;
                                else reused++;
                            }
                            else
                            {
                                await _ttsBridge.SynthesizeAsync(new TtsPreviewRequest(
                                    profile.ProviderKey,
                                    profile.VoiceId,
                                    block.Text,
                                    audioPath,
                                    effectivePitch,
                                    effectiveSpeed,
                                    effectiveVolume,
                                    profile.SampleRate), cancellationToken);
                                generated++;
                            }
                        }
                        VoiceCache.Store(_currentRepository.ProjectRoot, hash, audioPath);

                        var duration = GetWaveDurationMs(audioPath);
                        block = block with
                        {
                            GeneratedAudioPath = Path.GetRelativePath(_currentRepository.ProjectRoot, audioPath).Replace('\\', '/'),
                            GeneratedAudioHash = hash,
                            GeneratedDurationMs = duration
                        };
                        clock += block.Kind == ScriptBlockKind.Narration ? SceneComposer.AudioDuration(block) ?? duration : duration;
                    }
                }

                if (block.Kind == ScriptBlockKind.SoundEffect && block.AssetId is Guid assetId)
                {
                    if (!_scriptAssetCache.TryGetValue(assetId, out var asset)) errors.Add($"#{i + 1}: SFX no encontrado.");
                    else
                    {
                        var path = await ResolveAssetPathAsync(asset);
                        if (path is null || !File.Exists(path)) errors.Add($"#{i + 1}: archivo SFX no disponible.");
                        else
                        {
                            var duration = await SceneComposer.ProbeDurationAsync(path, cancellationToken);
                            block = block with { GeneratedDurationMs = duration };
                            if (SceneComposer.WaitForSound(block)) clock += SceneComposer.AudioDuration(block) ?? duration;
                        }
                    }
                }
                if (block.Kind == ScriptBlockKind.Transition)
                {
                    var (style, transitionMs) = SceneComposer.TransitionOptions(block);
                    pendingVisual = transitionMs > 0 && (style is "cambio" or "cruce")
                        ? new SceneTransition(clock, transitionMs, style, SceneComposer.TransitionEffect(block),
                            SceneComposer.TransitionTargets(block)) : null;
                    clock += transitionMs;
                }
                else if (!SceneComposer.IsVisualBlock(block.Kind) &&
                         block.Kind is not (ScriptBlockKind.Comment or ScriptBlockKind.CharacterHide or ScriptBlockKind.Camera or ScriptBlockKind.Cinema or ScriptBlockKind.Gesture or ScriptBlockKind.Blur))
                    pendingVisual = null;
                clock += Math.Max(0, block.PauseAfterMs);
                if (block.PauseAfterMs > 0) pendingVisual = null;
                updatedBlocks.Add(block);
            }

            AiDiagnostics.Note($"tts: escena «{sceneRow.Scene.Title}» {ordered.Length} bloques | {generated} generadas, {reused} reutilizadas, " +
                $"{errors.Count} errores | {voiceClock.ElapsedMilliseconds:N0} ms (fase paralela {prefetch.ElapsedMs:N0} ms, " +
                $"{prefetch.Generated.Count} voces, {prefetch.Parallelism} a la vez)");
            await _currentRepository.ReplaceSceneScriptBlocksAsync(sceneRow.Scene.Id, updatedBlocks);
            var lastSound = updatedBlocks.Where(x => x.Kind == ScriptBlockKind.SoundEffect ||
                (x.Kind == ScriptBlockKind.Music && SceneComposer.AudioDuration(x).HasValue))
                .Select(x => (x.StartOffsetMs ?? 0) + (SceneComposer.AudioDuration(x) ?? x.GeneratedDurationMs ?? 0)).DefaultIfEmpty(0).Max();
            long lastVisual = 0;
            foreach (var block in updatedBlocks) lastVisual = Math.Max(lastVisual, await VisualEndMsAsync(block, updatedBlocks, cancellationToken));
            var updatedScene = sceneRow.Scene with { DurationMs = Math.Max(clock, Math.Max(lastSound, lastVisual)) };
            await _currentRepository.UpsertSceneAsync(updatedScene);
            await RefreshScriptAsync(episodeRow.Episode.Id, updatedScene.Id, (ScriptBlocksGrid.SelectedItem as ScriptBlockRow)?.Block.Id);

            ScriptStatusText.Text = errors.Count == 0
                ? $"Voces listas: {generated} generadas, {reused} reutilizadas."
                : $"Terminó con {errors.Count} bloque(s) sin voz. {generated} generadas, {reused} reutilizadas.";
            if (errors.Count > 0)
                MessageBox.Show(this, string.Join(Environment.NewLine, errors.Take(12)), "Generación de voces", MessageBoxButton.OK, MessageBoxImage.Warning);
            return errors.Count == 0;
        }
        catch (OperationCanceledException)
        {
            ScriptStatusText.Text = "Generación cancelada.";
            return false;
        }
        catch (Exception ex)
        {
            ScriptStatusText.Text = ex.Message;
            ShowError(ex);
            return false;
        }
        finally
        {
            GenerateSceneVoicesButton.IsEnabled = true;
            ToggleScenePreviewButton.IsEnabled = !_previewPreparing;
            _sceneVoiceGenerationCancellation?.Dispose();
            _sceneVoiceGenerationCancellation = null;
        }
    }

    private static VoiceProfile? ResolveVoiceProfile(
        SceneScriptBlock block,
        IReadOnlyDictionary<Guid, CharacterDefinition> characterMap,
        IReadOnlyDictionary<Guid, VoiceProfile> profileMap)
    {
        if (block.VoiceProfileId is Guid explicitId && profileMap.TryGetValue(explicitId, out var explicitProfile))
            return LineProfile(explicitProfile, DirectVoiceOf(block));
        if (DirectVoiceOf(block) is { } directVoice)
            return DirectProfile(directVoice);
        if (block.CharacterId is Guid characterId && characterMap.TryGetValue(characterId, out var character) &&
            character.DefaultVoiceProfileId is Guid defaultId && profileMap.TryGetValue(defaultId, out var defaultProfile))
            return LineProfile(defaultProfile, null);
        // 1.2.0: nobody is left without a voice. NPCs, the narrator and characters without a profile speak with
        // the NPC profile (Voice Lab) instead of failing with «no tiene perfil de voz».
        return profileMap.TryGetValue(NpcProfileId, out var npc) ? NpcVoice(npc, null) : null;
    }

    /// <summary>TTS voice for a RECORDED take: only the voice assigned to the take itself (profile or
    /// direct voice). The character is just a label here, so its Voice Lab default is ignored.</summary>
    private static VoiceProfile? ResolveRecordedTakeVoice(SceneScriptBlock block, IReadOnlyDictionary<Guid, VoiceProfile> profileMap)
    {
        if (block.VoiceProfileId is Guid explicitId && profileMap.TryGetValue(explicitId, out var explicitProfile))
            return LineProfile(explicitProfile, DirectVoiceOf(block));
        return DirectVoiceOf(block) is { } directVoice ? DirectProfile(directVoice) : null;
    }

    private async void PlayGeneratedLine_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is null || ScriptBlocksGrid.SelectedItem is not ScriptBlockRow row || string.IsNullOrWhiteSpace(row.Block.GeneratedAudioPath))
        {
            ScriptStatusText.Text = "Ese bloque todavía no tiene audio generado.";
            return;
        }
        var path = ResolveGeneratedPath(row.Block.GeneratedAudioPath);
        if (path is null || !File.Exists(path))
        {
            ScriptStatusText.Text = "El WAV generado ya no existe; vuelve a generar la escena.";
            return;
        }

        try
        {
            _scriptAudioPlayer.Stop();
            _scriptAudioPlayer.Close();
            _scriptAudioPlayer.Volume = 1;
            _scriptAudioPlayer.Open(new Uri(path, UriKind.Absolute));
            _scriptAudioPlayer.Play();
            ScriptStatusText.Text = $"Reproduciendo bloque #{row.Block.OrderIndex + 1}.";
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private string? ResolveGeneratedPath(string? relativeOrAbsolute)
    {
        if (_currentRepository is null || string.IsNullOrWhiteSpace(relativeOrAbsolute))
            return null;
        return Path.IsPathRooted(relativeOrAbsolute)
            ? relativeOrAbsolute
            : Path.GetFullPath(Path.Combine(_currentRepository.ProjectRoot, relativeOrAbsolute.Replace('/', Path.DirectorySeparatorChar)));
    }

    private void UpdateScriptSceneSummary()
    {
        if (ScenesList.SelectedItem is not SceneScriptRow sceneRow)
        {
            ScriptSceneSummaryText.Text = "Selecciona una escena.";
            ScriptDurationText.Text = "Duración: 00:00.000";
            return;
        }

        var speechCount = _scriptBlocks.Count(x => IsSpeechBlock(x.Kind));
        ScriptSceneSummaryText.Text = $"{sceneRow.Scene.Title} · {_scriptBlocks.Count} bloques · {speechCount} líneas de voz";
        var duration = sceneRow.Scene.DurationMs > 0
            ? sceneRow.Scene.DurationMs
            : _scriptBlocks.Sum(x => (x.GeneratedDurationMs ?? 0) + Math.Max(0, x.PauseAfterMs) +
                (x.Kind == ScriptBlockKind.Transition ? SceneComposer.TransitionOptions(x).DurationMs : 0));
        ScriptDurationText.Text = $"Duración: {FormatTimelineTime(duration)}";
    }

    private void ClearBlockEditorForm()
    {
        _editingScriptBlockId = null;
        ScriptBlocksGrid.SelectedItem = null;
        ScriptBlockTextBox.Text = string.Empty;
        BlockPitchBox.Text = string.Empty;
        BlockSpeedBox.Text = string.Empty;
        BlockVolumeBox.Text = string.Empty;
        BlockPauseBox.Text = "0";
        VisualDurationBox.Text = string.Empty;
        ResourceVolumeBox.Text = SceneComposer.DefaultVolumePercent(
            (ScriptBlockTypeCombo.SelectedItem as ScriptBlockTypeChoice)?.Kind ?? ScriptBlockKind.Dialogue).ToString(CultureInfo.InvariantCulture);
        VideoLayerCombo.SelectedIndex = 1;
        VideoGreenScreenCheck.IsChecked = false;
        VideoKeyColorBox.Text = "00FF00";
        VideoKeyToleranceBox.Text = "0.30";
        ResetVisualTransformControls((ScriptBlockTypeCombo.SelectedItem as ScriptBlockTypeChoice)?.Kind ?? ScriptBlockKind.Dialogue);
        AudioDurationModeCombo.SelectedIndex = 0;
        SfxWaitCheck.IsChecked = false;
        CharacterPositionCombo.SelectedIndex = 1;
        CharacterFramingCombo.SelectedIndex = 0;
        BackgroundTrimBordersCheck.IsChecked = true;
        TransitionStyleCombo.SelectedIndex = 0;
        TransitionDurationBox.Text = "500";
        TransitionVegasEffectCombo.SelectedIndex = 0;
        VegasPluginCombo.SelectedIndex = -1;
        VegasPresetCombo.ItemsSource = null;
        TransitionBackgroundCheck.IsChecked = true;
        TransitionCharacterCheck.IsChecked = true;
        TransitionImageCheck.IsChecked = true;
        TransitionVideoCheck.IsChecked = true;
        ResetEffectControls();
        if (ScriptBlockTypeCombo.SelectedItem is null)
            ScriptBlockTypeCombo.SelectedItem = ScriptBlockTypes[0];
        ScriptCharacterCombo.ItemsSource = BuildCharacterChoices();
        ScriptVoiceProfileCombo.ItemsSource = BuildVoiceChoices();
        SelectChoiceById(ScriptCharacterCombo, null);
        SelectDefaultSpeechVoice();
        SpriteSubfoldersCheck.IsChecked = false;
        SpriteNameSearchBox.Text = string.Empty;
        ClearSpriteFolderSelection();
        var kind = (ScriptBlockTypeCombo.SelectedItem as ScriptBlockTypeChoice)?.Kind ?? ScriptBlockKind.Dialogue;
        RefreshAssetChoices(kind, null);
        UpdateScriptBlockEditorState();
    }

    private static void SelectChoiceById(ComboBox combo, Guid? id)
    {
        combo.SelectedItem = combo.Items.Cast<object>().FirstOrDefault(item => item switch
        {
            CharacterChoice character => character.Id == id,
            VoiceProfileChoice voice => voice.Id == id,
            AssetChoice asset => asset.Id == id,
            _ => false
        });
        if (combo.SelectedItem is null && combo.Items.Count > 0)
            combo.SelectedIndex = 0;
    }

    private static bool TryNullableInt(string text, out int? value)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            value = null;
            return true;
        }
        if (int.TryParse(trimmed, out var parsed))
        {
            value = parsed;
            return true;
        }
        value = null;
        return false;
    }

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool IsSpeechBlock(ScriptBlockKind kind) => kind is ScriptBlockKind.Dialogue or ScriptBlockKind.Narration;

    private static string ComputeVoiceCacheHash(VoiceProfile profile, string text, int? pitch, int? speed, int volume)
    {
        var canonical = string.Join("\n",
            profile.ProviderKey,
            profile.VoiceId,
            profile.SampleRate.ToString(),
            profile.ConfigJson,
            pitch?.ToString() ?? "default",
            speed?.ToString() ?? "default",
            volume.ToString(),
            text);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string SafeFilePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "voz" : cleaned;
    }

    private static bool IsValidWave(string path)
    {
        try
        {
            return File.Exists(path) && new FileInfo(path).Length > 44 && GetWaveDurationMs(path) > 0;
        }
        catch
        {
            return false;
        }
    }

    private static long GetWaveDurationMs(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);
        if (new string(reader.ReadChars(4)) != "RIFF")
            throw new InvalidDataException("El archivo generado no es RIFF/WAV.");
        _ = reader.ReadUInt32();
        if (new string(reader.ReadChars(4)) != "WAVE")
            throw new InvalidDataException("El archivo generado no es WAVE.");

        uint byteRate = 0;
        uint dataSize = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            var chunk = new string(reader.ReadChars(4));
            var size = reader.ReadUInt32();
            var chunkStart = stream.Position;
            if (chunk == "fmt " && size >= 12)
            {
                _ = reader.ReadUInt16();
                _ = reader.ReadUInt16();
                _ = reader.ReadUInt32();
                byteRate = reader.ReadUInt32();
            }
            else if (chunk == "data")
            {
                dataSize = size;
            }
            var next = chunkStart + size + (size % 2);
            if (next > stream.Length)
                break;
            stream.Position = next;
            if (byteRate > 0 && dataSize > 0)
                break;
        }

        if (byteRate == 0 || dataSize == 0)
            throw new InvalidDataException("El WAV no contiene chunks fmt/data válidos.");
        return (long)Math.Round(dataSize * 1000d / byteRate);
    }

    private static string FormatTimelineTime(long milliseconds)
    {
        var value = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return $"{(int)value.TotalMinutes:00}:{value.Seconds:00}.{value.Milliseconds:000}";
    }
}

public sealed record ScriptBlockTypeChoice(ScriptBlockKind Kind, string Name);
/// <summary>A character of the editor combos; <see cref="RenderAssetId"/>: an NPC render in «Ocultar personaje» (1.4.1).</summary>
public sealed record CharacterChoice(Guid? Id, string Name, Guid? RenderAssetId = null);
/// <summary>A voice of a selector: a saved profile (Id), a direct voice (DirectVoiceId + DirectProvider, any engine since
/// 1.4.0; no provider = Loquendo TTS7 as before), or the NPC profile with a voice of its own (both).</summary>
public sealed record VoiceProfileChoice(Guid? Id, string Name, string? DirectVoiceId = null, string? DirectProvider = null)
{
    public DirectVoiceRef? Direct => string.IsNullOrWhiteSpace(DirectVoiceId) ? null
        : new DirectVoiceRef(VoiceSelectorSettings.Canonical(DirectProvider ?? VoiceSelectorSettings.Tts7), DirectVoiceId);

    public static VoiceProfileChoice Of(Guid? id, string name, DirectVoiceRef voice) => new(id, name, voice.Voice, voice.Provider);
}
public sealed record AssetChoice(Guid? Id, string Name);

public sealed record EpisodeScriptRow(Episode Episode)
{
    public string Display => $"EP {Episode.Number:00} — {Episode.Title}";
}

public sealed record SceneScriptRow(Scene Scene)
{
    public string Display => $"{Scene.Index:00} — {Scene.Title}";
}

public sealed record ScriptBlockRow(SceneScriptBlock Block, string Subject)
{
    public int Order => Block.OrderIndex + 1;
    public string Start => Block.StartOffsetMs is long ms ? Format(ms) : "—";
    public string Kind => Block.Kind switch
    {
        ScriptBlockKind.Dialogue => "Diálogo",
        ScriptBlockKind.Narration => "Narración",
        ScriptBlockKind.Background => "Fondo",
        ScriptBlockKind.CharacterShow => "Mostrar",
        ScriptBlockKind.CharacterHide => "Ocultar",
        ScriptBlockKind.Image => "Imagen",
        ScriptBlockKind.SoundEffect => "SFX",
        ScriptBlockKind.Music => "Música",
        ScriptBlockKind.Video => "Video",
        ScriptBlockKind.Pause => "Pausa",
        ScriptBlockKind.TextOverlay => "Texto",
        ScriptBlockKind.Transition => "Transición",
        ScriptBlockKind.Comment => "Comentario",
        ScriptBlockKind.Camera => "Cámara",
        ScriptBlockKind.Cinema => "Cine",
        ScriptBlockKind.Gesture => "Gesto",
        ScriptBlockKind.Blur => "Desenfoque",
        _ => Block.Kind.ToString()
    };
    public string Content => Block.Kind == ScriptBlockKind.Camera
        ? CameraSummary(BlockParameters.Of(Block).Camera(Block.CharacterId is not null))
        : Block.Kind == ScriptBlockKind.Cinema
        ? BlockParameters.Of(Block).Cinema() is var cinema && cinema.Show
            ? $"barras {cinema.Style} · {cinema.MoveMs} ms" + (cinema.Layers == "todos" ? "" : " · " + cinema.Layers)
            : $"quitar barras · {cinema.MoveMs} ms"
        : Block.Kind == ScriptBlockKind.Blur
        ? BlockParameters.Of(Block).Blur(Block.CharacterId is not null) is var blur
            ? (blur.Target == "personaje" ? "" : blur.Target + " · ") + BlurPlan.StyleName(blur.Amount) + $" · {blur.MoveMs} ms"
            : ""
        : Block.Kind == ScriptBlockKind.Gesture
        ? GestureSummary(BlockParameters.Of(Block).Gesture())
        : Block.Kind == ScriptBlockKind.Pause
        ? $"{Block.PauseAfterMs} ms"
        : (SceneComposer.VisualDuration(Block) ?? SceneComposer.AudioDuration(Block)) is long duration
            ? $"{duration} ms · {Block.Text.Replace('\r', ' ').Replace('\n', ' ')}"
            : Block.Text.Replace('\r', ' ').Replace('\n', ' ');
    private static string GestureSummary(GestureSettings gesture) => gesture.Mode == "quitar"
        ? "deja de gesticular al hablar"
        : (gesture.Mode == "habla" ? "quien habla · " : "") + GestureSettings.StepsText(gesture.Steps) +
          $" · {gesture.Angle:0.#}° · estirar {gesture.StretchPercent:+0;-0}% · {gesture.Side} · ×{gesture.Speed:0.##} · eje {gesture.Pivot}";
    private static string CameraSummary(CameraSettings camera) => camera.Mode switch
    {
        "general" => $"plano general · {camera.MoveMs} ms",
        "habla" => $"quien habla · {camera.Zoom:0.##}× · {camera.MoveMs} ms · {camera.Focus}",
        "punto" => $"punto ({camera.OffsetX:+0;-0;0}, {camera.OffsetY:+0;-0;0}) · {camera.Zoom:0.##}× · {camera.MoveMs} ms",
        _ => $"{camera.Zoom:0.##}× · {camera.MoveMs} ms · {camera.Focus}"
    };
    public string AudioState => Block.Kind is ScriptBlockKind.Dialogue or ScriptBlockKind.Narration
        ? Block.GeneratedDurationMs is long duration
            ? $"{(Block.GeneratedAudioHash?.StartsWith("imported-sha256:", StringComparison.Ordinal) == true ? "Grabada " : "")}{duration / 1000d:0.00}s"
            : "Pendiente"
        : "—";

    private static string Format(long milliseconds)
    {
        var value = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return $"{(int)value.TotalMinutes:00}:{value.Seconds:00}.{value.Milliseconds:000}";
    }
}
