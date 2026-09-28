using System.Windows.Controls;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;
using LoquendoAI.Infrastructure.Tts;

namespace LoquendoAI.App;

/// <summary>
/// Direct voices: a line can speak with a TTS voice without a saved profile. Until 1.3.0 only Loquendo TTS7; since
/// 1.4.0 any voice of the selector (TTS7, SAPI4 Juan/Antonio through BALCON, SAPI5 IVONA… — Voice Lab edits which).
/// </summary>
public partial class MainWindow
{
    private const string DirectTts7Provider = VoiceSelectorSettings.Tts7;

    /// <summary>Installed voices per engine, as the TTS bridge lists them (an engine that failed has none).</summary>
    private Dictionary<string, IReadOnlyList<string>> _voiceCatalog = new(StringComparer.OrdinalIgnoreCase);
    private VoiceSelectorSettings _voiceSelector = VoiceSelectorSettings.Load(VoiceSelectorPath);

    private static string VoiceSelectorPath => AppPaths.PathOf("selector-voces.json");

    private IReadOnlyList<string> _directTts7Voices => InstalledVoices(DirectTts7Provider);

    private IReadOnlyList<string> InstalledVoices(string provider) =>
        _voiceCatalog.TryGetValue(provider, out var voices) ? voices : [];

    /// <summary>The voices the selectors offer (installed and shown, plus the ones added by hand in Voice Lab).</summary>
    private IReadOnlyList<DirectVoiceRef> SelectorVoices() => _voiceSelector.Visible(_voiceCatalog);

    /// <summary>
    /// Asks the engines for their voices (one bridge process each). Opening a project waits only for Loquendo TTS7
    /// (the NPC's voice); SAPI4 through BALCON and SAPI5 are listed in the background, with a time limit, and the
    /// selectors are refreshed when they answer. BALCON missing or an engine without voices is not an error here:
    /// that engine simply offers nothing.
    /// </summary>
    private async Task RefreshDirectVoicesAsync()
    {
        var (tts7, tts7Error) = await ScanVoicesAsync(DirectTts7Provider);
        _voiceCatalog = new Dictionary<string, IReadOnlyList<string>>(_voiceCatalog, StringComparer.OrdinalIgnoreCase)
        {
            [DirectTts7Provider] = tts7
        };
        if (tts7Error is not null) ScriptStatusText.Text = "Voces TTS7 no disponibles: " + tts7Error;
        RefreshVoiceSelectors();
        _ = RefreshOtherEnginesAsync();
    }

    private async Task RefreshOtherEnginesAsync()
    {
        try
        {
            var others = VoiceSelectorSettings.Providers.Where(x => x != DirectTts7Provider).ToArray();
            var results = await Task.WhenAll(others.Select(ScanVoicesAsync));
            var catalog = new Dictionary<string, IReadOnlyList<string>>(_voiceCatalog, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < others.Length; i++) catalog[others[i]] = results[i].Voices;
            _voiceCatalog = catalog;
            RefreshVoiceSelectors();
        }
        catch (Exception ex) { ErrorLog.Record(ex); }
    }

    /// <summary>The voices of one engine; an engine that fails or takes longer than 45 s offers none.</summary>
    private async Task<(IReadOnlyList<string> Voices, string? Error)> ScanVoicesAsync(string provider)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { return (await _ttsBridge.GetVoicesAsync(provider, timeout.Token), null); }
        catch (Exception ex) { return ([], timeout.IsCancellationRequested ? "no respondió a tiempo" : ex.Message); }
    }

    private async Task<(Dictionary<string, IReadOnlyList<string>> Catalog, string? Tts7Error)> ScanVoiceCatalogAsync()
    {
        var providers = VoiceSelectorSettings.Providers;
        var results = await Task.WhenAll(providers.Select(ScanVoicesAsync));
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < providers.Count; i++) catalog[providers[i]] = results[i].Voices;
        return (catalog, results[providers.ToList().IndexOf(DirectTts7Provider)].Error);
    }

    /// <summary>Voice Lab › «Voces en los selectores…»: edits which voices the selectors offer (saved per PC).</summary>
    private void EditVoiceSelector_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var dialog = new VoiceSelectorWindow(_voiceSelector, _voiceCatalog, async () =>
        {
            var (catalog, _) = await ScanVoiceCatalogAsync();
            return catalog;
        }, PreviewDirectVoiceAsync) { Owner = this };
        var saved = dialog.ShowDialog() == true;
        // A new scan is worth keeping even when the choices are cancelled.
        _voiceCatalog = new Dictionary<string, IReadOnlyList<string>>(dialog.Catalog, StringComparer.OrdinalIgnoreCase);
        if (saved)
        {
            _voiceSelector = dialog.Result;
            try { _voiceSelector.Save(VoiceSelectorPath); }
            catch (Exception ex) { ShowError(ex); }
            VoiceLabStatusText.Text = $"Selectores de voz: {SelectorVoices().Count} voces disponibles en el Editor y en Diálogos.";
        }
        RefreshVoiceSelectors();
    }

    /// <summary>Plays a direct voice with a short sample (Voice Lab player, the same one «▶ Escuchar» uses).</summary>
    private async Task PreviewDirectVoiceAsync(DirectVoiceRef voice)
    {
        var folder = _currentRepository is { } repository
            ? System.IO.Path.Combine(repository.ProjectRoot, "cache", "tts-preview")
            : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LoquendoAI-tts-preview");
        System.IO.Directory.CreateDirectory(folder);
        var output = System.IO.Path.Combine(folder, $"selector-{Guid.NewGuid():N}.wav");
        var profile = DirectProfile(voice);
        StopVoicePreview(deleteLastFile: false);
        var path = await _ttsBridge.SynthesizeAsync(new TtsPreviewRequest(profile.ProviderKey, profile.VoiceId,
            "Hola, esta es mi voz en Loquendo AI.", output, profile.Pitch, profile.Speed, profile.Volume, profile.SampleRate));
        if (!string.IsNullOrWhiteSpace(_lastVoicePreviewPath) && !PathsEqual(_lastVoicePreviewPath, path))
            TryDeletePreview(_lastVoicePreviewPath);
        _lastVoicePreviewPath = path;
        _voicePreviewPlayer.Open(new Uri(path, UriKind.Absolute));
        _voicePreviewPlayer.Play();
    }

    /// <summary>After the catalog or the selector settings change: only the combos that offer voices (Editor «Voz»,
    /// Voces grabadas, Diálogos). The character and asset combos are not touched, so a render being chosen in the
    /// Editor is kept when a background scan answers.</summary>
    private void RefreshVoiceSelectors()
    {
        if (ScriptVoiceProfileCombo is not null && ScriptCharacterCombo is not null) RefreshSpeakerVoiceChoice();
        RefreshRecordedVoiceProfileChoices();
        RefreshDialogueChoices();
    }

    private static DirectVoiceRef? DirectVoiceOf(SceneScriptBlock block) => DirectVoiceRef.Of(BlockParameters.Of(block));

    /// <summary>A direct voice as a profile, with the engine's neutral prosody (TTS7 and SAPI4 pitch 50 of 0–100,
    /// SAPI5 0 of −10…10), so the same voice and prosody share the voice cache.</summary>
    private static VoiceProfile DirectProfile(DirectVoiceRef voice) =>
        new(Guid.Empty, voice.Label, voice.Provider, voice.Voice, Pitch: DefaultPitch(voice.Provider), Speed: null,
            Volume: 100, SampleRate: 32000);

    private static BlockParameters WithDirect(BlockParameters parameters, DirectVoiceRef? voice) =>
        parameters.WithDirectVoice(voice?.Provider ?? DirectTts7Provider, voice?.Voice);

    private static string DirectVoiceParameters(DirectVoiceRef? voice) => WithDirect(BlockParameters.Empty, voice).ToJson();

    /// <summary>Speech blocks keep everything they had (recording, STT timing…) and update only
    /// what the editor shows: narration duration/mode and the direct voice.</summary>
    private string SerializeSpeechParameters(SceneScriptBlock? previous, ScriptBlockKind kind)
    {
        var parameters = previous is null ? BlockParameters.Empty : BlockParameters.Of(previous);
        if (kind == ScriptBlockKind.Narration)
            parameters = parameters with
            {
                AudioDurationMs = long.TryParse(VisualDurationBox.Text.Trim(), out var duration) ? duration : null,
                AudioDurationMode = AudioDurationModeCombo.SelectedIndex == 1 ? "tempo" : "loop"
            };
        return WithDirect(parameters, (ScriptVoiceProfileCombo.SelectedItem as VoiceProfileChoice)?.Direct).ToJson();
    }

    private static bool SameVoice(DirectVoiceRef? a, DirectVoiceRef? b) =>
        a is null ? b is null : b is not null && a.Is(b.Provider, b.Voice);

    private static void SelectVoiceChoice(ComboBox combo, Guid? profileId, DirectVoiceRef? direct)
    {
        var choices = combo.Items.OfType<VoiceProfileChoice>().ToArray();
        // Exact pair first: the NPC profile with a voice of its own is a choice of its own.
        var exact = direct is null ? null : choices.FirstOrDefault(choice => choice.Id == profileId && SameVoice(choice.Direct, direct));
        if (exact is not null)
        {
            combo.SelectedItem = exact;
            return;
        }
        if (profileId.HasValue)
        {
            SelectChoiceById(combo, profileId);
            return;
        }
        if (direct is not null && choices.FirstOrDefault(choice => SameVoice(choice.Direct, direct)) is { } same)
        {
            combo.SelectedItem = same;
            return;
        }
        SelectChoiceById(combo, profileId);
    }
}
