using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LoquendoAI.Core.Models;

namespace LoquendoAI.App;

public partial class MainWindow
{
    private async void PickDirectorDraftAsset_Click(object sender, RoutedEventArgs e)
    {
        var session = DraftSessionOf(sender);
        var grid = DraftGrid(session);
        var status = DraftStatus(session);
        if (_currentRepository is not { } repository ||
            grid.SelectedItem is not DirectorDraftRow { Instruction: not null } row) return;
        if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) ||
            !grid.CommitEdit(DataGridEditingUnit.Row, true)) return;
        var specs = ParseDirectorPrompt(row.Instruction);
        if (specs.Count != 1 || RelevantAssetKinds(specs[0].Kind).Count == 0)
        {
            status.Text = "Selecciona una fila de fondo, render, imagen, vídeo, música o SFX.";
            return;
        }
        var kind = specs[0].Kind;
        try
        {
            // Browse the catalogue (not the disk): the list is limited to files this row can
            // play, shows the library category and tags, and previews images/audio.
            var sources = await repository.GetAssetSourcesAsync();
            var audio = kind is ScriptBlockKind.Music or ScriptBlockKind.SoundEffect;
            var extensions = audio ? DirectorAudioExtensions : kind == ScriptBlockKind.Video
                ? DirectorVideoExtensions : DirectorImageExtensions;
            var picker = new AssetPickerWindow(repository, DirectorKindName(kind), extensions.ToArray(),
                RelevantAssetKinds(kind), sources, ResolveAssetPathAsync,
                kind == ScriptBlockKind.CharacterShow ? specs[0].CharacterName : null,
                audio: audio, image: kind is ScriptBlockKind.Background or ScriptBlockKind.CharacterShow or ScriptBlockKind.Image,
                audioUse: audio ? kind : null)
            { Owner = this };
            if (picker.ShowDialog() != true || picker.SelectedAsset is not { } asset) return;
            if (asset.IsMissing || !DirectorAssetCompatible(kind, asset))
                throw new InvalidOperationException("Ese recurso no está disponible o su formato no sirve para esta fila.");
            _scriptAssetCache[asset.Id] = asset;
            // The row object was captured before the dialog; it only has to still belong to the draft.
            if (_currentRepository != repository || grid.ItemsSource is not IEnumerable<DirectorDraftRow> current ||
                !current.Any(x => ReferenceEquals(x, row)) || row.Instruction is null)
            {
                status.Text = "El borrador cambió mientras elegías el recurso. Vuelve a seleccionar la fila.";
                return;
            }
            var parts = row.Instruction.Split('|', StringSplitOptions.TrimEntries).ToList();
            if (kind == ScriptBlockKind.CharacterShow)
            {
                // The first value is always the character. Insert the resource
                // before framing and position options if none is present yet.
                var resourceIndex = parts.FindIndex(1, x =>
                    !x.Contains('=') && !IsDirectorFramingOption(x) &&
                    !new[] { "izquierda", "derecha", "centro", "auto" }
                        .Any(position => SameDirectorName(x, position)));
                if (resourceIndex < 0) parts.Insert(1, asset.Id.ToString("D"));
                else parts[resourceIndex] = asset.Id.ToString("D");
            }
            else
            {
                var commandEnd = parts[0].IndexOf(']');
                parts[0] = parts[0][..(commandEnd + 1)] + " " + asset.Id.ToString("D");
            }
            row.Instruction = string.Join(" | ", parts);
            DraftApplyButton(session).IsEnabled = false;
            session.Validated = false;
            status.Text = "Recurso seleccionado. Pulsa Validar edición para revisar toda la escena.";
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }

    /// <summary>
    /// Director (prompt), 1.4.4: Supr removes the selected rows from the draft itself (the rows and the blocks behind
    /// them stay aligned), and the draft must be validated again before it is applied. The table of Director IA does
    /// not delete rows: that draft is sent whole and adjusted afterwards in the editor or here. In a voices draft the
    /// original blocks (the recorded WAVs) are not removed here either: that is done in the editor.
    /// </summary>
    private void DirectorDraftGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || DirectorDraftGrid.IsReadOnly || e.OriginalSource is TextBox) return;
        if (DirectorDraftGrid.ItemsSource is not IEnumerable<DirectorDraftRow> source) return;
        var selected = new HashSet<object>(DirectorDraftGrid.SelectedItems.Cast<object>(), ReferenceEqualityComparer.Instance);
        if (selected.Count == 0) return;
        e.Handled = true;
        var rows = source.ToList();
        if (rows.Count != _promptDraft.Draft.Length)
        {
            DirectorStatusText.Text = "El borrador no coincide con la tabla. Prepáralo de nuevo.";
            return;
        }
        if (_promptDraft.Recorded && rows.Any(x => selected.Contains(x) && x.Parameters == "Bloque original"))
        {
            DirectorStatusText.Text =
                "Con voces grabadas, los bloques originales no se quitan del borrador: bórralos o edítalos en el editor después de aplicarlo.";
            return;
        }
        var keptRows = new List<DirectorDraftRow>();
        var keptBlocks = new List<SceneScriptBlock>();
        for (var i = 0; i < rows.Count; i++)
        {
            if (selected.Contains(rows[i])) continue;
            keptRows.Add(rows[i] with { Order = keptRows.Count + 1 });
            keptBlocks.Add(_promptDraft.Draft[i] with { OrderIndex = keptBlocks.Count });
        }
        var removed = rows.Count - keptRows.Count;
        _promptDraft.Draft = keptBlocks.ToArray();
        DirectorDraftGrid.ItemsSource = keptRows;
        _promptDraft.Validated = false;
        DirectorApplyButton.IsEnabled = false;
        DirectorStatusText.Text = keptRows.Count == 0
            ? "El borrador quedó vacío."
            : $"Quitaste {removed} fila(s) del borrador. Pulsa Validar edición antes de aplicarlo.";
    }

    private void DirectorDraftGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.Item is not DirectorDraftRow { Instruction: not null })
        {
            e.Cancel = true; // Copied JSON and original recorded WAVs are not free-form instructions.
            return;
        }
        var session = DraftSessionOf(sender);
        DraftApplyButton(session).IsEnabled = false;
        session.Validated = false;
        DraftStatus(session).Text = "Cambios pendientes: pulsa Validar edición antes de aplicar al guion.";
    }

    private async void ValidateDirectorDraft_Click(object sender, RoutedEventArgs e)
    {
        var session = DraftSessionOf(sender);
        var grid = DraftGrid(session);
        var statusText = DraftStatus(session);
        if (_currentRepository is not { } repository || session.SceneId is not Guid sourceSceneId ||
            (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id is not Guid targetSceneId ||
            grid.ItemsSource is not IEnumerable<DirectorDraftRow> source) return;
        if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true)) return;
        var rows = source.ToArray();
        if (rows.Length != session.Draft.Length)
        {
            statusText.Text = "El borrador no contiene todos los bloques originales. Genéralo de nuevo.";
            return;
        }
        var validateButton = session.Ai ? AiValidateButton : DirectorValidateButton;
        var pickButton = session.Ai ? AiPickDraftAssetButton : DirectorPickDraftAssetButton;
        validateButton.IsEnabled = false;
        DraftApplyButton(session).IsEnabled = false;
        grid.IsReadOnly = true;
        pickButton.IsEnabled = false;
        try
        {
            // A changed scene is decided (and confirmed) by «Aplicar» (DraftApplyPolicy, 1.4.4), not here.
            if (_currentRepository != repository || (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id != targetSceneId)
                throw new InvalidOperationException("La escena cambió. Valida otra vez.");
            var sources = await repository.GetAssetSourcesAsync();
            var blocks = new List<SceneScriptBlock>();
            var validated = new List<DirectorDraftRow>();
            foreach (var row in rows)
            {
                if (row.Instruction is null)
                {
                    blocks.Add(session.Draft[blocks.Count]);
                    validated.Add(row);
                    continue;
                }
                var line = row.Instruction.Trim();
                if (line.Length == 0 || line.Length > 700 || line.Contains('\r') || line.Contains('\n'))
                {
                    blocks.Add(session.Draft[blocks.Count]);
                    validated.Add(row with { Status = "Escribe exactamente una instrucción (máximo 700 caracteres)", IsReady = false });
                    continue;
                }
                var parsed = ParseDirectorPrompt(line);
                if (parsed.Count != 1)
                {
                    blocks.Add(session.Draft[blocks.Count]);
                    validated.Add(row with
                    {
                        Status = parsed.Count > 1 && parsed.All(x => x.Kind == ScriptBlockKind.CharacterHide)
                            ? "Una fila oculta a un solo personaje: para varios a la vez escríbelo en el prompt y prepáralo de nuevo"
                            : "Una fila contiene una sola instrucción",
                        IsReady = false
                    });
                    continue;
                }
                var spec = parsed[0];
                var status = spec.Error ?? CinemaNamesIssue(spec);
                CharacterDefinition? character = null;
                AssetRecord? asset = null;
                string? resourceNote = null;
                if (session.Recorded && (spec.Kind is ScriptBlockKind.Dialogue or ScriptBlockKind.Narration))
                    status ??= "Con voces grabadas no se pueden añadir diálogos nuevos";
                if (status is null && spec.CharacterName.Length > 0)
                {
                    character = _characters.FirstOrDefault(x => SameDirectorName(x.Name, spec.CharacterName));
                    // A character without its own profile speaks with the NPC profile (1.2.0); «NPC: …» is a line
                    // without a registered character (1.4.0, what «Copiar escena» writes for them).
                    if (character is null && !IsNpcLine(spec) && !IsNpcRender(spec)) status = "Personaje sin registrar";
                }
                if (status is null && character is null && IsNpcRender(spec))
                {
                    var npc = await CheckNpcRenderAsync(repository, sources, spec, session.Original.Concat(blocks));
                    status = npc.Status;
                    asset = npc.Asset;
                    resourceNote = npc.Note;
                }
                if (status is null && RelevantAssetKinds(spec.Kind).Count > 0)
                {
                    if (spec.ResourceQuery.Length == 0) status = "Falta el recurso";
                    else
                    {
                        var match = await FindDirectorAssetAsync(repository, sources, spec.Kind,
                            spec.ResourceQuery, character);
                        asset = match.Asset;
                        if (asset is null) status = match.Issue ?? "Recurso no catalogado";
                        else
                        {
                            resourceNote = match.Note;
                            if (spec.Kind == ScriptBlockKind.CharacterShow &&
                                asset.SubjectName is { Length: > 0 } owner && character is not null &&
                                !SameDirectorName(owner, character.Name))
                                status = "Este render pertenece a otro personaje";
                            _scriptAssetCache[asset.Id] = asset;
                        }
                    }
                }
                var previous = session.Draft[blocks.Count];
                blocks.Add(new SceneScriptBlock(previous.Id, sourceSceneId, blocks.Count, spec.Kind,
                    CharacterId: character?.Id, Text: spec.Text, AssetId: asset?.Id,
                    VoiceProfileId: spec.Kind == ScriptBlockKind.Dialogue ? character?.DefaultVoiceProfileId : null,
                    PauseAfterMs: spec.PauseMs, ParametersJson: DirectorParameters(spec, CharacterIdByName)));
                validated.Add(row with
                {
                    Instruction = line, Kind = DirectorKindName(spec.Kind),
                    Subject = character?.Name ?? asset?.DisplayName ?? (spec.ResourceQuery.Length > 0 ? spec.ResourceQuery : spec.CharacterName),
                    Text = spec.Text, Parameters = spec.Summary,
                    Status = status ?? resourceNote ?? "Listo", IsReady = status is null
                });
            }
            if (_currentRepository != repository ||
                (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id != targetSceneId)
                throw new InvalidOperationException("La escena cambió. Valida otra vez.");
            session.Draft = blocks.ToArray();
            grid.ItemsSource = validated;
            var pending = validated.Count(x => !x.IsReady);
            session.Validated = pending == 0 && validated.Count > 0;
            DraftApplyButton(session).IsEnabled = session.Validated;
            statusText.Text = pending == 0
                ? "Edición validada. Revisa las filas y aplica al guion cuando quieras."
                : $"{pending} fila(s) pendientes. Corrige la instrucción y vuelve a validar.";
        }
        catch (Exception ex)
        {
            statusText.Text = ex.Message;
        }
        finally
        {
            grid.IsReadOnly = false;
            pickButton.IsEnabled = true;
            validateButton.IsEnabled = true;
        }
    }
}
