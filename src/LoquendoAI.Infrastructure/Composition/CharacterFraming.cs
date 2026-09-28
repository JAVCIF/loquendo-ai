using System.Diagnostics;
using System.Globalization;
using LoquendoAI.Core.Models;

namespace LoquendoAI.Infrastructure.Composition;

/// <summary>Automatic «primer plano» zoom on a character (see <see cref="CameraPlanner"/>).</summary>
public sealed record CameraShot(long StartMs, long EndMs, double Zoom, double CenterX, double CenterY);

/// <summary>Calculates the same character framing for the MP4 compositor and the VEGAS bridge.
/// The alpha scan only informs a suggestion: the user can always choose a specific framing.</summary>
public static class CharacterFraming
{
    private const int ProbeSize = 256;

    public static async Task<SceneComposition> ApplyAsync(SceneComposition scene, CancellationToken token = default)
    {
        var characters = scene.Media.Where(x => x.Kind == ScriptBlockKind.CharacterShow &&
            x.FramingPreset != "original").ToArray();
        var background = scene.Media.Where(x => x.Kind == ScriptBlockKind.Background && x.AutoTrimBorders).ToArray();
        if (characters.Length == 0 && background.Length == 0)
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
                updated.Add(clip);
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
            var width = laneCount == 1 ? 960 : Math.Max(64, 1280 / laneCount - 32);
            var position = clip.Position;
            if (position == "auto")
            {
                var slot = lanes.GetValueOrDefault(clip.CharacterId ?? clip.BlockId) % laneCount;
                position = $"slot:{slot}:{laneCount}";
            }
            // Trim transparent canvas padding. Explicit medium/close shots use the
            // upper body; automatic medium framing keeps an already cropped render intact.
            var cropHeight = style == "detalle" && aspect >= 1.55 ? bounds.H * .82 :
                clip.FramingPreset == "medio" && aspect >= 1.55 ? bounds.H * .65 : bounds.H;
            var fmt = CultureInfo.InvariantCulture;
            var crop = $"crop=iw*{bounds.W.ToString("0.######", fmt)}:ih*{cropHeight.ToString("0.######", fmt)}:" +
                $"iw*{bounds.X.ToString("0.######", fmt)}:ih*{bounds.Y.ToString("0.######", fmt)},";
            var maxWidth = Math.Min(clip.VisualMaxWidth, width);
            var maxHeight = Math.Min(clip.VisualMaxHeight, height);
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

    private static int MaximumVisibleCharacters(SceneComposition scene)
    {
        var visual = VegasBridge.BuildClips(scene).Where(x => x.Kind == nameof(ScriptBlockKind.CharacterShow));
        var points = visual.SelectMany(x => new[] { (Time: x.StartMs, Delta: 1),
            (Time: x.StartMs + x.DurationMs, Delta: -1) }).OrderBy(x => x.Time).ThenBy(x => x.Delta);
        var count = 0;
        var maximum = 0;
        foreach (var point in points) maximum = Math.Max(maximum, count += point.Delta);
        return maximum;
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

    private static async Task<(double X, double Y, double W, double H, double Aspect)> VisibleBoundsAsync(string path, CancellationToken token)
    {
        var fmt = CultureInfo.InvariantCulture;
        if (MediaProbeCache.TryGet("bounds1", path, out var cached))
        {
            var values = cached.Split(';');
            var numbers = new double[5];
            if (values.Length == 5 && values.Select((v, i) => double.TryParse(v, NumberStyles.Float, fmt, out numbers[i])).All(x => x))
                return (numbers[0], numbers[1], numbers[2], numbers[3], numbers[4]);
        }
        var (bounds, measured) = await VisibleBoundsUncachedAsync(path, token);
        if (measured)
            MediaProbeCache.Set("bounds1", path, string.Join(';', new[] { bounds.X, bounds.Y, bounds.W, bounds.H, bounds.Aspect }
                .Select(x => x.ToString("R", fmt))));
        return bounds;
    }

    private static async Task<((double X, double Y, double W, double H, double Aspect) Bounds, bool Measured)> VisibleBoundsUncachedAsync(
        string path, CancellationToken token)
    {
        var full = (X: 0d, Y: 0d, W: 1d, H: 1d, Aspect: 1d);
        var dimensions = await ImageDimensionsAsync(path, token);
        var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true,
            RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (var arg in new[] { "-v", "error", "-i", path, "-vf",
            $"format=rgba,alphaextract,scale={ProbeSize}:{ProbeSize}", "-frames:v", "1",
            "-f", "rawvideo", "-pix_fmt", "gray", "pipe:1" }) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo iniciar FFmpeg para analizar el render.");
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        using var output = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(output, token);
        var error = process.StandardError.ReadToEndAsync(token);
        await Task.WhenAll(copy, process.WaitForExitAsync(token), error);
        var pixels = output.ToArray();
        if (process.ExitCode != 0 || pixels.Length != ProbeSize * ProbeSize) return (full, false);
        var left = ProbeSize; var top = ProbeSize; var right = -1; var bottom = -1;
        for (var y = 0; y < ProbeSize; y++)
            for (var x = 0; x < ProbeSize; x++)
                if (pixels[y * ProbeSize + x] >= 24)
                {
                    left = Math.Min(left, x); top = Math.Min(top, y);
                    right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                }
        if (right < left || bottom < top) return (full, true);
        // Keep a small safety border for antialiasing and motion near the first frame.
        left = Math.Max(0, left - 5); top = Math.Max(0, top - 5);
        right = Math.Min(ProbeSize - 1, right + 5); bottom = Math.Min(ProbeSize - 1, bottom + 5);
        var visibleWidth = right - left + 1;
        var visibleHeight = bottom - top + 1;
        return ((left / 256d, top / 256d, visibleWidth / 256d, visibleHeight / 256d,
            visibleHeight * (double)dimensions.Height / Math.Max(1d, visibleWidth * (double)dimensions.Width)), dimensions != (1, 1));
    }

    private static async Task<(int Width, int Height)> ImageDimensionsAsync(string path, CancellationToken token)
    {
        try { return await SceneComposer.ProbeDimensionsAsync(path, token); }
        catch (InvalidDataException) { return (1, 1); }
    }
}
