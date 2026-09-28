using System.Text.Json;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;
using LoquendoAI.Infrastructure.Director;
using static LoquendoAI.Infrastructure.Director.DirectorScript;

namespace LoquendoAI.Tests;

/// <summary>Blur ([DESENFOQUE]): language, level over time, preview and VEGAS Gaussian blur.</summary>
internal static class BlurTests
{
    private static DirectorSpec One(string line) => ParseDirectorPrompt(line).Single();

    [Test("[DESENFOQUE]: objetivo, suavizar/ligero/quitar, duración, valor y errores")]
    public static void Language()
    {
        BlurSettings Parsed(string line, bool character = false)
        {
            var spec = One(line);
            Assert.Equal(null, spec.Error, "error de " + line);
            return BlockParameters.Parse(DirectorParameters(spec)).Blur(character);
        }
        Assert.Equal(new BlurSettings("fondo", 0.02, 0), Parsed("[DESENFOQUE] fondo"), "por defecto: suavizar, de golpe");
        Assert.Equal(new BlurSettings("todos", 0.01, 800), Parsed("[DESENFOQUE] todo | ligero | duracion=800"), "ligero animado");
        Assert.Equal(new BlurSettings("fondo", 0, 600), Parsed("[ENFOCAR] fondo | duracion=600"), "enfocar = quitar");
        Assert.Equal(new BlurSettings("personajes", 0.015, 0), Parsed("[DESENFOQUE] personajes | valor=0,015"), "valor propio");
        Assert.Equal(new BlurSettings("fondo", 0.01, 0), Parsed("[BLUR] fondo | estilo=ligero"), "estilo=");
        var bart = One("[DESENFOQUE] Bart | quitar | ms=300");
        Assert.Equal(("Bart", "personaje"), (bart.CharacterName, bart.Text), "un personaje");
        Assert.Equal(new BlurSettings("personaje", 0, 300), BlockParameters.Parse(DirectorParameters(bart)).Blur(true), "quitar a Bart");
        foreach (var bad in new[] { "[DESENFOQUE] fondo | borroso", "[DESENFOQUE] fondo | valor=0,5", "[DESENFOQUE] fondo | duracion=9000",
                     "[DESENFOQUE] fondo | quitar | valor=0,01", "[DESENFOQUE] fondo | suavizar | ligero", "[DESENFOQUE] fondo | zoom=2" })
            Assert.True(One(bad).Error is not null, "debe fallar: " + bad);
    }

    [Test("Nivel de desenfoque: rampa, de golpe, por objetivo, capas que aparecen después y claves por evento")]
    public static void Curve()
    {
        var bart = Guid.NewGuid();
        BlurCue Cue(long at, string target, double amount, long ms, Guid? who = null) => new(at, new BlurSettings(target, amount, ms), who);
        BlurCue[] cues = [Cue(1000, "fondo", 0.02, 400), Cue(2000, "todos", 0.01, 0), Cue(3000, "personaje", 0, 800, bart)];
        var background = BlurPlan.Curve(cues, ScriptBlockKind.Background, "guion", null);
        Assert.Equal(0d, BlurPlan.At(background, 900), "antes, nítido");
        Assert.Near(0.01, BlurPlan.At(background, 1200), 0.0001, "a mitad de la rampa");
        Assert.Equal(0.02, BlurPlan.At(background, 1500), "suavizar");
        Assert.Equal(0.01, BlurPlan.At(background, 2000), "«todos» lo cambia de golpe");
        var render = BlurPlan.Curve(cues, ScriptBlockKind.CharacterShow, "guion", bart);
        Assert.Equal((0d, 0.01), (BlurPlan.At(render, 1500), BlurPlan.At(render, 2500)), "Bart: solo desde «todos»");
        Assert.Near(0.005, BlurPlan.At(render, 3400), 0.0001, "y se enfoca en 800 ms");
        Assert.Equal(0, BlurPlan.Curve([Cue(0, "personaje", 0.02, 0, bart)], ScriptBlockKind.CharacterShow, "guion", Guid.NewGuid()).Count,
            "otro personaje: nada");

        var slice = BlurPlan.Slice(background, 1100, 2500)!.ToList();
        Assert.Equal(0, slice[0].AtMs, "relativo al evento");
        Assert.True(slice.Count(x => x.AtMs <= 400) >= 8, "una clave por fotograma mientras cambia");
        var jump = slice.FindIndex(x => x.AtMs == 900);
        Assert.True(jump > 0 && slice[jump - 1].AtMs == 899 && slice[jump - 1].Amount == 0.02 && slice[jump].Amount == 0.01,
            "salto: clave justo antes para que VEGAS no haga rampa");
        Assert.Equal(null, BlurPlan.Slice(background, 0, 900), "sin desenfoque en ese tramo: sin efecto");
    }

    /// <summary>Width in pixels of the soft edge of a red box on blue, along one row.</summary>
    private static int EdgeWidth((int Width, int Height, byte[] Rgba) frame, int row, int from, int to)
    {
        var width = 0;
        for (var x = from; x < to; x++)
        {
            var red = frame.Rgba[(row * frame.Width + x) * 4];
            if (red is > 30 and < 190) width++;
        }
        return width;
    }

    [Test("Desenfoque renderizado: se anima, no se corta en el borde del render, no crece con la cámara; VEGAS con claves y antes del Cortador")]
    public static async Task Rendered()
    {
        TestMedia.RequireFfmpeg();
        using var folder = new TempFolder();
        var background = TestMedia.SolidPng(folder.File("media/fondo.png"), 1280, 720, (20, 20, 200));
        var red = TestMedia.SolidPng(folder.File("media/bart.png"), 400, 800, (230, 10, 10));
        var bart = Guid.NewGuid();
        SceneMedia[] media =
        [
            new(Guid.NewGuid(), ScriptBlockKind.Background, 0, 0, background, AutoTrimBorders: false, VisualMaxWidth: 1280, VisualMaxHeight: 720),
            new(Guid.NewGuid(), ScriptBlockKind.CharacterShow, 0, 0, red, "centro", bart, VisualMaxWidth: 1280, VisualMaxHeight: 610)
        ];
        // Bart blurs from 400 ms to 1200 ms («suavizar»).
        var scene = new SceneComposition(2000, media,
            BlurCues: [new BlurCue(400, new BlurSettings("personaje", BlurPlan.Soft, 800), bart)]);
        var preview = folder.File("out/blur.mp4");
        await SceneComposer.RenderAsync(scene, preview);
        // The render (305×610, centred) has its left edge at x≈487; row 400 crosses it.
        int Edge(string file, double seconds) => EdgeWidth(TestMedia.Frame(file, seconds), 400, 440, 540);
        var sharp = Edge(preview, 0.2);
        var middle = Edge(preview, 0.8);
        var full = Edge(preview, 1.6);
        Assert.True(sharp <= 2, $"nítido al principio ({sharp} px)");
        Assert.True(middle > sharp + 3 && full > middle + 3, $"se desenfoca poco a poco ({sharp} → {middle} → {full} px)");
        var endFrame = TestMedia.Frame(preview, 1.6);
        var vertical = 0;
        for (var y = 60; y < 160; y++)
            if (endFrame.Rgba[(y * endFrame.Width + 640) * 4] is > 30 and < 190) vertical++;
        Assert.Near(full, vertical, 4, $"desenfoca igual en vertical ({vertical} px) que en horizontal ({full} px)");
        var box = TestMedia.BoundingBox(endFrame, (r, g, b) => r > 40)!;
        Assert.True(box[0] < 480, $"el desenfoque sale del borde del render (no se corta): {box[0]}");

        // With the camera at 2×, VEGAS blurs after Pan/Crop: the soft edge on screen keeps its width.
        var zoomed = scene with { CameraCues = [new CameraCue(0, new CameraSettings("punto", 2, 0, "cara", 0, 0), null)] };
        var zoomedPreview = folder.File("out/blur_zoom.mp4");
        await SceneComposer.RenderAsync(zoomed, zoomedPreview);
        var zoomFrame = TestMedia.Frame(zoomedPreview, 1.6);
        var zoomBox = TestMedia.BoundingBox(zoomFrame, (r, g, b) => r > 190)!;
        var zoomEdge = EdgeWidth(zoomFrame, 360, Math.Max(0, zoomBox[0] - 60), zoomBox[0] + 20);
        Assert.Near(full, zoomEdge, 4, "con la cámara 2× el desenfoque no se duplica");

        var withBars = scene with
        {
            BlurCues = [new BlurCue(400, new BlurSettings("fondo", BlurPlan.Light, 0), null), .. scene.BlurCues!],
            Cinema = CinemaPlan.Build([new CinemaCue(0, new CinemaSettings(true, "cerrado", 0, "todos", []))], null)
        };
        foreach (var mode in new[] { VegasExportMode.Normal, VegasExportMode.Legacy })
        {
            var export = folder.File($"out/vegas_{mode}");
            await VegasBridge.ExportAsync(withBars, export, "blur", 720, mode: mode);
            var script = await File.ReadAllTextAsync(Path.Combine(export, "Abrir_en_VEGAS_14_o_superior.cs"));
            Assert.Contains("static string AddBlur(", script, $"{mode}: ayudante del desenfoque gaussiano");
            var bartEvent = script[script.IndexOf("bart.png", StringComparison.Ordinal)..];
            var blurAt = bartEvent.IndexOf("AddBlur(vegas, ev, new double[] { 0, ", StringComparison.Ordinal);
            var barsAt = bartEvent.IndexOf("AddLetterbox(vegas, ev,", StringComparison.Ordinal);
            Assert.True(blurAt >= 0 && barsAt > blurAt, $"{mode}: el desenfoque va antes del Cortador (barras nítidas)");
            Assert.Contains("0.01, 0.01", script, $"{mode}: el fondo con «Desenfoque ligero»");
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(export, "escena.json")));
            var render = manifest.RootElement.GetProperty("Clips").EnumerateArray()
                .First(x => x.GetProperty("Kind").GetString() == nameof(ScriptBlockKind.CharacterShow));
            var keys = render.GetProperty("BlurKeys").EnumerateArray().Select(x => (x.GetProperty("AtMs").GetInt64(), x.GetProperty("Amount").GetDouble())).ToArray();
            Assert.True(keys.Length >= 20 && keys.Last().Item2 == BlurPlan.Soft, $"{mode}: una clave por fotograma en la rampa hasta 0,02");
        }
    }

    [Test("Acción «desenfoque» de la IA → línea [DESENFOQUE] válida")]
    public static void AiAction()
    {
        using var step = JsonDocument.Parse("""{"accion":"desenfoque","objetivo":"fondo","estilo":"ligero","duracion_ms":700}""");
        var line = DirectorAiSchema.ToLine(step.RootElement).Line;
        Assert.Equal("[DESENFOQUE] fondo | ligero | duracion=700", line, "línea");
        Assert.Equal(null, One(line).Error, "válida");
    }
}
