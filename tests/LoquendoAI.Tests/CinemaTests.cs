using System.Text.Json;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;
using LoquendoAI.Infrastructure.Director;

namespace LoquendoAI.Tests;

/// <summary>Cinematic bars ([CINE]): language, plan, continuity, preview and VEGAS Cookie Cutter.</summary>
internal static class CinemaTests
{
    private static DirectorSpec One(string line) => DirectorScript.ParseDirectorPrompt(line).Single();

    [Test("[CINE]: mostrar/quitar, estilo, duración y capas (todos, personajes o nombres)")]
    public static void Language()
    {
        Guid bart = Guid.NewGuid(), lisa = Guid.NewGuid();
        var names = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase) { ["Bart"] = bart, ["Lisa"] = lisa };
        CinemaSettings Settings(string line)
        {
            var spec = One(line);
            Assert.Equal(null, spec.Error, "error de " + line);
            return BlockParameters.Parse(DirectorScript.DirectorParameters(spec, n => names.TryGetValue(n, out var id) ? id : null)).Cinema();
        }
        var full = Settings("[CINE] mostrar");
        Assert.Equal((true, "cerrado", 800L, "todos"), (full.Show, full.Style, full.MoveMs, full.Layers), "valores por defecto");
        var list = Settings("[CINE] abrir | estilo=fino | duracion=600 | capas=Bart, lisa");
        Assert.Equal((true, "abierto", 600L, "lista"), (list.Show, list.Style, list.MoveMs, list.Layers), "estilo abierto y lista");
        Assert.Sequence([bart, lisa], list.Characters, "personajes de la lista");
        Assert.Equal("personajes", Settings("[BARRAS] mostrar | capas=personajes").Layers, "todos los renders");
        var off = Settings("[CINE] cerrar | duracion=0");
        Assert.Equal((false, 0L), (off.Show, off.MoveMs), "cerrar = quitar, 0 ms = de golpe");
        foreach (var bad in new[] { "[CINE] quizas", "[CINE] mostrar | estilo=raro", "[CINE] mostrar | duracion=9000", "[CINE] mostrar | zoom=2" })
            Assert.True(One(bad).Error is not null, "debe fallar: " + bad);
    }

    [Test("Plan de barras: entran, se quedan, salen; se interrumpen; cambio de estilo; siguen en la escena siguiente")]
    public static void Plan()
    {
        CinemaCue Show(long at, long ms, string style = "cerrado") => new(at, new CinemaSettings(true, style, ms, "todos", []));
        CinemaCue Hide(long at, long ms) => new(at, new CinemaSettings(false, "cerrado", ms, "todos", []));
        var plan = CinemaPlan.Build([Show(1000, 800), Hide(3000, 500)], null);
        Assert.Sequence([(1000L, 1800L), (1800L, 3000L), (3000L, 3500L)], plan.Select(x => (x.StartMs, x.EndMs)), "tramos");
        Assert.Near(0.874, plan[0].SizeAt(1400), 0.001, "a mitad de la entrada");
        Assert.Near(0.126, CinemaStyle.Closed.BarFraction(CinemaStyle.Closed.OpenSize), 0.001, "barras «cerrado»: 12,6 % arriba y abajo");
        Assert.Near(0.072, CinemaStyle.Open.BarFraction(CinemaStyle.Open.OpenSize), 0.001, "barras «abierto»: 7,2 %");
        Assert.Equal(0d, CinemaStyle.Open.BarFraction(CinemaStyle.Open.ClosedSize), "cerradas: sin barras");

        var interrupted = CinemaPlan.Build([Show(0, 1000), Hide(500, 1000)], null);
        Assert.Near(0.874, interrupted[1].FromSize, 0.001, "quitar a mitad de la entrada parte de donde iban");
        Assert.Equal(1500L, interrupted[^1].EndMs, "y salen en su duración");
        var switched = CinemaPlan.Build([Show(0, 0), Show(1000, 800, "abierto")], null);
        Assert.Equal(("abierto", 1000L), (switched[^1].Style, switched[^1].StartMs), "otro estilo: cambio al momento");
        Assert.Equal(1, CinemaPlan.Build([Show(0, 0), Show(2000, 0)], null).Count, "repetir mostrar no parte los eventos");

        var scene = new Guid();
        SceneScriptBlock Block(int order, string json) => new(Guid.NewGuid(), scene, order, ScriptBlockKind.Cinema, ParametersJson: json);
        var end = CinemaPlan.EndState([Block(0, """{"cinemaMode":"mostrar","cinemaStyle":"abierto"}""")], null);
        Assert.Equal("abierto", end?.Style, "la escena termina con barras");
        var carried = CinemaPlan.Build([], end);
        Assert.Equal((0L, long.MaxValue), (carried.Single().StartMs, carried.Single().EndMs), "la siguiente empieza con ellas");
        Assert.Equal(null, CinemaPlan.EndState([Block(0, """{"cinemaMode":"quitar"}""")], end), "quitar las apaga para las siguientes");
    }

    [Test("PlanAsync: una escena sin [CINE] hereda las barras de la anterior")]
    public static async Task PlanAsyncCarriesBars()
    {
        var blocks = new[] { new SceneScriptBlock(Guid.NewGuid(), Guid.Empty, 0, ScriptBlockKind.Pause, PauseAfterMs: 1000) };
        var plain = await SceneComposer.PlanAsync(blocks, _ => null);
        Assert.Equal(null, plain.Cinema, "sin barras");
        var carried = await SceneComposer.PlanAsync(blocks, _ => null, cinemaStart: new CinemaState("cerrado", "todos", []));
        Assert.Equal(1, carried.Cinema?.Count ?? 0, "con barras heredadas");
    }

    private static int BlackRowsFromTop((int Width, int Height, byte[] Rgba) frame, int column)
    {
        var rows = 0;
        for (var y = 0; y < frame.Height; y++)
        {
            var i = (y * frame.Width + column) * 4;
            if (frame.Rgba[i] > 30 || frame.Rgba[i + 1] > 30 || frame.Rgba[i + 2] > 30) break;
            rows++;
        }
        return rows;
    }

    [Test("Barras renderizadas: altura calibrada con VEGAS, entrada animada, fijas con la cámara y personaje por delante")]
    public static async Task Rendered()
    {
        TestMedia.RequireFfmpeg();
        using var folder = new TempFolder();
        var background = TestMedia.SolidPng(folder.File("media/fondo.png"), 1280, 720, (40, 110, 200));
        var red = TestMedia.SolidPng(folder.File("media/bart.png"), 400, 800, (220, 20, 20));
        var green = TestMedia.SolidPng(folder.File("media/lisa.png"), 400, 800, (20, 200, 40));
        Guid bart = Guid.NewGuid(), lisa = Guid.NewGuid();
        SceneMedia[] media =
        [
            new(Guid.NewGuid(), ScriptBlockKind.Background, 0, 0, background, AutoTrimBorders: false, VisualMaxWidth: 1280, VisualMaxHeight: 720),
            new(Guid.NewGuid(), ScriptBlockKind.CharacterShow, 0, 0, red, "izquierda", bart, VisualMaxWidth: 1280, VisualMaxHeight: 700),
            new(Guid.NewGuid(), ScriptBlockKind.CharacterShow, 0, 0, green, "derecha", lisa, VisualMaxWidth: 1280, VisualMaxHeight: 700)
        ];
        // Bars in 1 s from 0, on every layer; a 1.5× camera on Bart from 1.2 s (bars must not zoom).
        var all = new SceneComposition(2000, media,
            Cinema: CinemaPlan.Build([new CinemaCue(0, new CinemaSettings(true, "cerrado", 1000, "todos", []))], null),
            CameraCues: [new CameraCue(1200, new CameraSettings("personaje", 1.5, 0, "cara", 0, 0), bart)]);
        var preview = folder.File("out/cine.mp4");
        await SceneComposer.RenderAsync(all, preview);
        var expected = 0.126 * 720;
        Assert.Near(expected * 0.52, BlackRowsFromTop(TestMedia.Frame(preview, 0.52), 640), 2.5, "a mitad de la entrada");
        Assert.Near(expected, BlackRowsFromTop(TestMedia.Frame(preview, 1.0), 640), 2, "abiertas");
        Assert.Near(expected, BlackRowsFromTop(TestMedia.Frame(preview, 1.6), 640), 2, "con la cámara acercada siguen igual");

        // Only Bart is chosen, but Lisa is drawn after him (in front): she gets the same effect instead of
        // showing over the bars. The bars are intact over her column.
        var onlyBart = new SceneComposition(1000, media,
            Cinema: CinemaPlan.Build([new CinemaCue(0, new CinemaSettings(true, "cerrado", 0, "lista", [bart]))], null));
        var partial = folder.File("out/cine_bart.mp4");
        await SceneComposer.RenderAsync(onlyBart, partial);
        var frame = TestMedia.Frame(partial, 0.5);
        Assert.Near(expected, BlackRowsFromTop(frame, 640), 2, "barras sobre el fondo");
        var lisaBox = TestMedia.BoundingBox(frame, (r, g, b) => g > 150 && r < 90)!;
        Assert.Near(expected, BlackRowsFromTop(frame, (lisaBox[0] + lisaBox[2]) / 2), 2, "Lisa, delante de Bart, también bajo las barras");
        // Bars on Bart only + camera zoom: like VEGAS (Cookie Cutter after Pan/Crop) they keep their size.
        var zoomed = folder.File("out/cine_bart_camara.mp4");
        await SceneComposer.RenderAsync(onlyBart with
        {
            CameraCues = [new CameraCue(300, new CameraSettings("personaje", 1.5, 0, "cara", 0, 0), bart)]
        }, zoomed);
        Assert.Near(expected, BlackRowsFromTop(TestMedia.Frame(zoomed, 0.7), 640), 2.5, "barras parciales fijas con la cámara");

        // VEGAS: the Cookie Cutter with keyframes on every layer; events are only cut by the camera, not by the bars.
        var export = folder.File("out/vegas");
        await VegasBridge.ExportAsync(all, export, "cine", 720, mode: VegasExportMode.Normal);
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(export, "escena.json")));
        var clips = manifest.RootElement.GetProperty("Clips").EnumerateArray().Where(x => x.GetProperty("Layer").GetInt32() >= 0).ToArray();
        Assert.True(clips.All(x => x.GetProperty("Cinema").ValueKind == JsonValueKind.Object), "todas las capas llevan el efecto");
        var ramp = clips.First(x => x.GetProperty("StartMs").GetInt64() == 0).GetProperty("Cinema").GetProperty("Keys").EnumerateArray()
            .Select(x => (x.GetProperty("AtMs").GetInt64(), x.GetProperty("Border").GetDouble(), x.GetProperty("Size").GetDouble())).ToArray();
        Assert.Sequence([(0L, 1d, 1d), (1000L, 1d, 0.748), (1200L, 1d, 0.748)], ramp, "entrada: borde 1,0, tamaño 1,0 → 0,748 en 1 s, hasta el corte de cámara");
        Assert.Sequence([0L, 1200L], clips.Where(x => x.GetProperty("Kind").GetString() == "Background").Select(x => x.GetProperty("StartMs").GetInt64()),
            "el fondo solo se corta donde corta la cámara");
        var script = await File.ReadAllTextAsync(Path.Combine(export, "Abrir_en_VEGAS_14_o_superior.cs"));
        Assert.Contains("static string AddLetterbox(", script, "ayudante del Cortador de galletas");
        Assert.Contains("AddLetterbox(vegas, ev, new double[] { 0, 1000, 1200 }, new double[] { 1, 1, 1 }, new double[] { 1, 0.748, 0.748 })", script, "tamaño animado durante la entrada");

        var exportPartial = folder.File("out/vegas_bart");
        await VegasBridge.ExportAsync(onlyBart, exportPartial, "cine", 720, mode: VegasExportMode.Normal);
        Assert.Sequence(["Bart", "Lisa"], WithEffect(exportPartial, bart, lisa), "Bart (elegido) y Lisa (delante de él), el fondo no");

        // Lisa behind Bart: she is covered by Bart's bars and keeps no effect.
        var lisaBehind = onlyBart with { Media = [media[0], media[2], media[1]] };
        var exportBehind = folder.File("out/vegas_detras");
        await VegasBridge.ExportAsync(lisaBehind, exportBehind, "cine", 720, mode: VegasExportMode.Normal);
        Assert.Sequence(["Bart"], WithEffect(exportBehind, bart, lisa), "Lisa detrás: sin efecto");
        var behind = folder.File("out/cine_detras.mp4");
        await SceneComposer.RenderAsync(lisaBehind, behind);
        var behindFrame = TestMedia.Frame(behind, 0.5);
        var lisaBehindBox = TestMedia.BoundingBox(behindFrame, (r, g, b) => g > 150 && r < 90)!;
        Assert.Near(expected, BlackRowsFromTop(behindFrame, (lisaBehindBox[0] + lisaBehindBox[2]) / 2), 2, "Lisa detrás, tapada por las barras de Bart");
    }

    private static string[] WithEffect(string export, Guid bart, Guid lisa)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(export, "escena.json")));
        return manifest.RootElement.GetProperty("Clips").EnumerateArray()
            .Where(x => x.GetProperty("Layer").GetInt32() >= 0 && x.GetProperty("Cinema").ValueKind == JsonValueKind.Object)
            .Select(x => x.TryGetProperty("CharacterId", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetGuid() == bart ? "Bart" : id.GetGuid() == lisa ? "Lisa" : "?" : x.GetProperty("Kind").GetString()!)
            .Distinct().Order().ToArray();
    }

    [Test("Barras en algunas capas: solo mientras la capa elegida está en pantalla, y las capas delante la heredan")]
    public static void Resolve()
    {
        Guid bart = Guid.NewGuid(), lisa = Guid.NewGuid();
        Guid bg = Guid.NewGuid(), bartBlock = Guid.NewGuid(), lisaBlock = Guid.NewGuid();
        var scene = new SceneComposition(3000,
        [
            new(bg, ScriptBlockKind.Background, 0, 0, "fondo.png", AutoTrimBorders: false, VisualMaxWidth: 1280, VisualMaxHeight: 720),
            new(bartBlock, ScriptBlockKind.CharacterShow, 500, 0, "bart.png", "izquierda", bart, VisualMaxWidth: 1280, VisualMaxHeight: 700),
            new(lisaBlock, ScriptBlockKind.CharacterShow, 1000, 0, "lisa.png", "derecha", lisa, VisualMaxWidth: 1280, VisualMaxHeight: 700)
        ], Cinema: CinemaPlan.Build([new CinemaCue(0, new CinemaSettings(true, "abierto", 0, "lista", [bart]))], null));
        var resolved = CinemaPlan.Resolve(scene).Cinema!;
        Assert.Equal(500L, resolved[0].StartMs, "sin Bart en pantalla no hay barras");
        Assert.Sequence(new[] { bartBlock }, resolved[0].Blocks!, "de 0,5 s a 1 s solo Bart");
        Assert.Equal(1000L, resolved[1].StartMs, "Lisa entra delante");
        Assert.True(resolved[1].Blocks!.Contains(lisaBlock) && resolved[1].Blocks!.Contains(bartBlock) && !resolved[1].Blocks!.Contains(bg),
            "Bart y Lisa, no el fondo");
        Assert.Equal(long.MaxValue, resolved[^1].EndMs, "hasta el final");
        Assert.True(ReferenceEquals(CinemaPlan.Resolve(scene with { Cinema = resolved }).Cinema, resolved), "idempotente");
    }

    [Test("Barras en VEGAS: claves sin cortar eventos (ni dentro de un cruce), saltos y cambio de estilo")]
    public static void Keys()
    {
        // Bars at once at 500, thin style from 1500, removed in 400 ms from 2500.
        var plan = CinemaPlan.Build([new CinemaCue(500, new CinemaSettings(true, "cerrado", 0, "todos", [])),
            new CinemaCue(1500, new CinemaSettings(true, "abierto", 0, "todos", [])),
            new CinemaCue(2500, new CinemaSettings(false, "cerrado", 400, "todos", []))], null);
        var keys = CinemaPlan.Keys(plan, 0, 4000)!.Select(x => (x.AtMs, x.Border, x.Size)).ToArray();
        Assert.Sequence(
        [
            (0L, 1d, 1d), (499L, 1d, 1d), (500L, 1d, 0.748),          // no bars (size that removes them), then at once
            (1499L, 1d, 0.748), (1500L, 0.56, 0.639),                 // style change at once
            (2500L, 0.56, 0.639), (2900L, 0.56, 0.747), (4000L, 0.56, 0.747) // they leave in 400 ms and stay removed
        ], keys, "claves del Cortador");
        Assert.Equal(null, CinemaPlan.Keys(plan, 0, 400), "sin barras en ese tramo: sin efecto");
        var middle = CinemaPlan.Keys(plan, 2600, 2800)!;
        Assert.Near(0.639 + (0.747 - 0.639) * 0.25, middle[0].Size, 0.0001, "un evento que empieza a mitad de la salida sigue la rampa");
    }

    [Test("Acción «cine» de la IA → línea [CINE] válida")]
    public static void AiAction()
    {
        using var show = JsonDocument.Parse("""{"accion":"cine","modo":"mostrar","estilo":"abierto","duracion_ms":900}""");
        var line = DirectorAiSchema.ToLine(show.RootElement).Line;
        Assert.Equal("[CINE] mostrar | estilo=abierto | duracion=900", line, "mostrar");
        Assert.Equal(null, One(line).Error, "válida");
        using var hide = JsonDocument.Parse("""{"accion":"cine","modo":"quitar","estilo":"cerrado","duracion_ms":400}""");
        Assert.Equal("[CINE] quitar | duracion=400", DirectorAiSchema.ToLine(hide.RootElement).Line, "quitar");
    }
}
