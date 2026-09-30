using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;

namespace LoquendoAI.App;

public partial class MainWindow
{
    private bool _previewPreparing;
    private string? _scenePreviewSignature;
    private Guid? _scenePreviewSceneId;
    private bool _updatingSfxTiming;

    private async Task<string?> ResolveAssetPathAsync(AssetRecord asset)
    {
        if (_currentRepository is null) return null;
        if (asset.SourceId is Guid sourceId)
        {
            var source = (await _currentRepository.GetAssetSourcesAsync()).FirstOrDefault(x => x.Id == sourceId);
            if (source is null || string.IsNullOrWhiteSpace(asset.SourceRelativePath)) return null;
            var root = Path.GetFullPath(source.RootPath);
            var path = Path.GetFullPath(Path.Combine(root, asset.SourceRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return null;
            // "Missing" also covers files hidden by an Ignore rule but still used by a script:
            // they are gone from pickers, yet the script keeps working while the file exists.
            return asset.IsMissing && !File.Exists(path) ? null : path;
        }
        return asset.IsMissing ? null : ResolveGeneratedPath(asset.RelativePath);
    }

    /// <summary>■ next to the editor's ▶ and the blocks' «▶ Audio» (1.2.0): before, a sound only stopped when another
    /// one started.</summary>
    private void StopScriptAudio_Click(object sender, RoutedEventArgs e)
    {
        _scriptAudioPlayer.Stop();
        _scriptAudioPlayer.Close();
        ScriptStatusText.Text = "Audio detenido.";
    }

    private void StopRecordedVoice_Click(object sender, RoutedEventArgs e)
    {
        _scriptAudioPlayer.Stop();
        _scriptAudioPlayer.Close();
        RecordedVoiceStatusText.Text = "Audio detenido.";
    }

    private async void PreviewAsset_Click(object sender, RoutedEventArgs e)
    {
        if (ScriptBlockTypeCombo.SelectedItem is not ScriptBlockTypeChoice choice || choice.Kind is not (ScriptBlockKind.SoundEffect or ScriptBlockKind.Music) ||
            (ScriptAssetCombo.SelectedItem as AssetChoice)?.Id is not Guid id || !_scriptAssetCache.TryGetValue(id, out var asset))
        {
            ScriptStatusText.Text = "Selecciona un SFX o música para escucharlo.";
            return;
        }
        try
        {
            var path = await ResolveAssetPathAsync(asset);
            if (path is null || !File.Exists(path)) throw new FileNotFoundException("El asset ya no está en su carpeta fuente.", path);
            _scriptAudioPlayer.Stop();
            _scriptAudioPlayer.Close();
            var previewPercent = int.TryParse(ResourceVolumeBox.Text.Trim(), out var percent) && percent is >= 0 and <= 200
                ? percent : SceneComposer.DefaultVolumePercent(choice.Kind);
            // WPF MediaPlayer tops out at 1.0; gains above 100% are audible in the rendered scene.
            _scriptAudioPlayer.Volume = Math.Min(1, previewPercent / 100d);
            _scriptAudioPlayer.Open(new Uri(path));
            _scriptAudioPlayer.Play();
            ScriptStatusText.Text = $"Escuchando {asset.DisplayName}.";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    // The checkbox is an immediate edit for an existing SFX; selecting another block
    // changes the checkbox under _loadingScriptUi and must never modify that block.
    private async void SfxWaitCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingScriptUi || _updatingSfxTiming || _editingScriptBlockId is not Guid id ||
            ScriptBlockTypeCombo.SelectedItem is not ScriptBlockTypeChoice { Kind: ScriptBlockKind.SoundEffect } ||
            _currentRepository is null) return;
        var block = _scriptBlocks.FirstOrDefault(x => x.Id == id);
        if (block is null || SceneComposer.WaitForSound(block) == (SfxWaitCheck.IsChecked == true)) return;
        var sceneId = (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id;
        _updatingSfxTiming = true;
        SfxWaitCheck.IsEnabled = false;
        try
        {
            var changed = block with { ParametersJson = SerializeResourceParameters(ScriptBlockKind.SoundEffect) };
            var blocks = _scriptBlocks.Select(x => x.Id == id ? changed : x).ToArray();
            await RefreshSceneTimingAsync(blocks, id);
            if ((ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id == sceneId)
            {
                ClearScenePreview();
                ScriptStatusText.Text = "Tiempos actualizados. ▶ actualizará el video al reproducir.";
            }
        }
        catch (Exception ex)
        {
            if ((ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id == sceneId)
                SfxWaitCheck.IsChecked = SceneComposer.WaitForSound(block);
            ShowError(ex);
        }
        finally
        {
            _updatingSfxTiming = false;
            SfxWaitCheck.IsEnabled = ScriptBlockTypeCombo.SelectedItem is ScriptBlockTypeChoice { Kind: ScriptBlockKind.SoundEffect };
        }
    }

    private async Task RefreshSceneTimingAsync(IReadOnlyList<SceneScriptBlock> blocks, Guid? selectedBlockId = null)
    {
        if (_currentRepository is null || ScenesList.SelectedItem is not SceneScriptRow scene) return;
        var repository = _currentRepository;
        long clock = 0, end = 0;
        SceneTransition? pendingVisual = null;
        var updated = new List<SceneScriptBlock>(blocks.Count);
        foreach (var original in blocks.OrderBy(x => x.OrderIndex))
        {
            var start = pendingVisual is not null && SceneComposer.IsVisualBlock(original.Kind)
                ? SceneComposer.VisualStart(pendingVisual, original)
                : pendingVisual is { Style: "cambio" or "cruce" } && original.Kind == ScriptBlockKind.CharacterHide
                    ? SceneComposer.HideStart(pendingVisual) : clock;
            var block = original with { StartOffsetMs = start };
            if (IsSpeechBlock(block.Kind)) clock += Math.Max(0,
                block.Kind == ScriptBlockKind.Narration ? SceneComposer.AudioDuration(block) ?? block.GeneratedDurationMs ?? 0
                : block.GeneratedDurationMs ?? 0);
            if (block.Kind == ScriptBlockKind.SoundEffect && block.AssetId is Guid assetId)
            {
                if (!_scriptAssetCache.TryGetValue(assetId, out var asset)) throw new FileNotFoundException($"SFX del bloque #{block.OrderIndex + 1} no catalogado.");
                var path = await ResolveAssetPathAsync(asset);
                if (path is null || !File.Exists(path)) throw new FileNotFoundException($"Falta el SFX del bloque #{block.OrderIndex + 1}.", path);
                var duration = block.GeneratedDurationMs is > 0 ? block.GeneratedDurationMs.Value : await SceneComposer.ProbeDurationAsync(path);
                block = block with { GeneratedDurationMs = duration };
                var effectiveDuration = SceneComposer.AudioDuration(block) ?? duration;
                end = Math.Max(end, (block.StartOffsetMs ?? clock) + effectiveDuration);
                if (SceneComposer.WaitForSound(block)) clock += effectiveDuration;
            }
            if (block.Kind == ScriptBlockKind.Music && SceneComposer.AudioDuration(block) is long musicDuration)
                end = Math.Max(end, (block.StartOffsetMs ?? clock) + musicDuration);
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
            end = Math.Max(end, clock);
            updated.Add(block);
        }
        foreach (var block in updated)
            end = Math.Max(end, await VisualEndMsAsync(block, updated));
        if (_currentRepository != repository || (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id != scene.Scene.Id) return;
        await repository.ReplaceSceneScriptBlocksAsync(scene.Scene.Id, updated);
        var durationMs = Math.Max(clock, end);
        await repository.UpsertSceneAsync(scene.Scene with { DurationMs = durationMs });
        _scriptBlocks = updated;
        _loadingScriptUi = true;
        try
        {
            BindScriptBlockGrid(selectedBlockId ?? _editingScriptBlockId);
            var selectedSceneId = scene.Scene.Id;
            _scenes = _scenes.Select(x => x.Id == selectedSceneId ? x with { DurationMs = durationMs } : x).ToArray();
            ScenesList.ItemsSource = _scenes.Select(x => new SceneScriptRow(x)).ToArray();
            ScenesList.SelectedItem = ScenesList.Items.Cast<SceneScriptRow>().FirstOrDefault(x => x.Scene.Id == selectedSceneId);
        }
        finally { _loadingScriptUi = false; }
        UpdateScriptSceneSummary();
    }

    private async Task<long> VisualEndMsAsync(SceneScriptBlock block, IReadOnlyList<SceneScriptBlock> blocks, CancellationToken token = default)
    {
        var duration = SceneComposer.VisualDuration(block);
        if (duration is null && block.Kind == ScriptBlockKind.Video && block.AssetId is Guid id)
        {
            if (!_scriptAssetCache.TryGetValue(id, out var asset)) throw new FileNotFoundException($"Video del bloque #{block.OrderIndex + 1} no catalogado.");
            var path = await ResolveAssetPathAsync(asset);
            if (path is null || !File.Exists(path)) throw new FileNotFoundException($"Falta el video del bloque #{block.OrderIndex + 1}.", path);
            duration = await SceneComposer.ProbeDurationAsync(path, token);
        }
        if (duration is not long milliseconds) return 0;
        var cutoff = blocks.SkipWhile(x => x.Id != block.Id).Skip(1).Where(x => block.Kind switch
        {
            ScriptBlockKind.Background => x.Kind == ScriptBlockKind.Background,
            ScriptBlockKind.CharacterShow => x.Kind == ScriptBlockKind.CharacterShow &&
                block.CharacterId is not null && x.CharacterId == block.CharacterId ||
                // What an «Ocultar» hides is resolved (1.4.1): a character, or an NPC render named by its resource.
                x.Kind == ScriptBlockKind.CharacterHide && SceneComposer.HideTarget(blocks, x) is var hidden &&
                (block.CharacterId is not null ? hidden.CharacterId == block.CharacterId : hidden.RenderBlockId == block.Id),
            ScriptBlockKind.Image => x.Kind == ScriptBlockKind.Image,
            ScriptBlockKind.Video => SceneComposer.VideoOptions(block).Layer == "fondo" && x.Kind == ScriptBlockKind.Background ||
                x.Kind == ScriptBlockKind.Video && x.StartOffsetMs > block.StartOffsetMs &&
                VideoFollowsTransition(x, blocks) &&
                SceneComposer.VideoOptions(x).Layer == SceneComposer.VideoOptions(block).Layer &&
                blocks.TakeWhile(y => y.Id != x.Id)
                    .Where(y => y.Kind == ScriptBlockKind.Video &&
                        SceneComposer.VideoOptions(y).Layer == SceneComposer.VideoOptions(block).Layer &&
                        y.StartOffsetMs < x.StartOffsetMs).LastOrDefault()?.Id == block.Id,
            _ => false
        }).Select(x => VisualSuccessorTime(x, blocks)).DefaultIfEmpty(long.MaxValue).Min();
        return Math.Min((block.StartOffsetMs ?? 0) + milliseconds, cutoff);
    }

    private static long VisualSuccessorTime(SceneScriptBlock successor, IReadOnlyList<SceneScriptBlock> blocks)
    {
        var start = successor.StartOffsetMs ?? long.MaxValue;
        if (!SceneComposer.IsVisualBlock(successor.Kind)) return start;
        var index = -1;
        for (var i = 0; i < blocks.Count; i++)
            if (blocks[i].Id == successor.Id) { index = i; break; }
        for (var i = index - 1; i >= 0; i--)
        {
            var previous = blocks[i];
            if (previous.Kind == ScriptBlockKind.Transition)
            {
                var (style, durationMs) = SceneComposer.TransitionOptions(previous);
                return style == "cruce" && previous.StartOffsetMs == start ? start + durationMs : start;
            }
            if (!SceneComposer.IsVisualBlock(previous.Kind) &&
                previous.Kind is not (ScriptBlockKind.Comment or ScriptBlockKind.CharacterHide or ScriptBlockKind.Camera or ScriptBlockKind.Cinema or ScriptBlockKind.Gesture or ScriptBlockKind.Blur))
                break;
        }
        return start;
    }

    private static bool VideoFollowsTransition(SceneScriptBlock successor, IReadOnlyList<SceneScriptBlock> blocks)
    {
        for (var i = blocks.Count - 1; i >= 0; i--)
        {
            if (blocks[i].Id != successor.Id) continue;
            for (i--; i >= 0; i--)
            {
                var previous = blocks[i];
                if (previous.Kind == ScriptBlockKind.Transition)
                    return SceneComposer.TransitionOptions(previous).Style == "cruce";
                if (!SceneComposer.IsVisualBlock(previous.Kind) &&
                    previous.Kind is not (ScriptBlockKind.Comment or ScriptBlockKind.CharacterHide or ScriptBlockKind.Camera or ScriptBlockKind.Cinema or ScriptBlockKind.Gesture or ScriptBlockKind.Blur)) break;
            }
            break;
        }
        return false;
    }

    private bool SceneNeedsVoices()
    {
        var characters = _characters.ToDictionary(x => x.Id);
        var profiles = _voiceProfiles.ToDictionary(x => x.Id);
        foreach (var block in _scriptBlocks.Where(x => IsSpeechBlock(x.Kind)))
        {
            if (IsImportedVoice(block))
            {
                if (ResolveGeneratedPath(block.GeneratedAudioPath) is not string imported || !IsValidWave(imported)) return true;
                continue;
            }
            var profile = ResolveVoiceProfile(block, characters, profiles);
            if (profile is null) return true;
            var expected = ComputeVoiceCacheHash(profile, block.Text, block.VoicePitchOverride ?? profile.Pitch,
                block.VoiceSpeedOverride ?? profile.Speed, block.VoiceVolumeOverride ?? profile.Volume);
            if (block.GeneratedAudioHash != expected || ResolveGeneratedPath(block.GeneratedAudioPath) is not string path || !IsValidWave(path)) return true;
        }
        return false;
    }

    private async Task<Dictionary<Guid, string>> ResolveScenePathsAsync(SceneScriptBlock[] blocks, string projectRoot,
        Dictionary<Guid, string>? originalPaths = null)
    {
        var paths = new Dictionary<Guid, string>();
        foreach (var block in blocks)
        {
            if (IsSpeechBlock(block.Kind))
            {
                if (ResolveGeneratedPath(block.GeneratedAudioPath) is string voice) paths[block.Id] = voice;
            }
            else if (block.AssetId is Guid id && block.Kind is (ScriptBlockKind.Background or ScriptBlockKind.CharacterShow or ScriptBlockKind.Image or ScriptBlockKind.Video or ScriptBlockKind.SoundEffect or ScriptBlockKind.Music))
            {
                if (!_scriptAssetCache.TryGetValue(id, out var asset)) throw new FileNotFoundException($"Asset del bloque #{block.OrderIndex + 1} no catalogado.");
                if (await ResolveAssetPathAsync(asset) is string path)
                {
                    var input = block.Kind is (ScriptBlockKind.Background or ScriptBlockKind.CharacterShow or ScriptBlockKind.Image)
                        ? await ScenePngCache.GetInputAsync(path, projectRoot) : path;
                    paths[block.Id] = input;
                    if (input != path && originalPaths is not null) originalPaths[block.Id] = path;
                }
            }
        }
        return paths;
    }

    private static string PreviewFingerprint(SceneScriptBlock[] blocks, IReadOnlyDictionary<Guid, string> paths,
        CinemaState? cinemaStart = null)
    {
        var data = new StringBuilder("scene-preview-v17\n");
        // Bars carried over from the previous scene change the picture too.
        data.Append(JsonSerializer.Serialize(cinemaStart)).Append('\n');
        foreach (var block in blocks.OrderBy(x => x.OrderIndex))
        {
            data.Append(JsonSerializer.Serialize(new { block.Id, block.OrderIndex, block.Kind, block.CharacterId,
                block.Text, block.AssetId, block.VoiceProfileId, block.VoicePitchOverride, block.VoiceSpeedOverride,
                block.VoiceVolumeOverride, block.PauseAfterMs, block.ParametersJson, block.GeneratedAudioPath, block.GeneratedAudioHash })).Append('\n');
            if (paths.TryGetValue(block.Id, out var path))
            {
                var file = new FileInfo(path);
                if (!file.Exists) throw new FileNotFoundException($"Falta el archivo del bloque #{block.OrderIndex + 1}.", path);
                data.Append(file.FullName).Append('|').Append(file.Length).Append('|').Append(file.LastWriteTimeUtc.Ticks).Append('\n');
            }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data.ToString())));
    }

    private async void ToggleScenePreview_Click(object sender, RoutedEventArgs e)
    {
        if (_previewPreparing) return;
        if (_scenePreviewPlaying)
        {
            ScenePreviewPlayer.Pause();
            _scenePreviewPlaying = false;
            ToggleScenePreviewButton.Content = "▶ Reproducir";
            return;
        }
        if (_currentRepository is null || ScenesList.SelectedItem is not SceneScriptRow scene || _scriptBlocks.Count == 0) return;
        _previewPreparing = true;
        ToggleScenePreviewButton.IsEnabled = false;
        ToggleScenePreviewButton.Content = "Preparando…";
        _sceneRenderCancellation = new CancellationTokenSource();
        var token = _sceneRenderCancellation.Token;
        var repository = _currentRepository;
        var sceneId = scene.Scene.Id;
        try
        {
            if (SceneNeedsVoices() && !await GenerateSceneVoicesAsync()) return;
            if (_currentRepository != repository || (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id != sceneId) return;
            var blocks = _scriptBlocks.ToArray();
            var pendingVisuals = blocks.Any(x => x.Kind == ScriptBlockKind.TextOverlay)
                ? " Texto en pantalla aún no se compone." : string.Empty;
            ScriptStatusText.Text = "Preparando escena…";
            var paths = await ResolveScenePathsAsync(blocks, repository.ProjectRoot);
            var cinemaStart = await CinemaAtSceneStartAsync(repository, sceneId);
            var fingerprint = PreviewFingerprint(blocks, paths, cinemaStart);
            // WPF can keep the current MP4 open after Source is cleared. Each distinct
            // composition gets a different path so FFmpeg never replaces a playing file.
            var output = Path.Combine(repository.ProjectRoot, "generated", "previews",
                $"scene_{sceneId:N}_{fingerprint[..16]}.mp4");
            var reusable = File.Exists(output) && new FileInfo(output).Length > 100;
            if (reusable)
            {
                if (_scenePreviewPath != output || _scenePreviewSignature != fingerprint || _scenePreviewSceneId != sceneId)
                {
                    ClearScenePreview();
                    _scenePreviewPath = output;
                    _scenePreviewSignature = fingerprint;
                    _scenePreviewSceneId = sceneId;
                    ScenePreviewPlayer.Source = new Uri(output);
                }
                else if (ScenePreviewPlayer.NaturalDuration.HasTimeSpan && ScenePreviewPlayer.Position >= ScenePreviewPlayer.NaturalDuration.TimeSpan)
                    ScenePreviewPlayer.Position = TimeSpan.Zero;
                ScenePreviewPlayer.Play();
                _scenePreviewPlaying = true;
                ToggleScenePreviewButton.Content = "❚❚ Pausar";
                ScriptStatusText.Text = "Reproduciendo escena existente." + pendingVisuals;
                return;
            }
            var composition = await SceneComposer.PlanAsync(blocks, b => paths.GetValueOrDefault(b.Id), token,
                b => b.AssetId is Guid id && _scriptAssetCache.TryGetValue(id, out var asset) &&
                    SceneComposer.LooksLikeGreenScreen(asset.DisplayName), cinemaStart);
            ScriptStatusText.Text = "Componiendo MP4 con FFmpeg…";
            ClearScenePreview();
            await SceneComposer.RenderAsync(composition, output, token);
            if (_currentRepository != repository || (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id != sceneId) return;
            if (!blocks.SequenceEqual(_scriptBlocks))
            {
                ScriptStatusText.Text = "El guion cambió durante la composición. Pulsa ▶ para generar su versión actual.";
                return;
            }
            var byBlock = composition.Media.ToDictionary(x => x.BlockId);
            var updated = blocks.Select(block => byBlock.TryGetValue(block.Id, out var clip)
                ? block with { StartOffsetMs = clip.StartMs, GeneratedDurationMs = block.Kind == ScriptBlockKind.SoundEffect ? clip.DurationMs : block.GeneratedDurationMs }
                : block).ToArray();
            await repository.ReplaceSceneScriptBlocksAsync(sceneId, updated);
            await repository.UpsertSceneAsync(scene.Scene with { DurationMs = composition.DurationMs });
            await RefreshScriptAsync(scene.Scene.EpisodeId, sceneId);
            _scenePreviewPath = output;
            _scenePreviewSignature = fingerprint;
            _scenePreviewSceneId = sceneId;
            ScenePreviewPlayer.Source = new Uri(output);
            ScenePreviewPlayer.Play();
            _scenePreviewPlaying = true;
            ToggleScenePreviewButton.Content = "❚❚ Pausar";
            ScriptStatusText.Text = $"Escena actualizada: {FormatTimelineTime(composition.DurationMs)}." + pendingVisuals;
        }
        catch (OperationCanceledException) { ScriptStatusText.Text = "Preparación cancelada."; }
        catch (Exception ex) { ScriptStatusText.Text = ErrorLog.Summary(ex); ShowError(ex); }
        finally
        {
            _previewPreparing = false;
            ToggleScenePreviewButton.IsEnabled = true;
            ToggleScenePreviewButton.Content = _scenePreviewPlaying ? "❚❚ Pausar" : "▶ Reproducir";
            _sceneRenderCancellation?.Dispose();
            _sceneRenderCancellation = null;
        }
    }

    private void ClearScenePreview()
    {
        ScenePreviewPlayer.Stop();
        ScenePreviewPlayer.Source = null;
        _scenePreviewPath = null;
        _scenePreviewSignature = null;
        _scenePreviewSceneId = null;
        _scenePreviewPlaying = false;
        ToggleScenePreviewButton.Content = "▶ Reproducir";
        EndSeekDrag(resume: false);
        _updatingSeekSlider = true;
        try
        {
            SceneSeekSlider.Value = 0;
            SceneSeekSlider.Maximum = 1; // until the next video reports its length
        }
        finally { _updatingSeekSlider = false; }
        ScenePreviewTimeText.Text = "00:00.000 / 00:00.000";
    }

    private void ScenePreview_MediaOpened(object sender, RoutedEventArgs e)
    {
        // Programmatic: a shorter video must not «seek» to the clamped value of the previous one.
        _updatingSeekSlider = true;
        try { SceneSeekSlider.Maximum = Math.Max(1, ScenePreviewPlayer.NaturalDuration.HasTimeSpan ? ScenePreviewPlayer.NaturalDuration.TimeSpan.TotalMilliseconds : 1); }
        finally { _updatingSeekSlider = false; }
        UpdateScenePreviewTime();
    }

    private void ScenePreview_MediaEnded(object sender, RoutedEventArgs e)
    {
        ScenePreviewPlayer.Pause();
        _scenePreviewPlaying = false;
        ToggleScenePreviewButton.Content = "▶ Reproducir";
        UpdateScenePreviewTime();
    }

    private void StopScenePreview_Click(object sender, RoutedEventArgs e)
    {
        ScenePreviewPlayer.Stop();
        _scenePreviewPlaying = false;
        ToggleScenePreviewButton.Content = "▶ Reproducir";
        UpdateScenePreviewTime();
    }

    private void OpenSceneVideo_Click(object sender, RoutedEventArgs e)
    {
        if (_scenePreviewPath is not null && File.Exists(_scenePreviewPath))
            Process.Start(new ProcessStartInfo(_scenePreviewPath) { UseShellExecute = true });
    }

    private async void ExportVegas_Click(object sender, RoutedEventArgs e)
    {
        if (_previewPreparing || _currentRepository is null ||
            ScenesList.SelectedItem is not SceneScriptRow scene || _scriptBlocks.Count == 0) return;
        _previewPreparing = true;
        var button = (Button)sender;
        button.IsEnabled = false;
        var repository = _currentRepository;
        var sceneId = scene.Scene.Id;
        try
        {
            if (SceneNeedsVoices() && !await GenerateSceneVoicesAsync()) return;
            if (_currentRepository != repository || (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id != sceneId) return;
            var blocks = _scriptBlocks.ToArray();
            var originalPaths = new Dictionary<Guid, string>();
            var paths = await ResolveScenePathsAsync(blocks, repository.ProjectRoot, originalPaths);
            var cinemaStart = await CinemaAtSceneStartAsync(repository, sceneId);
            var fingerprint = PreviewFingerprint(blocks, paths, cinemaStart);
            var composition = await SceneComposer.PlanAsync(blocks, b => paths.GetValueOrDefault(b.Id),
                suggestedGreenScreen: b => b.AssetId is Guid id && _scriptAssetCache.TryGetValue(id, out var asset) &&
                    SceneComposer.LooksLikeGreenScreen(asset.DisplayName), cinemaStart: cinemaStart);
            if (_currentRepository != repository || (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id != sceneId ||
                !blocks.SequenceEqual(_scriptBlocks))
            {
                ScriptStatusText.Text = "La escena cambió durante la exportación. Vuelve a exportarla.";
                return;
            }
            var exportHeight = (VegasExportResolutionCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "1080" ? 1080 : 720;
            var mode = (VegasExportModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "Legacy"
                ? VegasExportMode.Legacy : VegasExportMode.Normal;
            var audioMode = SelectedVegasAudioMode();
            var folder = Path.Combine(repository.ProjectRoot, "generated", "vegas",
                $"scene_{sceneId:N}_{fingerprint[..16]}_v15_{exportHeight}p_{mode.ToString().ToLowerInvariant()}_{AudioModeTag(audioMode)}");
            ScriptStatusText.Text = $"Exportando {mode} a {exportHeight}p para VEGAS…";
            await VegasBridge.ExportAsync(composition, folder, scene.Scene.Title, exportHeight,
                originalPaths: originalPaths, mode: mode, audioMode: audioMode,
                characterNames: _characters.ToDictionary(x => x.Id, x => x.Name));
            if (_currentRepository != repository || (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id != sceneId ||
                !blocks.SequenceEqual(_scriptBlocks)) return;
            ScriptStatusText.Text = "Exportación preparada. Abre LEEME.txt y el script de tu versión de VEGAS.";
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) { ScriptStatusText.Text = ErrorLog.Summary(ex); ShowError(ex); }
        finally { button.IsEnabled = true; _previewPreparing = false; }
    }

    private VegasAudioTrackMode SelectedVegasAudioMode() =>
        (VegasAudioModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "Legacy"
            ? VegasAudioTrackMode.Legacy : VegasAudioTrackMode.PerCharacter;

    private static string AudioModeTag(VegasAudioTrackMode mode) =>
        mode == VegasAudioTrackMode.Legacy ? "audio-legacy" : "audio-personaje";

    /// <summary>Selects a scene of the current episode and loads its blocks without going through
    /// the selection handler, so batch operations reuse the single-scene voice/preview code.</summary>
    private async Task<bool> SelectSceneForBatchAsync(Guid sceneId)
    {
        var row = ScenesList.Items.OfType<SceneScriptRow>().FirstOrDefault(x => x.Scene.Id == sceneId);
        if (row is null) return false;
        if (!ReferenceEquals(ScenesList.SelectedItem, row))
        {
            _loadingScriptUi = true;
            try { ScenesList.SelectedItem = row; ScenesList.ScrollIntoView(row); }
            finally { _loadingScriptUi = false; }
            UpdateDirectorDraftTarget(false);
            LoadSceneForm(row.Scene);
        }
        await LoadBlocksAsync(sceneId);
        return true;
    }

    /// <summary>
    /// Exports every scene of the selected episode into ONE VEGAS project, one after another,
    /// with a named region per scene. Missing TTS voices are generated scene by scene first.
    /// </summary>
    private async void ExportEpisodeVegas_Click(object sender, RoutedEventArgs e)
    {
        if (_previewPreparing || _currentRepository is not { } repository ||
            EpisodesList.SelectedItem is not EpisodeScriptRow episodeRow) return;
        _previewPreparing = true;
        var button = (Button)sender;
        button.IsEnabled = false;
        var returnTo = (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id;
        try
        {
            var sceneIds = _scenes.OrderBy(x => x.Index).Select(x => x.Id).ToArray();
            var inputs = new List<VegasSceneInput>();
            var fingerprints = new StringBuilder();
            CinemaState? cinema = null; // bars carried from scene to scene
            for (var i = 0; i < sceneIds.Length; i++)
            {
                if (_currentRepository != repository || (EpisodesList.SelectedItem as EpisodeScriptRow)?.Episode.Id != episodeRow.Episode.Id)
                {
                    ScriptStatusText.Text = "El episodio cambió durante la exportación. Vuelve a exportarlo.";
                    return;
                }
                if (!await SelectSceneForBatchAsync(sceneIds[i])) continue;
                var title = (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Title ?? $"Escena {i + 1}";
                if (_scriptBlocks.Count == 0) continue;
                ScriptStatusText.Text = $"Episodio: preparando «{title}» ({i + 1}/{sceneIds.Length})…";
                if (SceneNeedsVoices() && !await GenerateSceneVoicesAsync())
                {
                    ScriptStatusText.Text = $"No se pudieron generar las voces de «{title}». Revísalas y vuelve a exportar el episodio.";
                    return;
                }
                if (_currentRepository != repository) return;
                var blocks = _scriptBlocks.ToArray();
                var originalPaths = new Dictionary<Guid, string>();
                var paths = await ResolveScenePathsAsync(blocks, repository.ProjectRoot, originalPaths);
                fingerprints.Append(PreviewFingerprint(blocks, paths, cinema)).Append('\n');
                var composition = await SceneComposer.PlanAsync(blocks, b => paths.GetValueOrDefault(b.Id),
                    suggestedGreenScreen: b => b.AssetId is Guid id && _scriptAssetCache.TryGetValue(id, out var asset) &&
                        SceneComposer.LooksLikeGreenScreen(asset.DisplayName), cinemaStart: cinema);
                cinema = CinemaPlan.EndState(blocks, cinema);
                inputs.Add(new VegasSceneInput(title, composition, originalPaths));
            }
            if (inputs.Count == 0)
            {
                ScriptStatusText.Text = "El episodio no tiene escenas con bloques para exportar.";
                return;
            }
            var exportHeight = (VegasExportResolutionCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "1080" ? 1080 : 720;
            var mode = (VegasExportModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "Legacy"
                ? VegasExportMode.Legacy : VegasExportMode.Normal;
            var audioMode = SelectedVegasAudioMode();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprints.ToString())))[..16];
            var folder = Path.Combine(repository.ProjectRoot, "generated", "vegas",
                $"episode_{episodeRow.Episode.Id:N}_{hash}_v15_{exportHeight}p_{mode.ToString().ToLowerInvariant()}_{AudioModeTag(audioMode)}");
            var name = $"Episodio {episodeRow.Episode.Number:00} - {episodeRow.Episode.Title}";
            ScriptStatusText.Text = $"Exportando {inputs.Count} escenas a un solo proyecto VEGAS ({exportHeight}p, {mode})…";
            await VegasBridge.ExportEpisodeAsync(inputs, folder, name, exportHeight, mode: mode, audioMode: audioMode,
                characterNames: _characters.ToDictionary(x => x.Id, x => x.Name));
            ScriptStatusText.Text = $"Episodio preparado: {inputs.Count} escenas en un solo proyecto. Abre LEEME.txt y el script de tu versión de VEGAS.";
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex) { ScriptStatusText.Text = ErrorLog.Summary(ex); ShowError(ex); }
        finally
        {
            button.IsEnabled = true;
            _previewPreparing = false;
            if (returnTo is Guid sceneId && _currentRepository == repository)
            {
                try { await SelectSceneForBatchAsync(sceneId); }
                catch (Exception) { /* The export result matters more than restoring the selection. */ }
            }
        }
    }
}
