using System.Text.Json;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;
using LoquendoAI.Infrastructure.Director;

namespace LoquendoAI.Tests;

/// <summary>
/// One geometry for the preview and VEGAS (LayerGeometry). The pure tests check the placement
/// rules; the render test draws each case in the preview and exports it to VEGAS (native Pan/Crop
/// and normalized media) and compares where the layer lands.
/// </summary>
internal static class GeometryTests
{
    private static LayerSpec Spec(ScriptBlockKind kind, string position, int maxWidth, int maxHeight, int offsetX = 0, int offsetY = 0,
        double rotation = 0, int moveX = 0, string crop = "") =>
        new(kind, "guion", position, maxWidth, maxHeight, offsetX, offsetY, rotation, moveX, 0, 0, crop);

    [Test("Personaje a la izquierda: margen de 80 px, apoyado abajo")]
    public static void CharacterLeft()
    {
        var layout = LayerGeometry.Place(Spec(ScriptBlockKind.CharacterShow, "izquierda", 1280, 610), 400, 800, 1280, 720)!;
        Assert.Equal((80d, 110d, 305d, 610d), (layout.X, layout.Y, layout.Width, layout.Height), "caja");
        var right = LayerGeometry.Place(Spec(ScriptBlockKind.CharacterShow, "derecha", 1280, 610, 40, -30), 400, 800, 1280, 720)!;
        Assert.Equal((1280 - 80 - 305 + 40d, 110 - 30d), (right.X, right.Y), "derecha con desplazamiento");
        var slot = LayerGeometry.Place(Spec(ScriptBlockKind.CharacterShow, "slot:1:2", 1280, 610), 400, 800, 1280, 720)!;
        Assert.Equal(960d, slot.CenterX, "segundo de dos carriles");
    }

    [Test("Imagen: margen inferior de 30 px y escala a 1080p proporcional")]
    public static void ImageAndScale()
    {
        var image = LayerGeometry.Place(Spec(ScriptBlockKind.Image, "centro", 500, 400), 300, 200, 1280, 720)!;
        Assert.Equal((500d, 333d), (image.Width, image.Height), "tamaño redondeado");
        Assert.Equal(720 - 333 - 30d, image.Y, "margen inferior");
        var hd = LayerGeometry.Place(Spec(ScriptBlockKind.Image, "centro", 500, 400), 300, 200, 1920, 1080)!;
        Assert.Near(image.Width * 1.5, hd.Width, 1, "ancho a 1080p");
        Assert.Near(image.Y * 1.5, hd.Y, 1.5, "posición a 1080p");
    }

    [Test("Fondo en caja menor que el cuadro: se recorta a la caja (Pan/Crop no puede)")]
    public static void BackgroundBox()
    {
        var layout = LayerGeometry.Place(Spec(ScriptBlockKind.Background, "centro", 800, 600), 1600, 1000, 1280, 720)!;
        Assert.Equal((800d, 600d), (layout.BoxWidth, layout.BoxHeight), "caja");
        Assert.Equal((960d, 600d), (layout.Width, layout.Height), "cubre la caja");
        Assert.True(LayerGeometry.NeedsBoxCrop(layout, 1280, 720), "necesita recorte a la caja");
        var full = LayerGeometry.Place(Spec(ScriptBlockKind.Background, "centro", 1280, 720), 1600, 1000, 1280, 720)!;
        Assert.False(LayerGeometry.NeedsBoxCrop(full, 1280, 720), "a pantalla completa basta Pan/Crop");
    }

    [Test("Fondo girado: el sobredimensionado cubre las esquinas en todo el giro")]
    public static void RotationOverscan()
    {
        Assert.Equal(1d, LayerGeometry.RotationOverscan(1280, 720, 0, 0), "sin giro");
        foreach (var angle in new[] { 5d, 10, 30, 45, 90 })
        {
            var factor = LayerGeometry.RotationOverscan(1280, 720, angle, 0);
            var radians = angle * Math.PI / 180;
            var needed = Math.Max(Math.Abs(Math.Cos(radians)) + 720d / 1280 * Math.Abs(Math.Sin(radians)),
                Math.Abs(Math.Cos(radians)) + 1280d / 720 * Math.Abs(Math.Sin(radians)));
            Assert.True(factor >= needed, $"{angle}°: {factor} < {needed}");
        }
        Assert.True(LayerGeometry.RotationOverscan(1280, 720, 0, 40) >= LayerGeometry.RotationOverscan(1280, 720, 40, 0),
            "un giro animado cubre también el ángulo final");
    }

    [Test("Recorte de bordes: se lee el filtro crop y se rechaza uno dañado")]
    public static void CropFractions()
    {
        Assert.Equal((1d, 1d, 0d, 0d), LayerGeometry.CropFractions("")!.Value, "sin recorte");
        Assert.Equal((0.8, 1d, 0.1, 0d), LayerGeometry.CropFractions("crop=iw*0.8:ih*1:iw*0.1:ih*0,")!.Value, "recorte");
        Assert.Equal(null, LayerGeometry.CropFractions("crop=100:100:0:0,"), "formato desconocido");
        Assert.Equal(null, LayerGeometry.CropFractions("crop=iw*0.9:ih*1:iw*0.5:ih*0,"), "fuera de la imagen");
    }

    // ---------- preview vs VEGAS, rendered ----------

    private static bool Red(byte r, byte g, byte b) => r > 150 && g < 90 && b < 90;
    private static bool Blue(byte r, byte g, byte b) => b > 150 && r < 90 && g < 90;
    private static bool Green(byte r, byte g, byte b) => g > 100 && r < 90 && b < 90;
    private static bool Yellow(byte r, byte g, byte b) => r > 170 && g > 170 && b < 90;

    [Test("Preview y VEGAS (Pan/Crop y medio normalizado) colocan cada capa en el mismo sitio (±2 px)")]
    public static async Task PreviewMatchesVegas()
    {
        TestMedia.RequireFfmpeg();
        using var folder = new TempFolder();
        var character = TestMedia.SolidPng(folder.File("media/pj.png"), 400, 800, (220, 20, 20));
        var prop = TestMedia.SolidPng(folder.File("media/prop.png"), 300, 200, (20, 20, 220));
        var background = TestMedia.Png(folder.File("media/fondo.png"), 1600, 1000,
            (x, y) => x is >= 750 and < 850 && y is >= 450 and < 550 ? ((byte)230, (byte)230, (byte)20, (byte)255) : ((byte)20, (byte)160, (byte)20, (byte)255));
        SceneMedia Character(string position, int offsetX = 0, int offsetY = 0, double rotation = 0) =>
            new(Guid.NewGuid(), ScriptBlockKind.CharacterShow, 0, 1000, character, position,
                VisualMaxWidth: 1280, VisualMaxHeight: 610, VisualOffsetX: offsetX, VisualOffsetY: offsetY, RotationDegrees: rotation);
        var cases = new (string Name, SceneMedia Media, Func<byte, byte, byte, bool> Color, (int X, int Y)[] Corners)[]
        {
            ("pj_izquierda", Character("izquierda"), Red, Box(400, 800)),
            ("pj_derecha_offset", Character("derecha", 40, -30), Red, Box(400, 800)),
            ("pj_centro_giro20", Character("centro", rotation: 20), Red, Box(400, 800)),
            ("pj_izquierda_giro25", Character("izquierda", rotation: 25), Red, Box(400, 800)),
            ("prop_giro-15_offset", new(Guid.NewGuid(), ScriptBlockKind.Image, 0, 1000, prop, "centro",
                VisualMaxWidth: 500, VisualMaxHeight: 400, VisualOffsetX: 100, RotationDegrees: -15), Blue, Box(300, 200)),
            ("fondo_caja_800x600", new(Guid.NewGuid(), ScriptBlockKind.Background, 0, 1000, background,
                AutoTrimBorders: false, VisualMaxWidth: 800, VisualMaxHeight: 600), Green, Box(1600, 1000)),
            ("fondo_giro10_offset", new(Guid.NewGuid(), ScriptBlockKind.Background, 0, 1000, background, AutoTrimBorders: false,
                VisualMaxWidth: 1280, VisualMaxHeight: 720, VisualOffsetX: 50, VisualOffsetY: 20, RotationDegrees: 10), Yellow,
                [(750, 450), (850, 450), (850, 550), (750, 550)]),
            // 1.4.0: background zoom (larger than the frame) moved to one side: still covers the frame.
            ("fondo_zoom150_offset", new(Guid.NewGuid(), ScriptBlockKind.Background, 0, 1000, background, AutoTrimBorders: false,
                VisualMaxWidth: 1920, VisualMaxHeight: 1080, VisualOffsetX: -120, VisualOffsetY: 60), Yellow,
                [(750, 450), (850, 450), (850, 550), (750, 550)])
        };
        var problems = new List<string>();
        foreach (var (name, media, color, corners) in cases)
        {
            var scene = new SceneComposition(1000, [media]);
            var preview = folder.File($"out/{name}.mp4");
            await SceneComposer.RenderAsync(scene, preview);
            var expected = TestMedia.BoundingBox(TestMedia.Frame(preview, 0.4), color)
                ?? throw new AssertionException($"{name}: la capa no aparece en la preview");
            foreach (var mode in new[] { VegasExportMode.Normal, VegasExportMode.Legacy })
            {
                var export = folder.File($"out/{name}_{mode}");
                await VegasBridge.ExportAsync(scene, export, name, 720, mode: mode);
                using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(export, "escena.json")));
                var clip = manifest.RootElement.GetProperty("Clips")[0];
                var actual = clip.TryGetProperty("Native", out var native) && native.ValueKind == JsonValueKind.Object
                    ? NativeBox(native, clip.GetProperty("RotationDegrees").GetDouble(), corners)
                    : TestMedia.BoundingBox(TestMedia.Frame(clip.GetProperty("ImportPath").GetString()!), color);
                var difference = actual is null ? int.MaxValue : expected.Zip(actual, (a, b) => Math.Abs(a - b)).Max();
                if (difference > 2)
                    problems.Add($"{name} {mode}: preview [{string.Join(",", expected)}] VEGAS [{string.Join(",", actual ?? [])}] ({difference} px)");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Test("Invertir en VEGAS (Pan/Crop nativo): el render se voltea en su sitio, como en la preview")]
    public static async Task FlipInPlaceMatchesPreview()
    {
        TestMedia.RequireFfmpeg();
        using var folder = new TempFolder();
        // Left half red, right half blue: the red half says which way the render faces.
        var render = TestMedia.Png(folder.File("media/bart.png"), 400, 800,
            (x, _) => x < 200 ? ((byte)220, (byte)20, (byte)20, (byte)255) : ((byte)20, (byte)20, (byte)220, (byte)255));
        var bart = Guid.NewGuid();
        SceneMedia Character(string position, bool h, bool v = false, double rotation = 0, int offsetX = 0) =>
            new(Guid.NewGuid(), ScriptBlockKind.CharacterShow, 0, 1000, render, position, bart, VisualMaxWidth: 1280, VisualMaxHeight: 610,
                VisualOffsetX: offsetX, FlipHorizontal: h, FlipVertical: v, RotationDegrees: rotation);
        // Tilted towards its right from 480 ms: at 640 ms the tilt peaks (a flipped render keeps the screen direction).
        var sway = new GestureCue(480, new GestureSettings("personaje", GestureSettings.ParseSteps("balanceo"), 8, "derecha", 0, 1, "pies"), bart);
        var cases = new (string Name, SceneMedia Media, GestureCue? Gesture, long[] Times)[]
        {
            ("sin_invertir_giro20", Character("izquierda", false, rotation: 20), null, [400]),
            ("izquierda_invertida", Character("izquierda", true), null, [400]),
            ("izquierda_invertida_giro20", Character("izquierda", true, rotation: 20), null, [400]),
            ("derecha_invertida_hv_offset", Character("derecha", true, true, offsetX: 40), null, [400]),
            ("izquierda_invertida_con_gesto", Character("izquierda", true), sway, [200, 640]),
        };
        var problems = new List<string>();
        foreach (var (name, media, gesture, times) in cases)
        {
            var scene = new SceneComposition(1000, [media], GestureCues: gesture is null ? null : [gesture]);
            var preview = folder.File($"out/{name}.mp4");
            await SceneComposer.RenderAsync(scene, preview);
            foreach (var mode in new[] { VegasExportMode.Normal, VegasExportMode.Legacy })
            {
                var export = folder.File($"out/{name}_{mode}");
                await VegasBridge.ExportAsync(scene, export, name, 720, mode: mode);
                using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(export, "escena.json")));
                var clip = manifest.RootElement.GetProperty("Clips")[0];
                var native = clip.TryGetProperty("Native", out var frame) && frame.ValueKind == JsonValueKind.Object;
                var script = await File.ReadAllTextAsync(Path.Combine(export, "Abrir_en_VEGAS_14_o_superior.cs"));
                foreach (var t in times)
                {
                    var expected = TestMedia.BoundingBox(TestMedia.Frame(preview, t / 1000d), Red)
                        ?? throw new AssertionException($"{name}: el render no aparece en la preview");
                    // Normalized media has the flip baked in: its own canvas is checked (at rest).
                    if (!native && t != times[0]) continue;
                    var actual = native ? ScriptBox(script, [(0, 0), (200, 0), (200, 800), (0, 800)], t)
                        : TestMedia.BoundingBox(TestMedia.Frame(clip.GetProperty("ImportPath").GetString()!), Red);
                    var difference = actual is null ? int.MaxValue : expected.Zip(actual, (a, b) => Math.Abs(a - b)).Max();
                    if (difference > 3)
                        problems.Add($"{name} {mode} {t} ms: preview [{string.Join(",", expected)}] VEGAS [{string.Join(",", actual ?? [])}] ({difference} px)");
                }
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Test("Cambiar dirección mira al otro lado en su sitio; Invertir horizontal refleja la capa al otro lado (preview = VEGAS)")]
    public static async Task ChangeDirectionAndMirror()
    {
        TestMedia.RequireFfmpeg();
        static DirectorSpec One(string line) => DirectorScript.ParseDirectorPrompt(line).Single();
        var turned = BlockParameters.Parse(DirectorScript.DirectorParameters(One("[MOSTRAR] Bart | izquierda | cambiar direccion=si")));
        Assert.Equal((true, false), (turned.ChangeDirection == true, turned.FlipHorizontal == true), "Director: cambiar direccion");
        Assert.True(BlockParameters.Parse(DirectorScript.DirectorParameters(One("[MOSTRAR] Bart | derecha | dar la vuelta=si"))).ChangeDirection == true,
            "Director: alias «dar la vuelta»");
        Assert.True(BlockParameters.Parse(turned.ToJson()).ChangeDirection == true, "se guarda con el bloque");

        using var folder = new TempFolder();
        var render = TestMedia.Png(folder.File("media/bart.png"), 400, 800,
            (x, _) => x < 200 ? ((byte)220, (byte)20, (byte)20, (byte)255) : ((byte)20, (byte)20, (byte)220, (byte)255));
        // Displayed 305×610 at the left margin (80): the red half is 80..232, the blue half 232..385.
        var cases = new (string Name, bool Mirror, bool Turn, int RedLeft, int RedRight)[]
        {
            ("tal_cual", false, false, 80, 232),
            ("cambiar_direccion", false, true, 232, 385),       // same place, looks the other way
            ("invertir_horizontal", true, false, 1048, 1200),   // mirrored: right side, red half on the outside
            ("invertir_y_cambiar", true, true, 895, 1048),      // right side, looking as before
        };
        var problems = new List<string>();
        foreach (var (name, mirror, turn, redLeft, redRight) in cases)
        {
            var parameters = new BlockParameters
            {
                Position = "izquierda", FramingPreset = "original", VisualMaxWidth = 1280, VisualMaxHeight = 610,
                VisualOffsetX = 30, FlipHorizontal = mirror, ChangeDirection = turn
            };
            var blocks = new[]
            {
                new SceneScriptBlock(Guid.NewGuid(), Guid.Empty, 0, ScriptBlockKind.CharacterShow, Guid.NewGuid(), ParametersJson: parameters.ToJson()),
                new SceneScriptBlock(Guid.NewGuid(), Guid.Empty, 1, ScriptBlockKind.Pause, PauseAfterMs: 1000)
            };
            var scene = await SceneComposer.PlanAsync(blocks, x => x.Kind == ScriptBlockKind.CharacterShow ? render : null);
            var preview = folder.File($"out/{name}.mp4");
            await SceneComposer.RenderAsync(scene, preview);
            var box = TestMedia.BoundingBox(TestMedia.Frame(preview, 0.4), Red)
                ?? throw new AssertionException($"{name}: el render no aparece en la preview");
            // x=30 moves the layer right; mirrored, it moves it left.
            var shift = mirror ? -30 : 30;
            if (Math.Abs(box[0] - (redLeft + shift)) > 3 || Math.Abs(box[2] - (redRight + shift)) > 3)
                problems.Add($"{name} preview: rojo en {box[0]}..{box[2]}, se esperaba {redLeft + shift}..{redRight + shift}");
            var export = folder.File($"out/{name}_vegas");
            await VegasBridge.ExportAsync(scene, export, name, 720);
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(export, "escena.json")));
            var clip = manifest.RootElement.GetProperty("Clips")[0];
            var actual = clip.TryGetProperty("Native", out var native) && native.ValueKind == JsonValueKind.Object
                ? ScriptBox(await File.ReadAllTextAsync(Path.Combine(export, "Abrir_en_VEGAS_14_o_superior.cs")), [(0, 0), (200, 0), (200, 800), (0, 800)])
                : TestMedia.BoundingBox(TestMedia.Frame(clip.GetProperty("ImportPath").GetString()!), Red);
            if (actual is null || box.Zip(actual, (a, b) => Math.Abs(a - b)).Max() > 3)
                problems.Add($"{name} VEGAS: [{string.Join(",", actual ?? [])}] preview [{string.Join(",", box)}]");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Simulates VEGAS on a Pan/Crop keyframe of the script (the first one, or the one at atMs): the Bounds quad, turned by RotateBy
    /// around Center, is what the whole 1280×720 frame shows; returns where the source corners land.</summary>
    private static int[] ScriptBox(string script, (double X, double Y)[] corners, long atMs = 0)
    {
        // The keyframe at atMs (key0 is the event start; the others are written with their time).
        var marker = $"Timecode.FromMilliseconds({atMs}))";
        if (script.IndexOf(marker, StringComparison.Ordinal) is var at and >= 0) script = script[at..];
        static double[] Numbers(string text) => System.Text.RegularExpressions.Regex.Matches(text, @"-?[0-9]+(\.[0-9]+)?(E-?[0-9]+)?")
            .Select(m => double.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var bounds = System.Text.RegularExpressions.Regex.Match(script, @"Bounds = new VideoMotionBounds\(([^)]*)\)");
        var rest = script[(bounds.Index + bounds.Length)..];
        var next = rest.IndexOf("Bounds = ", StringComparison.Ordinal);
        var block = next < 0 ? rest : rest[..next];
        var b = Numbers(bounds.Groups[1].Value);
        var c = Numbers(System.Text.RegularExpressions.Regex.Match(block, @"Center = new VideoMotionVertex\(([^)]*)\)").Groups[1].Value);
        var turn = System.Text.RegularExpressions.Regex.Match(block, @"RotateBy\(([^)]*)\)") is { Success: true } r
            ? Numbers(r.Groups[1].Value)[0] : 0;
        (double X, double Y) Turn(double x, double y) => (c[0] + (x - c[0]) * Math.Cos(turn) - (y - c[1]) * Math.Sin(turn),
            c[1] + (x - c[0]) * Math.Sin(turn) + (y - c[1]) * Math.Cos(turn));
        var tl = Turn(b[0], b[1]);
        var tr = Turn(b[2], b[3]);
        var bl = Turn(b[6], b[7]);
        // Source point = tl + u·(tr − tl) + v·(bl − tl); output = (u·1280, v·720).
        var (ax, ay, bx, by) = (tr.X - tl.X, tr.Y - tl.Y, bl.X - tl.X, bl.Y - tl.Y);
        var det = ax * by - ay * bx;
        var points = corners.Select(p =>
        {
            var (dx, dy) = (p.X - tl.X, p.Y - tl.Y);
            return ((dx * by - dy * bx) / det * 1280, (ax * dy - ay * dx) / det * 720);
        }).ToArray();
        return
        [
            Math.Max(0, (int)Math.Round(points.Min(p => p.Item1))), Math.Max(0, (int)Math.Round(points.Min(p => p.Item2))),
            Math.Min(1280, (int)Math.Round(points.Max(p => p.Item1))), Math.Min(720, (int)Math.Round(points.Max(p => p.Item2)))
        ];
    }

    private static (int, int)[] Box(int width, int height) => [(0, 0), (width, 0), (width, height), (0, height)];

    /// <summary>Where VEGAS draws the source corners with this Pan/Crop keyframe (the frame is the
    /// source rectangle Left/Top/Right/Bottom; rotation turns the source around the pivot).</summary>
    private static int[] NativeBox(JsonElement native, double rotationDegrees, (int X, int Y)[] corners)
    {
        double Get(string name) => native.GetProperty(name).GetDouble();
        double Pivot(string name, string fallback) =>
            native.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : Get(fallback);
        double left = Get("Left"), top = Get("Top"), right = Get("Right");
        double pivotX = Pivot("PivotX", "CenterX"), pivotY = Pivot("PivotY", "CenterY");
        var angle = rotationDegrees * Math.PI / 180;
        var scale = 1280 / (right - left);
        var points = corners.Select(c =>
        {
            var x = pivotX + (c.X - pivotX) * Math.Cos(angle) - (c.Y - pivotY) * Math.Sin(angle);
            var y = pivotY + (c.X - pivotX) * Math.Sin(angle) + (c.Y - pivotY) * Math.Cos(angle);
            return ((x - left) * scale, (y - top) * scale);
        }).ToArray();
        return
        [
            Math.Max(0, (int)Math.Round(points.Min(p => p.Item1))), Math.Max(0, (int)Math.Round(points.Min(p => p.Item2))),
            Math.Min(1280, (int)Math.Round(points.Max(p => p.Item1))), Math.Min(720, (int)Math.Round(points.Max(p => p.Item2)))
        ];
    }
}
