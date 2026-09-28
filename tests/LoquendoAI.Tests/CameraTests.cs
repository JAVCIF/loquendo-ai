using System.Text.Json;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;
using LoquendoAI.Infrastructure.Director;

namespace LoquendoAI.Tests;

/// <summary>Camera: the [CAMARA] line, its rules, the shared path and preview = VEGAS.</summary>
internal static class CameraTests
{
    private static DirectorSpec One(string line) => DirectorScript.ParseDirectorPrompt(line).Single();

    private static CameraSettings Settings(DirectorSpec spec) =>
        BlockParameters.Parse(DirectorScript.DirectorParameters(spec)).Camera(spec.CharacterName.Length > 0);

    [Test("[CAMARA]: personaje, quien habla, punto y general, con zoom, duración, enfoque y desplazamiento")]
    public static void Language()
    {
        var bart = One("[CÁMARA] Bart | zoom=1.5 | duracion=250 | enfoque=cuerpo | y=-40");
        Assert.Equal((ScriptBlockKind.Camera, "Bart", null as string), (bart.Kind, bart.CharacterName, bart.Error), "personaje");
        Assert.Equal(new CameraSettings("personaje", 1.5, 250, "cuerpo", 0, -40), Settings(bart), "ajustes");
        Assert.Equal(new CameraSettings("habla", 1.4, 300, "cara", 0, 0), Settings(One("[CAMARA] quien habla | zoom=140%")), "quien habla");
        Assert.Equal(new CameraSettings("punto", 1.3, BlockDefaults.CameraMoveMs, "cara", 100, 0), Settings(One("[CAMARA] centro | x=100")), "punto");
        Assert.Equal(new CameraSettings("general", 1, 0, "cara", 0, 0), Settings(One("[CAMARA] plano general | duracion=0")), "general (0 ms = corte)");
        Assert.Equal(new CameraSettings("personaje", BlockDefaults.CameraZoom, BlockDefaults.CameraMoveMs, "cara", 0, 0),
            Settings(One("[CAMARA] Bart")), "valores por defecto");
        Assert.Equal(1.25, DirectorScript.ParseZoom("x1,25"), "zoom con coma");
        foreach (var bad in new[] { "[CAMARA] Bart | zoom=5", "[CAMARA] Bart | duracion=9000", "[CAMARA] Bart | enfoque=pies",
                     "[CAMARA] Bart | ancho=300", "[CAMARA] Bart | x=900" })
            Assert.True(One(bad).Error is not null, "debe fallar: " + bad);
    }

    [Test("[CAMARA] sobre alguien fuera de pantalla: aviso si aparece después, error si no aparece")]
    public static void OnScreenRule()
    {
        var specs = DirectorScript.ParseDirectorPrompt("""
            [CAMARA] Bart | zoom=1.4
            [MOSTRAR] Bart | feliz
            [CAMARA] Lisa
            [CAMARA] general
            [MOSTRAR] Lisa | seria
            [OCULTAR] Bart
            [CAMARA] Bart
            [CAMARA] Lisa
            """);
        var checks = DirectorScript.CameraChecks(specs);
        Assert.False(checks[0].IsError, "Bart aparece antes de la siguiente cámara: solo aviso");
        Assert.True(checks[2].IsError, "Lisa no aparece mientras dura su cámara");
        Assert.True(checks[6].IsError, "Bart ya salió");
        Assert.False(checks.ContainsKey(7), "Lisa está en pantalla");
        Assert.False(checks.ContainsKey(3), "general no necesita a nadie");
    }

    [Test("Camino de cámara: movimientos lineales, cortes, interrupciones y muestras para VEGAS")]
    public static void CameraPathMath()
    {
        var close = CameraWindow.Around(640, 300, 2);
        var path = CameraPath.FromRequests([(1000, close, 500), (3000, CameraWindow.Full, 0)]);
        Assert.True(path.At(999).IsFull, "antes del movimiento");
        Assert.Near(960, path.At(1250).Width, 0.01, "a mitad del movimiento (lineal en la ventana)");
        Assert.True(path.At(2000).Near(close), "sostiene el encuadre");
        Assert.True(path.At(3000).IsFull, "vuelve de golpe");
        Assert.Sequence([3000L], path.Cuts, "solo el retorno es un corte");
        var samples = path.Samples(1100, 1000);
        Assert.Sequence([0L, 400L], samples.Select(x => x.AtMs), "claves: inicio del clip y fin del movimiento");
        Assert.Near(path.At(1100).Width, samples[0].Width, 0.01, "valor en el inicio");

        var interrupted = CameraPath.FromRequests([(0, close, 1000), (500, CameraWindow.Full, 1000)]);
        Assert.Near(960, interrupted.At(500).Width, 0.01, "una orden nueva parte de donde va la cámara");
        Assert.True(interrupted.At(1500).IsFull, "y llega a su destino");
        Assert.Equal(0, interrupted.Cuts.Count(), "sin saltos");

        var edge = CameraWindow.Around(10, 10, 2);
        Assert.Equal((0d, 0d), (edge.X, edge.Y), "la ventana no sale del cuadro");
        Assert.True(CameraPath.Still.IsStill && CameraPath.Still.PreviewFilter().Contains("scale="), "sin cámara");
    }

    private static VegasBridge.Clip Render(Guid character, long start, long duration, string position = "izquierda", int moveX = 0) =>
        new(nameof(ScriptBlockKind.CharacterShow), Guid.NewGuid(), start, duration, "x.png", 2, position, 1280, 610, 0, 0,
            false, false, false, "00FF00", 0.3, 0, false, "guion", false, MotionOffsetX: moveX, CharacterId: character);

    [Test("Planificador: sigue al personaje, su movimiento y su salida; «quien habla» salta de uno a otro")]
    public static void Planner()
    {
        Guid bart = Guid.NewGuid(), lisa = Guid.NewGuid();
        var bartClip = Render(bart, 0, 4000, moveX: 400);
        var lisaClip = Render(lisa, 0, 6000, "derecha");
        LayerLayout Layout(VegasBridge.Clip clip) => LayerGeometry.Place(LayerSpec.From(clip), 400, 800, 1280, 720)!;
        var settings = new CameraSettings("personaje", 2, 500, "cara", 0, 0);
        var scene = new SceneComposition(6000, []);
        var path = CameraPlanner.Plan(scene, [new CameraCue(1000, settings, bart)], [bartClip, lisaClip], Layout);
        double FocusX(long t) => CameraPlanner.FocusPoint(bartClip, Layout(bartClip), t, "cara")!.Value.X;
        Assert.True(path.At(1000).IsFull, "empieza en el plano general");
        var at1500 = path.At(1500);
        Assert.Near(FocusX(1500), at1500.X + at1500.Width / 2, 1, "llega centrado en Bart");
        var at3000 = path.At(3000);
        Assert.Near(Math.Clamp(FocusX(3000), 320, 960), at3000.X + at3000.Width / 2, 1, "lo sigue mientras se mueve");
        Assert.Near(FocusX(1500) + 400 * 1500 / 4000d, FocusX(3000), 1, "movimiento lineal de Bart");
        Assert.True(path.At(4500).IsFull, "Bart sale: vuelve al plano general");

        var talk = new SceneComposition(6000,
        [
            new(Guid.NewGuid(), ScriptBlockKind.Dialogue, 1000, 1000, "a.wav", CharacterId: bart),
            new(Guid.NewGuid(), ScriptBlockKind.Dialogue, 2000, 1000, "b.wav", CharacterId: lisa),
            new(Guid.NewGuid(), ScriptBlockKind.Dialogue, 3000, 1000, "c.wav", CharacterId: Guid.NewGuid())
        ]);
        var follow = CameraPlanner.Plan(talk, [new CameraCue(500, settings with { Mode = "habla", MoveMs = 0 }, null)],
            [bartClip with { MotionOffsetX = 0 }, lisaClip], Layout);
        Assert.True(follow.At(1500).X < 320, "encuadra a Bart (izquierda)");
        Assert.True(follow.At(2500).X > 320, "encuadra a Lisa (derecha)");
        Assert.True(follow.At(3500).Near(follow.At(2500)), "alguien fuera de pantalla habla: la cámara se queda");
    }

    [Test("Diálogo IA: la acción «camara» se convierte en una línea válida")]
    public static void AiAction()
    {
        using var step = JsonDocument.Parse("""{"accion":"camara","objetivo":"quien habla","zoom":150,"duracion_ms":400,"enfoque":"cara"}""");
        var (line, _) = DirectorAiSchema.ToLine(step.RootElement);
        Assert.Equal("[CAMARA] habla | zoom=1.5 | duracion=400 | enfoque=cara", line, "línea");
        Assert.Equal(null, One(line).Error, "válida");
        using var general = JsonDocument.Parse("""{"accion":"camara","objetivo":"general","zoom":110,"duracion_ms":0,"enfoque":"cara"}""");
        Assert.Equal("[CAMARA] general | duracion=0", DirectorAiSchema.ToLine(general.RootElement).Line, "general");
    }

    // ---------- rendered: preview vs VEGAS ----------

    private static bool Red(byte r, byte g, byte b) => r > 150 && g < 90 && b < 90;

    [Test("Cámara renderizada: la preview y VEGAS (Pan/Crop y medio normalizado) encuadran igual, quieta y a mitad del movimiento")]
    public static async Task PreviewMatchesVegas()
    {
        TestMedia.RequireFfmpeg();
        using var folder = new TempFolder();
        var character = TestMedia.SolidPng(folder.File("media/pj.png"), 400, 800, (220, 20, 20));
        var background = TestMedia.Png(folder.File("media/fondo.png"), 1280, 720, (x, y) =>
            ((byte)20, (byte)(80 + y / 8), (byte)(120 + x / 16), (byte)255));
        var bart = Guid.NewGuid();
        SceneMedia[] media =
        [
            new(Guid.NewGuid(), ScriptBlockKind.Background, 0, 0, background, AutoTrimBorders: false, VisualMaxWidth: 1280, VisualMaxHeight: 720),
            new(Guid.NewGuid(), ScriptBlockKind.CharacterShow, 0, 0, character, "izquierda", bart, VisualMaxWidth: 1280, VisualMaxHeight: 610)
        ];
        // 0–1000 ms: move to Bart at 1.6×; hold until 2000; cut back to the whole frame.
        var scene = new SceneComposition(3000, media, CameraCues:
        [
            new CameraCue(0, new CameraSettings("personaje", 1.6, 1000, "cara", 0, 0), bart),
            new CameraCue(2000, new CameraSettings("general", 1, 0, "cara", 0, 0), null)
        ]);
        var preview = folder.File("out/camara.mp4");
        await SceneComposer.RenderAsync(scene, preview);
        var problems = new List<string>();
        foreach (var mode in new[] { VegasExportMode.Normal, VegasExportMode.Legacy })
        {
            var export = folder.File("out/vegas_" + mode);
            await VegasBridge.ExportAsync(scene, export, "camara", 720, mode: mode);
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(export, "escena.json")));
            var clips = manifest.RootElement.GetProperty("Clips").EnumerateArray()
                .Where(x => x.GetProperty("Kind").GetString() == nameof(ScriptBlockKind.CharacterShow)).ToArray();
            Assert.Equal(2, clips.Length, $"{mode}: el render se corta solo en el corte de cámara");
            var script = await File.ReadAllTextAsync(Path.Combine(export, "Abrir_en_VEGAS_14_o_superior.cs"));
            Assert.Contains("VideoMotionKeyframe key1", script, $"{mode}: la cámara son keyframes de Pan/Crop");
            if (mode == VegasExportMode.Legacy)
                Assert.Equal(2, Directory.GetFiles(Path.Combine(export, "medios_normalizados"), "*.png").Length,
                    "Legacy: un PNG por imagen, reutilizado en cada tramo (la cámara no se hornea)");
            foreach (var time in new[] { 520L, 1520L }) // exact frames at 25 fps (every 40 ms)
            {
                var expected = TestMedia.BoundingBox(TestMedia.Frame(preview, time / 1000d), Red)!;
                var clip = clips.First(x => x.GetProperty("StartMs").GetInt64() <= time &&
                    time < x.GetProperty("StartMs").GetInt64() + x.GetProperty("DurationMs").GetInt64());
                var actual = VegasBox(clip, time - clip.GetProperty("StartMs").GetInt64());
                var difference = expected.Zip(actual, (a, b) => Math.Abs(a - b)).Max();
                if (difference > 3)
                    problems.Add($"{mode} t={time}: preview [{string.Join(",", expected)}] VEGAS [{string.Join(",", actual)}] ({difference} px)");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Test("«Primer plano» automático (sin bloques de cámara): preview y VEGAS encuadran igual")]
    public static async Task AutomaticCloseUp()
    {
        TestMedia.RequireFfmpeg();
        using var folder = new TempFolder();
        // A render with transparent margins, as real renders have: the framing trims them.
        var character = TestMedia.Png(folder.File("media/pj.png"), 400, 800, (x, y) =>
            x is >= 50 and < 350 && y >= 60 ? ((byte)220, (byte)20, (byte)20, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)0));
        var background = TestMedia.SolidPng(folder.File("media/fondo.png"), 1280, 720, (20, 90, 140));
        var bart = Guid.NewGuid();
        var scene = new SceneComposition(2000,
        [
            new(Guid.NewGuid(), ScriptBlockKind.Background, 0, 0, background, AutoTrimBorders: false, VisualMaxWidth: 1280, VisualMaxHeight: 720),
            new(Guid.NewGuid(), ScriptBlockKind.CharacterShow, 0, 0, character, "centro", bart, VisualMaxWidth: 1280, VisualMaxHeight: 610,
                FramingPreset: "detalle")
        ]);
        var preview = folder.File("out/primer_plano.mp4");
        await SceneComposer.RenderAsync(scene, preview);
        var expected = TestMedia.BoundingBox(TestMedia.Frame(preview, 1.0), Red)!;
        foreach (var mode in new[] { VegasExportMode.Normal, VegasExportMode.Legacy })
        {
            var export = folder.File("out/vegas_" + mode);
            await VegasBridge.ExportAsync(scene, export, "primer_plano", 720, mode: mode);
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(export, "escena.json")));
            var clip = manifest.RootElement.GetProperty("Clips").EnumerateArray()
                .First(x => x.GetProperty("Kind").GetString() == nameof(ScriptBlockKind.CharacterShow));
            Assert.True(clip.GetProperty("CameraKeys").ValueKind == JsonValueKind.Array, $"{mode}: el acercamiento está en el clip");
            var actual = VegasBox(clip, 1000 - clip.GetProperty("StartMs").GetInt64(), (50, 60, 350, 800));
            var difference = expected.Zip(actual, (a, b) => Math.Abs(a - b)).Max();
            Assert.True(difference <= 3, $"{mode}: preview [{string.Join(",", expected)}] VEGAS [{string.Join(",", actual)}]");
        }
    }

    /// <summary>Where VEGAS shows the red render at <paramref name="atMs"/> into the event, interpolating its
    /// linear keyframes: native Pan/Crop (source rectangle) or normalized media seen through the camera window.</summary>
    private static int[] VegasBox(JsonElement clip, long atMs, (double Left, double Top, double Right, double Bottom)? red = null)
    {
        var source = red ?? (0, 0, 400, 800);
        static double Lerp(double a, double b, double t) => a + (b - a) * t;
        (double Left, double Top, double Right) Interpolate(IEnumerable<(long At, double L, double T, double R)> keys)
        {
            var list = keys.ToList();
            var after = list.FindIndex(x => x.At >= atMs);
            if (after <= 0) return after == 0 ? (list[0].L, list[0].T, list[0].R) : (list[^1].L, list[^1].T, list[^1].R);
            var (a, b) = (list[after - 1], list[after]);
            var t = (atMs - a.At) / (double)(b.At - a.At);
            return (Lerp(a.L, b.L, t), Lerp(a.T, b.T, t), Lerp(a.R, b.R, t));
        }
        if (clip.TryGetProperty("NativeKeys", out var native) && native.ValueKind == JsonValueKind.Array)
        {
            var (left, top, right) = Interpolate(native.EnumerateArray().Select(k =>
            {
                var f = k.GetProperty("Frame");
                return (k.GetProperty("AtMs").GetInt64(), f.GetProperty("Left").GetDouble(), f.GetProperty("Top").GetDouble(), f.GetProperty("Right").GetDouble());
            }));
            var scale = 1280 / (right - left);
            (double X, double Y)[] corners = [(source.Left, source.Top), (source.Right, source.Bottom)];
            var points = corners.Select(c => ((c.X - left) * scale, (c.Y - top) * scale)).ToArray();
            return [Math.Max(0, (int)Math.Round(points[0].Item1)), Math.Max(0, (int)Math.Round(points[0].Item2)),
                Math.Min(1280, (int)Math.Round(points[1].Item1)), Math.Min(720, (int)Math.Round(points[1].Item2))];
        }
        var box = TestMedia.BoundingBox(TestMedia.Frame(clip.GetProperty("ImportPath").GetString()!), Red)!;
        var keysJson = clip.GetProperty("CameraKeys");
        var (wx, wy, wr) = keysJson.ValueKind == JsonValueKind.Array
            ? Interpolate(keysJson.EnumerateArray().Select(k => (k.GetProperty("AtMs").GetInt64(), k.GetProperty("X").GetDouble(),
                k.GetProperty("Y").GetDouble(), k.GetProperty("X").GetDouble() + k.GetProperty("Width").GetDouble())))
            : (0, 0, 1280);
        var zoom = 1280 / (wr - wx);
        return [Math.Max(0, (int)Math.Round((box[0] - wx) * zoom)), Math.Max(0, (int)Math.Round((box[1] - wy) * zoom)),
            Math.Min(1280, (int)Math.Round((box[2] - wx) * zoom)), Math.Min(720, (int)Math.Round((box[3] - wy) * zoom))];
    }
}
