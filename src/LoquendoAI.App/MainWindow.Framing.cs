using System.Globalization;
using System.IO;
using System.Windows;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;
using LoquendoAI.Infrastructure.Persistence;

namespace LoquendoAI.App;

/// <summary>
/// «Revisar encuadre» (1.4.4): renders the AI (or a hand edit) left out of the frame or tiny. The rules and fixes are in
/// FramingAudit and the per-scene memory in FramingMemory (both tested); this part runs them on the open scene, shows the
/// review window, fills the editor fields («Ajustar al cuadro», «Escala %») and asks before the first preview after the
/// Director applied a scene with warnings.
/// </summary>
public partial class MainWindow
{
    /// <summary>Scenes the Director just filled with framing warnings: their next ▶ asks to review them first.</summary>
    private readonly HashSet<Guid> _framingPendingScenes = [];

    private static string FramingMemoryPath(SqliteProjectRepository repository, Guid sceneId) =>
        Path.Combine(repository.ProjectRoot, "generated", "encuadre", $"escena_{sceneId:N}.json");

    /// <summary>How a block is named in the review: the character, «NPC «render»» or «Prop «recurso»».</summary>
    private string FramingName(SceneScriptBlock block)
    {
        var asset = block.AssetId is Guid id && _scriptAssetCache.TryGetValue(id, out var record) ? record.DisplayName : "recurso";
        if (block.Kind == ScriptBlockKind.Image) return $"Prop «{asset}»";
        return _characters.FirstOrDefault(x => x.Id == block.CharacterId)?.Name ?? $"NPC «{asset}»";
    }

    /// <summary>
    /// Plans the scene for the review without generating anything: lines without a voice yet (right after the Director)
    /// count as a pause of about their length, and blocks whose file is missing are left out.
    /// </summary>
    private async Task<IReadOnlyList<FramingIssue>> AuditFramingAsync(SqliteProjectRepository repository, Guid sceneId,
        IReadOnlyList<SceneScriptBlock> blocks, CancellationToken token = default)
    {
        var paths = await ResolveScenePathsAsync(blocks.ToArray(), repository.ProjectRoot);
        var stand = blocks.Select(block =>
        {
            if (paths.ContainsKey(block.Id)) return block;
            if (IsSpeechBlock(block.Kind))
            {
                var estimate = block.GeneratedDurationMs is long known && known > 0 ? known : Math.Max(800L, block.Text.Length * 65L);
                return new SceneScriptBlock(block.Id, block.SceneId, block.OrderIndex, ScriptBlockKind.Pause,
                    PauseAfterMs: block.PauseAfterMs + (int)Math.Min(60_000L, estimate));
            }
            return block.Kind is ScriptBlockKind.Background or ScriptBlockKind.CharacterShow or ScriptBlockKind.Image or
                ScriptBlockKind.Video or ScriptBlockKind.SoundEffect or ScriptBlockKind.Music
                ? new SceneScriptBlock(block.Id, block.SceneId, block.OrderIndex, ScriptBlockKind.Comment, PauseAfterMs: block.PauseAfterMs)
                : block;
        }).ToArray();
        var planned = await SceneComposer.PlanAsync(stand, b => paths.GetValueOrDefault(b.Id), token,
            b => b.AssetId is Guid id && _scriptAssetCache.TryGetValue(id, out var asset) && SceneComposer.LooksLikeGreenScreen(asset.DisplayName),
            await CinemaAtSceneStartAsync(repository, sceneId));
        return await FramingAudit.AuditAsync(blocks, planned, FramingName, token);
    }

    /// <summary>After «Aplicar»: counts the framing warnings of the scene; the next ▶ of that scene asks to review them.
    /// A failure here never undoes or blocks the applied draft.</summary>
    private async Task ReportFramingAfterApplyAsync(DirectorDraftSession session)
    {
        if (_currentRepository is not { } repository || ScenesList.SelectedItem is not SceneScriptRow scene) return;
        try
        {
            var blocks = _scriptBlocks.ToArray();
            var memory = FramingMemory.Load(FramingMemoryPath(repository, scene.Scene.Id));
            memory.Prune(blocks);
            var pending = memory.Pending(await AuditFramingAsync(repository, scene.Scene.Id, blocks));
            if (_currentRepository != repository || (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id != scene.Scene.Id) return;
            if (pending.Count == 0)
            {
                _framingPendingScenes.Remove(scene.Scene.Id);
                return;
            }
            _framingPendingScenes.Add(scene.Scene.Id);
            DraftStatus(session).Text += $" ⚠ {pending.Count} aviso(s) de encuadre (renders fuera del cuadro o muy pequeños): " +
                "revísalos con «Revisar encuadre», junto a ▶ Reproducir.";
        }
        catch (Exception ex) { ErrorLog.Record(ex); }
    }

    /// <summary>Before the first preview of a scene the Director left with framing warnings: review now or generate anyway.
    /// Returns false when the preview must not start now.</summary>
    private async Task<bool> ConfirmFramingBeforePreviewAsync(Guid sceneId)
    {
        if (!_framingPendingScenes.Remove(sceneId)) return true;
        var answer = MessageBox.Show(this,
            "El Director dejó avisos de encuadre en esta escena (renders fuera del cuadro o muy pequeños).\n\n" +
            "¿Revisarlos antes de generar la preview?\n\nSí: revisar ahora · No: generar igual",
            "Revisar encuadre", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Yes);
        if (answer == MessageBoxResult.No) return true;
        if (answer == MessageBoxResult.Yes) await OpenFramingReviewAsync();
        return false;
    }

    private async void ReviewFraming_Click(object sender, RoutedEventArgs e) => await OpenFramingReviewAsync();

    /// <summary>The review window for the whole scene: pending warnings, remembered fixes (to switch or undo) and blocks
    /// marked «es a propósito». Writes the blocks the user changes.</summary>
    private async Task OpenFramingReviewAsync()
    {
        if (_currentRepository is not { } repository || ScenesList.SelectedItem is not SceneScriptRow scene || _scriptBlocks.Count == 0)
            return;
        var sceneId = scene.Scene.Id;
        IReadOnlyList<FramingIssue> issues;
        FramingMemory memory;
        var blocks = _scriptBlocks.ToArray();
        try
        {
            ScriptStatusText.Text = "Revisando el encuadre…";
            memory = FramingMemory.Load(FramingMemoryPath(repository, sceneId));
            memory.Prune(blocks);
            issues = await AuditFramingAsync(repository, sceneId, blocks);
        }
        catch (Exception ex)
        {
            ScriptStatusText.Text = "No se pudo revisar el encuadre: " + ErrorLog.Summary(ex);
            ErrorLog.Record(ex);
            return;
        }
        if (_currentRepository != repository || (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id != sceneId) return;
        _framingPendingScenes.Remove(sceneId);
        var byId = blocks.ToDictionary(x => x.Id);
        var window = new FramingReviewWindow(issues, memory, byId, FramingName) { Owner = this };
        if (window.ShowDialog() != true)
        {
            ScriptStatusText.Text = memory.Pending(issues).Count == 0 ? "Encuadre revisado: todo en orden." : "Revisión de encuadre cerrada sin cambios.";
            if (window.OpenBlockId is Guid open) await LoadBlocksAsync(sceneId, open);
            return;
        }
        try
        {
            var changed = 0;
            foreach (var (blockId, choice, scale) in window.Choices)
            {
                if (!byId.TryGetValue(blockId, out var block)) continue;
                var result = memory.Choose(block, issues.FirstOrDefault(x => x.BlockId == blockId), choice, scale);
                if (result.ParametersJson == block.ParametersJson) continue;
                await repository.UpsertSceneScriptBlockAsync(result);
                changed++;
            }
            memory.Save(FramingMemoryPath(repository, sceneId));
            if ((ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id != sceneId) return;
            await LoadBlocksAsync(sceneId, window.OpenBlockId);
            await RefreshSceneTimingAsync(_scriptBlocks);
            ScriptStatusText.Text = changed == 0 ? "Encuadre revisado; no hubo cambios en los bloques."
                : $"Encuadre corregido en {changed} bloque(s). Pulsa ▶ para ver la escena; «Revisar encuadre» deja cambiar o deshacer cada corrección.";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    /// <summary>
    /// «Ajustar al cuadro» of the editor: reviews the block with the values in the fields (not yet saved) and writes the
    /// fix back into the fields, so the change is seen before saving. For a first appearance that leaves the frame it
    /// asks whether it was an entrance.
    /// </summary>
    private async void FitToFrame_Click(object sender, RoutedEventArgs e)
    {
        var kind = CurrentEditorKind;
        if (_currentRepository is not { } repository || ScenesList.SelectedItem is not SceneScriptRow scene ||
            kind is not (ScriptBlockKind.CharacterShow or ScriptBlockKind.Image))
        {
            ScriptStatusText.Text = "«Ajustar al cuadro» es para renders de personaje e imágenes/props.";
            return;
        }
        if ((ScriptAssetCombo.SelectedItem as AssetChoice)?.Id is not Guid assetId)
        {
            ScriptStatusText.Text = "Elige primero el render o la imagen.";
            return;
        }
        var previous = _editingScriptBlockId is Guid id ? _scriptBlocks.FirstOrDefault(x => x.Id == id) : null;
        var parameters = kind == ScriptBlockKind.CharacterShow ? SerializeSpriteParameters() : SerializeResourceParameters(kind);
        var probe = new SceneScriptBlock(previous?.Id ?? Guid.NewGuid(), scene.Scene.Id, previous?.OrderIndex ?? _scriptBlocks.Count, kind,
            (ScriptCharacterCombo.SelectedItem as CharacterChoice)?.Id, "", assetId, ParametersJson: parameters);
        var blocks = _scriptBlocks.Where(x => x.Id != probe.Id).Append(probe).OrderBy(x => x.OrderIndex).ToArray();
        FramingIssue[] found;
        try
        {
            ScriptStatusText.Text = "Revisando el encuadre de este bloque…";
            found = (await AuditFramingAsync(repository, scene.Scene.Id, blocks)).Where(x => x.BlockId == probe.Id).ToArray();
        }
        catch (Exception ex)
        {
            ScriptStatusText.Text = "No se pudo revisar el encuadre: " + ErrorLog.Summary(ex);
            return;
        }
        if (found.Length == 0)
        {
            ScriptStatusText.Text = "Este bloque se ve bien en el cuadro.";
            return;
        }
        var result = BlockParameters.Parse(parameters);
        var notes = new List<string>();
        foreach (var issue in found)
        {
            var fix = issue.Fixes[0];
            if (issue.Fixes.FirstOrDefault(x => x.Id == "entrada") is { } entrance)
            {
                var answer = MessageBox.Show(this,
                    $"{issue.Message}.\n\n¿Era una entrada? Sí: empieza fuera del cuadro y entra hasta su sitio.\n" +
                    "No: se limita el movimiento para que no se salga (un empujón, una pelea…).",
                    "Ajustar al cuadro", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.No);
                if (answer == MessageBoxResult.Cancel) return;
                if (answer == MessageBoxResult.Yes) fix = entrance;
            }
            result = FramingAudit.Apply(result, fix.Changes);
            notes.Add($"{issue.Message} → {fix.Label.ToLowerInvariant()}");
        }
        var transform = result.Transform(kind);
        VisualWidthBox.Text = transform.MaxWidth.ToString(CultureInfo.InvariantCulture);
        VisualHeightBox.Text = transform.MaxHeight.ToString(CultureInfo.InvariantCulture);
        // The fields hold the block's own values: «Invertir horizontal» mirrors them only when the scene is planned.
        VisualOffsetXBox.Text = (result.VisualOffsetX ?? 0).ToString(CultureInfo.InvariantCulture);
        VisualOffsetYBox.Text = (result.VisualOffsetY ?? 0).ToString(CultureInfo.InvariantCulture);
        MotionOffsetXBox.Text = (result.MotionOffsetX ?? 0).ToString(CultureInfo.InvariantCulture);
        MotionOffsetYBox.Text = (result.MotionOffsetY ?? 0).ToString(CultureInfo.InvariantCulture);
        if (kind == ScriptBlockKind.CharacterShow)
            CharacterFramingCombo.SelectedIndex = result.Framing(kind) switch
            {
                "auto" => 1, "entero" => 2, "medio" => 3, "detalle" => 4, _ => 0
            };
        ScriptStatusText.Text = "Ajustado: " + string.Join("; ", notes) + ". Revisa los campos y guarda el bloque.";
    }

    /// <summary>«Escala %» of the editor: width and height of the visual times the percentage, within its limits.</summary>
    private void ApplyEditorScale_Click(object sender, RoutedEventArgs e)
    {
        var kind = CurrentEditorKind;
        if (!double.TryParse(EditorScaleBox.Text.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) ||
            percent is < 10 or > 500 ||
            !int.TryParse(VisualWidthBox.Text.Trim(), out var width) || !int.TryParse(VisualHeightBox.Text.Trim(), out var height))
        {
            ScriptStatusText.Text = "Escala: un porcentaje entre 10 y 500 (100 = sin cambio), con ancho y alto válidos.";
            return;
        }
        var max = BlockDefaults.VisualMaxBox(kind);
        VisualWidthBox.Text = Math.Clamp((int)Math.Round(width * percent / 100), 64, max.Width).ToString(CultureInfo.InvariantCulture);
        VisualHeightBox.Text = Math.Clamp((int)Math.Round(height * percent / 100), 64, max.Height).ToString(CultureInfo.InvariantCulture);
        EditorScaleBox.Text = "100";
        ScriptStatusText.Text = $"Tamaño al {percent.ToString("0.#", CultureInfo.InvariantCulture)} %: {VisualWidthBox.Text}×{VisualHeightBox.Text}. Guarda el bloque para aplicarlo.";
    }
}
