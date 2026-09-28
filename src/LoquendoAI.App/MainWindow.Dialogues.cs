using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Dialogue;
using LoquendoAI.Infrastructure.Persistence;
using LoquendoAI.Infrastructure.Tts;

namespace LoquendoAI.App;

/// <summary>
/// «Diálogos» tab (1.2.0): the lines of an episode in a table (who speaks, voice, per-line prosody, text,
/// comment), imported from text/Word/Excel/CSV, written by hand or by the AI, voiced with TTS and exported to
/// scenes as recorded takes. Each script is a JSON file in the project folder «dialogos» and saves itself.
/// </summary>
public partial class MainWindow
{
    private const string DialogueFolderName = "dialogos";
    private static readonly JsonSerializerOptions DialogueJson = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ObservableCollection<DialogueRow> _dialogueRows = [];
    private readonly DispatcherTimer _dialogueSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(1200) };
    private DialogueSetChoice? _dialogueSet;
    private DialogueAiData _dialogueAi = new();
    private bool _dialogueDirty;
    private bool _loadingDialogue;
    private bool _adjustingDialogueRow;
    private IReadOnlyList<VoiceProfileChoice> _dialogueVoiceChoices = [];
    private IReadOnlyList<string> _dialogueSpeakerChoices = [];
    private Dictionary<Guid, CharacterDefinition> _dialogueCharacterMap = new();
    private Dictionary<Guid, VoiceProfile> _dialogueProfileMap = new();

    private void InitializeDialogues()
    {
        DialogueGrid.ItemsSource = _dialogueRows;
        _dialogueSaveTimer.Tick += (_, _) =>
        {
            _dialogueSaveTimer.Stop();
            SaveDialogueNow();
        };
        InitializeDialoguePlayback();
    }

    private string? DialogueFolder => _currentRepository is { } repository
        ? Path.Combine(repository.ProjectRoot, DialogueFolderName) : null;

    // ───────────────────────────── Project and scripts ─────────────────────────────

    /// <summary>Called when a project opens: its scripts, the dialogue «Prompt maestro» and the choices.</summary>
    private async Task OpenDialogueProjectAsync(SqliteProjectRepository repository)
    {
        DialogueTab.IsEnabled = true;
        await LoadDialogueMasterPromptAsync(repository);
        RefreshDialogueChoices();
        LoadDialogueSets(null);
        await RefreshDialogueEpisodesAsync();
    }

    /// <summary>Before another project opens or the window closes.</summary>
    private void CloseDialogueProject()
    {
        StopDialoguePlayback();
        _dialogueWork?.Cancel();
        CommitDialogueEdits();
        SaveDialogueNow();
        _loadingDialogue = true;
        try { ClearDialogueRows(); }
        finally { _loadingDialogue = false; }
        _dialogueSet = null;
        DialogueSetCombo.ItemsSource = null;
    }

    private void LoadDialogueSets(string? selectPath)
    {
        var sets = new List<DialogueSetChoice>();
        if (DialogueFolder is { } folder && Directory.Exists(folder))
            foreach (var file in Directory.EnumerateFiles(folder, "*.json").OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                try
                {
                    using var json = JsonDocument.Parse(File.ReadAllText(file));
                    if (json.RootElement.TryGetProperty("name", out var saved) && saved.GetString() is { Length: > 0 } savedName) name = savedName;
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { name += " (dañado)"; }
                sets.Add(new DialogueSetChoice(name, file));
            }
        if (sets.Count == 0 && DialogueFolder is { } empty)
            sets.Add(new DialogueSetChoice("Guion 1", Path.Combine(empty, "Guion 1.json"))); // saved with its first line
        DialogueSetCombo.ItemsSource = sets;
        DialogueSetCombo.SelectedItem = sets.FirstOrDefault(x => selectPath is not null && PathsEqual(x.Path, selectPath)) ?? sets.FirstOrDefault();
    }

    private void DialogueSetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DialogueSetCombo.SelectedItem is not DialogueSetChoice set || set == _dialogueSet) return;
        StopDialoguePlayback();
        CommitDialogueEdits();
        SaveDialogueNow();
        LoadDialogueSet(set);
    }

    private void LoadDialogueSet(DialogueSetChoice set)
    {
        _dialogueSet = set;
        _loadingDialogue = true;
        try
        {
            ClearDialogueRows();
            _dialogueAi = new DialogueAiData();
            if (File.Exists(set.Path))
            {
                var document = JsonSerializer.Deserialize<DialogueDocument>(File.ReadAllText(set.Path), DialogueJson) ?? new DialogueDocument();
                _dialogueAi = document.Ai ?? new DialogueAiData();
                var lineCount = 0;
                // The limit counts lines, not scene cuts (12 × 40 lines may come with a dozen cuts).
                foreach (var line in (document.Lines ?? []).TakeWhile(x => x.Kind == "escena" || ++lineCount <= DialogueScript.MaxLines))
                    AddDialogueRow(new DialogueRow
                    {
                        Kind = line.Kind switch { "narracion" => DialogueKind.Narration, "escena" => DialogueKind.SceneBreak, _ => DialogueKind.Dialogue },
                        Speaker = line.Speaker, Text = line.Text, Comment = line.Comment, ProfileId = line.Profile,
                        DirectVoice = line.Voice, DirectProvider = line.VoiceProvider, Pitch = line.Pitch, Speed = line.Speed, Volume = line.Volume,
                        AudioHash = line.Hash, DurationMs = line.DurationMs, ParseWarning = line.Warning
                    }, _dialogueRows.Count);
            }
            DialogueStoryBox.Text = _dialogueAi.Story;
            DialogueAiLinesBox.Text = Math.Clamp(_dialogueAi.Lines, 4, DialogueScript.MaxAiLinesPerRun).ToString();
            _dialogueDirty = false;
            DialogueStatusText.Text = _dialogueRows.Count == 0
                ? "Guion vacío: importa un archivo, pega texto, añade líneas o genera con IA."
                : $"«{set.Name}» cargado.";
        }
        catch (Exception ex)
        {
            // Never overwrite a file that could not be read: it is kept aside and the script starts empty.
            ErrorLog.Record(ex);
            ClearDialogueRows();
            var kept = "";
            try
            {
                kept = Path.ChangeExtension(set.Path, $".ilegible-{DateTime.Now:yyyyMMdd-HHmmss}.bak");
                File.Move(set.Path, kept);
            }
            catch (Exception) { kept = ""; _dialogueSet = null; }
            DialogueStatusText.Text = $"No se pudo leer «{set.Name}» ({ex.Message})." +
                (kept.Length > 0 ? $" Se guardó aparte como {Path.GetFileName(kept)} y el guion empieza vacío." : " No se guardarán cambios en él.");
        }
        finally { _loadingDialogue = false; }
        RefreshDialogueChoices(); // also offers the voices the loaded rows use
    }

    private void SaveDialogueNow()
    {
        _dialogueSaveTimer.Stop();
        if (!_dialogueDirty || _dialogueSet is not { } set || _currentRepository is null) return;
        if (_dialogueRows.Count == 0 && !File.Exists(set.Path)) { _dialogueDirty = false; return; }
        try
        {
            _dialogueAi.Story = DialogueStoryBox.Text;
            if (int.TryParse(DialogueAiLinesBox.Text.Trim(), out var lines)) _dialogueAi.Lines = Math.Clamp(lines, 4, DialogueScript.MaxAiLinesPerRun);
            var document = new DialogueDocument
            {
                Name = set.Name, Ai = _dialogueAi,
                Lines = _dialogueRows.Select(row => new DialogueLineData
                {
                    Kind = row.Kind switch { DialogueKind.Narration => "narracion", DialogueKind.SceneBreak => "escena", _ => "dialogo" },
                    Speaker = row.Speaker, Text = row.Text, Comment = row.Comment, Profile = row.ProfileId, Voice = row.Direct?.Voice,
                    VoiceProvider = row.Direct is { Provider: not VoiceSelectorSettings.Tts7 } direct ? direct.Provider : null,
                    Pitch = row.Pitch, Speed = row.Speed, Volume = row.Volume, Hash = row.AudioHash, DurationMs = row.DurationMs,
                    Warning = row.ParseWarning
                }).ToList()
            };
            Directory.CreateDirectory(Path.GetDirectoryName(set.Path)!);
            var temp = set.Path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(document, DialogueJson));
            File.Move(temp, set.Path, true);
            _dialogueDirty = false;
        }
        catch (Exception ex)
        {
            ErrorLog.Record(ex);
            DialogueStatusText.Text = "No se pudo guardar el guion de diálogos: " + ex.Message;
        }
    }

    private void MarkDialogueDirty()
    {
        if (_loadingDialogue) return;
        _dialogueDirty = true;
        _dialogueSaveTimer.Stop();
        _dialogueSaveTimer.Start();
        UpdateDialogueStats();
    }

    private void DialogueStoryBox_TextChanged(object sender, TextChangedEventArgs e) => MarkDialogueDirty();

    private void NewDialogueSet_Click(object sender, RoutedEventArgs e)
    {
        if (DialogueFolder is not { } folder || DialogueBusy()) return;
        var name = AskDialogueText("Nuevo guion de diálogos", "Nombre del guion:", $"Guion {DialogueSetCount() + 1}");
        if (name is null) return;
        var path = UniqueDialoguePath(folder, name);
        SaveDialogueNow();
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(path, JsonSerializer.Serialize(new DialogueDocument { Name = name }, DialogueJson));
        }
        catch (Exception ex) { ShowError(ex); return; }
        _dialogueSet = null;
        LoadDialogueSets(path);
    }

    private void RenameDialogueSet_Click(object sender, RoutedEventArgs e)
    {
        if (_dialogueSet is not { } set || DialogueFolder is not { } folder || DialogueBusy()) return;
        var name = AskDialogueText("Renombrar guion", "Nuevo nombre:", set.Name);
        if (name is null || name == set.Name) return;
        SaveDialogueNow();
        var path = UniqueDialoguePath(folder, name, set.Path);
        try
        {
            if (File.Exists(set.Path) && !PathsEqual(path, set.Path)) File.Move(set.Path, path);
            _dialogueSet = set with { Name = name, Path = path };
            _dialogueDirty = true;
            SaveDialogueNow();
        }
        catch (Exception ex) { ShowError(ex); return; }
        LoadDialogueSets(path);
    }

    private void DeleteDialogueSet_Click(object sender, RoutedEventArgs e)
    {
        if (_dialogueSet is not { } set || DialogueBusy()) return;
        if (MessageBox.Show(this, $"¿Eliminar el guion de diálogos «{set.Name}»?\n\nLos audios generados siguen en la caché de voces " +
                "y las escenas ya exportadas no cambian.", "Diálogos", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        StopDialoguePlayback();
        try { if (File.Exists(set.Path)) File.Delete(set.Path); }
        catch (Exception ex) { ShowError(ex); return; }
        _dialogueDirty = false;
        _dialogueSet = null;
        LoadDialogueSets(null);
    }

    /// <summary>While audio, the AI or an export runs, the table and the script must not change under it.</summary>
    private bool DialogueBusy()
    {
        if (_dialogueWork is null) return false;
        DialogueStatusText.Text = "Espera a que termine (o pulsa Cancelar).";
        return true;
    }

    private int DialogueSetCount() => (DialogueSetCombo.ItemsSource as IEnumerable<DialogueSetChoice>)?.Count() ?? 0;

    private static string UniqueDialoguePath(string folder, string name, string? current = null)
    {
        var baseName = SafeFilePart(name);
        var path = Path.Combine(folder, baseName + ".json");
        for (var i = 2; File.Exists(path) && !(current is not null && PathsEqual(path, current)); i++)
            path = Path.Combine(folder, $"{baseName} ({i}).json");
        return path;
    }

    // ───────────────────────────── Rows ─────────────────────────────

    private void AddDialogueRow(DialogueRow row, int index)
    {
        row.PropertyChanged += DialogueRow_PropertyChanged;
        _dialogueRows.Insert(Math.Clamp(index, 0, _dialogueRows.Count), row);
    }

    private void RemoveDialogueRow(DialogueRow row)
    {
        row.PropertyChanged -= DialogueRow_PropertyChanged;
        _dialogueRows.Remove(row);
    }

    private void ClearDialogueRows()
    {
        foreach (var row in _dialogueRows) row.PropertyChanged -= DialogueRow_PropertyChanged;
        _dialogueRows.Clear();
    }

    private void RenumberDialogueRows()
    {
        for (var i = 0; i < _dialogueRows.Count; i++) _dialogueRows[i].Order = i + 1;
    }

    /// <summary>User edits (cells, voice combo): keep kind and speaker consistent, then recompute voice and state.</summary>
    private void DialogueRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loadingDialogue || _adjustingDialogueRow || sender is not DialogueRow row) return;
        switch (e.PropertyName)
        {
            case nameof(DialogueRow.Kind):
                _adjustingDialogueRow = true;
                try
                {
                    row.ParseWarning = null;
                    if (row.Kind == DialogueKind.Narration) row.Speaker = DialogueScript.Narrator;
                    else if (row.Kind == DialogueKind.Dialogue && IsNarratorName(row.Speaker)) row.Speaker = "";
                }
                finally { _adjustingDialogueRow = false; }
                break;
            case nameof(DialogueRow.Speaker):
                _adjustingDialogueRow = true;
                try
                {
                    row.ParseWarning = null;
                    if (IsNarratorName(row.Speaker)) { row.Kind = DialogueKind.Narration; row.Speaker = DialogueScript.Narrator; }
                    else if (row.Kind == DialogueKind.Narration && row.Speaker.Length > 0) row.Kind = DialogueKind.Dialogue;
                }
                finally { _adjustingDialogueRow = false; }
                break;
            case nameof(DialogueRow.Text) or nameof(DialogueRow.PitchText) or nameof(DialogueRow.SpeedText) or
                 nameof(DialogueRow.VolumeText) or nameof(DialogueRow.VoiceChoice):
                break;
            case nameof(DialogueRow.Comment):
                MarkDialogueDirty();
                return;
            default:
                return; // derived values set by RefreshDialogueRow
        }
        RefreshDialogueRow(row);
        if (e.PropertyName == nameof(DialogueRow.Speaker)) RefreshDialogueSpeakerChoices();
        MarkDialogueDirty();
    }

    private static bool IsNarratorName(string name) =>
        SameDirectorName(name, "Narrador") || SameDirectorName(name, "Narradora") || SameDirectorName(name, "Narración");

    /// <summary>Choices shared by every row: NPC voices (NPC profile, the voices of the selector — TTS7, SAPI4, SAPI5,
    /// edited in Voice Lab —, saved profiles) and speakers. Called whenever profiles, characters, the voice catalog or
    /// the selector change (RefreshScriptReferenceChoices).</summary>
    private void RefreshDialogueChoices()
    {
        if (DialogueGrid is null) return;
        _dialogueCharacterMap = _characters.ToDictionary(x => x.Id);
        _dialogueProfileMap = _voiceProfiles.ToDictionary(x => x.Id);
        // First (and what «Asignar voz NPC» gives by default): the NPC profile's own voice, «TTS7 · Jorge» — here without
        // the «(voz NPC)» mark (1.4.1), and not repeated among the other voices.
        var voices = new List<VoiceProfileChoice>
        {
            new(NpcProfileId, NpcVoiceLabel(mark: false))
        };
        var npcVoice = NpcLineVoice();
        voices.AddRange(DirectVoiceOptions(_dialogueRows.Select(x => x.Direct))
            .Where(x => npcVoice is null || !x.Is(npcVoice.Provider, npcVoice.Voice))
            .Select(x => VoiceProfileChoice.Of(NpcProfileId, x.Label, x)));
        voices.AddRange(_voiceProfiles.Where(x => x.Id != NpcProfileId).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => new VoiceProfileChoice(x.Id, "Perfil · " + x.Name)));
        _dialogueVoiceChoices = voices;
        var selectedVoice = DialogueBulkVoiceCombo.SelectedItem as VoiceProfileChoice;
        DialogueBulkVoiceCombo.ItemsSource = voices;
        DialogueBulkVoiceCombo.SelectedItem = voices.FirstOrDefault(x => x == selectedVoice) ?? voices[0];
        RefreshDialogueSpeakerChoices();
        RefreshAllDialogueRows();
    }

    private void RefreshDialogueSpeakerChoices()
    {
        var speakers = new[] { DialogueScript.Narrator }
            .Concat(_characters.Select(x => x.Name).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
            .Concat(_dialogueRows.Where(x => x.Kind == DialogueKind.Dialogue).Select(x => x.Speaker)
                .Where(x => x.Length > 0).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase))
            .Distinct(StringComparer.CurrentCultureIgnoreCase).ToArray();
        _dialogueSpeakerChoices = speakers;
        var typed = DialogueBulkSpeakerCombo.Text;
        DialogueBulkSpeakerCombo.ItemsSource = speakers;
        DialogueBulkSpeakerCombo.Text = typed;
        foreach (var row in _dialogueRows) row.SpeakerChoices = speakers;
    }

    private void RefreshAllDialogueRows()
    {
        foreach (var row in _dialogueRows) RefreshDialogueRow(row);
        RenumberDialogueRows();
        UpdateDialogueStats();
    }

    /// <summary>Who speaks, which voice, and whether the audio is ready (the voice cache holds the WAV of the line's
    /// current voice + prosody + text).</summary>
    private void RefreshDialogueRow(DialogueRow row)
    {
        _adjustingDialogueRow = true;
        try
        {
            row.VoiceChoices = _dialogueVoiceChoices;
            row.SpeakerChoices = _dialogueSpeakerChoices;
            if (!row.IsLine)
            {
                row.CharacterId = null;
                row.IsNpc = false;
                row.VoiceText = "";
                row.DurationMs = null;
                row.StateText = row.Text.Length == 0 ? "⚠ Escena sin título" : "Corte de escena";
                row.StateDetail = row.Text.Length == 0
                    ? "Escribe el título de la escena en la columna «Diálogo»."
                    : "Aquí empieza una escena nueva al exportar.";
                row.NotifyLayout();
                return;
            }
            var notes = new List<string>();
            if (row.ParseWarning is { Length: > 0 } warning) notes.Add(warning);
            CharacterDefinition? character = null;
            if (row.Kind == DialogueKind.Dialogue)
            {
                if (row.Speaker.Length == 0) notes.Add("Elige quién habla.");
                else character = _characters.FirstOrDefault(x => SameDirectorName(x.Name, row.Speaker));
            }
            row.CharacterId = character?.Id;
            var own = character?.DefaultVoiceProfileId is Guid id && id != NpcProfileId && _dialogueProfileMap.TryGetValue(id, out var found)
                ? found : null;
            row.IsNpc = own is null;
            if (own is not null) row.VoiceText = $"{own.Name} · {own.VoiceId}";
            else
            {
                if (row.ProfileId is Guid chosen && chosen != NpcProfileId && !_dialogueProfileMap.ContainsKey(chosen))
                {
                    row.ProfileId = null;
                    row.DirectVoice = null;
                    row.DirectProvider = null;
                    notes.Add("El perfil elegido ya no existe: se usa la voz NPC.");
                }
                row.VoiceText = row.VoiceChoice?.Name ?? "NPC";
                if (row.Kind == DialogueKind.Dialogue && row.Speaker.Length > 0)
                    notes.Add(character is null
                        ? $"«{row.Speaker}» no es un personaje registrado: habla como NPC (al exportar se crea como personaje sin perfil)."
                        : $"{character.Name} no tiene perfil propio: habla con la voz NPC.");
            }

            string main;
            if (row.Text.Length == 0)
            {
                main = "⚠ Sin texto";
                row.DurationMs = null;
            }
            else if (ResolveDialogueVoice(row) is { } voice)
            {
                var root = _currentRepository?.ProjectRoot;
                var cached = root is null ? null : VoiceCache.PathFor(root, voice.Hash);
                if (cached is not null && File.Exists(cached) && IsValidWave(cached))
                {
                    if (row.AudioHash != voice.Hash || row.DurationMs is null) row.DurationMs = GetWaveDurationMs(cached);
                    row.AudioHash = voice.Hash;
                    main = "✓ Audio listo";
                }
                else
                {
                    main = row.AudioHash is { Length: > 0 } && row.AudioHash != voice.Hash ? "Cambió · sin audio" : "Pendiente";
                    row.DurationMs = null;
                }
                notes.Insert(0, $"Voz: {voice.Profile.Name} ({voice.Profile.VoiceId}) · pitch {voice.Pitch?.ToString() ?? "auto"} · " +
                    $"velocidad {voice.Speed?.ToString() ?? "auto"} · volumen {voice.Volume}");
            }
            else
            {
                main = "⚠ Sin voz";
                notes.Add("No hay perfil NPC en el proyecto: vuelve a abrirlo.");
            }
            if (row.IsPlaying) main = "▶ Sonando";
            var warnings = notes.Count(x => !x.StartsWith("Voz:", StringComparison.Ordinal));
            row.StateText = main + (warnings > 0 && !main.StartsWith('⚠') ? " · ⚠" : "");
            row.StateDetail = string.Join("\n", notes);
            row.NotifyProsody();
            row.NotifyLayout();
            row.NotifyVoiceChoice();
        }
        finally { _adjustingDialogueRow = false; }
    }

    internal sealed record DialogueVoice(VoiceProfile Profile, int? Pitch, int? Speed, int Volume, string Hash, SceneScriptBlock Block);

    /// <summary>The voice of a line exactly as a scene resolves it (ResolveVoiceProfile), so the audio generated
    /// here is the one the scene finds in the voice cache.</summary>
    private DialogueVoice? ResolveDialogueVoice(DialogueRow row)
    {
        if (!row.IsLine || row.Text.Length == 0) return null;
        var block = DialogueBlock(row);
        var profile = ResolveVoiceProfile(block, _dialogueCharacterMap, _dialogueProfileMap);
        if (profile is null) return null;
        var pitch = row.Pitch ?? profile.Pitch;
        var speed = row.Speed ?? profile.Speed;
        var volume = row.Volume ?? profile.Volume;
        return new DialogueVoice(profile, pitch, speed, volume, ComputeVoiceCacheHash(profile, row.Text, pitch, speed, volume), block);
    }

    /// <summary>A line as a script block: NPC rows carry their profile (and own voice); character rows inherit.</summary>
    private static SceneScriptBlock DialogueBlock(DialogueRow row)
    {
        var profileId = row.IsNpc ? row.ProfileId ?? NpcProfileId : (Guid?)null;
        var direct = row.IsNpc && profileId == NpcProfileId ? row.Direct : null;
        return new SceneScriptBlock(Guid.Empty, Guid.Empty, 0,
            row.Kind == DialogueKind.Narration ? ScriptBlockKind.Narration : ScriptBlockKind.Dialogue,
            CharacterId: row.CharacterId, Text: row.Text, VoiceProfileId: profileId,
            VoicePitchOverride: row.Pitch, VoiceSpeedOverride: row.Speed, VoiceVolumeOverride: row.Volume,
            ParametersJson: direct is null ? "{}" : DirectVoiceParameters(direct));
    }

    private void UpdateDialogueStats()
    {
        var lines = _dialogueRows.Where(x => x.IsLine).ToArray();
        var ready = lines.Count(x => x.StateText.StartsWith('✓') || x.StateText.StartsWith('▶'));
        var total = lines.Sum(x => x.DurationMs ?? 0);
        var speakers = lines.Where(x => x.Kind == DialogueKind.Dialogue && x.Speaker.Length > 0)
            .Select(x => x.Speaker).Distinct(StringComparer.CurrentCultureIgnoreCase).Count();
        var npc = lines.Count(x => x.Kind == DialogueKind.Dialogue && x.IsNpc);
        var warnings = _dialogueRows.Count(x => x.StateText.Contains('⚠'));
        DialogueStatsText.Text = $"{lines.Length}/{DialogueScript.MaxLines} líneas · {_dialogueRows.Count(x => !x.IsLine)} escenas · " +
            $"{speakers} personajes ({npc} líneas NPC) · audio {ready}/{lines.Length}" +
            (total > 0 ? $" · {FormatTimelineTime(total)}" : "") + (warnings > 0 ? $" · ⚠ {warnings}" : "");
    }

    // ───────────────────────────── Import ─────────────────────────────

    private DialogueParseMode SelectedDialogueParseMode =>
        DialogueParseModeCombo.SelectedIndex == 1 ? DialogueParseMode.Analysis : DialogueParseMode.Recommended;

    private void ImportDialogueFile_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is null || DialogueBusy()) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importar guion de diálogos",
            Filter = "Guiones (*.txt;*.md;*.docx;*.xlsx;*.csv;*.tsv)|*.txt;*.md;*.docx;*.docm;*.xlsx;*.xlsm;*.csv;*.tsv|" +
                     "Texto (*.txt;*.md)|*.txt;*.md|Word (*.docx)|*.docx|Excel (*.xlsx)|*.xlsx|CSV (*.csv;*.tsv)|*.csv;*.tsv|Todos (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var entries = DialogueScript.ParseFile(dialog.FileName, SelectedDialogueParseMode);
            ImportDialogueEntries(entries, Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or
                                       System.Xml.XmlException)
        {
            DialogueStatusText.Text = "No se pudo leer el archivo: " + ex.Message;
            MessageBox.Show(this, ex.Message, "Importar diálogos", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Adds parsed rows at the end or replaces the table (asked when it already has lines).</summary>
    private void ImportDialogueEntries(IReadOnlyList<DialogueEntry> entries, string source, bool? append = null)
    {
        if (entries.Count == 0)
        {
            DialogueStatusText.Text = $"No se encontraron líneas en {source}." +
                (SelectedDialogueParseMode == DialogueParseMode.Recommended ? " Prueba con «Modo análisis»." : "");
            return;
        }
        if (append is null && _dialogueRows.Count > 0)
        {
            var answer = MessageBox.Show(this, $"Se leyeron {entries.Count} fila(s) de {source}.\n\nSí = añadirlas al final\nNo = reemplazar la tabla actual",
                "Importar diálogos", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) return;
            append = answer == MessageBoxResult.Yes;
        }
        StopDialoguePlayback();
        if (append != true) ClearDialogueRows();
        var room = DialogueScript.MaxLines - _dialogueRows.Count(x => x.IsLine);
        var added = 0;
        var dropped = 0;
        _loadingDialogue = true;
        try
        {
            foreach (var entry in entries)
            {
                if (entry.Kind != DialogueKind.SceneBreak && added >= room) { dropped++; continue; }
                AddDialogueRow(new DialogueRow
                {
                    Kind = entry.Kind, Speaker = entry.Kind == DialogueKind.Narration ? DialogueScript.Narrator : entry.Speaker,
                    Text = entry.Text, Comment = entry.Comment, ParseWarning = entry.Warning
                }, _dialogueRows.Count);
                if (entry.Kind != DialogueKind.SceneBreak) added++;
            }
        }
        finally { _loadingDialogue = false; }
        RefreshDialogueSpeakerChoices();
        RefreshAllDialogueRows();
        MarkDialogueDirty();
        var imported = _dialogueRows.Skip(Math.Max(0, _dialogueRows.Count - entries.Count)).ToArray();
        var npcNames = imported.Where(x => x.Kind == DialogueKind.Dialogue && x.CharacterId is null && x.Speaker.Length > 0)
            .Select(x => x.Speaker).Distinct(StringComparer.CurrentCultureIgnoreCase).ToArray();
        var warnings = imported.Count(x => x.ParseWarning is not null);
        DialogueStatusText.Text = $"{added} línea(s) de {source}" +
            $" ({entries.Count(x => x.Kind == DialogueKind.Dialogue)} diálogos, {entries.Count(x => x.Kind == DialogueKind.Narration)} narraciones, " +
            $"{entries.Count(x => x.Kind == DialogueKind.SceneBreak)} cortes)." +
            (npcNames.Length > 0 ? $" NPC sin registrar: {string.Join(", ", npcNames.Take(6))}{(npcNames.Length > 6 ? "…" : "")}." : "") +
            (warnings > 0 ? $" {warnings} con aviso (columna Estado)." : "") +
            (dropped > 0 ? $" ⚠ {dropped} no cupieron: máximo {DialogueScript.MaxLines} líneas por guion." : "");
        if (dropped > 0)
            MessageBox.Show(this, $"El guion admite {DialogueScript.MaxLines} líneas (12 escenas × 40, el límite del Director IA por episodio). " +
                $"{dropped} línea(s) no se importaron: créalas en otro guion de diálogos.", "Importar diálogos", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ───────────────────────────── Editing ─────────────────────────────

    private DialogueRow[] SelectedDialogueRows() =>
        DialogueGrid.SelectedItems.OfType<DialogueRow>().OrderBy(x => _dialogueRows.IndexOf(x)).ToArray();

    private bool CommitDialogueEdits() =>
        DialogueGrid.CommitEdit(DataGridEditingUnit.Cell, true) && DialogueGrid.CommitEdit(DataGridEditingUnit.Row, true);

    private void AddDialogueLine_Click(object sender, RoutedEventArgs e) => InsertDialogueRow(DialogueKind.Dialogue);
    private void AddDialogueScene_Click(object sender, RoutedEventArgs e) => InsertDialogueRow(DialogueKind.SceneBreak);

    private void InsertDialogueRow(DialogueKind kind)
    {
        if (_currentRepository is null || DialogueBusy()) return;
        CommitDialogueEdits();
        if (kind != DialogueKind.SceneBreak && _dialogueRows.Count(x => x.IsLine) >= DialogueScript.MaxLines)
        {
            DialogueStatusText.Text = $"Máximo {DialogueScript.MaxLines} líneas por guion: crea otro guion de diálogos.";
            return;
        }
        var selected = SelectedDialogueRows();
        var index = selected.Length > 0 ? _dialogueRows.IndexOf(selected[^1]) + 1 : _dialogueRows.Count;
        var previous = index > 0 ? _dialogueRows[index - 1] : null;
        var row = new DialogueRow
        {
            Kind = kind,
            Speaker = kind == DialogueKind.SceneBreak ? "" : previous is { Kind: DialogueKind.Dialogue } ? previous.Speaker : "",
            Text = kind == DialogueKind.SceneBreak ? $"Escena {_dialogueRows.Count(x => !x.IsLine) + 1}" : ""
        };
        AddDialogueRow(row, index);
        RefreshDialogueRow(row);
        RenumberDialogueRows();
        MarkDialogueDirty();
        DialogueGrid.SelectedItem = row;
        DialogueGrid.ScrollIntoView(row);
    }

    private void DuplicateDialogueLines_Click(object sender, RoutedEventArgs e)
    {
        if (DialogueBusy()) return;
        CommitDialogueEdits();
        var selected = SelectedDialogueRows();
        if (selected.Length == 0) return;
        if (_dialogueRows.Count(x => x.IsLine) + selected.Count(x => x.IsLine) > DialogueScript.MaxLines)
        {
            DialogueStatusText.Text = $"No caben: máximo {DialogueScript.MaxLines} líneas por guion.";
            return;
        }
        var index = _dialogueRows.IndexOf(selected[^1]) + 1;
        var copies = selected.Select(x => x.Clone()).ToArray();
        foreach (var copy in copies) AddDialogueRow(copy, index++);
        foreach (var copy in copies) RefreshDialogueRow(copy);
        RenumberDialogueRows();
        MarkDialogueDirty();
        DialogueGrid.SelectedItems.Clear();
        foreach (var copy in copies) DialogueGrid.SelectedItems.Add(copy);
    }

    private void MoveDialogueUp_Click(object sender, RoutedEventArgs e) => MoveDialogueRows(-1);
    private void MoveDialogueDown_Click(object sender, RoutedEventArgs e) => MoveDialogueRows(1);

    private void MoveDialogueRows(int delta)
    {
        if (DialogueBusy()) return;
        CommitDialogueEdits();
        var selected = SelectedDialogueRows();
        if (selected.Length == 0) return;
        var indexes = selected.Select(x => _dialogueRows.IndexOf(x)).ToArray();
        if (delta < 0 && indexes[0] == 0 || delta > 0 && indexes[^1] == _dialogueRows.Count - 1) return;
        foreach (var index in delta < 0 ? indexes : indexes.Reverse())
            _dialogueRows.Move(index, index + delta);
        RenumberDialogueRows();
        MarkDialogueDirty();
        DialogueGrid.SelectedItems.Clear();
        foreach (var row in selected) DialogueGrid.SelectedItems.Add(row);
        DialogueGrid.ScrollIntoView(selected[0]);
    }

    private void DeleteDialogueLines_Click(object sender, RoutedEventArgs e) => DeleteSelectedDialogueRows();

    private void DeleteSelectedDialogueRows()
    {
        if (DialogueBusy()) return;
        CommitDialogueEdits();
        var selected = SelectedDialogueRows();
        if (selected.Length == 0) return;
        if (selected.Length > 1 && MessageBox.Show(this, $"¿Eliminar {selected.Length} filas?", "Diálogos",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (selected.Any(x => x.IsPlaying)) StopDialoguePlayback();
        var next = _dialogueRows.IndexOf(selected[0]);
        foreach (var row in selected) RemoveDialogueRow(row);
        RenumberDialogueRows();
        RefreshDialogueSpeakerChoices();
        MarkDialogueDirty();
        if (_dialogueRows.Count > 0) DialogueGrid.SelectedItem = _dialogueRows[Math.Min(next, _dialogueRows.Count - 1)];
    }

    private void DialogueGrid_KeyDown(object sender, KeyEventArgs e)
    {
        // Editing cells handle Delete themselves; here it removes the selected rows.
        if (e.Key == Key.Delete && e.OriginalSource is DataGridCell && DialogueGrid.SelectedItems.Count > 0)
        {
            DeleteSelectedDialogueRows();
            e.Handled = true;
        }
    }

    private void DialogueGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || e.Row.Item is not DialogueRow row) return;
        // After the binding has written the value: the row was reviewed, its import warning goes away.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (row.ParseWarning is null || !_dialogueRows.Contains(row)) return;
            row.ParseWarning = null;
            RefreshDialogueRow(row);
            MarkDialogueDirty();
        }), DispatcherPriority.Background);
    }

    private void AssignDialogueSpeaker_Click(object sender, RoutedEventArgs e)
    {
        CommitDialogueEdits();
        var name = DialogueBulkSpeakerCombo.Text?.Trim() ?? "";
        var rows = SelectedDialogueRows().Where(x => x.IsLine).ToArray();
        if (rows.Length == 0 || name.Length == 0)
        {
            DialogueStatusText.Text = "Selecciona filas y escribe o elige quién habla.";
            return;
        }
        if (name.Length > 40 || name.Contains(':') || name.Contains('|'))
        {
            DialogueStatusText.Text = "Nombre no válido: máximo 40 caracteres, sin «:» ni «|».";
            return;
        }
        foreach (var row in rows) row.Speaker = name; // DialogueRow_PropertyChanged adjusts kind and voice
        DialogueStatusText.Text = $"{rows.Length} fila(s) asignadas a {name}.";
    }

    private void AssignDialogueVoice_Click(object sender, RoutedEventArgs e)
    {
        CommitDialogueEdits();
        if (DialogueBulkVoiceCombo.SelectedItem is not VoiceProfileChoice voice) return;
        var rows = SelectedDialogueRows().Where(x => x.IsLine).ToArray();
        var npc = rows.Where(x => x.IsNpc).ToArray();
        foreach (var row in npc) row.VoiceChoice = voice;
        DialogueStatusText.Text = rows.Length == 0 ? "Selecciona las filas NPC a las que dar esa voz."
            : $"{npc.Length} fila(s) NPC con «{voice.Name}»." +
              (npc.Length < rows.Length ? $" {rows.Length - npc.Length} de personajes con perfil no cambian (su voz se edita en Voice Lab)." : "");
    }

    private void ApplyDialogueProsody_Click(object sender, RoutedEventArgs e)
    {
        CommitDialogueEdits();
        var rows = SelectedDialogueRows().Where(x => x.IsLine).ToArray();
        if (rows.Length == 0)
        {
            DialogueStatusText.Text = "Selecciona las líneas a ajustar.";
            return;
        }
        foreach (var row in rows)
        {
            row.PitchText = DialogueBulkPitchBox.Text;
            row.SpeedText = DialogueBulkSpeedBox.Text;
            row.VolumeText = DialogueBulkVolumeBox.Text;
        }
        DialogueStatusText.Text = $"Ajuste aplicado a {rows.Length} línea(s) (vacío = el valor del perfil). El perfil no cambia.";
    }

    private void EditDialogueProfile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DialogueRow row || row.CharacterId is not Guid characterId) return;
        var character = _characters.FirstOrDefault(x => x.Id == characterId);
        VoiceLabTab.IsSelected = true;
        CharactersGrid.SelectedItem = CharactersGrid.Items.OfType<CharacterVoiceRow>().FirstOrDefault(x => x.Character.Id == characterId);
        if (character?.DefaultVoiceProfileId is Guid profileId)
            VoiceProfileCombo.SelectedItem = _voiceProfiles.FirstOrDefault(x => x.Id == profileId);
        VoiceLabStatusText.Text = $"Perfil de {character?.Name}: al guardarlo, las líneas de Diálogos que cambien quedarán pendientes de audio.";
    }

    private void DialogueSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        FindNextDialogue();
        e.Handled = true;
    }

    private void FindDialogue_Click(object sender, RoutedEventArgs e) => FindNextDialogue();

    private void FindNextDialogue()
    {
        var query = DialogueSearchBox.Text.Trim();
        if (query.Length == 0 || _dialogueRows.Count == 0) return;
        var start = DialogueGrid.SelectedItem is DialogueRow current ? _dialogueRows.IndexOf(current) + 1 : 0;
        for (var i = 0; i < _dialogueRows.Count; i++)
        {
            var row = _dialogueRows[(start + i) % _dialogueRows.Count];
            if (row.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                row.Speaker.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                row.Comment.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            {
                DialogueGrid.SelectedItem = row;
                DialogueGrid.ScrollIntoView(row);
                DialogueStatusText.Text = $"«{query}» en la línea {row.Order}.";
                return;
            }
        }
        DialogueStatusText.Text = $"No se encontró «{query}».";
    }
}
