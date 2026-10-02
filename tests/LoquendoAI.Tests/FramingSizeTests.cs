using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;
using LoquendoAI.Infrastructure.Director;

namespace LoquendoAI.Tests;

/// <summary>
/// Sizes of the characters (1.4.7): the lanes of the automatic framing count characters, not clips (a cross-fade between
/// two renders of the same character is still one); «Revisar encuadre» measures against the frame and against whoever is
/// on screen at the same time, and also sees renders with nearly transparent dirt or without transparency.
/// </summary>
internal static class FramingSizeTests
{
    /// <summary>A scene written as Director lines: «[MOSTRAR] Bart | archivo» uses media/archivo.png.</summary>
    private sealed class Scene
    {
        public readonly List<SceneScriptBlock> Blocks;
        private readonly Dictionary<string, Guid> _characters;
        private readonly Dictionary<Guid, string> _paths;

        public Scene(TempFolder folder, string script)
        {
            Blocks = Build(folder, script, out _characters, out _paths).ToList();
        }

        private static IEnumerable<SceneScriptBlock> Build(TempFolder folder, string script, out Dictionary<string, Guid> characters,
            out Dictionary<Guid, string> assets)
        {
            var ids = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var files = new Dictionary<Guid, string>();
            var byName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var blocks = new List<SceneScriptBlock>();
            foreach (var spec in DirectorScript.ParseDirectorPrompt(script))
            {
                if (spec.Error is { } error) throw new InvalidOperationException($"{spec.SourceLine}: {error}");
                Guid? character = spec.CharacterName.Length > 0 && spec.Kind is ScriptBlockKind.CharacterShow or ScriptBlockKind.CharacterHide
                    or ScriptBlockKind.Dialogue
                    ? ids.TryGetValue(spec.CharacterName, out var known) ? known : ids[spec.CharacterName] = Guid.NewGuid()
                    : null;
                Guid? asset = null;
                if (spec.ResourceQuery.Length > 0)
                {
                    if (!byName.TryGetValue(spec.ResourceQuery, out var id))
                    {
                        byName[spec.ResourceQuery] = id = Guid.NewGuid();
                        files[id] = folder.File($"media/{spec.ResourceQuery}.png");
                    }
                    asset = id;
                }
                blocks.Add(new SceneScriptBlock(Guid.NewGuid(), Guid.Empty, blocks.Count, spec.Kind, character, spec.Text, asset,
                    PauseAfterMs: spec.PauseMs, ParametersJson: DirectorScript.DirectorParameters(spec, name => ids.GetValueOrDefault(name))));
            }
            characters = ids;
            assets = files;
            return blocks;
        }

        public string Name(SceneScriptBlock block) =>
            _characters.FirstOrDefault(x => x.Value == block.CharacterId).Key ?? "Prop";

        public Task<SceneComposition> PlanAsync() =>
            SceneComposer.PlanAsync(Blocks, b => b.AssetId is Guid id ? _paths.GetValueOrDefault(id) : null);

        public async Task<IReadOnlyList<FramingIssue>> AuditAsync() =>
            await FramingAudit.AuditAsync(Blocks, await PlanAsync(), Name);

        /// <summary>The size each render gets from the automatic framing (the box it is fitted into).</summary>
        public async Task<Dictionary<Guid, (int Width, int Height)>> BoxesAsync()
        {
            var prepared = await CharacterFraming.ApplyAsync(await PlanAsync());
            return prepared.Media.Where(x => x.Kind == ScriptBlockKind.CharacterShow)
                .ToDictionary(x => x.BlockId, x => (x.VisualMaxWidth, x.VisualMaxHeight));
        }

        public SceneScriptBlock Show(int index) => Blocks.Where(x => x.Kind == ScriptBlockKind.CharacterShow).ElementAt(index);

        public void Fix(Guid blockId, BlockParameters changes)
        {
            var index = Blocks.FindIndex(x => x.Id == blockId);
            Blocks[index] = Blocks[index] with { ParametersJson = FramingAudit.Apply(BlockParameters.Of(Blocks[index]), changes).ToJson() };
        }
    }

    /// <summary>A character on a transparent canvas: an opaque body of <paramref name="width"/>×<paramref name="height"/>,
    /// with <paramref name="padding"/> px of empty canvas around it.</summary>
    private static string Character(TempFolder folder, string name, int width, int height, int padding = 2,
        Func<int, int, (byte, byte, byte, byte)?>? dirt = null) =>
        TestMedia.Png(folder.File($"media/{name}.png"), width + padding * 2, height + padding * 2, (x, y) =>
            x >= padding && x < padding + width && y >= padding && y < padding + height ? ((byte)220, (byte)40, (byte)40, (byte)255)
                : dirt?.Invoke(x, y) ?? ((byte)0, (byte)0, (byte)0, (byte)0));

    [Test("Encuadre: un cruce entre renders del mismo personaje no cuenta como otro personaje (carriles de 2, no de 4)")]
    public static async Task CrossFadeKeepsLanes()
    {
        TestMedia.RequireFfmpeg();
        using var folder = new TempFolder();
        TestMedia.SolidPng(folder.File("media/casa.png"), 1280, 720, (60, 60, 60));
        TestMedia.SolidPng(folder.File("media/sala.png"), 1280, 720, (40, 40, 80));
        // Bart sitting (620×709) and a wide pony (1456×1214), like the user's scene.
        Character(folder, "bart_sentado", 620, 709);
        Character(folder, "pony", 1456, 1214);
        Character(folder, "bart_parado", 400, 800);
        Character(folder, "pony_feliz", 1400, 1200);
        var scene = new Scene(folder, """
            [FONDO] casa
            [MOSTRAR] Bart | bart_sentado | izquierda | auto
            [MOSTRAR] Fluttershy | pony | derecha | auto
            [PAUSA] 1500
            [TRANSICION] cruce | duracion=1000 | capas=personajes
            [FONDO] sala
            [MOSTRAR] Bart | bart_parado | izquierda | auto
            [MOSTRAR] Fluttershy | pony_feliz | derecha | auto
            [PAUSA] 1500
            """);
        var boxes = await scene.BoxesAsync();
        foreach (var (id, box) in boxes)
            Assert.True(box.Width >= 600, $"dos personajes: carril de ~608 px, no de 288 ({box.Width}×{box.Height})");
        // Bart sitting 490×560 and the pony 608×507: well framed, nothing to report.
        var issues = await scene.AuditAsync();
        Assert.True(!issues.Any(x => x.Problem == FramingProblem.Small), "bien encuadrados: " + string.Join("; ", issues.Select(x => x.Message)));
    }

    [Test("Encuadre: con 3 y 4 personajes a la vez los carriles se reparten (nunca más angostos que un tercio); un pony ancho bajito se avisa y «Escala» lo saca del carril")]
    public static async Task ThreeAndFour()
    {
        TestMedia.RequireFfmpeg();
        using var folder = new TempFolder();
        TestMedia.SolidPng(folder.File("media/casa.png"), 1280, 720, (60, 60, 60));
        Character(folder, "alto", 400, 800);
        Character(folder, "pony", 1456, 1214);
        var three = new Scene(folder, """
            [FONDO] casa
            [MOSTRAR] Bart | alto | auto
            [MOSTRAR] Lisa | alto | auto
            [MOSTRAR] Homero | alto | auto
            [PAUSA] 1000
            [TRANSICION] cruce | duracion=800 | capas=personajes
            [MOSTRAR] Lisa | alto | auto
            [PAUSA] 1000
            """);
        Assert.True((await three.BoxesAsync()).Values.All(x => x.Width == 1280 / 3 - 32), "tres personajes: tres carriles, aunque Lisa cambie de render con cruce");

        var four = new Scene(folder, """
            [FONDO] casa
            [MOSTRAR] Bart | alto | auto
            [MOSTRAR] Lisa | alto | auto
            [MOSTRAR] Homero | alto | auto
            [MOSTRAR] Fluttershy | pony | auto
            [PAUSA] 1500
            """);
        Assert.True((await four.BoxesAsync()).Values.All(x => x.Width == 1280 / 3 - 32), "cuatro personajes: el ancho no baja de un tercio (394, no 288)");
        var issues = await four.AuditAsync();
        var pony = issues.Single(x => x.Problem == FramingProblem.Small);
        Assert.Contains("Fluttershy", pony.Message, "el pony ancho queda bajito en su carril (los altos no)");
        Assert.Contains("carril (4 personajes a la vez)", pony.Message, "dice por qué");
        four.Fix(pony.BlockId, pony.Fixes[0].Changes);
        Assert.True(BlockParameters.Of(four.Blocks.First(x => x.Id == pony.BlockId)).Scale > 1.5, "la corrección es su «Escala»");
        var box = (await four.BoxesAsync())[pony.BlockId];
        Assert.True(box.Width > 394, $"con «Escala» puede pasar de su carril ({box.Width})");
        Assert.True(!(await four.AuditAsync()).Any(x => x.Problem == FramingProblem.Small), "corregido");
    }

    [Test("Encuadre: 3 al frente y 2 detrás no quedan diminutos; los de atrás no se comparan con los del frente, pero si todo queda pequeño se avisa")]
    public static async Task FrontAndBackRows()
    {
        TestMedia.RequireFfmpeg();
        using var folder = new TempFolder();
        TestMedia.SolidPng(folder.File("media/casa.png"), 1280, 720, (60, 60, 60));
        Character(folder, "alto", 400, 800);
        Character(folder, "pony", 1456, 1214);
        Character(folder, "sentado", 620, 709);
        // The back row first (drawn under), moved towards the middle and a little up.
        var humans = new Scene(folder, """
            [FONDO] casa
            [MOSTRAR] Homero | alto | izquierda | auto | x=230 | y=-70
            [MOSTRAR] Marge | alto | derecha | auto | x=-230 | y=-70
            [MOSTRAR] Bart | alto | izquierda | auto
            [MOSTRAR] Lisa | alto | centro | auto
            [MOSTRAR] Maggie | alto | derecha | auto
            [PAUSA] 1500
            """);
        Assert.True((await humans.BoxesAsync()).Values.All(x => x.Width == 1280 / 3 - 32), "cinco a la vez: carriles de 394, no de 224");
        Assert.Equal(0, (await humans.AuditAsync()).Count, "humanos de buen tamaño, superpuestos a propósito: nada que avisar");

        var mixed = new Scene(folder, """
            [FONDO] casa
            [MOSTRAR] Twilight | pony | izquierda | auto | x=230 | y=-70
            [MOSTRAR] Rarity | pony | derecha | auto | x=-230 | y=-70
            [MOSTRAR] Bart | sentado | izquierda | auto
            [MOSTRAR] Fluttershy | pony | centro | auto
            [MOSTRAR] Lisa | alto | derecha | auto
            [PAUSA] 1500
            """);
        var issues = await mixed.AuditAsync();
        Assert.True(!issues.Any(x => x.Message.StartsWith("Twilight") || x.Message.StartsWith("Rarity")),
            "las de atrás (46 % del cuadro) no se comparan con Lisa: " + string.Join("; ", issues.Select(x => x.Message)));
        Assert.True(issues.Any(x => x.Message.StartsWith("Fluttershy")), "la pony del frente, bajita junto a Lisa, sí");
        foreach (var each in issues) mixed.Fix(each.BlockId, each.Fixes[0].Changes);
        Assert.True(!(await mixed.AuditAsync()).Any(x => x.Problem == FramingProblem.Small), "corregidos; las de atrás siguen sin aviso");

        // Everything small (a back row of 200 px, partly covered by the front row): the back row is warned too, against the frame.
        var tiny = new Scene(folder, """
            [FONDO] casa
            [MOSTRAR] Homero | alto | izquierda | auto | x=60 | y=-70 | ancho=100 | alto=200
            [MOSTRAR] Marge | alto | derecha | auto | x=-60 | y=-70 | ancho=100 | alto=200
            [MOSTRAR] Bart | alto | izquierda | auto | ancho=150 | alto=300
            [MOSTRAR] Lisa | alto | centro | auto | ancho=150 | alto=300
            [MOSTRAR] Maggie | alto | derecha | auto | ancho=150 | alto=300
            [PAUSA] 1500
            """);
        var small = await tiny.AuditAsync();
        Assert.Equal(5, small.Count(x => x.Problem == FramingProblem.Small), "todo pequeño: se avisa a los cinco");
        Assert.Contains("está detrás de", small.First(x => x.Message.StartsWith("Homero")).Message, "dice que está detrás");
        foreach (var each in small) tiny.Fix(each.BlockId, each.Fixes[0].Changes);
        Assert.True(!(await tiny.AuditAsync()).Any(x => x.Problem == FramingProblem.Small), "corregidos");
    }

    [Test("Encuadre: un personaje solo, o dos a la vez, pequeños frente al cuadro se avisan aunque se parezcan entre sí")]
    public static async Task SmallAgainstTheFrame()
    {
        TestMedia.RequireFfmpeg();
        using var folder = new TempFolder();
        TestMedia.SolidPng(folder.File("media/casa.png"), 1280, 720, (60, 60, 60));
        Character(folder, "alto", 400, 800);
        var alone = new Scene(folder, """
            [FONDO] casa
            [MOSTRAR] Bart | alto | centro | auto | ancho=150 | alto=300
            [PAUSA] 1000
            """);
        var issue = (await alone.AuditAsync()).Single();
        Assert.Contains("el 41 % del cuadro", issue.Message, "uno solo: contra el cuadro (296 px de 720)");
        alone.Fix(issue.BlockId, issue.Fixes[0].Changes);
        Assert.True(!(await alone.AuditAsync()).Any(), "corregido con su «Escala»");

        var both = new Scene(folder, """
            [FONDO] casa
            [MOSTRAR] Bart | alto | izquierda | auto | ancho=150 | alto=300
            [MOSTRAR] Lisa | alto | derecha | auto | ancho=150 | alto=300
            [PAUSA] 1000
            """);
        var issues = await both.AuditAsync();
        Assert.Equal(2, issues.Count(x => x.Problem == FramingProblem.Small), "los dos pequeños: compararlos entre sí no bastaba");
        foreach (var each in issues) both.Fix(each.BlockId, each.Fixes[0].Changes);
        Assert.True(!(await both.AuditAsync()).Any(), "corregidos, y en la misma proporción");
    }

    [Test("Encuadre estricto: todos los renders con «Cambiar tamaño»; el de la animación mala trae su sugerencia; tamaño y límite se conservan juntos")]
    public static async Task StrictReview()
    {
        TestMedia.RequireFfmpeg();
        using var folder = new TempFolder();
        TestMedia.SolidPng(folder.File("media/casa.png"), 1280, 720, (60, 60, 60));
        Character(folder, "alto", 400, 800);
        var scene = new Scene(folder, """
            [FONDO] casa
            [MOSTRAR] Bart | alto | izquierda | auto | animar x=-300 | animar ms=600
            [MOSTRAR] Lisa | alto | derecha | auto
            [PAUSA] 1500
            """);
        var planned = await scene.PlanAsync();
        var automatic = await FramingAudit.AuditAsync(scene.Blocks, planned, scene.Name);
        Assert.Sequence([FramingProblem.OutOfFrame], automatic.Select(x => x.Problem), "automático: solo la animación mala");
        var strict = await FramingAudit.AuditAsync(scene.Blocks, planned, scene.Name, strict: true);
        var bart = strict.Single(x => x.Message.StartsWith("Bart"));
        var lisa = strict.Single(x => x.Message.StartsWith("Lisa"));
        Assert.Equal(FramingProblem.OutOfFrame, bart.Problem, "estricto: Bart sigue con su aviso");
        Assert.Sequence(["limitar", "entrada", "tamano"], bart.Fixes.Select(x => x.Id), "su sugerencia primero y también «Cambiar tamaño»");
        Assert.True(!bart.Message.Contains("px de alto, el"), "el mensaje es el del aviso: " + bart.Message);
        Assert.Equal((FramingProblem.Review, "Cambiar tamaño", (double?)1), (lisa.Problem, lisa.Fixes.Single().Label, lisa.Scale),
            "Lisa, sin avisos: solo su tamaño, al 100 %");

        // Limit Bart's move (automatic review), then make him 20 % bigger (strict review): both stay.
        var memory = new FramingMemory();
        var block = scene.Blocks.First(x => x.Id == bart.BlockId);
        var limited = memory.Choose(block, automatic.Single(), "limitar");
        var resized = memory.Choose(limited, bart, "tamano", 1.2);
        var result = BlockParameters.Of(resized);
        Assert.Equal((double?)1.2, result.Scale, "más grande");
        Assert.Equal(BlockParameters.Of(limited).MotionOffsetX, result.MotionOffsetX, "conserva el movimiento limitado");
        var lisaBlock = scene.Blocks.First(x => x.Id == lisa.BlockId);
        Assert.Equal((double?)0.9, BlockParameters.Of(memory.Choose(lisaBlock, lisa, "tamano", 0.9)).Scale, "también achicar");
    }

    [Test("Director: «escala=130» en [MOSTRAR] guarda la escala del render; fuera de 25–300 o en otro bloque es un error")]
    public static void ScaleOption()
    {
        var spec = DirectorScript.ParseDirectorPrompt("[MOSTRAR] Bart | izquierda | auto | escala=130").Single();
        Assert.Equal(null, spec.Error, "válida");
        Assert.Equal((double?)1.3, BlockParameters.Parse(DirectorScript.DirectorParameters(spec)).Scale, "1.3");
        Assert.Equal(null, BlockParameters.Parse(DirectorScript.DirectorParameters(
            DirectorScript.ParseDirectorPrompt("[MOSTRAR] Bart | escala=100").Single())).Scale, "100 = sin escala");
        Assert.True(DirectorScript.ParseDirectorPrompt("[MOSTRAR] Bart | escala=500").Single().Error is not null, "máximo 300");
        Assert.True(DirectorScript.ParseDirectorPrompt("[IMAGEN] flor | escala=130").Single().Error is not null, "solo renders");
    }

    [Test("Encuadre: un PNG «sucio» (velo casi transparente y motas sueltas) se recorta por el personaje; uno sin transparencia se avisa")]
    public static async Task DirtyAndOpaque()
    {
        TestMedia.RequireFfmpeg();
        using var folder = new TempFolder();
        TestMedia.SolidPng(folder.File("media/casa.png"), 1280, 720, (60, 60, 60));
        // A 300×600 figure in the middle of a 900×1200 canvas covered by a 6 % haze, with single opaque specks in the corners.
        Character(folder, "sucio", 300, 600, padding: 300, dirt: (x, y) =>
            (x, y) is (3, 3) or (895, 1195) or (895, 4) ? ((byte)0, (byte)0, (byte)0, (byte)255) : ((byte)255, (byte)255, (byte)255, (byte)16));
        var surface = await CharacterFraming.SurfaceAsync(folder.File("media/sucio.png"), CancellationToken.None);
        // The figure is 1/3 × 1/2 of the canvas, plus the small safety border.
        Assert.True(surface.Bounds.X > 0.29 && surface.Bounds.W < 0.4 && surface.Bounds.Y > 0.2 && surface.Bounds.H < 0.56,
            $"el recorte sigue al personaje, no al lienzo ({surface.Bounds})");
        Assert.True(!surface.Opaque, "tiene transparencia");
        var dirty = new Scene(folder, """
            [FONDO] casa
            [MOSTRAR] Bart | sucio | centro | auto
            [PAUSA] 1000
            """);
        Assert.True(!(await dirty.AuditAsync()).Any(), "recortado bien: tamaño normal, nada que avisar");

        // Drawn in Paint: a figure on a white background, without transparency.
        TestMedia.Png(folder.File("media/paint.png"), 800, 800, (x, y) => x is >= 300 and < 500 && y is >= 200 and < 760
            ? ((byte)220, (byte)40, (byte)40, (byte)255) : ((byte)255, (byte)255, (byte)255, (byte)255));
        var paint = await CharacterFraming.SurfaceAsync(folder.File("media/paint.png"), CancellationToken.None);
        Assert.Equal((true, (string?)"FFFFFF"), (paint.Opaque, paint.Background), "sin transparencia, fondo blanco");
        Assert.True(paint.Bounds.W < 0.35, $"se mide la figura, no el fondo ({paint.Bounds})");
        var opaque = new Scene(folder, """
            [FONDO] casa
            [MOSTRAR] Bart | paint | centro | auto
            [PAUSA] 1000
            """);
        var warning = (await opaque.AuditAsync()).Single();
        Assert.Equal(FramingProblem.OpaqueBackground, warning.Problem, "aviso de imagen sin transparencia");
        Assert.Contains("#FFFFFF", warning.Message, "dice el color del fondo");
    }
}
