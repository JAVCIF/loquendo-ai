using System.Text.Json;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;

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
