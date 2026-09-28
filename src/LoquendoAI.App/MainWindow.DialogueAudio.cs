using System.IO;
using System.Windows;
using System.Windows.Media;
using LoquendoAI.Infrastructure.Dialogue;
using LoquendoAI.Infrastructure.Tts;

namespace LoquendoAI.App;

/// <summary>
/// Audio of the dialogue module: TTS generation into the shared voice cache (the same file a scene will use),
/// listening to one line, playing the whole script in a row, and stopping.
/// </summary>
public partial class MainWindow
{
    private readonly MediaPlayer _dialoguePlayer = new();
    private readonly Queue<DialogueRow> _dialogueQueue = new();
    private DialogueRow? _dialoguePlaying;
    private CancellationTokenSource? _dialoguePlayback;
    private CancellationTokenSource? _dialogueWork;

    private void InitializeDialoguePlayback()
    {
        _dialoguePlayer.MediaEnded += (_, _) => _ = ContinueDialoguePlaybackAsync(ended: true);
        _dialoguePlayer.MediaFailed += (_, e) =>
        {
            DialogueStatusText.Text = "No se pudo reproducir el audio: " + e.ErrorException?.Message;
            _ = ContinueDialoguePlaybackAsync(ended: true);
        };
    }

    // ───────────────────────────── Generation ─────────────────────────────

    private async void GenerateDialogueAudio_Click(object sender, RoutedEventArgs e)
    {
        CommitDialogueEdits();
        await GenerateDialogueAudioAsync(_dialogueRows.Where(x => x.IsLine).ToArray(), force: false);
    }

    private async void RegenerateDialogueAudio_Click(object sender, RoutedEventArgs e)
    {
        CommitDialogueEdits();
        var rows = SelectedDialogueRows().Where(x => x.IsLine).ToArray();
        if (rows.Length == 0)
        {
            DialogueStatusText.Text = "Selecciona las líneas que quieres volver a sintetizar.";
            return;
        }
        StopDialoguePlayback(); // the player keeps the file it played open, and it is about to be replaced
        await GenerateDialogueAudioAsync(rows, force: true);
    }

    private void CancelDialogueWork_Click(object sender, RoutedEventArgs e) => _dialogueWork?.Cancel();

    /// <summary>
    /// Synthesizes the lines without audio (or all given ones with <paramref name="force"/>), several at a time like
    /// «Generar voces», each into the voice cache. Lines with the same voice and text share one synthesis.
    /// Returns true when every line has its audio.
    /// </summary>
    private async Task<bool> GenerateDialogueAudioAsync(IReadOnlyList<DialogueRow> rows, bool force)
    {
        if (_currentRepository is not { } repository || _dialogueWork is not null) return false;
        var root = repository.ProjectRoot;
        var jobs = new Dictionary<string, (DialogueVoice Voice, List<DialogueRow> Rows)>(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var row in rows)
        {
            RefreshDialogueRow(row);
            if (row.Text.Length == 0) { problems.Add($"#{row.Order}: sin texto"); continue; }
            if (ResolveDialogueVoice(row) is not { } voice) { problems.Add($"#{row.Order}: sin voz"); continue; }
            if (!force && VoiceCache.Find(root, voice.Hash, IsValidWave) is not null) continue;
            if (jobs.TryGetValue(voice.Hash, out var job)) job.Rows.Add(row);
            else jobs[voice.Hash] = (voice, [row]);
        }
        if (jobs.Count == 0)
        {
            RefreshAllDialogueRows();
            DialogueStatusText.Text = problems.Count == 0 ? "Todas las líneas ya tienen audio." : "Sin audio: " + string.Join(", ", problems.Take(8));
            return problems.Count == 0;
        }
        if (_ttsBridge.BridgePath is null)
        {
            DialogueStatusText.Text = $"Faltan {jobs.Count} audio(s) y no se encontró el TTS bridge x86 (scripts\\tts-publish.cmd).";
            return false;
        }

        using var cancellation = new CancellationTokenSource();
        _dialogueWork = cancellation;
        SetDialogueBusy(true);
        var done = 0;
        var failed = new List<string>();
        var parallelism = Math.Max(1, TtsParallelism);
        var staging = Path.Combine(VoiceCache.Folder(root), "_staging");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        DialogueStatusText.Text = $"Generando {jobs.Count} audio(s) ({parallelism} a la vez)…";
        try
        {
            using var gate = new SemaphoreSlim(parallelism);
            var tasks = jobs.Select(async pair =>
            {
                var (voice, jobRows) = pair.Value;
                await gate.WaitAsync(cancellation.Token);
                var output = Path.Combine(staging, pair.Key + "." + Guid.NewGuid().ToString("N")[..8] + ".wav");
                try
                {
                    foreach (var row in jobRows) row.StateText = "Generando…";
                    if (force)
                    {
                        var old = VoiceCache.PathFor(root, pair.Key);
                        try { File.Delete(old); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                        if (File.Exists(old)) throw new IOException("el audio anterior está en uso (¿se está reproduciendo en otra parte?)");
                    }
                    await _ttsBridge.SynthesizeAsync(new TtsPreviewRequest(voice.Profile.ProviderKey, voice.Profile.VoiceId,
                        jobRows[0].Text, output, voice.Pitch, voice.Speed, voice.Volume, voice.Profile.SampleRate), cancellation.Token);
                    if (!IsValidWave(output)) throw new InvalidDataException("el TTS no dejó un WAV válido");
                    VoiceCache.Store(root, pair.Key, output);
                    if (!IsValidWave(VoiceCache.PathFor(root, pair.Key))) throw new IOException("no se pudo guardar en la caché de voces");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    failed.Add($"#{jobRows[0].Order}: {ex.Message.Replace('\r', ' ').Replace('\n', ' ')}");
                    AiDiagnostics.Note($"dialogos: fallo TTS ({voice.Profile.ProviderKey}/{voice.Profile.VoiceId}): {ex.Message}");
                }
                finally
                {
                    gate.Release();
                    try { if (File.Exists(output)) File.Delete(output); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                    foreach (var row in jobRows) if (_dialogueRows.Contains(row)) RefreshDialogueRow(row);
                    DialogueStatusText.Text = $"Generando audios: {++done}/{jobs.Count}…";
                }
            }).ToArray();
            await Task.WhenAll(tasks);
            DialogueStatusText.Text = failed.Count == 0
                ? $"Audios listos: {jobs.Count} generado(s) en {clock.Elapsed.TotalSeconds:0.#} s." +
                  (problems.Count > 0 ? " Sin audio: " + string.Join(", ", problems.Take(6)) : "")
                : $"{jobs.Count - failed.Count}/{jobs.Count} generados. Fallaron: {string.Join(" · ", failed.Take(4))}";
            if (failed.Count > 0)
                MessageBox.Show(this, string.Join(Environment.NewLine, failed.Take(12)), "Generar audios", MessageBoxButton.OK, MessageBoxImage.Warning);
            return failed.Count == 0 && problems.Count == 0;
        }
        catch (OperationCanceledException)
        {
            DialogueStatusText.Text = $"Generación cancelada ({done}/{jobs.Count}). Los audios terminados se conservan.";
            return false;
        }
        finally
        {
            _dialogueWork = null;
            SetDialogueBusy(false);
            RefreshAllDialogueRows();
            MarkDialogueDirty();
        }
    }

    private void SetDialogueBusy(bool busy)
    {
        DialogueCancelButton.IsEnabled = busy;
        DialogueGenerateButton.IsEnabled = DialogueAiButton.IsEnabled = DialogueSetCombo.IsEnabled = !busy;
        DialogueGrid.IsReadOnly = busy; // cells are not edited under running work (listening still works)
    }

    // ───────────────────────────── Playback ─────────────────────────────

    private void PlayDialogueRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DialogueRow row || !row.IsLine) return;
        CommitDialogueEdits();
        StopDialoguePlayback();
        DialogueGrid.SelectedItem = row;
        _dialogueQueue.Enqueue(row);
        _ = ContinueDialoguePlaybackAsync(ended: false);
    }

    private void PlayAllDialogue_Click(object sender, RoutedEventArgs e)
    {
        CommitDialogueEdits();
        StopDialoguePlayback();
        var start = DialogueGrid.SelectedItem is DialogueRow selected ? Math.Max(0, _dialogueRows.IndexOf(selected)) : 0;
        foreach (var row in _dialogueRows.Skip(start).Where(x => x.IsLine && x.Text.Length > 0)) _dialogueQueue.Enqueue(row);
        if (_dialogueQueue.Count == 0)
        {
            DialogueStatusText.Text = "No hay líneas con texto para reproducir.";
            return;
        }
        DialogueStatusText.Text = $"Reproduciendo {_dialogueQueue.Count} línea(s) de corrido…";
        _ = ContinueDialoguePlaybackAsync(ended: false);
    }

    private void StopDialoguePlayback_Click(object sender, RoutedEventArgs e)
    {
        StopDialoguePlayback();
        DialogueStatusText.Text = "Reproducción detenida.";
    }

    private void StopDialoguePlayback()
    {
        _dialoguePlayback?.Cancel();
        _dialoguePlayback = null;
        _dialogueQueue.Clear();
        _dialoguePlayer.Stop();
        _dialoguePlayer.Close();
        if (_dialoguePlaying is { } row)
        {
            row.IsPlaying = false;
            if (_dialogueRows.Contains(row)) RefreshDialogueRow(row);
        }
        _dialoguePlaying = null;
    }

    /// <summary>Plays the next queued line: generates its audio first when it has none (a line at a time, so the
    /// first one starts at once), selects it and plays it; the end of a line starts the next one.</summary>
    private async Task ContinueDialoguePlaybackAsync(bool ended)
    {
        if (_dialoguePlaying is { } previous)
        {
            previous.IsPlaying = false;
            if (_dialogueRows.Contains(previous)) RefreshDialogueRow(previous);
            _dialoguePlaying = null;
        }
        if (ended && _dialogueQueue.Count == 0)
        {
            DialogueStatusText.Text = "Reproducción terminada.";
            return;
        }
        _dialoguePlayback ??= new CancellationTokenSource();
        var token = _dialoguePlayback.Token;
        try
        {
            if (ended) await Task.Delay(180, token); // a short breath between lines
            while (_dialogueQueue.Count > 0 && !token.IsCancellationRequested)
            {
                var row = _dialogueQueue.Dequeue();
                if (!_dialogueRows.Contains(row)) continue;
                var path = await DialogueAudioPathAsync(row, token);
                if (token.IsCancellationRequested) return;
                if (path is null) continue; // the reason is in the status bar; go on with the next line
                _dialoguePlaying = row;
                row.IsPlaying = true;
                RefreshDialogueRow(row);
                DialogueGrid.SelectedItem = row;
                DialogueGrid.ScrollIntoView(row);
                _dialoguePlayer.Close();
                _dialoguePlayer.Volume = 1;
                _dialoguePlayer.Open(new Uri(path, UriKind.Absolute));
                _dialoguePlayer.Play();
                return;
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>The WAV of a line in the voice cache, synthesized now if it is missing.</summary>
    private async Task<string?> DialogueAudioPathAsync(DialogueRow row, CancellationToken token)
    {
        if (_currentRepository is not { } repository) return null;
        RefreshDialogueRow(row);
        if (ResolveDialogueVoice(row) is not { } voice)
        {
            DialogueStatusText.Text = $"Línea {row.Order}: sin texto o sin voz.";
            return null;
        }
        var cached = VoiceCache.Find(repository.ProjectRoot, voice.Hash, IsValidWave);
        if (cached is not null) return cached;
        if (_ttsBridge.BridgePath is null)
        {
            DialogueStatusText.Text = "No se encontró el TTS bridge x86 (scripts\\tts-publish.cmd).";
            return null;
        }
        var output = Path.Combine(VoiceCache.Folder(repository.ProjectRoot), "_staging", voice.Hash + "." + Guid.NewGuid().ToString("N")[..8] + ".wav");
        try
        {
            row.StateText = "Generando…";
            DialogueStatusText.Text = $"Generando la línea {row.Order}…";
            await _ttsBridge.SynthesizeAsync(new TtsPreviewRequest(voice.Profile.ProviderKey, voice.Profile.VoiceId, row.Text, output,
                voice.Pitch, voice.Speed, voice.Volume, voice.Profile.SampleRate), token);
            VoiceCache.Store(repository.ProjectRoot, voice.Hash, output);
            var path = VoiceCache.PathFor(repository.ProjectRoot, voice.Hash);
            if (!IsValidWave(path)) throw new InvalidDataException("el TTS no dejó un WAV válido");
            MarkDialogueDirty();
            return path;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            DialogueStatusText.Text = $"Línea {row.Order}: {ex.Message}";
            return null;
        }
        finally
        {
            try { if (File.Exists(output)) File.Delete(output); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            if (_dialogueRows.Contains(row)) RefreshDialogueRow(row);
            UpdateDialogueStats();
        }
    }
}
