using System.Globalization;
using LoquendoAI.Core.Models;

namespace LoquendoAI.Infrastructure.Composition;

/// <summary>What the geometry needs to know about a visual, from the preview (SceneMedia) or the
/// VEGAS export (VegasBridge.Clip). Sizes and offsets are in the 1280×720 composition.</summary>
public sealed record LayerSpec(ScriptBlockKind Kind, string VideoLayer, string Position, int MaxWidth, int MaxHeight,
    int OffsetX, int OffsetY, double RotationDegrees, int MotionOffsetX, int MotionOffsetY, double MotionRotationDegrees,
    string VisualCrop)
{
    public static LayerSpec From(SceneMedia media) => new(media.Kind, media.VideoLayer, media.Position,
        media.VisualMaxWidth, media.VisualMaxHeight, media.VisualOffsetX, media.VisualOffsetY, media.RotationDegrees,
        media.MotionOffsetX, media.MotionOffsetY, media.MotionRotationDegrees, media.VisualCrop);

    public static LayerSpec From(VegasBridge.Clip clip) => new(Enum.Parse<ScriptBlockKind>(clip.Kind), clip.VideoLayer,
        clip.Position, clip.MaxWidth, clip.MaxHeight, clip.OffsetX, clip.OffsetY, clip.RotationDegrees,
        clip.MotionOffsetX, clip.MotionOffsetY, clip.MotionRotationDegrees, clip.VisualCrop);

    /// <summary>Backgrounds and fixed-background videos cover their box; everything else fits inside it.</summary>
    public bool Fills => Kind == ScriptBlockKind.Background || Kind == ScriptBlockKind.Video && VideoLayer == "fondo";

    public bool Moves => MotionOffsetX != 0 || MotionOffsetY != 0 || Math.Abs(MotionRotationDegrees) >= 0.0001;
}

/// <summary>
/// Where a visual lands in the frame. X/Y/Width/Height is the displayed (cropped, scaled) image before
/// rotation; CenterX/CenterY is also the rotation and motion pivot. For covering layers, BoxWidth/BoxHeight
/// is the box the image is cut to (the image covers it, enlarged by the rotation overscan when it rotates). Scale = output px per
/// source px. SourceX/Y/Width/Height is the part of the source file that is shown (after trimming).
/// </summary>
public sealed record LayerLayout(double X, double Y, double Width, double Height, double BoxWidth, double BoxHeight,
    double Scale, double SourceX, double SourceY, double SourceWidth, double SourceHeight, bool Fill)
{
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
    public double BoxLeft => CenterX - BoxWidth / 2;
    public double BoxTop => CenterY - BoxHeight / 2;
}

/// <summary>
/// The single definition of layer placement, used by the MP4 preview, the VEGAS native Pan/Crop and the
/// VEGAS normalized media. Before 1.0.0-beta.4 each one computed positions on its own, which is where the
/// differences between the preview and VEGAS came from (rotation pivot, box cropping, overscan).
/// </summary>
public static class LayerGeometry
{
    /// <summary>Fractions of the source kept by a VisualCrop filter ("crop=iw*W:ih*H:iw*X:ih*Y,").</summary>
    public static (double Width, double Height, double Left, double Top)? CropFractions(string visualCrop)
    {
        if (string.IsNullOrEmpty(visualCrop)) return (1, 1, 0, 0);
        var parts = visualCrop.TrimEnd(',').Split(':');
        var prefixes = new[] { "crop=iw*", "ih*", "iw*", "ih*" };
        if (parts.Length != 4) return null;
        var values = new double[4];
        for (var i = 0; i < 4; i++)
            if (!parts[i].StartsWith(prefixes[i], StringComparison.Ordinal) ||
                !double.TryParse(parts[i][prefixes[i].Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                return null;
        if (values[0] <= 0 || values[1] <= 0 || values[2] < 0 || values[3] < 0 ||
            values[0] + values[2] > 1.001 || values[1] + values[3] > 1.001) return null;
        return (values[0], values[1], values[2], values[3]);
    }

    /// <summary>Scale that keeps a width×height box covered at every angle between rotation and
    /// rotation + motion (checked every 3°), so rotating backgrounds never show empty corners.</summary>
    public static double RotationOverscan(double width, double height, double rotationDegrees, double motionRotationDegrees)
    {
        if (Math.Abs(rotationDegrees) < 0.0001 && Math.Abs(motionRotationDegrees) < 0.0001) return 1;
        var factor = 1d;
        var steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(motionRotationDegrees) / 3));
        for (var i = 0; i <= steps; i++)
        {
            var angle = (rotationDegrees + motionRotationDegrees * i / steps) * Math.PI / 180;
            var cosine = Math.Abs(Math.Cos(angle));
            var sine = Math.Abs(Math.Sin(angle));
            factor = Math.Max(factor, Math.Max(cosine + height / width * sine, cosine + width / height * sine) + 0.01);
        }
        return factor;
    }

    /// <summary>Box a covering layer fills, in output pixels, before rotation overscan: its size plus
    /// room for its offset and motion when it moves (so no edge is revealed while moving).</summary>
    public static (double Width, double Height) FillBox(LayerSpec layer, int canvasWidth, int canvasHeight)
    {
        var factor = canvasHeight / 720d;
        var background = layer.Kind == ScriptBlockKind.Background;
        double width = background ? layer.MaxWidth * factor : canvasWidth;
        double height = background ? layer.MaxHeight * factor : canvasHeight;
        if (layer.Moves)
        {
            width = FillExtent(width, canvasWidth, layer.OffsetX * factor, layer.MotionOffsetX * factor, 8 * factor, background);
            height = FillExtent(height, canvasHeight, layer.OffsetY * factor, layer.MotionOffsetY * factor, 8 * factor, background);
        }
        return (width, height);
    }

    /// <summary>
    /// One side of the box of a MOVING covering layer (the preview and VEGAS use this same rule). Classic: its size plus
    /// room for the offset and the whole motion on both sides. A background larger than the frame (zoom, 1.4.0) only
    /// grows when its travel really needs it: the box must still cover the frame at the start (offset) and at the end
    /// (offset + motion). So a still background and a moving one with the same zoom keep the same scale, and the
    /// background does not «jump» when it starts to move.
    /// </summary>
    public static double FillExtent(double size, double canvas, double offset, double motion, double margin, bool background)
    {
        if (background && size > canvas + 0.5)
            return Math.Max(size, canvas + 2 * (Math.Max(Math.Abs(offset), Math.Abs(offset + motion)) + margin));
        return size + 2 * ((background ? Math.Abs(offset) : 0) + Math.Abs(motion) + margin);
    }

    /// <summary>Smallest background zoom (size ÷ frame) that lets a background with this offset and motion keep its
    /// scale while it moves: what the Editor suggests when the chosen zoom falls short.</summary>
    public static double ZoomToCover(int offsetX, int offsetY, int motionX, int motionY) => Math.Max(
        1 + 2 * (Math.Max(Math.Abs(offsetX), Math.Abs(offsetX + motionX)) + 8) / 1280d,
        1 + 2 * (Math.Max(Math.Abs(offsetY), Math.Abs(offsetY + motionY)) + 8) / 720d);

    /// <summary>Placement of <paramref name="layer"/> for a source of sourceWidth×sourceHeight pixels in a
    /// canvasWidth×canvasHeight frame. Null when its crop cannot be read.</summary>
    public static LayerLayout? Place(LayerSpec layer, int sourceWidth, int sourceHeight, int canvasWidth, int canvasHeight)
    {
        if (CropFractions(layer.VisualCrop) is not { } crop || sourceWidth <= 0 || sourceHeight <= 0) return null;
        var factor = canvasHeight / 720d;
        var sourceX = crop.Left * sourceWidth;
        var sourceY = crop.Top * sourceHeight;
        var sourceW = crop.Width * sourceWidth;
        var sourceH = crop.Height * sourceHeight;
        if (layer.Fills)
        {
            // The image covers the box enlarged by the rotation overscan, is rotated and is then cut to the box.
            var (boxWidth, boxHeight) = FillBox(layer, canvasWidth, canvasHeight);
            var overscan = RotationOverscan(boxWidth, boxHeight, layer.RotationDegrees, layer.MotionRotationDegrees);
            var cover = Math.Max(boxWidth * overscan / sourceW, boxHeight * overscan / sourceH);
            var width = sourceW * cover;
            var height = sourceH * cover;
            var background = layer.Kind == ScriptBlockKind.Background;
            var centerX = canvasWidth / 2d + (background ? layer.OffsetX * factor : 0);
            var centerY = canvasHeight / 2d + (background ? layer.OffsetY * factor : 0);
            return new LayerLayout(centerX - width / 2, centerY - height / 2, width, height, boxWidth, boxHeight,
                cover, sourceX, sourceY, sourceW, sourceH, true);
        }
        var fit = Math.Min(layer.MaxWidth * factor / sourceW, layer.MaxHeight * factor / sourceH);
        var displayedW = Math.Max(1, Math.Round(sourceW * fit));
        var displayedH = Math.Max(1, Math.Round(sourceH * fit));
        var scale = displayedW / sourceW;
        var margin = BlockDefaults.CharacterMargin * factor;
        var x = layer.Position switch
        {
            "izquierda" => margin,
            "derecha" => canvasWidth - displayedW - margin,
            _ => (canvasWidth - displayedW) / 2
        };
        if (layer.Position.StartsWith("slot:", StringComparison.Ordinal))
        {
            var parts = layer.Position.Split(':');
            if (parts.Length == 3 && int.TryParse(parts[1], out var slot) && int.TryParse(parts[2], out var lanes) &&
                lanes > 0 && slot >= 0 && slot < lanes)
                x = canvasWidth * (slot + .5) / lanes - displayedW / 2;
        }
        var y = canvasHeight - displayedH - (layer.Kind == ScriptBlockKind.Image ? BlockDefaults.ImageBottomMargin * factor : 0);
        x += layer.OffsetX * factor;
        y += layer.OffsetY * factor;
        return new LayerLayout(x, y, displayedW, displayedH, displayedW, displayedH, scale, sourceX, sourceY, sourceW, sourceH, false);
    }

    /// <summary>True when a covering layer's box leaves part of the frame empty or cuts the image inside
    /// the frame: VEGAS Pan/Crop cannot cut to a box, so such a layer is exported as normalized media.</summary>
    public static bool NeedsBoxCrop(LayerLayout layout, int canvasWidth, int canvasHeight)
    {
        if (!layout.Fill) return false;
        const double tolerance = 1;
        var cutsWidth = layout.Width > layout.BoxWidth + tolerance && layout.BoxWidth < canvasWidth - tolerance;
        var cutsHeight = layout.Height > layout.BoxHeight + tolerance && layout.BoxHeight < canvasHeight - tolerance;
        return cutsWidth || cutsHeight;
    }

    internal static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
