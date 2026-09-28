using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Director;

namespace LoquendoAI.Tests;

/// <summary>Episode Director: how recorded takes are split into scenes and how scenes continue.</summary>
internal static class EpisodePlanningTests
{
    private static SceneScriptBlock[] Takes(int count) => Enumerable.Range(0, count)
        .Select(i => new SceneScriptBlock(Guid.NewGuid(), Guid.Empty, i, ScriptBlockKind.Dialogue, Text: $"t{i}")).ToArray();

    private static void CoversEveryTakeOnce(IReadOnlyList<EpisodeScenePlan> plan, SceneScriptBlock[] takes, int maxPerScene)
    {
        Assert.Sequence(takes.Select(x => x.Id), plan.SelectMany(x => x.Takes).Select(x => x.Id), "cada toma una vez y en orden");
        Assert.True(plan.All(x => x.Takes.Count <= maxPerScene), "ninguna escena supera el límite: " +
            string.Join(",", plan.Select(x => x.Takes.Count)));
    }

    private static Dictionary<int, (string Title, string Place, string Summary)> Starts(params (int At, string Title)[] starts) =>
        starts.ToDictionary(x => x.At, x => (x.Title, "", ""));

    [Test("Reparto de tomas sin plan de la IA: partes iguales de hasta 40")]
    public static void EvenSplit()
    {
        var takes = Takes(100);
        var plan = EpisodePlanning.SplitTakes(takes, Starts(), 40);
        CoversEveryTakeOnce(plan, takes, 40);
        Assert.Sequence([33, 33, 34], plan.Select(x => x.Takes.Count), "tamaños");
    }

    [Test("Reparto de tomas: respeta los cortes de la IA y parte las escenas largas")]
    public static void AiStarts()
    {
        var takes = Takes(100);
        var plan = EpisodePlanning.SplitTakes(takes, Starts((0, "Intro"), (10, "Cocina"), (55, "Calle")), 40);
        CoversEveryTakeOnce(plan, takes, 40);
        Assert.Sequence(["Intro", "Cocina (1/2)", "Cocina (2/2)", "Calle (1/2)", "Calle (2/2)"], plan.Select(x => x.Title), "títulos");
        var missingFirst = EpisodePlanning.SplitTakes(Takes(50), Starts((30, "B")), 40);
        Assert.Equal(30, missingFirst[0].Takes.Count, "se añade el inicio que faltaba");
        Assert.Equal("B", missingFirst[1].Title, "segunda escena");
    }

    [Test("Reparto de tomas: demasiadas escenas se fusionan sin pasar el límite por escena")]
    public static void MergeScenes()
    {
        var takes = Takes(60);
        var plan = EpisodePlanning.SplitTakes(takes, Enumerable.Range(0, 30).ToDictionary(i => i * 2, i => ($"s{i}", "", "")), 40);
        CoversEveryTakeOnce(plan, takes, 40);
        Assert.True(plan.Count <= EpisodePlanning.EpisodeMaxScenes, $"{plan.Count} escenas");
        var huge = Takes(500);
        var many = EpisodePlanning.SplitTakes(huge, Starts(), 40);
        CoversEveryTakeOnce(many, huge, 40); // the per-scene limit wins over the scene count
    }

    [Test("Borrador revisado: filas rechazadas → «⚠ Revisar», tomas olvidadas al final")]
    public static void SceneBlocks()
    {
        var sceneId = Guid.NewGuid();
        var original = Takes(4).Select(x => x with { SceneId = sceneId }).ToArray();
        var background = new SceneScriptBlock(Guid.NewGuid(), sceneId, 0, ScriptBlockKind.Background);
        var badSfx = new SceneScriptBlock(Guid.NewGuid(), sceneId, 0, ScriptBlockKind.SoundEffect);
        SceneScriptBlock[] draft = [background, original[1], badSfx, original[0], original[3]]; // the AI forgot original[2]
        var blocks = EpisodePlanning.SceneBlocks(draft,
            i => i == 2 ? ("[SFX] A7 | volumen=60", "Parece música, no un efecto") : null, original, sceneId, out var review);
        Assert.Equal(ScriptBlockKind.Comment, blocks[2].Kind, "fila rechazada");
        Assert.Contains("[SFX] A7", blocks[2].Text);
        Assert.Contains("Parece música", blocks[2].Text);
        Assert.Equal(2, review, "avisos");
        Assert.True(original.All(o => blocks.Count(x => x.Id == o.Id) == 1), "todas las tomas una vez");
        Assert.Equal(original[2].Id, blocks[^1].Id, "la toma olvidada va al final");
        Assert.Sequence(Enumerable.Range(0, blocks.Length), blocks.Select(x => x.OrderIndex), "orden");
        Assert.True(blocks.All(x => x.SceneId == sceneId), "escena");
    }

    [Test("Continuidad: la escena siguiente recibe fondo, renders y música con los que terminó la anterior")]
    public static void Continuity()
    {
        Guid bart = Guid.NewGuid(), flutter = Guid.NewGuid(), bg1 = Guid.NewGuid(), bg2 = Guid.NewGuid();
        Guid r1 = Guid.NewGuid(), r2 = Guid.NewGuid(), r3 = Guid.NewGuid(), music = Guid.NewGuid();
        var names = new Dictionary<Guid, string> { [bart] = "Bart", [flutter] = "Fluttershy" };
        var scene = Guid.NewGuid();
        var order = 0;
        SceneScriptBlock Block(ScriptBlockKind kind, Guid? asset = null, Guid? character = null, string position = "") =>
            new(Guid.NewGuid(), scene, order++, kind, character, "", asset,
                ParametersJson: position.Length > 0 ? $$"""{"position":"{{position}}"}""" : "{}");
        SceneScriptBlock[] blocks =
        [
            Block(ScriptBlockKind.Background, bg1), Block(ScriptBlockKind.Music, music),
            Block(ScriptBlockKind.CharacterShow, r1, bart, "izquierda"), Block(ScriptBlockKind.CharacterShow, r2, flutter, "derecha"),
            Block(ScriptBlockKind.Dialogue, character: bart), Block(ScriptBlockKind.Transition), Block(ScriptBlockKind.Background, bg2),
            Block(ScriptBlockKind.CharacterShow, r3, bart, "centro"), Block(ScriptBlockKind.CharacterHide, character: flutter)
        ];
        var state = EpisodePlanning.EndStateOf(blocks.Reverse(), id => names.GetValueOrDefault(id), "parque");
        Assert.Equal(bg2, state.BackgroundId, "último fondo");
        Assert.Equal(music, state.MusicId, "música");
        Assert.Sequence([new SceneOnScreen("Bart", r3, "centro")], state.OnScreen, "en pantalla");
        var line = EpisodePlanning.ContinuityLine(state, new Dictionary<Guid, string> { [bg2] = "A1", [r3] = "A2", [music] = "A3" });
        foreach (var part in new[] { "fondo A1", "Bart con A2 (centro)", "música A3", "parque" }) Assert.Contains(part, line);
        Assert.Equal("", EpisodePlanning.ContinuityLine(state, new Dictionary<Guid, string>()), "sin referencias no hay línea");
        var broken = new SceneScriptBlock(Guid.NewGuid(), scene, 0, ScriptBlockKind.CharacterShow, bart, "", r1, ParametersJson: "{roto");
        Assert.Equal("", EpisodePlanning.EndStateOf([broken], id => names.GetValueOrDefault(id), "").OnScreen[0].Position, "JSON dañado");
    }
}
