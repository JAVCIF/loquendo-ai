using System.Diagnostics;
using System.Globalization;
using LoquendoAI.Core.Models;

namespace LoquendoAI.Infrastructure.Composition;

/// <summary>Automatic «primer plano» zoom on a character (see <see cref="CameraPlanner"/>).</summary>
public sealed record CameraShot(long StartMs, long EndMs, double Zoom, double CenterX, double CenterY);

/// <summary>Calculates the same character framing for the MP4 compositor and the VEGAS bridge.
/// The alpha scan only informs a suggestion: the user can always choose a specific framing.</summary>
/// <summary>What a render shows: see <see cref="CharacterFraming.SurfaceAsync"/>. Fractions of the image (0–1).</summary>
public sealed record RenderSurface((double X, double Y, double W, double H, double Aspect) Bounds,
    (double X, double Y, double W, double H) Solid, bool Opaque, string? Background)
{
    private static readonly CultureInfo Fmt = CultureInfo.InvariantCulture;

    public string Serialize() => string.Join(';', new[] { Bounds.X, Bounds.Y, Bounds.W, Bounds.H, Bounds.Aspect, Solid.X, Solid.Y, Solid.W, Solid.H }
        .Select(x => x.ToString("R", Fmt)).Append(Opaque ? "1" : "0").Append(Background ?? ""));

    public static RenderSurface? Parse(string text)
    {
        var parts = text.Split(';');
        if (parts.Length != 11) return null;
        var n = new double[9];
        for (var i = 0; i < 9; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, Fmt, out n[i])) return null;
        return new RenderSurface((n[0], n[1], n[2], n[3], n[4]), (n[5], n[6], n[7], n[8]), parts[9] == "1",
            parts[10].Length == 6 ? parts[10] : null);
    }
}

public static class CharacterFraming
{
    private const int ProbeSize = 256;
    /// <summary>The width of a lane is that of one in this many (more characters share the places, not the size).</summary>
    internal const int MaxWidthLanes = 3;

    public static async Task<SceneComposition> ApplyAsync(SceneComposition scene, CancellationToken token = default)
    {
        var characters = scene.Media.Where(x => x.Kind == ScriptBlockKind.CharacterShow &&
            x.FramingPreset != "original").ToArray();
        var background = scene.Media.Where(x => x.Kind == ScriptBlockKind.Background && x.AutoTrimBorders).ToArray();
        var scaled = scene.Media.Any(x => x.Kind == ScriptBlockKind.CharacterShow && x.Scale != 1);
        if (characters.Length == 0 && background.Length == 0 && !scaled)
            return scene.CameraCues is { Count: > 0 }
                ? scene with { Camera = await CameraPlanner.BuildAsync(scene, [], token) } : scene;

        // Keep each character in the same lane for the entire scene so another entrance
        // does not abruptly move a render while the camera is playing.
        var concurrent = MaximumVisibleCharacters(scene);
        var laneCount = Math.Max(1, concurrent);
        var lanes = AssignLanes(scene, laneCount);
        var measurements = new Dictionary<string, (double X, double Y, double W, double H, double Aspect)>(StringComparer.OrdinalIgnoreCase);
        var focus = new List<(Guid BlockId, double X, double Y, double Zoom)>();
        var borders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var updated = new List<SceneMedia>(scene.Media.Count);
        foreach (var clip in scene.Media)
        {
            token.ThrowIfCancellationRequested();
            if (clip.Kind == ScriptBlockKind.Background && clip.AutoTrimBorders)
            {
                if (!borders.TryGetValue(clip.Path, out var trim))
                    borders[clip.Path] = trim = await BackgroundBorders.CropAsync(clip.Path, token);
                updated.Add(clip with { VisualCrop = trim });
                continue;
            }
            if (clip.Kind != ScriptBlockKind.CharacterShow || clip.FramingPreset == "original")
            {
                // «Escala» with «original»: the block's own box, enlarged or reduced.
                updated.Add(clip.Kind == ScriptBlockKind.CharacterShow && clip.Scale != 1 ? clip with
                {
                    VisualMaxWidth = Math.Clamp((int)Math.Round(clip.VisualMaxWidth * clip.Scale), 16, 1280),
                    VisualMaxHeight = Math.Clamp((int)Math.Round(clip.VisualMaxHeight * clip.Scale), 16, 720)
                } : clip);
                continue;
            }
            if (!measurements.TryGetValue(clip.Path, out var bounds))
            {
                bounds = await VisibleBoundsAsync(clip.Path, token);
                measurements[clip.Path] = bounds;
            }
            var aspect = bounds.Aspect;
            var style = clip.FramingPreset == "auto" ? aspect >= 1.55 ? "entero" : "medio" : clip.FramingPreset;
            // A render that already ends at the shoulders cannot supply a tighter
            // portrait. Keep the whole image and let the shared camera make a small move.
            var height = style switch { "detalle" when aspect < 1.55 => 560,
                "detalle" => 610, "medio" => 560, _ => 670 };
            // Never narrower than a third of the frame (1.4.7): with 4 or 5 on screen (some behind others) a lane of
            // 224–288 px left wide renders tiny; tall renders never reach this width, wide ones may overlap a little.
            var width = laneCount == 1 ? 960 : 1280 / Math.Min(laneCount, MaxWidthLanes) - 32;
            var position = clip.Position;
            if (position == "auto")
            {
                var slot = lanes.GetValueOrDefault(clip.CharacterId ?? clip.BlockId) % laneCount;
                if (clip.MirrorPlacement) slot = laneCount - 1 - slot; // «Invertir horizontal»: the mirrored lane
                position = $"slot:{slot}:{laneCount}";
            }
            // Trim transparent canvas padding. Explicit medium/close shots use the
            // upper body; automatic medium framing keeps an already cropped render intact.
            var cropHeight = style == "detalle" && aspect >= 1.55 ? bounds.H * .82 :
                clip.FramingPreset == "medio" && aspect >= 1.55 ? bounds.H * .65 : bounds.H;
            var fmt = CultureInfo.InvariantCulture;
            var crop = $"crop=iw*{bounds.W.ToString("0.######", fmt)}:ih*{cropHeight.ToString("0.######", fmt)}:" +
                $"iw*{bounds.X.ToString("0.######", fmt)}:ih*{bounds.Y.ToString("0.######", fmt)},";
            // «Escala» (1.4.7) enlarges or reduces what the automatic framing gives, lane and shot included: a bigger
            // render may go past its lane (overlapping a little is normal) but never past the frame.
            var maxWidth = Math.Clamp((int)Math.Round(Math.Min(clip.VisualMaxWidth, width) * clip.Scale), 16, 1280);
            var maxHeight = Math.Clamp((int)Math.Round(Math.Min(clip.VisualMaxHeight, height) * clip.Scale), 16, 720);
            updated.Add(clip with
            {
                Position = position,
                VisualMaxWidth = maxWidth,
                VisualMaxHeight = maxHeight,
                VisualCrop = crop
            });
            if (clip.FramingPreset == "detalle")
            {
                var imageAspect = bounds.Aspect * (cropHeight / bounds.H);
                var displayedHeight = Math.Min(maxHeight, maxWidth * imageAspect);
                var displayedWidth = Math.Min(maxWidth, maxHeight / Math.Max(.01, imageAspect));
                var x = position.StartsWith("slot:", StringComparison.Ordinal) ?
                    SlotCenter(position, displayedWidth) - displayedWidth / 2 : position switch
                    {
                        "izquierda" => 80d,
                        "derecha" => 1200d - displayedWidth,
                        _ => (1280 - displayedWidth) / 2d
                    };
                focus.Add((clip.BlockId, Math.Clamp(x + displayedWidth / 2 + clip.VisualOffsetX, 0, 1280),
                    Math.Clamp(720 - displayedHeight * .68 + clip.VisualOffsetY, 0, 720),
                    aspect < 1.55 ? 1.12 : 1.17));
            }
        }
        var prepared = scene with { Media = updated };
        var visible = VegasBridge.BuildClips(prepared).Where(x => x.Kind == nameof(ScriptBlockKind.CharacterShow))
            .ToDictionary(x => x.BlockId);
        var shots = new List<CameraShot>();
        foreach (var target in focus)
        {
            if (!visible.TryGetValue(target.BlockId, out var characterClip)) continue;
            var nextChange = scene.Media.Where(x => x.Kind is (ScriptBlockKind.CharacterShow or ScriptBlockKind.CharacterHide) &&
                x.StartMs > characterClip.StartMs).Select(x => x.StartMs).DefaultIfEmpty(scene.DurationMs).Min();
            var end = Math.Min(characterClip.StartMs + characterClip.DurationMs, nextChange);
            var shotStart = (scene.Transitions ?? Array.Empty<SceneTransition>())
                .Where(x => x.Style == "cruce" && characterClip.StartMs > x.StartMs &&
                    characterClip.StartMs < x.StartMs + x.DurationMs)
                .Select(x => x.StartMs + x.DurationMs)
                .DefaultIfEmpty(characterClip.StartMs).Max();
            if (end > shotStart)
                shots.Add(new CameraShot(shotStart, end, target.Zoom, target.X, target.Y));
        }
        // Camera blocks, if the scene has any, replace the automatic «primer plano» zoom.
        return prepared with { Camera = await CameraPlanner.BuildAsync(prepared, shots, token) };
    }

    private static double SlotCenter(string position, double width)
    {
        var parts = position.Split(':');
        return parts.Length == 3 && int.TryParse(parts[1], out var slot) &&
            int.TryParse(parts[2], out var lanes) && lanes > 0
            ? 1280d * (slot + .5) / lanes : (1280 - width) / 2 + width / 2;
    }

    /// <summary>
    /// Most characters on screen at once. Characters, not clips (1.4.7): during a cross-fade the outgoing and the incoming
    /// render of the same character overlap, and counting them twice turned a scene of two into one of four (lanes of
    /// 288 px instead of 608 for the whole scene). A render without character counts on its own.
    /// </summary>
    internal static int MaximumVisibleCharacters(SceneComposition scene)
    {
        var who = scene.Media.Where(x => x.Kind == ScriptBlockKind.CharacterShow)
            .ToDictionary(x => x.BlockId, x => x.CharacterId ?? x.BlockId);
        var spans = VegasBridge.BuildClips(scene).Where(x => x.Kind == nameof(ScriptBlockKind.CharacterShow) && x.DurationMs > 0)
            .Select(x => (Id: who.GetValueOrDefault(x.BlockId, x.BlockId), Start: x.StartMs, End: x.StartMs + x.DurationMs)).ToArray();
        return spans.Select(at => spans.Where(x => x.Start <= at.Start && at.Start < x.End).Select(x => x.Id).Distinct().Count())
            .DefaultIfEmpty(0).Max();
    }

    public static string XPosition(string position, int margin)
    {
        if (position.StartsWith("slot:", StringComparison.Ordinal))
        {
            var parts = position.Split(':');
            if (parts.Length == 3 && int.TryParse(parts[1], out var slot) &&
                int.TryParse(parts[2], out var lanes) && lanes > 0 && slot >= 0 && slot < lanes)
                return $"W*({slot}+0.5)/{lanes}-w/2";
        }
        return position switch { "izquierda" => margin.ToString(CultureInfo.InvariantCulture),
            "derecha" => $"W-w-{margin}", _ => "(W-w)/2" };
    }

    private static Dictionary<Guid, int> AssignLanes(SceneComposition scene, int laneCount)
    {
        var characterIds = scene.Media.Where(x => x.Kind == ScriptBlockKind.CharacterShow)
            .ToDictionary(x => x.BlockId, x => x.CharacterId ?? x.BlockId);
        var spans = VegasBridge.BuildClips(scene).Where(x => x.Kind == nameof(ScriptBlockKind.CharacterShow))
            .Select(x => (Id: characterIds[x.BlockId], Start: x.StartMs, End: x.StartMs + x.DurationMs)).ToArray();
        var lanes = new Dictionary<Guid, int>();
        foreach (var group in spans.GroupBy(x => x.Id).OrderBy(x => x.Min(y => y.Start)))
        {
            var occupied = spans.Where(x => x.Id != group.Key && lanes.ContainsKey(x.Id) &&
                group.Any(y => x.Start < y.End && y.Start < x.End))
                .Select(x => lanes[x.Id]).ToHashSet();
            lanes[group.Key] = Enumerable.Range(0, laneCount).FirstOrDefault(x => !occupied.Contains(x));
        }
        return lanes;
    }

    internal static async Task<(double X, double Y, double W, double H, double Aspect)> VisibleBoundsAsync(string path, CancellationToken token) =>
        (await SurfaceAsync(path, token)).Bounds;

    /// <summary>
    /// What a render shows (1.4.7), measured on a 256×256 copy:
    /// <list type="bullet">
    /// <item><see cref="RenderSurface.Bounds"/>: what the automatic framing keeps (with a small safety border). Only
    /// clearly visible pixels count, and a row or column needs more than one: a nearly transparent haze, a faint
    /// shadow or a stray speck no longer stretches it to the whole canvas.</item>
    /// <item><see cref="RenderSurface.Solid"/>: the opaque body, what the eye takes as the character («Revisar encuadre»).</item>
    /// <item>An image without transparency and with a uniform border (Paint, a white background) is measured against that
    /// colour; <see cref="RenderSurface.Background"/> says which, to suggest the chroma key.</item>
    /// </list>
    /// </summary>
    internal static async Task<RenderSurface> SurfaceAsync(string path, CancellationToken token)
    {
        if (MediaProbeCache.TryGet("surface2", path, out var cached) && RenderSurface.Parse(cached) is { } known) return known;
        var (surface, measured) = await SurfaceUncachedAsync(path, token);
        if (measured) MediaProbeCache.Set("surface2", path, surface.Serialize());
        return surface;
    }

    private static async Task<(RenderSurface Surface, bool Measured)> SurfaceUncachedAsync(string path, CancellationToken token)
    {
        var full = new RenderSurface((0, 0, 1, 1, 1), (0, 0, 1, 1), false, null);
        var dimensions = await ImageDimensionsAsync(path, token);
        var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true,
            RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (var arg in new[] { "-v", "error", "-i", path, "-vf",
            $"format=rgba,scale={ProbeSize}:{ProbeSize}", "-frames:v", "1",
            "-f", "rawvideo", "-pix_fmt", "rgba", "pipe:1" }) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo iniciar FFmpeg para analizar el render.");
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        using var output = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(output, token);
        var error = process.StandardError.ReadToEndAsync(token);
        await Task.WhenAll(copy, process.WaitForExitAsync(token), error);
        var pixels = output.ToArray();
        if (process.ExitCode != 0 || pixels.Length != ProbeSize * ProbeSize * 4) return (full with { Bounds = full.Bounds with { Aspect = Aspect(full.Bounds, dimensions) } }, false);
        var surface = Analyze(pixels, ProbeSize);
        return (surface with { Bounds = surface.Bounds with { Aspect = Aspect(surface.Bounds, dimensions) } }, dimensions != (1, 1));
    }

    private static double Aspect((double X, double Y, double W, double H, double Aspect) bounds, (int Width, int Height) dimensions) =>
        bounds.H * dimensions.Height / Math.Max(1e-6, bounds.W * dimensions.Width);

    /// <summary>The measurement itself, on <paramref name="size"/>×<paramref name="size"/> RGBA pixels (Aspect left at 1).</summary>
    internal static RenderSurface Analyze(byte[] rgba, int size)
    {
        byte A(int i) => rgba[i * 4 + 3];
        int Diff(int i, (byte R, byte G, byte B) c) => Math.Max(Math.Abs(rgba[i * 4] - c.R),
            Math.Max(Math.Abs(rgba[i * 4 + 1] - c.G), Math.Abs(rgba[i * 4 + 2] - c.B)));
        var border = Enumerable.Range(0, size).SelectMany(k => new[] { k, (size - 1) * size + k, k * size, k * size + size - 1 })
            .Distinct().ToArray();
        string? background = null;
        Func<int, bool> visible, solid;
        if (border.All(i => A(i) >= 250))
        {
            // No transparency around it: measured against the border colour when that colour is uniform.
            byte Median(int channel) => border.Select(i => rgba[i * 4 + channel]).OrderBy(x => x).ElementAt(border.Length / 2);
            var color = (R: Median(0), G: Median(1), B: Median(2));
            if (border.Count(i => Diff(i, color) <= 40) >= border.Length * 0.9)
            {
                background = $"{color.R:X2}{color.G:X2}{color.B:X2}";
                visible = solid = i => Diff(i, color) > 48;
            }
            else visible = solid = _ => true;
        }
        else
        {
            visible = i => A(i) >= 64;
            solid = i => A(i) >= 200;
        }
        var box = Box(visible) ?? Box(i => A(i) >= 24);
        if (box is null) return new RenderSurface((0, 0, 1, 1, 1), (0, 0, 1, 1), background is not null, background);
        var body = Box(solid) ?? box.Value;
        // A small safety border for antialiasing and motion near the first frame.
        var (left, top, right, bottom) = (Math.Max(0, box.Value.Left - 5), Math.Max(0, box.Value.Top - 5),
            Math.Min(size - 1, box.Value.Right + 5), Math.Min(size - 1, box.Value.Bottom + 5));
        double Unit(int pixels) => pixels / (double)size;
        return new RenderSurface((Unit(left), Unit(top), Unit(right - left + 1), Unit(bottom - top + 1), 1),
            (Unit(body.Left), Unit(body.Top), Unit(body.Right - body.Left + 1), Unit(body.Bottom - body.Top + 1)),
            background is not null, background);

        (int Left, int Top, int Right, int Bottom)? Box(Func<int, bool> inside)
        {
            var rows = new int[size];
            var columns = new int[size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                    if (inside(y * size + x)) { rows[y]++; columns[x]++; }
            // A line with a single pixel is a speck, not the character (unless that is all there is).
            var least = rows.Any(x => x >= 2) ? 2 : 1;
            int First(int[] counts) => Array.FindIndex(counts, x => x >= least);
            int Last(int[] counts) => Array.FindLastIndex(counts, x => x >= least);
            return First(rows) < 0 ? null : (First(columns), First(rows), Last(columns), Last(rows));
        }
    }

    private static async Task<(int Width, int Height)> ImageDimensionsAsync(string path, CancellationToken token)
    {
        try { return await SceneComposer.ProbeDimensionsAsync(path, token); }
        catch (InvalidDataException) { return (1, 1); }
    }
}
