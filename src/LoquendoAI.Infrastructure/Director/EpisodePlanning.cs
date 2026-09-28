using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;

namespace LoquendoAI.Infrastructure.Director;

public sealed record EpisodeScenePlan(string Title, string Place, string Summary, IReadOnlyList<string> Characters,
    IReadOnlyList<SceneScriptBlock> Takes);

public sealed record SceneOnScreen(string Character, Guid RenderId, string Position);

/// <summary>What the audience sees and hears when a scene ends: the next scene of the episode
/// is offered the same background, renders and music so it can continue where this one left off.</summary>
public sealed record SceneEndState(string Place, Guid? BackgroundId, IReadOnlyList<SceneOnScreen> OnScreen, Guid? MusicId);

/// <summary>
/// Pure parts of the episode Director (1.0.0-beta.4, moved out of the window to be testable):
/// how recorded takes are split into scenes, what a scene leaves on screen for the next one, and
/// how a reviewed AI draft becomes the blocks of a new scene.
/// </summary>
public static class EpisodePlanning
{
    public const int EpisodeMaxScenes = 12;
    public const int EpisodeMaxTakesPerScene = 40;

    public static SceneEndState EndStateOf(IEnumerable<SceneScriptBlock> blocks, Func<Guid, string?> characterName, string place)
    {
        Guid? background = null, music = null;
        var shown = new List<(Guid CharacterId, SceneOnScreen Render)>();
        var ordered = blocks.OrderBy(x => x.OrderIndex).ToArray();
        foreach (var block in ordered)
            switch (block.Kind)
            {
                case ScriptBlockKind.Background when block.AssetId is Guid id:
                    background = id;
                    break;
                case ScriptBlockKind.Music when block.AssetId is Guid id:
                    music = id;
                    break;
                case ScriptBlockKind.CharacterShow when block.AssetId is Guid id && block.CharacterId is Guid character &&
                                                        characterName(character) is { Length: > 0 } name:
                    shown.RemoveAll(x => x.CharacterId == character);
                    shown.Add((character, new SceneOnScreen(name, id, BlockPosition(block))));
                    break;
                // A hide of an NPC render that has a character by now hides that character (1.4.1).
                case ScriptBlockKind.CharacterHide when SceneComposer.HideTarget(ordered, block).CharacterId is Guid character:
                    shown.RemoveAll(x => x.CharacterId == character);
                    break;
            }
        return new SceneEndState(place, background, shown.Select(x => x.Render).ToArray(), music);
    }

    private static string BlockPosition(SceneScriptBlock block) =>
        BlockParameters.Of(block).Position ?? "";

    /// <summary>Context line for the next scene, using the aliases of ITS catalog.</summary>
    public static string ContinuityLine(SceneEndState state, IReadOnlyDictionary<Guid, string> aliasById)
    {
        var parts = new List<string>();
        if (state.BackgroundId is Guid background && aliasById.TryGetValue(background, out var bg)) parts.Add("fondo " + bg);
        var people = state.OnScreen.Where(x => aliasById.ContainsKey(x.RenderId))
            .Select(x => $"{x.Character} con {aliasById[x.RenderId]}" + (x.Position.Length > 0 ? $" ({x.Position})" : "")).ToArray();
        parts.Add(people.Length > 0 ? "en pantalla " + string.Join(", ", people) : "sin personajes en pantalla");
        if (state.MusicId is Guid music && aliasById.TryGetValue(music, out var track)) parts.Add("música " + track);
        if (parts.Count == 1 && people.Length == 0) return "";
        return "CONTINUIDAD VISUAL: la escena anterior" + (state.Place.Length > 0 ? $" (lugar: {state.Place})" : "") +
            " terminó con " + string.Join("; ", parts) + ". Si esta escena sigue en el mismo lugar y momento, empieza con ese " +
            "fondo, esos renders en las mismas posiciones y, si encaja, la misma música; si cambia de lugar o de momento, " +
            "empieza con otro fondo tras una transición.";
    }

    /// <summary>Ready rows become their blocks; rejected rows become "⚠ Revisar" comments so the
    /// scene is usable and the problem stays visible. Recorded takes the AI forgot are appended.</summary>
    /// <param name="rejected">For draft index i: the instruction and reason when that row was
    /// rejected, or null when it is ready (or has no review row).</param>
    public static SceneScriptBlock[] SceneBlocks(IReadOnlyList<SceneScriptBlock> draft,
        Func<int, (string Instruction, string Status)?> rejected, IReadOnlyList<SceneScriptBlock> original,
        Guid sceneId, out int review)
    {
        var blocks = new List<SceneScriptBlock>();
        review = 0;
        for (var i = 0; i < draft.Count; i++)
        {
            if (rejected(i) is not { } row) { blocks.Add(draft[i]); continue; }
            review++;
            blocks.Add(new SceneScriptBlock(Guid.NewGuid(), sceneId, 0, ScriptBlockKind.Comment,
                Text: $"⚠ Revisar: {row.Instruction} — {row.Status}"));
        }
        var used = blocks.Select(x => x.Id).ToHashSet();
        var forgotten = original.Where(x => !used.Contains(x.Id)).ToArray();
        if (forgotten.Length > 0)
        {
            review++;
            blocks.Add(new SceneScriptBlock(Guid.NewGuid(), sceneId, 0, ScriptBlockKind.Comment,
                Text: $"⚠ Revisar: la IA omitió {forgotten.Length} toma(s); se añadieron a continuación en su orden original."));
            blocks.AddRange(forgotten);
        }
        return blocks.Select((block, index) => block with { SceneId = sceneId, OrderIndex = index }).ToArray();
    }

    /// <summary>Turns scene start positions into contiguous ranges that cover every take once,
    /// then splits any range longer than the per-scene limit into equal parts.</summary>
    public static IReadOnlyList<EpisodeScenePlan> SplitTakes(IReadOnlyList<SceneScriptBlock> takes,
        IReadOnlyDictionary<int, (string Title, string Place, string Summary)> starts, int maxPerScene)
    {
        var points = starts.Keys.Where(x => x > 0 && x < takes.Count).Append(0).Distinct().Order().ToList();
        var result = new List<EpisodeScenePlan>();
        for (var i = 0; i < points.Count; i++)
        {
            var from = points[i];
            var to = i + 1 < points.Count ? points[i + 1] : takes.Count;
            var info = starts.TryGetValue(from, out var found) ? found : (Title: "", Place: "", Summary: "");
            var count = to - from;
            var parts = Math.Max(1, (int)Math.Ceiling(count / (double)maxPerScene));
            for (var p = 0; p < parts; p++)
            {
                var a = from + count * p / parts;
                var b = from + count * (p + 1) / parts;
                var title = info.Title.Length > 0 ? info.Title : $"Parte {result.Count + 1}";
                if (parts > 1) title += $" ({p + 1}/{parts})";
                result.Add(new EpisodeScenePlan(title, info.Place, info.Summary, [], takes.Skip(a).Take(b - a).ToArray()));
            }
        }
        // More scenes than allowed: merge the smallest neighbours while every scene stays within the limit.
        while (result.Count > EpisodeMaxScenes)
        {
            var best = -1;
            for (var i = 0; i + 1 < result.Count; i++)
                if (result[i].Takes.Count + result[i + 1].Takes.Count <= maxPerScene &&
                    (best < 0 || result[i].Takes.Count + result[i + 1].Takes.Count < result[best].Takes.Count + result[best + 1].Takes.Count))
                    best = i;
            if (best < 0) break; // Respect the per-scene limit over the scene count.
            result[best] = result[best] with { Takes = result[best].Takes.Concat(result[best + 1].Takes).ToArray() };
            result.RemoveAt(best + 1);
        }
        return result;
    }
}
