using System.IO;
using System.Windows;
using System.Windows.Controls;
using LoquendoAI.Core.Models;

namespace LoquendoAI.App;

public partial class MainWindow
{
    private async void PickDirectorDraftAsset_Click(object sender, RoutedEventArgs e)
    {
        var grid = sender == AiPickDraftAssetButton ? AiDraftGrid : DirectorDraftGrid;
        if (_currentRepository is not { } repository ||
            grid.SelectedItem is not DirectorDraftRow { Instruction: not null } row) return;
        if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) ||
            !grid.CommitEdit(DataGridEditingUnit.Row, true)) return;
        var specs = ParseDirectorPrompt(row.Instruction);
        if (specs.Count != 1 || RelevantAssetKinds(specs[0].Kind).Count == 0)
        {
            DirectorStatusText.Text = AiDirectorStatusText.Text =
                "Selecciona una fila de fondo, render, imagen, vídeo, música o SFX.";
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
                DirectorStatusText.Text = AiDirectorStatusText.Text =
                    "El borrador cambió mientras elegías el recurso. Vuelve a seleccionar la fila.";
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
            DirectorApplyButton.IsEnabled = AiApplyButton.IsEnabled = false;
            _directorDraftValidated = false;
            DirectorStatusText.Text = AiDirectorStatusText.Text =
                "Recurso seleccionado. Pulsa Validar edición para revisar toda la escena.";
        }
        catch (Exception ex) { DirectorStatusText.Text = AiDirectorStatusText.Text = ex.Message; }
    }

    private void DirectorDraftGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.Item is not DirectorDraftRow { Instruction: not null })
        {
            e.Cancel = true; // Copied JSON and original recorded WAVs are not free-form instructions.
            return;
        }
        DirectorApplyButton.IsEnabled = AiApplyButton.IsEnabled = false;
        _directorDraftValidated = false;
        DirectorStatusText.Text = AiDirectorStatusText.Text =
            "Cambios pendientes: pulsa Validar edición antes de aplicar al guion.";
    }

    private async void ValidateDirectorDraft_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is not { } repository || _directorSceneId is not Guid sourceSceneId ||
            (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id is not Guid targetSceneId ||
            DirectorDraftGrid.ItemsSource is not IEnumerable<DirectorDraftRow> source) return;
        if (!DirectorDraftGrid.CommitEdit(DataGridEditingUnit.Cell, true) ||
            !DirectorDraftGrid.CommitEdit(DataGridEditingUnit.Row, true) ||
            AiDraftGrid.ItemsSource is not null &&
            (!AiDraftGrid.CommitEdit(DataGridEditingUnit.Cell, true) ||
             !AiDraftGrid.CommitEdit(DataGridEditingUnit.Row, true))) return;
        var rows = source.ToArray();
        if (rows.Length != _directorDraft.Length)
        {
            DirectorStatusText.Text = AiDirectorStatusText.Text =
                "El borrador no contiene todos los bloques originales. Genéralo de nuevo.";
            return;
        }
        DirectorValidateButton.IsEnabled = AiValidateButton.IsEnabled = false;
        DirectorApplyButton.IsEnabled = AiApplyButton.IsEnabled = false;
        DirectorDraftGrid.IsReadOnly = AiDraftGrid.IsReadOnly = true;
        DirectorPickDraftAssetButton.IsEnabled = AiPickDraftAssetButton.IsEnabled = false;
        try
        {
            var current = (await repository.GetSceneScriptBlocksAsync(targetSceneId)).ToArray();
            if (_currentRepository != repository ||
                (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id != targetSceneId ||
                targetSceneId == sourceSceneId && !current.SequenceEqual(_directorOriginal))
                throw new InvalidOperationException("El guion original cambió. Elige otra escena o copia el borrador para conservarlo.");
            var sources = await repository.GetAssetSourcesAsync();
            var blocks = new List<SceneScriptBlock>();
            var validated = new List<DirectorDraftRow>();
            foreach (var row in rows)
            {
                if (row.Instruction is null)
                {
                    blocks.Add(_directorDraft[blocks.Count]);
                    validated.Add(row);
                    continue;
                }
                var line = row.Instruction.Trim();
                if (line.Length == 0 || line.Length > 700 || line.Contains('\r') || line.Contains('\n'))
                {
                    blocks.Add(_directorDraft[blocks.Count]);
                    validated.Add(row with { Status = "Escribe exactamente una instrucción (máximo 700 caracteres)", IsReady = false });
                    continue;
                }
                var parsed = ParseDirectorPrompt(line);
                if (parsed.Count != 1)
                {
                    blocks.Add(_directorDraft[blocks.Count]);
                    validated.Add(row with { Status = "Una fila contiene una sola instrucción", IsReady = false });
                    continue;
                }
                var spec = parsed[0];
                var status = spec.Error ?? CinemaNamesIssue(spec);
                CharacterDefinition? character = null;
                AssetRecord? asset = null;
                string? resourceNote = null;
                if (_aiRecordedDraft && (spec.Kind is ScriptBlockKind.Dialogue or ScriptBlockKind.Narration))
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
                    var npc = await CheckNpcRenderAsync(repository, sources, spec, _directorOriginal.Concat(blocks));
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
                var previous = _directorDraft[blocks.Count];
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
            _directorDraft = blocks.ToArray();
            DirectorDraftGrid.ItemsSource = validated;
            if (_aiDraftPrompt is not null) AiDraftGrid.ItemsSource = validated;
            var pending = validated.Count(x => !x.IsReady);
            _directorDraftValidated = pending == 0 && validated.Count > 0;
            DirectorApplyButton.IsEnabled = _directorDraftValidated;
            AiApplyButton.IsEnabled = _aiDraftPrompt is not null && DirectorApplyButton.IsEnabled;
            DirectorStatusText.Text = AiDirectorStatusText.Text = pending == 0
                ? "Edición validada. Revisa las filas y aplica al guion cuando quieras."
                : $"{pending} fila(s) pendientes. Corrige la instrucción y vuelve a validar.";
        }
        catch (Exception ex)
        {
            DirectorStatusText.Text = AiDirectorStatusText.Text = ex.Message;
        }
        finally
        {
            DirectorDraftGrid.IsReadOnly = AiDraftGrid.IsReadOnly = false;
            DirectorPickDraftAssetButton.IsEnabled = AiPickDraftAssetButton.IsEnabled = true;
            DirectorValidateButton.IsEnabled = AiValidateButton.IsEnabled = true;
        }
    }
}
