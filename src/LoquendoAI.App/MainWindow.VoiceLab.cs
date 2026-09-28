using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Tts;

namespace LoquendoAI.App;

public partial class MainWindow
{
    private readonly TtsBridgeClient _ttsBridge = new();
    private readonly MediaPlayer _voicePreviewPlayer = new();
    private IReadOnlyList<VoiceProfile> _voiceProfiles = Array.Empty<VoiceProfile>();
    private IReadOnlyList<CharacterDefinition> _characters = Array.Empty<CharacterDefinition>();
    private Guid? _editingVoiceProfileId;
    private CancellationTokenSource? _voicePreviewCancellation;
    private string? _lastVoicePreviewPath;
    private bool _loadingVoiceForm;

    private static readonly VoiceProviderChoice[] VoiceProviders =
    [
        new("loquendo7-native", "Loquendo TTS7 Native"),
        new("sapi5-x86", "SAPI5 x86"),
        new("balcon-sapi4", "Infovox / SAPI4 vía BALCON")
    ];

    private void InitializeVoiceLab()
    {
        VoiceProviderCombo.ItemsSource = VoiceProviders;
        VoiceSampleRateCombo.ItemsSource = new[] { 8000, 16000, 22050, 32000, 44100, 48000 };
        VoiceSampleRateCombo.SelectedItem = 32000;
        VoiceProviderCombo.SelectedIndex = 0;
        UpdateVoiceBridgeStatus();
        UpdateVoiceValueLabels();
    }

    private void UpdateVoiceBridgeStatus()
    {
        VoiceBridgeStatusText.Text = _ttsBridge.BridgePath is { } path
            ? $"Bridge x86: {Path.GetFileName(path)}"
            : "Bridge x86 no publicado — ejecuta scripts\\tts-publish.cmd";
    }

    private async Task RefreshVoiceLabAsync(Guid? selectProfileId = null, Guid? selectCharacterId = null)
    {
        if (_currentRepository is null)
            return;

        _voiceProfiles = await _currentRepository.GetVoiceProfilesAsync();
        _characters = await _currentRepository.GetCharactersAsync();
        var profileNames = _voiceProfiles.ToDictionary(x => x.Id, x => x.Name);

        VoiceProfileCombo.ItemsSource = _voiceProfiles;
        CharactersGrid.ItemsSource = _characters
            .Select(c => new CharacterVoiceRow(
                c,
                c.DefaultVoiceProfileId is Guid id && profileNames.TryGetValue(id, out var name) ? name : NoProfileLabel))
            .ToArray();

        if (selectCharacterId is Guid characterId)
            CharactersGrid.SelectedItem = CharactersGrid.Items.Cast<CharacterVoiceRow>().FirstOrDefault(x => x.Character.Id == characterId);

        var desiredProfile = selectProfileId ?? _editingVoiceProfileId;
        if (desiredProfile is Guid profileId)
        {
            var match = _voiceProfiles.FirstOrDefault(x => x.Id == profileId);
            if (match is not null)
                VoiceProfileCombo.SelectedItem = match;
        }
        else if (_voiceProfiles.Count > 0 && VoiceProfileCombo.SelectedItem is null)
        {
            VoiceProfileCombo.SelectedIndex = 0;
        }
        else if (_voiceProfiles.Count == 0)
        {
            BeginNewVoiceProfile();
        }

        UpdateVoiceBridgeStatus();
        RefreshScriptReferenceChoices();
    }

    private async void VoiceProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingVoiceForm || VoiceProfileCombo.SelectedItem is not VoiceProfile profile)
            return;

        await LoadVoiceProfileIntoFormAsync(profile);
    }

    private async Task LoadVoiceProfileIntoFormAsync(VoiceProfile profile)
    {
        _editingVoiceProfileId = profile.Id;
        _loadingVoiceForm = true;
        try
        {
            VoiceProfileNameBox.Text = profile.Name;
            VoiceProviderCombo.SelectedItem = VoiceProviders.FirstOrDefault(x => x.Key.Equals(profile.ProviderKey, StringComparison.OrdinalIgnoreCase))
                ?? VoiceProviders[0];
            ConfigureProviderRanges(profile.ProviderKey, resetValues: false);
            VoicePitchSlider.Value = ClampToSlider(VoicePitchSlider, profile.Pitch ?? DefaultPitch(profile.ProviderKey));
            VoiceSpeedEnabledCheck.IsChecked = profile.Speed is not null;
            VoiceSpeedSlider.IsEnabled = profile.Speed is not null;
            VoiceSpeedSlider.Value = ClampToSlider(VoiceSpeedSlider, profile.Speed ?? DefaultSpeed(profile.ProviderKey));
            VoiceVolumeSlider.Value = Math.Clamp(profile.Volume, 0, 100);
            VoiceSampleRateCombo.SelectedItem = new[] { 8000, 16000, 22050, 32000, 44100, 48000 }.Contains(profile.SampleRate)
                ? profile.SampleRate
                : 32000;
            UpdateVoiceValueLabels();
        }
        finally
        {
            _loadingVoiceForm = false;
        }

        await LoadVoicesForProviderAsync(profile.ProviderKey, profile.VoiceId);
        VoiceLabStatusText.Text = $"Editando {profile.Name}.";
    }

    private void NewVoiceProfile_Click(object sender, RoutedEventArgs e) => BeginNewVoiceProfile();

    private void BeginNewVoiceProfile()
    {
        _editingVoiceProfileId = null;
        _loadingVoiceForm = true;
        try
        {
            VoiceProfileCombo.SelectedItem = null;
            VoiceProfileNameBox.Text = string.Empty;
            if (VoiceProviderCombo.SelectedItem is not VoiceProviderChoice)
                VoiceProviderCombo.SelectedIndex = 0;
            var provider = (VoiceProviderCombo.SelectedItem as VoiceProviderChoice)?.Key ?? VoiceProviders[0].Key;
            ConfigureProviderRanges(provider, resetValues: true);
            VoiceSpeedEnabledCheck.IsChecked = false;
            VoiceSpeedSlider.IsEnabled = false;
            VoiceSampleRateCombo.SelectedItem = 32000;
            UpdateVoiceValueLabels();
        }
        finally
        {
            _loadingVoiceForm = false;
        }

        var currentProvider = (VoiceProviderCombo.SelectedItem as VoiceProviderChoice)?.Key;
        if (currentProvider is not null)
            _ = LoadVoicesForProviderAsync(currentProvider, null);
        VoiceLabStatusText.Text = "Nuevo perfil. Elige una voz y prueba cómo suena antes de guardarlo.";
    }

    private async void VoiceProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingVoiceForm || VoiceProviderCombo.SelectedItem is not VoiceProviderChoice provider)
            return;

        ConfigureProviderRanges(provider.Key, resetValues: true);
        await LoadVoicesForProviderAsync(provider.Key, null);
    }

    private async Task LoadVoicesForProviderAsync(string providerKey, string? preferredVoice)
    {
        VoiceIdCombo.IsEnabled = false;
        VoiceLabStatusText.Text = "Consultando voces del bridge x86…";
        try
        {
            var voices = await _ttsBridge.GetVoicesAsync(providerKey);
            if (VoiceSelectorSettings.IsDirectProvider(providerKey))
            {
                _voiceCatalog = new Dictionary<string, IReadOnlyList<string>>(_voiceCatalog, StringComparer.OrdinalIgnoreCase)
                {
                    [VoiceSelectorSettings.Canonical(providerKey)] = voices
                };
                RefreshVoiceSelectors();
            }
            VoiceIdCombo.ItemsSource = voices;
            if (!string.IsNullOrWhiteSpace(preferredVoice))
                VoiceIdCombo.SelectedItem = voices.FirstOrDefault(x => x.Equals(preferredVoice, StringComparison.OrdinalIgnoreCase));
            if (VoiceIdCombo.SelectedItem is null && voices.Count > 0)
                VoiceIdCombo.SelectedIndex = 0;
            VoiceLabStatusText.Text = voices.Count == 0
                ? "El provider respondió, pero no expuso voces."
                : $"{voices.Count} voces disponibles en {ProviderName(providerKey)}.";
        }
        catch (Exception ex)
        {
            VoiceIdCombo.ItemsSource = Array.Empty<string>();
            VoiceLabStatusText.Text = ex.Message;
        }
        finally
        {
            VoiceIdCombo.IsEnabled = true;
            UpdateVoiceBridgeStatus();
        }
    }

    private void ConfigureProviderRanges(string providerKey, bool resetValues)
    {
        if (providerKey.Equals("loquendo7-native", StringComparison.OrdinalIgnoreCase))
        {
            VoiceSampleRateCombo.IsEnabled = true;
            VoiceVolumeSlider.IsEnabled = true;
            VoicePitchSlider.Minimum = 0;
            VoicePitchSlider.Maximum = 100;
            VoiceSpeedSlider.Minimum = 0;
            VoiceSpeedSlider.Maximum = 100;
            if (resetValues)
            {
                VoicePitchSlider.Value = 50;
                VoiceSpeedSlider.Value = 50;
                VoiceVolumeSlider.Value = 50;
            }
        }
        else if (providerKey.Equals("balcon-sapi4", StringComparison.OrdinalIgnoreCase)
                 || providerKey.Equals("infovox-sapi4", StringComparison.OrdinalIgnoreCase))
        {
            // BALCON's documented SAPI4 controls use 0..100 for pitch and speed.
            // SAPI4 volume is not exposed by BALCON. Keep the slider disabled to avoid
            // pretending that a saved value changes the engine.
            VoiceSampleRateCombo.IsEnabled = false;
            VoicePitchSlider.Minimum = 0;
            VoicePitchSlider.Maximum = 100;
            VoiceSpeedSlider.Minimum = 0;
            VoiceSpeedSlider.Maximum = 100;
            VoiceVolumeSlider.IsEnabled = false;
            if (resetValues)
            {
                VoicePitchSlider.Value = 50;
                VoiceSpeedSlider.Value = 50;
                VoiceVolumeSlider.Value = 100;
            }
        }
        else
        {
            VoiceSampleRateCombo.IsEnabled = false;
            VoicePitchSlider.Minimum = -10;
            VoicePitchSlider.Maximum = 10;
            VoiceSpeedSlider.Minimum = -10;
            VoiceSpeedSlider.Maximum = 10;
            VoiceVolumeSlider.IsEnabled = true;
            if (resetValues)
            {
                VoicePitchSlider.Value = 0;
                VoiceSpeedSlider.Value = 0;
                VoiceVolumeSlider.Value = 100;
            }
        }
        UpdateVoiceValueLabels();
    }

    private async void SaveVoiceProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is null)
            return;
        if (!TryBuildVoiceProfile(out var profile, requireSavedName: true))
            return;

        try
        {
            await _currentRepository.UpsertVoiceProfileAsync(profile!);
            _editingVoiceProfileId = profile!.Id;
            await RefreshVoiceLabAsync(profile.Id);
            VoiceLabStatusText.Text = $"Perfil '{profile.Name}' guardado.";
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private bool TryBuildVoiceProfile(out VoiceProfile? profile, bool requireSavedName)
    {
        profile = null;
        if (VoiceProviderCombo.SelectedItem is not VoiceProviderChoice provider)
        {
            MessageBox.Show(this, "Selecciona un motor TTS.", "Voice Lab", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (VoiceIdCombo.SelectedItem is not string voice || string.IsNullOrWhiteSpace(voice))
        {
            MessageBox.Show(this, "Selecciona una voz.", "Voice Lab", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var name = VoiceProfileNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            if (requireSavedName)
            {
                MessageBox.Show(this, "Ponle un nombre al perfil.", "Voice Lab", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }
            name = $"{voice} ({ProviderName(provider.Key)})";
        }

        profile = new VoiceProfile(
            _editingVoiceProfileId ?? Guid.NewGuid(),
            name,
            provider.Key,
            voice,
            (int)Math.Round(VoicePitchSlider.Value),
            VoiceSpeedEnabledCheck.IsChecked == true ? (int)Math.Round(VoiceSpeedSlider.Value) : null,
            (int)Math.Round(VoiceVolumeSlider.Value),
            VoiceSampleRateCombo.SelectedItem is int sampleRate ? sampleRate : 32000);
        return true;
    }

    private async void DeleteVoiceProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is null || VoiceProfileCombo.SelectedItem is not VoiceProfile profile)
            return;
        if (IsNpcProfile(profile.Id))
        {
            MessageBox.Show(this, "El perfil NPC es del sistema: es la voz de quien no tiene perfil propio (NPC, narrador, " +
                "personajes sin voz). Puedes cambiar su voz, pitch, velocidad y volumen, pero no eliminarlo.",
                "Voice Lab", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this,
                $"¿Eliminar el perfil '{profile.Name}'? Los personajes que lo usen quedarán sin voz base asignada.",
                "Voice Lab", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            await _currentRepository.DeleteVoiceProfileAsync(profile.Id);
            _editingVoiceProfileId = null;
            await RefreshVoiceLabAsync();
            VoiceLabStatusText.Text = "Perfil eliminado.";
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async void AddCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is null)
            return;
        var name = NewCharacterNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;
        if (_characters.Any(x => x.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)))
        {
            MessageBox.Show(this, "Ya existe un personaje con ese nombre.", "Voice Lab", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var character = new CharacterDefinition(Guid.NewGuid(), name, null);
        try
        {
            await _currentRepository.UpsertCharacterAsync(character);
            NewCharacterNameBox.Clear();
            await RefreshVoiceLabAsync(selectCharacterId: character.Id);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async void DeleteCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is null || CharactersGrid.SelectedItem is not CharacterVoiceRow row)
            return;
        if (MessageBox.Show(this, $"¿Eliminar al personaje '{row.Name}' del proyecto?", "Voice Lab",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            await _currentRepository.DeleteCharacterAsync(row.Character.Id);
            await RefreshVoiceLabAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async void AssignVoiceProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is null || CharactersGrid.SelectedItem is not CharacterVoiceRow characterRow)
        {
            MessageBox.Show(this, "Selecciona un personaje.", "Voice Lab", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (VoiceProfileCombo.SelectedItem is not VoiceProfile profile)
        {
            MessageBox.Show(this, "Guarda y selecciona un perfil de voz primero.", "Voice Lab", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            await _currentRepository.UpsertCharacterAsync(characterRow.Character with { DefaultVoiceProfileId = profile.Id });
            await RefreshVoiceLabAsync(profile.Id, characterRow.Character.Id);
            VoiceLabStatusText.Text = $"{profile.Name} asignado a {characterRow.Name}.";
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void CharactersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CharactersGrid.SelectedItem is CharacterVoiceRow row)
            VoiceLabStatusText.Text = row.VoiceProfileName == NoProfileLabel
                ? $"{row.Name} todavía no tiene voz base: habla con el perfil NPC."
                : $"{row.Name} usa '{row.VoiceProfileName}'.";
    }

    private async void PreviewVoice_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is null)
            return;
        if (!TryBuildVoiceProfile(out var profile, requireSavedName: false))
            return;
        var text = VoicePreviewTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            MessageBox.Show(this, "Escribe un texto de prueba.", "Voice Lab", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        StopVoicePreview(deleteLastFile: false);
        _voicePreviewCancellation = new CancellationTokenSource();
        VoicePreviewButton.IsEnabled = false;
        VoiceLabStatusText.Text = "Generando preview…";

        var previewDir = Path.Combine(_currentRepository.ProjectRoot, "cache", "tts-preview");
        Directory.CreateDirectory(previewDir);
        var output = Path.Combine(previewDir, $"preview-{Guid.NewGuid():N}.wav");

        try
        {
            var path = await _ttsBridge.SynthesizeAsync(new TtsPreviewRequest(
                profile!.ProviderKey,
                profile.VoiceId,
                text,
                output,
                profile.Pitch,
                profile.Speed,
                profile.Volume,
                profile.SampleRate), _voicePreviewCancellation.Token);

            if (!string.IsNullOrWhiteSpace(_lastVoicePreviewPath) && !PathsEqual(_lastVoicePreviewPath, path))
                TryDeletePreview(_lastVoicePreviewPath);
            _lastVoicePreviewPath = path;

            _voicePreviewPlayer.Close();
            _voicePreviewPlayer.Open(new Uri(path, UriKind.Absolute));
            _voicePreviewPlayer.Play();
            VoiceLabStatusText.Text = $"Reproduciendo {profile.VoiceId} — pitch {profile.Pitch}, velocidad {(profile.Speed?.ToString() ?? "Auto")}.";
        }
        catch (OperationCanceledException)
        {
            VoiceLabStatusText.Text = "Preview cancelado.";
            TryDeletePreview(output);
        }
        catch (Exception ex)
        {
            VoiceLabStatusText.Text = ex.Message;
            TryDeletePreview(output);
        }
        finally
        {
            _voicePreviewCancellation?.Dispose();
            _voicePreviewCancellation = null;
            VoicePreviewButton.IsEnabled = true;
            UpdateVoiceBridgeStatus();
        }
    }

    private void StopVoicePreview_Click(object sender, RoutedEventArgs e)
    {
        _voicePreviewCancellation?.Cancel();
        StopVoicePreview(deleteLastFile: false);
        VoiceLabStatusText.Text = "Preview detenido.";
    }

    private void StopVoicePreview(bool deleteLastFile)
    {
        _voicePreviewPlayer.Stop();
        _voicePreviewPlayer.Close();
        if (deleteLastFile && !string.IsNullOrWhiteSpace(_lastVoicePreviewPath))
        {
            TryDeletePreview(_lastVoicePreviewPath);
            _lastVoicePreviewPath = null;
        }
    }

    private static void TryDeletePreview(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private void VoicePitchSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateVoiceValueLabels();
    private void VoiceSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateVoiceValueLabels();
    private void VoiceVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateVoiceValueLabels();

    private void VoiceSpeedEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (VoiceSpeedSlider is null || VoiceSpeedEnabledCheck is null)
            return;
        VoiceSpeedSlider.IsEnabled = VoiceSpeedEnabledCheck.IsChecked == true;
        UpdateVoiceValueLabels();
    }

    private void UpdateVoiceValueLabels()
    {
        if (VoicePitchValueText is null || VoiceSpeedValueText is null || VoiceVolumeValueText is null)
            return;
        VoicePitchValueText.Text = Math.Round(VoicePitchSlider.Value).ToString("0");
        VoiceSpeedValueText.Text = VoiceSpeedEnabledCheck.IsChecked == true
            ? Math.Round(VoiceSpeedSlider.Value).ToString("0")
            : "Auto";
        VoiceVolumeValueText.Text = Math.Round(VoiceVolumeSlider.Value).ToString("0");
    }

    private static double ClampToSlider(System.Windows.Controls.Primitives.RangeBase slider, double value)
        => Math.Clamp(value, slider.Minimum, slider.Maximum);

    /// <summary>A character without its own profile speaks with the NPC one.</summary>
    private const string NoProfileLabel = "— (usa NPC)";

    private static int DefaultPitch(string providerKey)
        => providerKey.Equals("loquendo7-native", StringComparison.OrdinalIgnoreCase)
           || providerKey.Equals("balcon-sapi4", StringComparison.OrdinalIgnoreCase)
           || providerKey.Equals("infovox-sapi4", StringComparison.OrdinalIgnoreCase) ? 50 : 0;

    private static int DefaultSpeed(string providerKey)
        => providerKey.Equals("loquendo7-native", StringComparison.OrdinalIgnoreCase)
           || providerKey.Equals("balcon-sapi4", StringComparison.OrdinalIgnoreCase)
           || providerKey.Equals("infovox-sapi4", StringComparison.OrdinalIgnoreCase) ? 50 : 0;

    private static string ProviderName(string providerKey)
        => VoiceProviders.FirstOrDefault(x => x.Key.Equals(providerKey, StringComparison.OrdinalIgnoreCase))?.Name ?? providerKey;
}

public sealed record VoiceProviderChoice(string Key, string Name);

public sealed record CharacterVoiceRow(CharacterDefinition Character, string VoiceProfileName)
{
    public string Name => Character.Name;
}
