using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;
using LoquendoAI.Infrastructure.Persistence;

namespace LoquendoAI.App;

public partial class MainWindow
{
    private sealed record CopiedScene(int Version, string SceneTitle, string? DirectionNotes,
        CopiedSceneBlock[] Blocks, string?[]? DraftInstructions = null);
    private sealed record CopiedSceneBlock(SceneScriptBlock Block, string? CharacterName,
        string? AssetName, string? AssetPath, string? AudioOrigin);

    private async void CopySceneForDirector_Click(object sender, RoutedEventArgs e)
    {
        if (_currentRepository is null || ScenesList.SelectedItem is not SceneScriptRow scene)
        {
            DirectorStatusText.Text = "Selecciona primero una escena.";
            return;
        }
        var format = ChooseSceneCopyFormat();
        if (format is null) return;
        try
        {
            var repository = _currentRepository;
            var blocks = await repository.GetSceneScriptBlocksAsync(scene.Scene.Id);
            var assets = (await repository.GetAssetsByIdsAsync(blocks.Where(x => x.AssetId.HasValue)
                .Select(x => x.AssetId!.Value).Distinct().ToArray())).ToDictionary(x => x.Id);
            var sources = (await repository.GetAssetSourcesAsync()).ToDictionary(x => x.Id);
            var characters = _characters.ToDictionary(x => x.Id, x => x.Name);
            var items = blocks.OrderBy(x => x.OrderIndex).Select(block =>
            {
                var asset = block.AssetId is Guid id ? assets.GetValueOrDefault(id) : null;
                var source = asset?.SourceId is Guid sourceId ? sources.GetValueOrDefault(sourceId) : null;
                var path = asset is null ? null : source is null ? asset.RelativePath :
                    Path.Combine(source.RootPath, (asset.SourceRelativePath ?? asset.RelativePath)
                        .Replace('/', Path.DirectorySeparatorChar));
                return new CopiedSceneBlock(block,
                    (block.Kind == ScriptBlockKind.CharacterHide ? SceneComposer.HideTarget(blocks, block).CharacterId : block.CharacterId)
                        is Guid characterId ? characters.GetValueOrDefault(characterId) : null,
                    asset?.DisplayName, path, IsImportedVoice(block) ? "grabación importada" :
                        block.GeneratedAudioPath is not null ? "TTS" : null);
            }).ToArray();
            var content = format == SceneCopyFormat.Json
                ? JsonSerializer.Serialize(new CopiedScene(1, scene.Scene.Title, scene.Scene.DirectionNotes, items),
                    new JsonSerializerOptions { WriteIndented = true })
                : BuildSimplifiedScene(items, assets, sources, scene.Scene.DirectionNotes);
            Clipboard.SetText(content);
            DirectorStatusText.Text = format == SceneCopyFormat.Json
                ? $"Escena copiada como JSON ({items.Length} bloques). Conserva los WAV, ajustes y referencias; puedes pegarlo aquí para preparar un borrador."
                : $"Escena copiada como bloque simplificado ({items.Length} bloques). Puedes pegarlo aquí para preparar un borrador; las voces grabadas requieren JSON.";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private enum SceneCopyFormat { Block, Json }

    private async void CopyDraftForDirector_Click(object sender, RoutedEventArgs e)
    {
        // Only an empty draft blocks the copy (1.4.3). «Aplicar al guion» ends the draft's link to its scene so it is
        // not applied twice, but its rows stay in the table and can still be copied. Each Director copies its own.
        var session = DraftSessionOf(sender);
        var grid = DraftGrid(session);
        if (_currentRepository is not { } repository ||
            session.Draft.Length == 0 || grid.ItemsSource is not IEnumerable<DirectorDraftRow> rowsSource)
        {
            DraftStatus(session).Text = "Primero prepara un borrador para copiarlo.";
            return;
        }
        if (!grid.CommitEdit(DataGridEditingUnit.Cell, true) || !grid.CommitEdit(DataGridEditingUnit.Row, true)) return;
        var format = ChooseSceneCopyFormat("borrador");
        if (format is null) return;
        try
        {
            var rows = rowsSource.ToArray();
            var blocks = session.Draft.ToArray();
            if (rows.Length != blocks.Length)
                throw new InvalidOperationException("El borrador tiene filas pendientes; revísalo antes de copiar.");
            var assets = (await repository.GetAssetsByIdsAsync(blocks.Where(x => x.AssetId.HasValue)
                .Select(x => x.AssetId!.Value).Distinct().ToArray())).ToDictionary(x => x.Id);
            var sources = (await repository.GetAssetSourcesAsync()).ToDictionary(x => x.Id);
            if (_currentRepository != repository) return;
            var characters = _characters.ToDictionary(x => x.Id, x => x.Name);
            var items = blocks.Select(block =>
            {
                var asset = block.AssetId is Guid id ? assets.GetValueOrDefault(id) : null;
                var source = asset?.SourceId is Guid sourceId ? sources.GetValueOrDefault(sourceId) : null;
                var path = asset is null ? null : source is null ? asset.RelativePath :
                    Path.Combine(source.RootPath, (asset.SourceRelativePath ?? asset.RelativePath)
                        .Replace('/', Path.DirectorySeparatorChar));
                return new CopiedSceneBlock(block,
                    (block.Kind == ScriptBlockKind.CharacterHide ? SceneComposer.HideTarget(blocks, block).CharacterId : block.CharacterId)
                        is Guid characterId ? characters.GetValueOrDefault(characterId) : null,
                    asset?.DisplayName, path, IsImportedVoice(block) ? "grabación importada" :
                        block.GeneratedAudioPath is not null ? "TTS" : null);
            }).ToArray();
            var content = format == SceneCopyFormat.Json
                ? JsonSerializer.Serialize(new CopiedScene(1, "Borrador", null, items,
                    rows.Select(x => x.Instruction).ToArray()),
                    new JsonSerializerOptions { WriteIndented = true })
                : string.Join(Environment.NewLine, items.Select((item, index) =>
                    rows[index].Instruction ?? BuildSimplifiedScene([item], assets, sources, null)));
            Clipboard.SetText(content);
            DraftStatus(session).Text = format == SceneCopyFormat.Json
                ? "Borrador copiado como JSON, con voces, ajustes e instrucciones editables. Puedes pegarlo en Director (prompt) y validarlo sin llamar a la IA."
                : "Borrador copiado como bloque simplificado. Para conservar los WAV, usa JSON completo.";
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private SceneCopyFormat? ChooseSceneCopyFormat(string subject = "escena")
    {
        SceneCopyFormat? chosen = null;
        var dialog = new Window
        {
            Owner = this, Title = "Copiar " + subject, Width = 460, Height = 165,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false
        };
        var layout = new StackPanel { Margin = new Thickness(16) };
        layout.Children.Add(new TextBlock
        {
            Text = "¿En qué formato quieres copiar el " + subject + "?",
            FontWeight = FontWeights.SemiBold
        });
        layout.Children.Add(new TextBlock
        {
            Text = "Bloque: instrucciones legibles. JSON: copia completa, incluidos WAV grabados.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12)
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button AddButton(string label, SceneCopyFormat? selected)
        {
            var button = new Button { Content = label, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(5, 0, 0, 0) };
            button.Click += (_, _) => { chosen = selected; dialog.DialogResult = selected.HasValue; };
            buttons.Children.Add(button);
            return button;
        }
        var blockButton = AddButton("Bloque simplificado", SceneCopyFormat.Block);
        AddButton("JSON completo", SceneCopyFormat.Json);
        AddButton("Cancelar", null);
        layout.Children.Add(buttons);
        dialog.Content = layout;
        dialog.Loaded += (_, _) => blockButton.Focus();
        dialog.ShowDialog();
        return chosen;
    }

    /// <summary>The [CINE] line of a cinema block.</summary>
    private string CinemaLine(SceneScriptBlock block)
    {
        var cinema = BlockParameters.Of(block).Cinema();
        var ms = cinema.MoveMs.ToString(CultureInfo.InvariantCulture);
        if (!cinema.Show) return $"[CINE] quitar | duracion={ms}";
        var layers = cinema.Layers switch
        {
            "personajes" => " | capas=personajes",
            "lista" => " | capas=" + string.Join(",", cinema.Characters.Select(id => _characters.FirstOrDefault(x => x.Id == id)?.Name).OfType<string>()),
            _ => ""
        };
        return $"[CINE] mostrar | estilo={cinema.Style} | duracion={ms}{layers}";
    }

    /// <summary>The [DESENFOQUE] line of a blur block (same syntax the Director reads).</summary>
    private static string BlurLine(SceneScriptBlock block, string? characterName)
    {
        var blur = BlockParameters.Of(block).Blur(block.CharacterId is not null);
        var style = BlurPlan.StyleName(blur.Amount);
        var level = style is "suavizar" or "ligero" or "quitar" ? style
            : "suavizar | valor=" + blur.Amount.ToString("0.####", CultureInfo.InvariantCulture);
        return $"[DESENFOQUE] {(blur.Target == "personaje" ? characterName : blur.Target)} | {level} | duracion={blur.MoveMs}";
    }

    /// <summary>The [GESTO] line of a gesture block (same syntax the Director reads).</summary>
    private static string GestureLine(SceneScriptBlock block, string? characterName)
    {
        var gesture = BlockParameters.Of(block).Gesture();
        if (gesture.Mode == "quitar") return "[GESTO] habla | quitar";
        string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        return $"[GESTO] {(gesture.Mode == "habla" ? "habla" : characterName)} | {GestureSettings.StepsText(gesture.Steps)}" +
            $" | angulo={N(gesture.Angle)} | estirar={N(gesture.StretchPercent)} | lado={gesture.Side}" +
            $" | velocidad={N(gesture.Speed)} | eje={gesture.Pivot}";
    }

    /// <summary>The [CAMARA] line of a camera block (same syntax the Director reads).</summary>
    private static string CameraLine(SceneScriptBlock block, string? characterName)
    {
        var camera = BlockParameters.Of(block).Camera(block.CharacterId is not null);
        var ms = camera.MoveMs.ToString(CultureInfo.InvariantCulture);
        var zoom = camera.Zoom.ToString("0.##", CultureInfo.InvariantCulture);
        return camera.Mode switch
        {
            "general" => $"[CAMARA] general | duracion={ms}",
            "habla" => $"[CAMARA] habla | zoom={zoom} | duracion={ms} | enfoque={camera.Focus}",
            "punto" => $"[CAMARA] punto | zoom={zoom} | duracion={ms} | x={camera.OffsetX} | y={camera.OffsetY}",
            _ => $"[CAMARA] {characterName} | zoom={zoom} | duracion={ms} | enfoque={camera.Focus}" +
                (camera.OffsetX != 0 ? $" | x={camera.OffsetX}" : "") + (camera.OffsetY != 0 ? $" | y={camera.OffsetY}" : "")
        };
    }

    private string BuildSimplifiedScene(IReadOnlyList<CopiedSceneBlock> items,
        IReadOnlyDictionary<Guid, AssetRecord> assets, IReadOnlyDictionary<Guid, AssetSource> sources,
        string? directionNotes)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(directionNotes))
            foreach (var note in directionNotes.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                lines.Add("# Dirección: " + note.Trim());
        if (items.Any(x => x.AudioOrigin == "grabación importada"))
            lines.Add("# Hay voces grabadas: este formato solo copia el texto. Usa JSON para conservar los WAV al aplicar.");
        foreach (var item in items)
        {
            var block = item.Block;
            var resource = "";
            if (block.AssetId is Guid id && assets.TryGetValue(id, out var asset))
            {
                var source = asset.SourceId is Guid sourceId ? sources.GetValueOrDefault(sourceId) : null;
                resource = (source is null ? "" : source.Name + "/") + DirectorAssetLabel(asset);
            }
            var options = new List<string>();
            var command = block.Kind switch
            {
                ScriptBlockKind.Background => "[FONDO] " + resource,
                // A render without character is an NPC (1.4.1): [MOSTRAR] NPC | render … [OCULTAR] NPC | render.
                ScriptBlockKind.CharacterShow => "[MOSTRAR] " + (item.CharacterName ?? NpcCharacterLabel) + " | " + resource,
                ScriptBlockKind.CharacterHide => "[OCULTAR] " + (item.CharacterName ??
                    (resource.Length > 0 ? NpcCharacterLabel + " | " + resource : "")),
                ScriptBlockKind.Image => "[IMAGEN] " + resource,
                ScriptBlockKind.Video => "[VIDEO] " + resource,
                ScriptBlockKind.SoundEffect => "[SFX] " + resource,
                ScriptBlockKind.Music => "[MUSICA] " + resource,
                ScriptBlockKind.Dialogue => (block.PauseAfterMs > 0 ? "[DIALOGO] " : "") +
                    (item.CharacterName ?? NpcCharacterLabel) + ": " + SingleLine(block.Text),
                ScriptBlockKind.Narration => "[NARRACION] " + SingleLine(block.Text),
                ScriptBlockKind.Pause => "[PAUSA] " + block.PauseAfterMs.ToString(CultureInfo.InvariantCulture),
                ScriptBlockKind.Transition => "[TRANSICION] " + SceneComposer.TransitionOptions(block).Style,
                ScriptBlockKind.Camera => CameraLine(block, item.CharacterName),
                ScriptBlockKind.Cinema => CinemaLine(block),
                ScriptBlockKind.Gesture => GestureLine(block, item.CharacterName),
                ScriptBlockKind.Blur => BlurLine(block, item.CharacterName),
                ScriptBlockKind.TextOverlay => "# Texto en pantalla (solo JSON): " + SingleLine(block.Text),
                _ => "# " + SingleLine(block.Text)
            };
            if (block.Kind == ScriptBlockKind.CharacterShow)
            {
                options.Add(SceneComposer.Position(block));
                options.Add(SceneComposer.FramingPreset(block) switch
                {
                    "entero" => "cuerpo entero", "medio" => "medio cuerpo", "detalle" => "primer plano",
                    "auto" => "auto", _ => "original"
                });
            }
            if (SceneComposer.IsVisualBlock(block.Kind))
            {
                if (block.Kind != ScriptBlockKind.CharacterShow && SceneComposer.Position(block) is var position && position != "centro")
                    options.Add(position);
                var visual = SceneComposer.VisualTransform(block);
                var (defaultWidth, defaultHeight) = block.Kind switch
                {
                    ScriptBlockKind.Background => (1280, 720),
                    ScriptBlockKind.CharacterShow => (1280, 610),
                    ScriptBlockKind.Image => (500, 400),
                    _ => (960, 700)
                };
                var zoom = visual.MaxWidth / 1280d;
                if (block.Kind == ScriptBlockKind.Background && visual.MaxWidth > 1280 && Math.Abs(Math.Round(zoom, 3) - zoom) < 1e-9 &&
                    (int)Math.Round(720 * zoom) == visual.MaxHeight && (int)Math.Round(1280 * Math.Round(zoom, 3)) == visual.MaxWidth)
                    options.Add("zoom=" + zoom.ToString("0.###", CultureInfo.InvariantCulture)); // background zoom (1.4.0)
                else
                {
                    if (visual.MaxWidth != defaultWidth) options.Add("ancho=" + visual.MaxWidth.ToString(CultureInfo.InvariantCulture));
                    if (visual.MaxHeight != defaultHeight) options.Add("alto=" + visual.MaxHeight.ToString(CultureInfo.InvariantCulture));
                }
                if (visual.OffsetX != 0) options.Add("x=" + visual.OffsetX.ToString(CultureInfo.InvariantCulture));
                if (visual.OffsetY != 0) options.Add("y=" + visual.OffsetY.ToString(CultureInfo.InvariantCulture));
                if (visual.FlipHorizontal) options.Add("voltear h=si");
                if (visual.FlipVertical) options.Add("voltear v=si");
                if (visual.ChangeDirection) options.Add("cambiar direccion=si");
                if (BlockParameters.Of(block).ScaleFactor(block.Kind) is var scale && scale != 1)
                    options.Add("escala=" + Math.Round(scale * 100).ToString(CultureInfo.InvariantCulture));
                if (visual.RotationDegrees != 0) options.Add("rotacion=" + visual.RotationDegrees.ToString("0.###", CultureInfo.InvariantCulture));
                if (visual.MotionOffsetX != 0) options.Add("animar x=" + visual.MotionOffsetX.ToString(CultureInfo.InvariantCulture));
                if (visual.MotionOffsetY != 0) options.Add("animar y=" + visual.MotionOffsetY.ToString(CultureInfo.InvariantCulture));
                if (visual.MotionRotationDegrees != 0) options.Add("animar giro=" + visual.MotionRotationDegrees.ToString("0.###", CultureInfo.InvariantCulture));
                if (visual.MotionDurationMs != 0) options.Add("animar ms=" + visual.MotionDurationMs.ToString(CultureInfo.InvariantCulture));
                if (SceneComposer.VisualDuration(block) is long duration) options.Add("duracion=" + duration.ToString(CultureInfo.InvariantCulture));
                var mode = SceneComposer.VisualTransitionMode(block);
                if (mode != "heredar")
                {
                    options.Add("transicion=" + mode);
                    if (mode == "plugin") AddPluginOptions(block, options);
                }
            }
            if (block.Kind == ScriptBlockKind.Background && !SceneComposer.AutoTrimBorders(block)) options.Add("bordes=no");
            if (block.Kind == ScriptBlockKind.Video)
            {
                var video = SceneComposer.VideoOptions(block);
                options.Add("capa=" + video.Layer);
                if (!SceneComposer.AutoGreenScreen(block)) options.Add("croma=" + (video.GreenScreen ? "si" : "no"));
                if (video.GreenScreen)
                {
                    options.Add("color=" + video.KeyColor);
                    options.Add("tolerancia=" + video.Tolerance.ToString("0.###", CultureInfo.InvariantCulture));
                }
            }
            if (block.Kind is ScriptBlockKind.SoundEffect or ScriptBlockKind.Music or ScriptBlockKind.Video)
                options.Add("volumen=" + SceneComposer.VolumePercent(block).ToString(CultureInfo.InvariantCulture));
            if (block.Kind == ScriptBlockKind.SoundEffect)
                options.Add("esperar=" + (SceneComposer.WaitForSound(block) ? "si" : "no"));
            if (block.Kind is ScriptBlockKind.Narration or ScriptBlockKind.SoundEffect or ScriptBlockKind.Music)
            {
                if (SceneComposer.AudioDuration(block) is long duration) options.Add("duracion=" + duration.ToString(CultureInfo.InvariantCulture));
                if (SceneComposer.StretchAudio(block)) options.Add("modo audio=tempo");
            }
            if (block.Kind == ScriptBlockKind.Transition)
            {
                var transition = SceneComposer.TransitionOptions(block);
                if (transition.DurationMs > 0) options.Add("duracion=" + transition.DurationMs.ToString(CultureInfo.InvariantCulture));
                if (transition.Style == "cruce")
                {
                    var effect = SceneComposer.TransitionEffect(block);
                    if (effect == "plugin") AddPluginOptions(block, options);
                    else if (effect != "ninguno") options.Add("vegas=" + effect);
                    var targets = SceneComposer.TransitionTargets(block);
                    if (targets != SceneComposer.TargetAll)
                    {
                        var names = new List<string>();
                        if ((targets & SceneComposer.TargetBackground) != 0) names.Add("fondo");
                        if ((targets & SceneComposer.TargetCharacter) != 0) names.Add("personajes");
                        if ((targets & SceneComposer.TargetImage) != 0) names.Add("imagenes");
                        if ((targets & SceneComposer.TargetVideo) != 0) names.Add("videos");
                        options.Add("capas=" + string.Join(",", names));
                    }
                }
            }
            if (block.Kind is not (ScriptBlockKind.Pause or ScriptBlockKind.Comment or ScriptBlockKind.TextOverlay) && block.PauseAfterMs > 0)
                options.Add("pausa=" + block.PauseAfterMs.ToString(CultureInfo.InvariantCulture));
            if (item.AudioOrigin == "grabación importada")
                lines.Add("# Voz grabada: " + (block.GeneratedAudioPath ?? "ruta desconocida"));
            if (DirectVoiceOf(block) is { } directVoice)
                lines.Add("# Voz directa (solo JSON): " + directVoice.Label);
            if (block.Kind is (ScriptBlockKind.Dialogue or ScriptBlockKind.Narration) && string.IsNullOrWhiteSpace(block.Text))
                lines.Add("# Diálogo sin transcripción (solo JSON): " + (block.GeneratedAudioPath ?? "sin texto"));
            else lines.Add(command + (options.Count == 0 ? "" : " | " + string.Join(" | ", options)));
            if (block.VoiceProfileId.HasValue || block.VoicePitchOverride.HasValue ||
                block.VoiceSpeedOverride.HasValue || block.VoiceVolumeOverride.HasValue)
                lines.Add("# Perfil y ajustes de voz del bloque disponibles en JSON.");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static void AddPluginOptions(SceneScriptBlock block, List<string> options)
    {
        var (id, preset) = SceneComposer.VegasPlugin(block);
        if (id.Length > 0) options.Add("vegas=" + id);
        if (preset.Length > 0) options.Add("preset=" + preset);
    }

    private static string SingleLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private async Task PrepareCopiedSceneAsync(SqliteProjectRepository repository, Guid sceneId, string prompt)
    {
        DirectorApplyButton.IsEnabled = false;
        _promptDraft.Clear();
        DirectorDraftGrid.ItemsSource = null;
        DirectorDraftButton.IsEnabled = false;
        try
        {
            var copy = JsonSerializer.Deserialize<CopiedScene>(prompt);
            if (copy is not { Version: 1, Blocks: not null } || copy.Blocks.Length == 0)
                throw new InvalidDataException("El esquema de escena está vacío o usa una versión desconocida.");
            if (copy.Blocks.Length > 1000) throw new InvalidDataException("El esquema tiene demasiados bloques.");
            if (copy.DraftInstructions is not null && copy.DraftInstructions.Length != copy.Blocks.Length)
                throw new InvalidDataException("Las instrucciones del borrador no corresponden a sus bloques.");
            var original = (await repository.GetSceneScriptBlocksAsync(sceneId)).ToArray();
            var ids = copy.Blocks.Where(x => x.Block is { AssetId: not null })
                .Select(x => x.Block.AssetId!.Value).Distinct().ToArray();
            var assets = (await repository.GetAssetsByIdsAsync(ids)).ToDictionary(x => x.Id);
            var characters = _characters.ToDictionary(x => x.Id, x => x.Name);
            var rows = new List<DirectorDraftRow>();
            var draft = new List<SceneScriptBlock>();
            foreach (var item in copy.Blocks)
            {
                var source = item.Block ?? throw new InvalidDataException("El esquema contiene un bloque incompleto.");
                var block = source with { Id = Guid.NewGuid(), SceneId = sceneId, OrderIndex = draft.Count, StartOffsetMs = null };
                draft.Add(block);
                string? error = null;
                if (block.CharacterId is Guid character && !characters.ContainsKey(character)) error = "Personaje inexistente en este proyecto";
                if (block.AssetId is Guid assetId)
                {
                    if (!assets.TryGetValue(assetId, out var asset)) error = "Recurso no catalogado en este proyecto";
                    else if (SceneComposer.IsVisualBlock(block.Kind) && !DirectorAssetCompatible(block.Kind, asset))
                        error = "Formato de recurso incompatible";
                    else if (await ResolveAssetPathAsync(asset) is not string assetPath || !File.Exists(assetPath))
                        error = "Falta el archivo del recurso";
                    else _scriptAssetCache[assetId] = asset;
                }
                if (IsSpeechBlock(block.Kind) && block.GeneratedAudioPath is not null &&
                    (ResolveGeneratedPath(block.GeneratedAudioPath) is not string voice || !IsValidWave(voice)))
                    error = "Falta el WAV de la voz";
                var instruction = copy.DraftInstructions?[rows.Count];
                if (instruction is not null && error is null) error = "Revisa y valida la instrucción antes de aplicar";
                rows.Add(new DirectorDraftRow(rows.Count + 1, DirectorKindName(block.Kind),
                    item.CharacterName ?? item.AssetName ?? "—", block.Text,
                    block.ParametersJson + (item.AudioOrigin is null ? "" : " · " + item.AudioOrigin),
                    error ?? "Listo", error is null) { Instruction = instruction });
            }
            if (_currentRepository != repository || (ScenesList.SelectedItem as SceneScriptRow)?.Scene.Id != sceneId)
                throw new InvalidOperationException("La escena cambió. Prepara el borrador de nuevo.");
            _promptDraft.SceneId = sceneId;
            _promptDraft.Original = original;
            _promptDraft.Draft = draft.ToArray();
            DirectorDraftGrid.ItemsSource = rows;
            var issues = rows.Count(x => !x.IsReady);
            _promptDraft.Validated = issues == 0;
            DirectorApplyButton.IsEnabled = _promptDraft.Validated;
            DirectorStatusText.Text = issues == 0
                ? $"Copia preparada: {rows.Count} bloques. Revisa y aplica; las voces importadas conservan su audio."
                : copy.DraftInstructions is not null
                    ? $"Copia preparada: pulsa Validar edición para revisar {issues} fila(s) antes de aplicar."
                    : $"{issues} bloque(s) con recursos faltantes. Corrige las referencias en el esquema y vuelve a preparar.";
        }
        catch (Exception ex)
        {
            DirectorStatusText.Text = $"No se pudo leer la copia de escena: {ex.Message}";
        }
        finally { DirectorDraftButton.IsEnabled = true; }
    }
}
