using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;
using LoquendoAI.Infrastructure.Tts;
using Microsoft.Win32;

namespace LoquendoAI.App;

public partial class MainWindow
{
    // A distinct hash prefix makes imported takes independent of TTS cache keys.
    private const string ImportedVoicePrefix = "imported-sha256:";
    private static bool IsImportedVoice(SceneScriptBlock block) =>
        block.Kind is ScriptBlockKind.Dialogue or ScriptBlockKind.Narration &&
        block.GeneratedAudioHash?.StartsWith(ImportedVoicePrefix, StringComparison.Ordinal) == true;

    /// <summary>A line that belongs to the recorded voices: its WAV is in use, or it was regenerated
    /// with TTS and still keeps the recording for «Restaurar grabación».</summary>
    private static bool IsRecordedTake(SceneScriptBlock block) =>
        IsImportedVoice(block) || block.Kind is ScriptBlockKind.Dialogue or ScriptBlockKind.Narration &&
        BlockParameters.Of(block).RecordedTake is not null;

    private readonly ObservableCollection<RecordedVoiceRow> _recordedVoices = [];
    private Guid? _recordedVoiceSceneId;
    private LoquendoAI.Infrastructure.Persistence.SqliteProjectRepository? _recordedVoiceRepository;
    private CancellationTokenSource? _sttCancellation;

    private void PrepareRecordedVoiceScene(SceneScriptRow scene)
    {
        if (_recordedVoiceSceneId != scene.Scene.Id || _recordedVoiceRepository != _currentRepository)
        {
            _recordedVoices.Clear();
            RecordedVoiceNotesBox.Clear();
            _recordedVoiceSceneId = scene.Scene.Id;
            _recordedVoiceRepository = _currentRepository;
        }
        RecordedVoicesGrid.ItemsSource = _recordedVoices;
        RefreshRecordedVoiceCharacterChoices((RecordedVoiceCharacterCombo.SelectedItem as CharacterChoice)?.Id);
        RefreshRecordedVoiceProfileChoices();
    }

    private void RefreshRecordedVoiceCharacterChoices(Guid? selectId)
    {
        var choices = new[] { new CharacterChoice(null, "Narrador") }
            .Concat(_characters.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(x => new CharacterChoice(x.Id, x.Name))).ToArray();
        RecordedVoiceCharacterCombo.ItemsSource = choices;
        RecordedVoiceCharacterCombo.SelectedItem = choices.FirstOrDefault(x => x.Id == selectId) ?? choices[0];
    }

    /// <summary>
    /// In recorded mode a character is only a name label: the WAV is the voice. Typing a new name
    /// creates a character without TTS profile (Voice Lab can add one later), so the Director
    /// knows who speaks and can pair the takes with renders from folders of that name.
    /// </summary>
    private async Task<CharacterChoice?> EnsureRecordedVoiceCharacterAsync(string name)
    {
        name = name.Trim();
        if (name.Length == 0) return null;
        if (SameDirectorName(name, "Narrador")) return new CharacterChoice(null, "Narrador");
        var existing = _characters.FirstOrDefault(x => SameDirectorName(x.Name, name));
        if (existing is not null) return new CharacterChoice(existing.Id, existing.Name);
        if (name.Length > 60 || name.Contains(':') || name.Contains('|'))
        {
            RecordedVoiceStatusText.Text = "Nombre de personaje no válido: máximo 60 caracteres, sin «:» ni «|».";
            return null;
        }
        if (_currentRepository is not { } repository) return null;
        var character = new CharacterDefinition(Guid.NewGuid(), name, null, "Creado desde Voces grabadas (sin voz TTS)");
        await repository.UpsertCharacterAsync(character);
        if (_currentRepository != repository) return null;
        await RefreshVoiceLabAsync();
        RefreshRecordedVoiceCharacterChoices(character.Id);
        RecordedVoiceStatusText.Text = $"Personaje «{name}» creado como etiqueta. La voz TTS es opcional (Voice Lab).";
        return new CharacterChoice(character.Id, character.Name);
    }

    /// <summary>Finds a registered character named in the file (see DirectorScript.CharacterFromFileName).</summary>
    private CharacterDefinition? DetectCharacterFromFileName(string file) => CharacterFromFileName(file, _characters);

    private void AddRecordedVoices_Click(object sender, RoutedEventArgs e)
    {
        if (_sttCancellation is not null) return;
        if (_currentRepository is null || ScenesList.SelectedItem is not SceneScriptRow selectedScene)
        {
            RecordedVoiceStatusText.Text = "Selecciona primero un proyecto y una escena.";
            return;
        }
        var dialog = new OpenFileDialog
        {
            Title = "Seleccionar voces grabadas",
            Filter = "Voces WAV (*.wav)|*.wav",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;
        PrepareRecordedVoiceScene(selectedScene);
        var detected = 0;
        foreach (var file in dialog.FileNames)
        {
            if (_recordedVoices.Any(x => string.Equals(x.Path, file, StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                if (!IsValidWave(file)) throw new InvalidDataException("El archivo no contiene audio WAV válido.");
                var speaker = DetectCharacterFromFileName(file);
                _recordedVoices.Add(new RecordedVoiceRow(file, GetWaveDurationMs(file))
                {
                    Order = _recordedVoices.Count + 1,
                    CharacterId = speaker?.Id,
                    CharacterName = speaker?.Name ?? "Narrador"
                });
                if (speaker is not null) detected++;
            }
            catch (Exception ex)
            {
                RecordedVoiceStatusText.Text = $"No se pudo agregar {Path.GetFileName(file)}: {ex.Message}";
            }
        }
        if (_recordedVoices.Count > 0)
            RecordedVoiceStatusText.Text = $"{_recordedVoices.Count} toma(s) listas" +
                (detected > 0 ? $"; {detected} con personaje reconocido por el nombre del archivo" : "") +
                ". Revisa orden, personajes y texto.";
    }

    private async void LoadSceneRecordedVoices_Click(object sender, RoutedEventArgs e)
    {
        if (_sttCancellation is not null) return;
        if (_currentRepository is null || ScenesList.SelectedItem is not SceneScriptRow scene)
        {
            RecordedVoiceStatusText.Text = "Selecciona primero un proyecto y una escena.";
            return;
        }
        var repository = _currentRepository;
        try
        {
            var blocks = await repository.GetSceneScriptBlocksAsync(scene.Scene.Id);
            if (_currentRepository != repository || (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id != scene.Scene.Id) return;
            PrepareRecordedVoiceScene(scene);
            var count = 0;
            foreach (var block in blocks.Where(IsImportedVoice))
            {
                if (_recordedVoices.Any(x => x.ExistingBlockId == block.Id)) continue;
                var path = ResolveGeneratedPath(block.GeneratedAudioPath);
                if (path is null || !IsValidWave(path))
                {
                    RecordedVoiceStatusText.Text = $"No se puede abrir la toma del bloque {block.OrderIndex + 1}. Comprueba el WAV en el proyecto.";
                    continue;
                }
                var parameters = BlockParameters.Of(block);
                var profile = _voiceProfiles.FirstOrDefault(x => x.Id == block.VoiceProfileId);
                var directVoice = DirectVoiceOf(block);
                _recordedVoices.Add(new RecordedVoiceRow(path, GetWaveDurationMs(path))
                {
                    Order = _recordedVoices.Count + 1, ExistingBlockId = block.Id,
                    CharacterId = block.CharacterId,
                    CharacterName = _characters.FirstOrDefault(x => x.Id == block.CharacterId)?.Name ?? "Narrador",
                    ProfileId = block.VoiceProfileId, Direct = directVoice,
                    VoiceName = profile is not null && IsNpcProfile(profile.Id) && directVoice is not null ? $"{profile.Name} · {directVoice.Label}"
                        : profile?.Name ?? directVoice?.Label ?? "",
                    Transcript = block.Text, IsReviewed = parameters.TranscriptReviewed ?? false,
                    TranscriptionState = string.IsNullOrWhiteSpace(block.Text) ? "Sin texto" : "Texto guardado",
                    TranscriptOrigin = parameters.TranscriptOrigin ?? "manual"
                });
                count++;
            }
            RecordedVoiceStatusText.Text = $"{count} toma(s) existentes cargadas. Corrige la transcripción y guarda para actualizar la escena.";
        }
        catch (Exception ex) { RecordedVoiceStatusText.Text = ex.Message; ShowError(ex); }
    }

    private static string VoiceParameters(RecordedVoiceRow row, string? previous = null)
    {
        var parameters = WithDirect(BlockParameters.Parse(previous), row.Direct) with
        {
            TranscriptReviewed = row.IsReviewed,
            TranscriptOrigin = row.TranscriptOrigin
        };
        // Word timing from the last transcription: where speech starts/ends in the WAV and each word,
        // kept for subtitles, silence trimming or lip-sync. Replaced when the take is transcribed again.
        if (row.Stt is { Words.Count: > 0 } stt)
            parameters = parameters.WithExtra("stt", new JsonObject
            {
                ["speechStartMs"] = (long)Math.Round((stt.SpeechStart ?? 0) * 1000),
                ["speechEndMs"] = (long)Math.Round((stt.SpeechEnd ?? 0) * 1000),
                ["language"] = stt.Language,
                ["words"] = new JsonArray(stt.Words.Take(500).Select(word => (JsonNode)new JsonObject
                {
                    ["w"] = word.Word, ["s"] = (long)Math.Round(word.Start * 1000), ["e"] = (long)Math.Round(word.End * 1000),
                    ["p"] = Math.Round(word.Probability, 2)
                }).ToArray())
            });
        return parameters.ToJson();
    }

    /// <summary>Whisper initial prompt: the speakers' and registered characters' names, so proper
    /// names («Fluttershy», «Pinkie Pie») are written the way the project spells them.</summary>
    private string? SttNameHint(IEnumerable<RecordedVoiceRow> rows)
    {
        var names = rows.Select(x => x.CharacterName)
            .Concat(_characters.Select(x => x.Name))
            .Where(x => x.Length > 1 && !SameDirectorName(x, "Narrador"))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(40).ToArray();
        if (names.Length == 0) return null;
        var text = "Personajes: " + string.Join(", ", names) + ".";
        return text.Length <= 400 ? text : text[..text.LastIndexOf(',', 399)] + ".";
    }

    internal static string SttDetail(LocalTranscriptionResult result, long durationMs)
    {
        var parts = new List<string>();
        if (result.SpeechStart is double start && result.SpeechEnd is double end)
        {
            parts.Add($"voz {start:0.00}–{end:0.00} s");
            var silence = durationMs / 1000d - end + start;
            if (silence >= 0.8) parts.Add($"{silence:0.0} s de silencio");
        }
        if (result.DoubtfulWords.Count > 0) parts.Add("dudosas: " + string.Join(", ", result.DoubtfulWords.Take(6)));
        if (result.Compute is { Length: > 0 } compute) parts.Add($"{result.Device}/{compute}");
        return string.Join(" · ", parts);
    }

    private void RecordedVoicesGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || e.Row.Item is not RecordedVoiceRow row) return;
        if (e.Column.Header?.ToString() != "Transcripción revisable") return;
        if (e.EditingElement is TextBox text && text.Text != row.Transcript)
        {
            row.Transcript = text.Text;
            row.IsReviewed = true;
            row.TranscriptOrigin = "manual";
            row.TranscriptionState = "Texto editado";
        }
    }

    private void TranscribePendingVoices_Click(object sender, RoutedEventArgs e) =>
        _ = TranscribeRecordedVoicesAsync(_recordedVoices.Where(x => string.IsNullOrWhiteSpace(x.Transcript)).ToArray());

    private void TranscribeSelectedVoices_Click(object sender, RoutedEventArgs e)
    {
        RecordedVoicesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        RecordedVoicesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var rows = RecordedVoicesGrid.SelectedItems.Cast<RecordedVoiceRow>().ToArray();
        if (rows.Any(x => !string.IsNullOrWhiteSpace(x.Transcript)) &&
            MessageBox.Show(this, "La selección contiene texto existente. ¿Deseas sobrescribirlo con STT?",
                "Transcribir selección", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _ = TranscribeRecordedVoicesAsync(rows);
    }

    private void CancelTranscription_Click(object sender, RoutedEventArgs e) => _sttCancellation?.Cancel();

    private async Task TranscribeRecordedVoicesAsync(RecordedVoiceRow[] rows)
    {
        if (_sttCancellation is not null) return;
        if (rows.Length == 0)
        {
            RecordedVoiceStatusText.Text = "No hay tomas que transcribir; añade WAV o selecciona filas.";
            return;
        }
        if (_currentRepository is null || _currentRepository != _recordedVoiceRepository ||
            (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id != _recordedVoiceSceneId)
        {
            RecordedVoiceStatusText.Text = "Selecciona la escena correspondiente a las tomas antes de transcribir.";
            return;
        }
        using var cancellation = new CancellationTokenSource();
        _sttCancellation = cancellation;
        CancelTranscriptionButton.IsEnabled = true;
        TranscribePendingButton.IsEnabled = false;
        TranscribeSelectedButton.IsEnabled = false;
        ImportRecordedVoicesButton.IsEnabled = false;
        RecordedVoicesGrid.IsReadOnly = true;
        var completed = 0;
        try
        {
            string Option(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "auto";
            var hint = SttHintCheck.IsChecked == true ? SttNameHint(rows) : null;
            if (!LocalTranscriptionClient.IsInstalled && !await InstallLocalTranscriptionAsync(cancellation.Token, missing: true))
                return;
            RecordedVoiceStatusText.Text = $"Cargando STT local y transcribiendo {rows.Length} toma(s)…";
            Task Transcribe() => LocalTranscriptionClient.TranscribeAsync(rows.Select(x => x.Path).ToArray(),
                Option(SttModelCombo), Option(SttDeviceCombo), Option(SttLanguageCombo), result =>
                {
                    var row = rows[result.Index];
                    completed++;
                    if (string.IsNullOrWhiteSpace(result.Error))
                    {
                        row.Transcript = result.Text ?? "";
                        row.IsReviewed = false;
                        row.TranscriptOrigin = "stt";
                        row.Stt = result;
                        var doubtful = result.DoubtfulWords;
                        row.TranscriptionState = doubtful.Count == 0 ? "STT · revisar" : $"STT · revisar ({doubtful.Count} dudosa{(doubtful.Count == 1 ? "" : "s")})";
                        row.TranscriptionDetail = SttDetail(result, row.DurationMs);
                    }
                    else row.TranscriptionState = result.Error;
                    RecordedVoicesGrid.Items.Refresh();
                    RecordedVoiceStatusText.Text = $"{completed}/{rows.Length} transcritas · {row.FileName}: {row.TranscriptionState}" +
                        (row.TranscriptionDetail.Length > 0 ? " · " + row.TranscriptionDetail : "");
                }, cancellation.Token, hint);
            try { await Transcribe(); }
            catch (SttNotInstalledException) when (completed == 0)
            {
                // Half-installed (faster-whisper missing): repair it and try once more.
                if (!await InstallLocalTranscriptionAsync(cancellation.Token, missing: false)) return;
                RecordedVoiceStatusText.Text = $"Cargando STT local y transcribiendo {rows.Length} toma(s)…";
                await Transcribe();
            }
            RecordedVoiceStatusText.Text = $"STT finalizado ({completed}/{rows.Length}). Revisa el texto y pulsa Incorporar voces a la escena.";
        }
        catch (OperationCanceledException)
        {
            RecordedVoiceStatusText.Text = $"STT cancelado tras {completed}/{rows.Length} tomas. Las terminadas siguen en la tabla.";
        }
        catch (Exception ex)
        {
            RecordedVoiceStatusText.Text = $"STT interrumpido tras {completed}/{rows.Length}: {ex.Message}";
        }
        finally
        {
            _sttCancellation = null;
            CancelTranscriptionButton.IsEnabled = false;
            TranscribePendingButton.IsEnabled = true;
            TranscribeSelectedButton.IsEnabled = true;
            ImportRecordedVoicesButton.IsEnabled = true;
            RecordedVoicesGrid.IsReadOnly = false;
        }
    }

    /// <summary>
    /// The published version brings the local transcription installed (1.4.0). When it is missing (built with -SinSTT,
    /// a deleted folder, development without the venv), it is installed here after asking, never behind the user's back:
    /// it downloads Python/faster-whisper (≈ 300 MB). Progress goes to the status line; «Cancelar» stops it.
    /// </summary>
    private async Task<bool> InstallLocalTranscriptionAsync(CancellationToken token, bool missing)
    {
        if (MessageBox.Show(this,
                (missing ? "La transcripción local todavía no está instalada." : "A la transcripción local le falta faster-whisper.") +
                "\n\n¿Instalarla ahora? Se descarga Python (si tu PC no lo tiene) y faster-whisper, unos 300 MB, en la carpeta del " +
                "programa. No hay que ejecutar nada más.\n\nEl modelo de Whisper elegido se descarga la primera vez que transcribes.",
                "Transcripción local", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes) != MessageBoxResult.Yes)
        {
            RecordedVoiceStatusText.Text = "Transcripción local sin instalar. Puedes escribir el texto de las tomas a mano.";
            return false;
        }
        RecordedVoiceStatusText.Text = "Instalando la transcripción local…";
        var installing = true;
        try
        {
            await LocalTranscriptionClient.InstallAsync(line => _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                // Lines queued before the end must not overwrite the messages that follow.
                if (installing) RecordedVoiceStatusText.Text = "Instalando la transcripción local · " + line;
            })), token);
        }
        finally { installing = false; }
        RecordedVoiceStatusText.Text = "Transcripción local instalada.";
        return true;
    }

    private async void AssignRecordedVoiceCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (_sttCancellation is not null) return;
        var rows = RecordedVoicesGrid.SelectedItems.Cast<RecordedVoiceRow>().ToArray();
        if (rows.Length == 0)
        {
            RecordedVoiceStatusText.Text = "Selecciona una o varias tomas y luego elige o escribe el personaje.";
            return;
        }
        try
        {
            var typed = RecordedVoiceCharacterCombo.Text?.Trim() ?? "";
            var character = RecordedVoiceCharacterCombo.SelectedItem as CharacterChoice;
            if (character is null || typed.Length > 0 && !SameDirectorName(character.Name, typed))
                character = await EnsureRecordedVoiceCharacterAsync(typed);
            if (character is null) return;
            foreach (var row in rows)
            {
                row.CharacterId = character.Id;
                row.CharacterName = character.Name;
            }
            RecordedVoicesGrid.Items.Refresh();
            RecordedVoiceStatusText.Text = $"{rows.Length} toma(s) asignadas a {character.Name}. Pulsa Incorporar voces a la escena para guardarlo.";
        }
        catch (Exception ex) { RecordedVoiceStatusText.Text = ex.Message; ShowError(ex); }
    }

    private void RefreshRecordedVoiceProfileChoices()
    {
        if (RecordedVoiceProfileCombo is null) return;
        var selected = RecordedVoiceProfileCombo.SelectedItem as VoiceProfileChoice;
        RecordedVoiceProfileCombo.ItemsSource = new[] { new VoiceProfileChoice(null, "Sin perfil") }
            .Concat(BuildVoiceChoices().Skip(1)).ToArray();
        SelectVoiceChoice(RecordedVoiceProfileCombo, selected?.Id, selected?.Direct);
    }

    private void AssignRecordedVoiceProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_sttCancellation is not null) return;
        if (RecordedVoiceProfileCombo.SelectedItem is not VoiceProfileChoice voice) return;
        foreach (var row in RecordedVoicesGrid.SelectedItems.Cast<RecordedVoiceRow>())
        {
            row.ProfileId = voice.Id;
            row.Direct = voice.Direct;
            row.VoiceName = row.ProfileId.HasValue || row.Direct is not null ? voice.Name : "";
        }
        RecordedVoicesGrid.Items.Refresh();
    }

    private void MoveRecordedVoiceUp_Click(object sender, RoutedEventArgs e) => MoveRecordedVoice(-1);
    private void MoveRecordedVoiceDown_Click(object sender, RoutedEventArgs e) => MoveRecordedVoice(1);
    private void MoveRecordedVoice(int delta)
    {
        if (_sttCancellation is not null) return;
        if (RecordedVoicesGrid.SelectedItem is not RecordedVoiceRow row) return;
        if (_recordedVoices.Any(x => x.ExistingBlockId.HasValue))
        {
            RecordedVoiceStatusText.Text = "Para cambiar el orden de voces existentes usa las flechas del editor de guion.";
            return;
        }
        var index = _recordedVoices.IndexOf(row);
        var target = index + delta;
        if (target < 0 || target >= _recordedVoices.Count) return;
        _recordedVoices.Move(index, target);
        for (var i = 0; i < _recordedVoices.Count; i++) _recordedVoices[i].Order = i + 1;
        RecordedVoicesGrid.Items.Refresh();
        RecordedVoicesGrid.SelectedItem = row;
    }

    private void RemoveRecordedVoices_Click(object sender, RoutedEventArgs e)
    {
        if (_sttCancellation is not null) return;
        foreach (var row in RecordedVoicesGrid.SelectedItems.Cast<RecordedVoiceRow>().ToArray()) _recordedVoices.Remove(row);
        for (var i = 0; i < _recordedVoices.Count; i++) _recordedVoices[i].Order = i + 1;
        RecordedVoicesGrid.Items.Refresh();
    }

    private void ListenRecordedVoice_Click(object sender, RoutedEventArgs e)
    {
        if (RecordedVoicesGrid.SelectedItem is not RecordedVoiceRow row) return;
        _scriptAudioPlayer.Stop();
        _scriptAudioPlayer.Close();
        _scriptAudioPlayer.Volume = 1;
        _scriptAudioPlayer.Open(new Uri(row.Path, UriKind.Absolute));
        _scriptAudioPlayer.Play();
    }

    private async void ImportRecordedVoices_Click(object sender, RoutedEventArgs e)
    {
        if (_sttCancellation is not null) return;
        if (_currentRepository is null || ScenesList.SelectedItem is not SceneScriptRow scene || _recordedVoices.Count == 0 ||
            _recordedVoiceSceneId != scene.Scene.Id || _recordedVoiceRepository != _currentRepository)
        {
            RecordedVoiceStatusText.Text = "Selecciona una escena y añade al menos un WAV.";
            return;
        }
        var repository = _currentRepository;
        var sceneId = scene.Scene.Id;
        var copiedPaths = new List<string>();
        ImportRecordedVoicesButton.IsEnabled = false;
        try
        {
            if (!RecordedVoicesGrid.CommitEdit(DataGridEditingUnit.Cell, true) ||
                !RecordedVoicesGrid.CommitEdit(DataGridEditingUnit.Row, true))
                throw new InvalidOperationException("No se pudo confirmar la edición de la transcripción. Corrige la celda y vuelve a intentarlo.");
            var original = (await repository.GetSceneScriptBlocksAsync(sceneId)).ToArray();
            var profileIds = (await repository.GetVoiceProfilesAsync()).Select(x => x.Id).ToHashSet();
            var additions = new List<SceneScriptBlock>();
            var updates = _recordedVoices.Where(x => x.ExistingBlockId.HasValue)
                .ToDictionary(x => x.ExistingBlockId!.Value);
            if (updates.Keys.Any(id => !original.Any(b => b.Id == id && IsImportedVoice(b))))
                throw new InvalidOperationException("Una toma importada cambió en el guion. Carga de nuevo las voces de la escena.");
            var targetFolder = Path.Combine(repository.ProjectRoot, "generated", "imported_voices", $"scene_{sceneId:N}");
            Directory.CreateDirectory(targetFolder);
            foreach (var row in _recordedVoices)
            {
                if (row.ProfileId is Guid profileId && !profileIds.Contains(profileId))
                    throw new InvalidOperationException($"El perfil de voz asignado a {row.FileName} ya no existe.");
                if (row.ExistingBlockId.HasValue) continue;
                if (!IsValidWave(row.Path)) throw new InvalidDataException($"El WAV cambió o está dañado: {row.FileName}");
                var blockId = Guid.NewGuid();
                var targetPath = Path.Combine(targetFolder, $"{blockId:N}.wav");
                File.Copy(row.Path, targetPath);
                copiedPaths.Add(targetPath);
                using var copiedStream = File.OpenRead(targetPath);
                var hash = Convert.ToHexString(SHA256.HashData(copiedStream)).ToLowerInvariant();
                additions.Add(new SceneScriptBlock(blockId, sceneId, original.Length + additions.Count,
                    row.CharacterId is null ? ScriptBlockKind.Narration : ScriptBlockKind.Dialogue,
                    CharacterId: row.CharacterId, Text: row.Transcript.Trim(), VoiceProfileId: row.ProfileId,
                    ParametersJson: VoiceParameters(row),
                    GeneratedAudioPath: Path.GetRelativePath(repository.ProjectRoot, targetPath).Replace('\\', '/'),
                    GeneratedAudioHash: ImportedVoicePrefix + hash, GeneratedDurationMs: GetWaveDurationMs(targetPath)));
            }
            if (_currentRepository != repository || (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id != sceneId)
                throw new InvalidOperationException("La escena cambió durante la importación. Vuelve a intentarlo.");
            var updatedBlocks = original.Select(block => updates.TryGetValue(block.Id, out var row)
                ? block with
                {
                    // A take without speaker is narration, except an NPC dialogue line (1.4.0) that still has none.
                    Kind = row.CharacterId is not null ? ScriptBlockKind.Dialogue
                        : block.Kind == ScriptBlockKind.Dialogue && block.CharacterId is null ? ScriptBlockKind.Dialogue : ScriptBlockKind.Narration,
                    CharacterId = row.CharacterId, Text = row.Transcript.Trim(), VoiceProfileId = row.ProfileId,
                    ParametersJson = VoiceParameters(row, block.ParametersJson)
                } : block).Concat(additions).ToArray();
            await repository.ReplaceSceneScriptBlocksAsync(sceneId, updatedBlocks);
            copiedPaths.Clear(); // Los archivos ya pertenecen al guion guardado.
            if (!string.IsNullOrWhiteSpace(RecordedVoiceNotesBox.Text))
            {
                var updated = scene.Scene with
                {
                    DirectionNotes = string.Join(Environment.NewLine,
                        new[] { scene.Scene.DirectionNotes, RecordedVoiceNotesBox.Text.Trim() }.Where(x => !string.IsNullOrWhiteSpace(x)))
                };
                await repository.UpsertSceneAsync(updated);
                SceneNotesBox.Text = updated.DirectionNotes ?? "";
                RecordedVoiceNotesBox.Clear();
            }
            _recordedVoices.Clear();
            await LoadBlocksAsync(sceneId);
            await RefreshSceneTimingAsync(_scriptBlocks);
            RecordedVoiceStatusText.Text = $"{additions.Count} toma(s) añadidas y {updates.Count} actualizadas. El preview conserva los WAV importados.";
        }
        catch (Exception ex)
        {
            foreach (var path in copiedPaths)
                try { File.Delete(path); } catch (IOException) { /* Recuperable en un nuevo intento. */ }
            RecordedVoiceStatusText.Text = ex.Message;
            ShowError(ex);
        }
        finally { ImportRecordedVoicesButton.IsEnabled = true; }
    }


    private async void RegenerateImportedVoice_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is null || ScenesList.SelectedItem is not SceneScriptRow scene ||
            ScriptBlocksGrid.SelectedItem is not ScriptBlockRow { Block: var selected } || !IsImportedVoice(selected))
        {
            ScriptStatusText.Text = "Selecciona un bloque de voz grabada para regenerarlo con TTS.";
            return;
        }
        await RegenerateRecordedTakeAsync(scene, selected, askFirst: true);
    }

    /// <summary>
    /// Replaces a recorded take with TTS only on explicit request. The recorded WAV reference is
    /// kept in the block parameters, so «Restaurar grabación» can bring it back at any time.
    /// «Generar voces de escena», previews and exports never replace recorded takes.
    /// </summary>
    private async Task RegenerateRecordedTakeAsync(SceneScriptRow scene, SceneScriptBlock selected, bool askFirst)
    {
        if (string.IsNullOrWhiteSpace(selected.Text))
        {
            ScriptStatusText.Text = "Escribe y guarda primero el diálogo que quieres generar.";
            return;
        }
        // Only the voice assigned to the take counts: the speaker is a label, and its Voice Lab
        // default never decides how a recorded line is regenerated.
        if (ResolveRecordedTakeVoice(selected, _voiceProfiles.ToDictionary(x => x.Id)) is not { } profile)
        {
            ScriptStatusText.Text = "Esta toma no tiene voz TTS propia. Asígnale un perfil o una voz TTS " +
                "(«Asignar voz» en Voces grabadas o «Voz» en el editor) para regenerarla.";
            return;
        }
        if (_ttsBridge.BridgePath is null)
        {
            ScriptStatusText.Text = "No se encontró el TTS bridge; la grabación se conserva.";
            return;
        }
        if (askFirst && MessageBox.Show(this,
                $"La línea #{selected.OrderIndex + 1} usa una voz GRABADA.\n\nSe generará con TTS usando «{profile.Name}» y la sustituirá en la escena. " +
                "La grabación no se borra: podrás volver a ella con «Restaurar grabación».\n\n¿Regenerar con TTS?",
                "Regenerar con TTS", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            ScriptStatusText.Text = "Se conservó la voz grabada.";
            return;
        }
        try
        {
            var repository = _currentRepository!;
            var parameters = BlockParameters.Of(selected) with
            {
                RecordedTake = new RecordedTakeReference(selected.GeneratedAudioPath, selected.GeneratedAudioHash, selected.GeneratedDurationMs)
            };
            await repository.UpsertSceneScriptBlockAsync(selected with
            {
                ParametersJson = parameters.ToJson(),
                GeneratedAudioPath = null, GeneratedAudioHash = null, GeneratedDurationMs = null
            });
            await LoadBlocksAsync(scene.Scene.Id, selected.Id);
            if (!await GenerateSceneVoicesAsync() && _currentRepository == repository &&
                (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id == scene.Scene.Id)
            {
                await repository.UpsertSceneScriptBlockAsync(selected);
                await LoadBlocksAsync(scene.Scene.Id, selected.Id);
                await RefreshSceneTimingAsync(_scriptBlocks, selected.Id);
                ScriptStatusText.Text = "No se completó la regeneración; se conservó la voz grabada.";
                return;
            }
            ScriptStatusText.Text = $"Línea regenerada con TTS («{profile.Name}»). «Restaurar grabación» vuelve a la toma original.";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void RestoreRecordedVoice_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is not { } repository || ScenesList.SelectedItem is not SceneScriptRow scene ||
            ScriptBlocksGrid.SelectedItem is not ScriptBlockRow { Block: var selected }) return;
        var parameters = BlockParameters.Of(selected);
        if (IsImportedVoice(selected) || parameters.RecordedTake is not { Path: { Length: > 0 } relative, Hash: { Length: > 0 } hash })
        {
            ScriptStatusText.Text = IsImportedVoice(selected)
                ? "Este bloque ya usa su voz grabada."
                : "Este bloque no tiene una grabación guardada.";
            return;
        }
        if (ResolveGeneratedPath(relative) is not string path || !IsValidWave(path))
        {
            ScriptStatusText.Text = "No se encuentra el WAV grabado original en el proyecto.";
            return;
        }
        try
        {
            await repository.UpsertSceneScriptBlockAsync(selected with
            {
                ParametersJson = (parameters with { RecordedTake = null }).ToJson(),
                GeneratedAudioPath = relative, GeneratedAudioHash = hash, GeneratedDurationMs = GetWaveDurationMs(path)
            });
            await LoadBlocksAsync(scene.Scene.Id, selected.Id);
            await RefreshSceneTimingAsync(_scriptBlocks, selected.Id);
            ScriptStatusText.Text = $"Bloque #{selected.OrderIndex + 1}: se restauró la voz grabada.";
        }
        catch (Exception ex) { ShowError(ex); }
    }
}

public sealed class RecordedVoiceRow(string path, long durationMs) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private string _transcript = "";
    private bool _isReviewed;
    private string _transcriptionState = "Sin texto";

    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public Guid? ExistingBlockId { get; set; }
    public int Order { get; set; }
    public string Path { get; } = path;
    public string FileName => System.IO.Path.GetFileName(Path);
    public Guid? CharacterId { get; set; }
    public string CharacterName { get; set; } = "Narrador";
    /// <summary>Derived: a take without speaker is narration.</summary>
    public string KindText => CharacterId is null ? "Narración" : "Diálogo";
    public Guid? ProfileId { get; set; }
    /// <summary>The take's own direct voice (any engine of the selector since 1.4.0).</summary>
    public DirectVoiceRef? Direct { get; set; }
    public string VoiceName { get; set; } = "";
    public string Transcript
    {
        get => _transcript;
        set { if (_transcript == value) return; _transcript = value; Notify(); }
    }
    public bool IsReviewed
    {
        get => _isReviewed;
        set { if (_isReviewed == value) return; _isReviewed = value; Notify(); }
    }
    public string TranscriptionState
    {
        get => _transcriptionState;
        set { if (_transcriptionState == value) return; _transcriptionState = value; Notify(); }
    }
    public string TranscriptOrigin { get; set; } = "manual";
    /// <summary>Last STT result (word timestamps), saved with the take when it is incorporated.</summary>
    public LocalTranscriptionResult? Stt { get; set; }
    public string TranscriptionDetail { get; set; } = "";
    public long DurationMs { get; } = durationMs;
    public string DurationText => $"{DurationMs / 1000d:0.00}s";
}
