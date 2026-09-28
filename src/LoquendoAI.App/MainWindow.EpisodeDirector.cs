using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Persistence;

namespace LoquendoAI.App;

/// <summary>
/// Episode director: splits a long premise (or a long list of recorded takes) into scenes and
/// runs the single-scene AI Director on each one. Every scene is written as a NEW scene at the
/// end of the episode as soon as its draft is ready, so nothing existing is overwritten and a
/// cancelled run keeps the scenes already finished. Rows the validator rejects are kept as
/// "⚠ Revisar" comments with their instruction and reason.
/// </summary>
public partial class MainWindow
{
    private async void AiGenerateEpisode_Click(object sender, RoutedEventArgs e)
    {
        if (_aiRequestCancellation is not null || !AiGenerateButton.IsEnabled) return;
        if (_currentRepository is not { } repository || EpisodesList.SelectedItem is not EpisodeScriptRow episodeRow)
        {
            AiDirectorStatusText.Text = "Abre un proyecto y selecciona el episodio.";
            return;
        }
        var prompt = AiDirectorPromptBox.Text.Trim();
        var model = (AiDirectorModelBox.SelectedItem as AiModelChoice)?.Model ?? "";
        var provider = (AiDirectorModelBox.SelectedItem as AiModelChoice)?.Provider ?? "ollama";
        var recorded = AiDirectorModeCombo.SelectedIndex == 1;
        if (!recorded && prompt.Length is < 8 or > 12000 || recorded && prompt.Length > 12000 || model.Length == 0)
        {
            AiDirectorStatusText.Text = recorded
                ? "Selecciona un modelo. La premisa es opcional en voces grabadas (máximo 12 000 caracteres)."
                : "Escribe la premisa del episodio (8 a 12 000 caracteres) y selecciona un modelo.";
            return;
        }
        var sourceScene = ScenesList.SelectedItem as SceneScriptRow;
        using var cancellation = new CancellationTokenSource();
        _aiRequestCancellation = cancellation;
        AiGenerateButton.IsEnabled = AiEpisodeButton.IsEnabled = DirectorDraftButton.IsEnabled = false;
        AiCancelButton.IsEnabled = AssetCancelAnalysisButton.IsEnabled = true;
        var progress = new Progress<string>(text =>
        {
            if (ReferenceEquals(_aiRequestCancellation, cancellation)) AiDirectorStatusText.Text = text;
        });
        var created = new List<(Scene Scene, int Blocks, int Review)>();
        try
        {
            IReadOnlyList<SceneScriptBlock> takes = [];
            if (recorded)
            {
                if (sourceScene is null)
                    throw new InvalidOperationException("Selecciona la escena donde incorporaste todas las voces grabadas del episodio.");
                takes = (await repository.GetSceneScriptBlocksAsync(sourceScene.Scene.Id)).Where(IsRecordedTake).ToArray();
                if (takes.Count == 0)
                    throw new InvalidOperationException("Esa escena no tiene voces incorporadas. Impórtalas en Voces grabadas.");
            }
            else if (_characters.Count == 0)
                throw new InvalidOperationException("Crea al menos un personaje en Voice Lab (sin perfil propio habla con la voz NPC).");

            var plan = recorded
                ? await PlanRecordedEpisodeAsync(takes, prompt, provider, model, progress, cancellation.Token)
                : await PlanStoryEpisodeAsync(prompt, provider, model, progress, cancellation.Token);
            if (plan.Count == 0) throw new InvalidDataException("La IA no propuso ninguna escena.");

            var outline = string.Join("\n", plan.Select((scene, i) =>
                $"{i + 1}. {scene.Title}" + (scene.Place.Length > 0 ? $" ({scene.Place})" : "") +
                (recorded ? $" · {scene.Takes.Count} tomas" : "")));
            if (MessageBox.Show(this,
                    $"La IA propone {plan.Count} escena(s):\n\n{outline}\n\nSe crearán como escenas NUEVAS al final de «{episodeRow.Episode.Title}». " +
                    "Las existentes no se modifican. ¿Generar sus borradores?",
                    "Director IA · episodio", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                AiDirectorStatusText.Text = "Episodio cancelado antes de crear escenas.";
                return;
            }

            var existingScenes = await repository.GetScenesAsync(episodeRow.Episode.Id);
            var nextIndex = existingScenes.Count == 0 ? 1 : existingScenes.Max(x => x.Index) + 1;
            var continuity = "";
            SceneEndState? endState = null;
            var premise = prompt.Length > 4000 ? prompt[..4000] + "…" : prompt;
            for (var i = 0; i < plan.Count; i++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var part = plan[i];
                var sceneId = Guid.NewGuid();
                var scenePrompt = new StringBuilder();
                if (premise.Length > 0) scenePrompt.AppendLine("EPISODIO (contexto general): " + premise);
                scenePrompt.AppendLine("ESCENAS DEL EPISODIO:").AppendLine(outline);
                scenePrompt.AppendLine($"DIRIGE AHORA SOLO LA ESCENA {i + 1}/{plan.Count}: «{part.Title}»." +
                    (part.Place.Length > 0 ? $" Lugar: {part.Place}." : "") +
                    (part.Characters.Count > 0 ? $" Personajes: {string.Join(", ", part.Characters)}." : ""));
                if (part.Summary.Length > 0) scenePrompt.AppendLine(part.Summary);
                if (continuity.Length > 0) scenePrompt.AppendLine("CONTINUIDAD: la escena anterior terminó así: " + continuity);
                if (endState is not null && endState.Place.Length > 0 && part.Place.Length > 0)
                    scenePrompt.AppendLine(SameDirectorName(endState.Place, part.Place)
                        ? "Esta escena ocurre en el MISMO lugar que la anterior."
                        : $"Esta escena cambia de lugar: de «{endState.Place}» a «{part.Place}».");

                // Recorded takes are copied with new IDs: the staging scene keeps its blocks.
                var original = part.Takes.Select((take, index) => take with
                {
                    Id = Guid.NewGuid(), SceneId = sceneId, OrderIndex = index, StartOffsetMs = null
                }).ToArray();
                var label = $"Escena {i + 1}/{plan.Count} «{part.Title}»";
                var sceneProgress = new Progress<string>(text => ((IProgress<string>)progress).Report(label + " · " + text));
                AiSceneDraftResult result;
                try
                {
                    result = await BuildAiSceneDraftAsync(repository, sceneId, original, scenePrompt.ToString(), recorded,
                        provider, model, sceneProgress, cancellation.Token, recorded ? "episodio-voces" : "episodio-historia", endState);
                }
                catch (Exception ex) when (ex is not OperationCanceledException && recorded)
                {
                    // Takes are never lost: an AI failure still yields the scene with its WAVs in order.
                    result = new AiSceneDraftResult(original, original.Select((block, index) => new DirectorDraftRow(index + 1,
                        DirectorKindName(block.Kind), "", block.Text, "", "Bloque original", true)).ToList());
                    AiDiagnostics.Note($"episodio: escena {i + 1} sin montaje IA ({ex.Message}); se conservaron las tomas");
                }
                var blocks = EpisodeSceneBlocks(result, original, sceneId, out var review);
                var scene = new Scene(sceneId, episodeRow.Episode.Id, nextIndex++, TrimTitle($"{i + 1:00} · {part.Title}"), 0,
                    DirectionNotes: string.Join("\n", new[] { part.Place.Length > 0 ? "Lugar: " + part.Place : "", part.Summary }
                        .Where(x => x.Length > 0)));
                if (_currentRepository != repository) throw new OperationCanceledException();
                await repository.UpsertSceneAsync(scene);
                await repository.ReplaceSceneScriptBlocksAsync(sceneId, blocks);
                created.Add((scene, blocks.Length, review));
                endState = EndStateOf(blocks, id => _characters.FirstOrDefault(c => c.Id == id)?.Name, part.Place);
                continuity = (part.Summary.Length > 0 ? part.Summary + " " : "") + string.Join(" / ", blocks
                    .Where(b => b.Kind is ScriptBlockKind.Dialogue or ScriptBlockKind.Narration && b.Text.Length > 0)
                    .TakeLast(3).Select(b => (_characters.FirstOrDefault(c => c.Id == b.CharacterId)?.Name ?? "Narrador") + ": " + b.Text));
                if (continuity.Length > 700) continuity = continuity[^700..];
            }
            await RefreshScriptAsync(episodeRow.Episode.Id, created.FirstOrDefault().Scene?.Id);
            var toReview = created.Where(x => x.Review > 0).ToArray();
            AiDirectorStatusText.Text = $"{created.Count} escena(s) creadas con {created.Sum(x => x.Blocks)} bloques. " +
                (toReview.Length == 0 ? "Todas quedaron listas."
                    : $"{toReview.Sum(x => x.Review)} fila(s) marcadas «⚠ Revisar» en: {string.Join(", ", toReview.Select(x => x.Scene.Title))}.") +
                (recorded ? " La escena original con las tomas no se modificó; bórrala cuando ya no la necesites."
                          : " Genera las voces al previsualizar o al exportar el episodio.");
        }
        catch (OperationCanceledException)
        {
            if (created.Count > 0 && _currentRepository == repository)
                await RefreshScriptAsync(episodeRow.Episode.Id, created[0].Scene.Id);
            AiDirectorStatusText.Text = created.Count == 0
                ? "Episodio cancelado; no se creó ninguna escena."
                : $"Episodio cancelado; se conservaron las {created.Count} escena(s) ya creadas.";
        }
        catch (Exception ex)
        {
            ErrorLog.Record(ex);
            AiDirectorStatusText.Text = ex.Message + (created.Count > 0 ? $" Se conservaron {created.Count} escena(s) ya creadas." : "");
            if (created.Count > 0 && _currentRepository == repository)
                await RefreshScriptAsync(episodeRow.Episode.Id, created[0].Scene.Id);
        }
        finally
        {
            _aiRequestCancellation = null;
            AiGenerateButton.IsEnabled = AiEpisodeButton.IsEnabled = DirectorDraftButton.IsEnabled = true;
            AiCancelButton.IsEnabled = AssetCancelAnalysisButton.IsEnabled = false;
        }
    }

    private static string TrimTitle(string title) => title.Length <= 80 ? title : title[..80];

    /// <summary>Ready rows become their blocks; rejected rows become "⚠ Revisar" comments
    /// (see EpisodePlanning.SceneBlocks).</summary>
    private static SceneScriptBlock[] EpisodeSceneBlocks(AiSceneDraftResult result, SceneScriptBlock[] original,
        Guid sceneId, out int review) =>
        SceneBlocks(result.Draft, i => i < result.Rows.Count && result.Rows[i] is { IsReady: false } row
            ? (row.Instruction ?? row.Text, row.Status) : null, original, sceneId, out review);

    private async Task<IReadOnlyList<EpisodeScenePlan>> PlanStoryEpisodeAsync(string prompt, string provider, string model,
        IProgress<string> progress, CancellationToken token)
    {
        var names = _characters.Select(x => x.Name).ToArray();
        var scene = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["titulo"] = new Dictionary<string, object> { ["type"] = "string" },
                ["lugar"] = new Dictionary<string, object> { ["type"] = "string" },
                ["personajes"] = new Dictionary<string, object>
                {
                    ["type"] = "array",
                    ["items"] = names.Length > 0
                        ? new Dictionary<string, object> { ["type"] = "string", ["enum"] = names }
                        : new Dictionary<string, object> { ["type"] = "string" }
                },
                ["resumen"] = new Dictionary<string, object> { ["type"] = "string" }
            },
            ["required"] = new[] { "titulo", "lugar", "personajes", "resumen" },
            ["additionalProperties"] = false
        };
        var schema = EpisodeSchema(scene);
        var system = $"""
            Eres el guionista jefe de Loquendo AI. Divide el encargo en escenas para un episodio corto.
            Responde SOLO con el JSON del esquema. Entre 1 y {EpisodeMaxScenes} escenas, en orden.
            Cada escena debe caber en unas 15–30 acciones (1–3 minutos): córtala donde cambie el lugar,
            el momento o la situación. titulo: 2–6 palabras. lugar: dónde ocurre. personajes: quién aparece.
            resumen: 2–4 frases con lo que pasa, el tono y cómo termina la escena (para enlazar con la siguiente).
            """;
        var context = "ENCARGO DEL EPISODIO:\n" + prompt + "\n\nPERSONAJES REGISTRADOS: " +
            string.Join(", ", _characters.Select(x => x.Name + (x.Notes is { Length: > 0 } notes ? $" ({ShortAiContext(notes, 80)})" : "")));
        progress.Report("Planificando las escenas del episodio…");
        var output = await GenerateAiResponseAsync(provider, model, system, context, schema, null, token, progress,
            new OllamaChatOptions(3072, "episodio-plan"));
        return ParseEpisodePlan(output, _ => []);
    }

    private async Task<IReadOnlyList<EpisodeScenePlan>> PlanRecordedEpisodeAsync(IReadOnlyList<SceneScriptBlock> takes,
        string prompt, string provider, string model, IProgress<string> progress, CancellationToken token)
    {
        var refs = takes.Select((_, i) => $"B{i + 1}").ToArray();
        IReadOnlyList<EpisodeScenePlan> Fallback() => SplitTakes(takes, new Dictionary<int, (string Title, string Place, string Summary)>(), EpisodeMaxTakesPerScene);
        if (takes.Count <= 12) return Fallback();
        var scene = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["primer_bloque"] = new Dictionary<string, object> { ["type"] = "string", ["enum"] = refs },
                ["titulo"] = new Dictionary<string, object> { ["type"] = "string" },
                ["lugar"] = new Dictionary<string, object> { ["type"] = "string" },
                ["resumen"] = new Dictionary<string, object> { ["type"] = "string" }
            },
            ["required"] = new[] { "primer_bloque", "titulo", "lugar", "resumen" },
            ["additionalProperties"] = false
        };
        var system = $"""
            Eres el editor de Loquendo AI. Recibes las tomas de voz grabadas de un episodio, en orden.
            Divídelas en escenas consecutivas: cada escena empieza en su primer_bloque y termina justo antes de la siguiente.
            La primera escena empieza en B1. Corta donde cambie el lugar, el momento o la situación; cada escena
            debe tener entre 5 y {EpisodeMaxTakesPerScene} tomas y como mucho {EpisodeMaxScenes} escenas en total.
            titulo: 2–6 palabras. lugar: dónde ocurre. resumen: 1–3 frases con lo que pasa y el tono.
            Responde SOLO con el JSON del esquema.
            """;
        var list = new StringBuilder();
        if (prompt.Length > 0) list.AppendLine("INDICACIONES DEL USUARIO:\n" + prompt + "\n");
        list.AppendLine("TOMAS:");
        for (var i = 0; i < takes.Count; i++)
            list.AppendLine($"B{i + 1} [{_characters.FirstOrDefault(x => x.Id == takes[i].CharacterId)?.Name ?? "Narrador"}] " +
                ShortAiContext(takes[i].Text, 160));
        progress.Report($"Dividiendo {takes.Count} tomas en escenas…");
        try
        {
            var output = await GenerateAiResponseAsync(provider, model, system, list.ToString(), EpisodeSchema(scene),
                null, token, progress, new OllamaChatOptions(3072, "episodio-plan-voces"));
            var starts = new SortedDictionary<int, (string Title, string Place, string Summary)>();
            using (var json = JsonDocument.Parse(output))
                foreach (var item in json.RootElement.GetProperty("escenas").EnumerateArray())
                {
                    var first = item.GetProperty("primer_bloque").GetString() ?? "";
                    if (first.Length > 1 && int.TryParse(first[1..], out var number) && number >= 1 && number <= takes.Count)
                        starts.TryAdd(number - 1, (Text(item, "titulo"), Text(item, "lugar"), Text(item, "resumen")));
                }
            return SplitTakes(takes, starts, EpisodeMaxTakesPerScene);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A plan is a convenience: equal parts are still a usable split.
            AiDiagnostics.Note($"episodio: plan de voces no válido ({ex.Message}); se usan partes iguales");
            return Fallback();
        }
    }

    private static object EpisodeSchema(Dictionary<string, object> scene) => new Dictionary<string, object>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object>
        {
            ["escenas"] = new Dictionary<string, object>
            {
                ["type"] = "array", ["items"] = scene, ["minItems"] = 1, ["maxItems"] = EpisodeMaxScenes
            }
        },
        ["required"] = new[] { "escenas" },
        ["additionalProperties"] = false
    };

    private static string Text(JsonElement item, string key) =>
        item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? (value.GetString() ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim() : "";

    private IReadOnlyList<EpisodeScenePlan> ParseEpisodePlan(string output, Func<int, IReadOnlyList<SceneScriptBlock>> takes)
    {
        using var json = JsonDocument.Parse(output);
        var result = new List<EpisodeScenePlan>();
        foreach (var item in json.RootElement.GetProperty("escenas").EnumerateArray().Take(EpisodeMaxScenes))
        {
            var characters = item.TryGetProperty("personajes", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).Distinct().ToArray() : [];
            var title = Text(item, "titulo");
            result.Add(new EpisodeScenePlan(title.Length == 0 ? $"Escena {result.Count + 1}" : title,
                Text(item, "lugar"), Text(item, "resumen"), characters, takes(result.Count)));
        }
        return result;
    }
}
