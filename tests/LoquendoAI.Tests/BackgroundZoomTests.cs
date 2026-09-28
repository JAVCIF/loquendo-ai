using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;
using LoquendoAI.Infrastructure.Director;

namespace LoquendoAI.Tests;

/// <summary>Background zoom (1.4.0): a background larger than the frame can pan without edges and without a jump.</summary>
internal static class BackgroundZoomTests
{
    private static LayerSpec Background(int width, int height, int offsetX = 0, int moveX = 0, int offsetY = 0, int moveY = 0) =>
        new(ScriptBlockKind.Background, "guion", "centro", width, height, offsetX, offsetY, 0, moveX, moveY, 0, "");

    [Test("Fondo con zoom: quieto y en movimiento tienen la misma escala (no salta al empezar a moverse)")]
    public static void SameScale()
    {
        var still = LayerGeometry.Place(Background(1920, 1080), 1920, 1080, 1280, 720)!;
        var moving = LayerGeometry.Place(Background(1920, 1080, moveX: -300), 1920, 1080, 1280, 720)!;
        Assert.Equal(still.Scale, moving.Scale, "misma escala con zoom 150 % y 300 px de paneo");
        Assert.Equal((1920d, 1080d), (moving.BoxWidth, moving.BoxHeight), "la caja no crece: el zoom ya cubre el recorrido");
        Assert.False(LayerGeometry.NeedsBoxCrop(moving, 1280, 720), "cubre el cuadro: Pan/Crop nativo en VEGAS");
        // At the end of the pan the frame is still covered: box half-width ≥ 640 + |offset + motion|.
        Assert.True(moving.BoxWidth / 2 >= 640 + 300, "sin bordes negros al final del paneo");
    }

    [Test("Fondo con zoom insuficiente: crece solo lo necesario; sin zoom se comporta como antes")]
    public static void GrowsOnlyWhenNeeded()
    {
        var box = LayerGeometry.FillBox(Background(1400, 788, moveX: -400), 1280, 720);
        Assert.Equal(1280 + 2 * (400 + 8d), box.Width, "crece hasta cubrir el recorrido");
        Assert.Equal(788d, box.Height, "el alto ya cubría");
        var classic = LayerGeometry.FillBox(Background(1280, 720, offsetX: 50, moveX: -200), 1280, 720);
        Assert.Equal((1280 + 2 * (50 + 200 + 8d), 720 + 2 * 8d), classic, "sin zoom: la regla de siempre");
        Assert.True(LayerGeometry.ZoomToCover(0, 0, -300, 0) <= 1.5, "300 px caben en 150 %");
        Assert.Near(1 + 2 * (400 + 8) / 1280d, LayerGeometry.ZoomToCover(100, 0, -500, 0), 0.0001, "zoom sugerido con desplazamiento");
    }

    [Test("Tamaño del fondo hasta 3840×2160; los demás visuales se limitan al cuadro")]
    public static void Limits()
    {
        var zoomed = BlockParameters.Empty with { VisualMaxWidth = 2560, VisualMaxHeight = 1440 };
        Assert.Equal((2560, 1440), (zoomed.Transform(ScriptBlockKind.Background).MaxWidth, zoomed.Transform(ScriptBlockKind.Background).MaxHeight), "fondo 200 %");
        Assert.Equal((1280, 720), (zoomed.Transform(ScriptBlockKind.CharacterShow).MaxWidth, zoomed.Transform(ScriptBlockKind.CharacterShow).MaxHeight), "render: al cuadro");
        var huge = BlockParameters.Empty with { VisualMaxWidth = 9000, VisualMaxHeight = 30 };
        Assert.Equal((3840, 720), (huge.Transform(ScriptBlockKind.Background).MaxWidth, huge.Transform(ScriptBlockKind.Background).MaxHeight), "tope 300 % y alto inválido = predeterminado");
    }

    [Test("Fondo con zoom en la preview: quieto y luego paneando, el decorado no salta y no aparece negro")]
    public static async Task PreviewContinuity()
    {
        TestMedia.RequireFfmpeg();
        using var folder = new TempFolder();
        static bool Yellow(byte r, byte g, byte b) => r > 170 && g > 170 && b < 90;
        static bool Black(byte r, byte g, byte b) => r < 12 && g < 12 && b < 12;
        var image = TestMedia.Png(folder.File("media/fondo.png"), 1920, 1080,
            (x, y) => x is >= 900 and < 1020 && y is >= 480 and < 600 ? ((byte)230, (byte)230, (byte)20, (byte)255) : ((byte)20, (byte)120, (byte)200, (byte)255));
        var still = new SceneMedia(Guid.NewGuid(), ScriptBlockKind.Background, 0, 1000, image, AutoTrimBorders: false,
            VisualMaxWidth: 1920, VisualMaxHeight: 1080);
        var pan = new SceneMedia(Guid.NewGuid(), ScriptBlockKind.Background, 1000, 1000, image, AutoTrimBorders: false,
            VisualMaxWidth: 1920, VisualMaxHeight: 1080, MotionOffsetX: -300, MotionDurationMs: 1000);
        var output = folder.File("out/paneo.mp4");
        await SceneComposer.RenderAsync(new SceneComposition(2000, [still, pan]), output);
        var before = TestMedia.BoundingBox(TestMedia.Frame(output, 0.9), Yellow) ?? throw new AssertionException("sin marca antes");
        var after = TestMedia.BoundingBox(TestMedia.Frame(output, 1.04), Yellow) ?? throw new AssertionException("sin marca al empezar");
        Assert.Near(before[2] - before[0], after[2] - after[0], 3, "mismo tamaño: sin salto de escala");
        Assert.True(Math.Abs(after[0] - before[0]) <= 20, $"sin salto de posición ({before[0]} → {after[0]})");
        var end = TestMedia.Frame(output, 1.96);
        Assert.True(TestMedia.BoundingBox(end, Black) is null, "sin bordes negros al final del paneo");
    }

    [Test("Director: [FONDO] … | zoom=1.5 da un fondo de 1920×1080 (ancho/alto mandan si están)")]
    public static void DirectorZoom()
    {
        var spec = DirectorScript.ParseDirectorPrompt("[FONDO] ciudad | zoom=1.5 | animar x=-300 | animar ms=3000")[0];
        Assert.Equal(null, spec.Error, "zoom válido en FONDO");
        var parameters = BlockParameters.Parse(DirectorScript.DirectorParameters(spec));
        Assert.Equal((1920, 1080), (parameters.VisualMaxWidth ?? 0, parameters.VisualMaxHeight ?? 0), "tamaño por zoom");
        var explicitSize = BlockParameters.Parse(DirectorScript.DirectorParameters(
            DirectorScript.ParseDirectorPrompt("[FONDO] ciudad | zoom=2 | ancho=2000")[0]));
        Assert.Equal((2000, 1440), (explicitSize.VisualMaxWidth ?? 0, explicitSize.VisualMaxHeight ?? 0), "ancho explícito, alto por zoom");
        Assert.True(DirectorScript.ParseDirectorPrompt("[FONDO] ciudad | zoom=5")[0].Error is not null, "zoom máximo 3");
    }
}
