using System.Globalization;
using LoquendoAI.Core.Models;

namespace LoquendoAI.Infrastructure.Composition;

/// <summary>The part of the 1280×720 composition the camera shows (always 16:9, always inside the frame).</summary>
public readonly record struct CameraWindow(double X, double Y, double Width)
{
    public double Height => Width * 720 / 1280;
    public double Zoom => 1280 / Width;
    public static readonly CameraWindow Full = new(0, 0, 1280);
    public bool IsFull => Width >= 1279.999 && Math.Abs(X) < 0.001 && Math.Abs(Y) < 0.001;

    /// <summary>Window of <paramref name="zoom"/> centred on (cx, cy), pushed back inside the frame.</summary>
    public static CameraWindow Around(double centerX, double centerY, double zoom)
    {
        var width = 1280 / Math.Clamp(zoom, BlockDefaults.CameraZoomMin, BlockDefaults.CameraZoomMax);
        var height = width * 720 / 1280;
        return new CameraWindow(Math.Clamp(centerX - width / 2, 0, 1280 - width),
            Math.Clamp(centerY - height / 2, 0, 720 - height), width);
    }

    public static CameraWindow Lerp(CameraWindow a, CameraWindow b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Width + (b.Width - a.Width) * t);

    public bool Near(CameraWindow other) =>
        Math.Abs(X - other.X) < 0.01 && Math.Abs(Y - other.Y) < 0.01 && Math.Abs(Width - other.Width) < 0.01;
}

/// <summary>From <see cref="StartMs"/> (inclusive) to <see cref="EndMs"/> (exclusive) the window moves
/// linearly from <see cref="From"/> to <see cref="To"/>; the last piece never ends.</summary>
public sealed record CameraPiece(long StartMs, long EndMs, CameraWindow From, CameraWindow To)
{
    public bool Moves => !From.Near(To);

    public CameraWindow At(long timeMs) => EndMs == long.MaxValue || EndMs <= StartMs ? To
        : CameraWindow.Lerp(From, To, Math.Clamp((timeMs - StartMs) / (double)(EndMs - StartMs), 0, 1));
}

/// <summary>A camera block placed on the scene clock.</summary>
public sealed record CameraCue(long StartMs, CameraSettings Settings, Guid? CharacterId);

/// <summary>One camera window at a time relative to a clip (VEGAS keyframes).</summary>
public sealed record CameraSample(long AtMs, double X, double Y, double Width)
{
    public CameraWindow Window => new(X, Y, Width);
}

/// <summary>
/// The camera of a scene as linear pieces: the preview evaluates the same pieces per frame and the
/// VEGAS export writes them as Pan/Crop keyframes (a jump between pieces cuts the event), so both show
/// exactly the same framing. Nothing is rendered in advance for VEGAS: the camera stays editable there.
/// </summary>
public sealed class CameraPath
{
    public IReadOnlyList<CameraPiece> Pieces { get; }

    public CameraPath(IReadOnlyList<CameraPiece> pieces) =>
        Pieces = pieces.Count == 0 ? [new CameraPiece(0, long.MaxValue, CameraWindow.Full, CameraWindow.Full)] : pieces;

    public static CameraPath Still { get; } = new([]);

    /// <summary>True when the camera never leaves the whole frame.</summary>
    public bool IsStill => Pieces.All(x => x.From.IsFull && x.To.IsFull);

    public CameraWindow At(long timeMs)
    {
        for (var i = Pieces.Count - 1; i >= 0; i--)
            if (timeMs >= Pieces[i].StartMs) return Pieces[i].At(timeMs);
        return CameraWindow.Full;
    }

    /// <summary>Value approached from the left: at a cut this is the framing before the jump.</summary>
    public CameraWindow AtLeft(long timeMs)
    {
        for (var i = Pieces.Count - 1; i >= 0; i--)
            if (timeMs > Pieces[i].StartMs) return Pieces[i].At(timeMs);
        return CameraWindow.Full;
    }

    /// <summary>Times where the framing jumps (a camera move of 0 ms): VEGAS events are split there.</summary>
    public IEnumerable<long> Cuts => Pieces.Skip(1).Where(x => !AtLeft(x.StartMs).Near(x.From)).Select(x => x.StartMs);

    /// <summary>Keyframes for a clip that starts at <paramref name="startMs"/>: its start, every point where
    /// the camera changes speed inside it, and its end while the camera is still moving. Times are relative
    /// to the clip. The caller splits clips at <see cref="Cuts"/> first.</summary>
    public IReadOnlyList<CameraSample> Samples(long startMs, long durationMs)
    {
        var end = startMs + durationMs;
        var samples = new List<CameraSample>();
        void Add(long absolute, CameraWindow window)
        {
            var at = absolute - startMs;
            if (samples.Count > 0 && samples[^1].AtMs == at) samples.RemoveAt(samples.Count - 1);
            samples.Add(new CameraSample(at, window.X, window.Y, window.Width));
        }
        Add(startMs, At(startMs));
        foreach (var piece in Pieces.Where(x => x.StartMs > startMs && x.StartMs < end)) Add(piece.StartMs, At(piece.StartMs));
        if (durationMs > 0 && Pieces.Any(x => x.Moves && x.StartMs < end && x.EndMs > end)) Add(end, AtLeft(end));
        return samples;
    }

    /// <summary>FFmpeg expression (of t) of one window value along the path.</summary>
    public string Expression(Func<CameraWindow, double> value)
    {
        var terms = new List<string>();
        foreach (var piece in Pieces)
        {
            var a = Seconds(piece.StartMs);
            var condition = piece.EndMs == long.MaxValue ? $"gte(t,{a})" : $"gte(t,{a})*lt(t,{Seconds(piece.EndMs)})";
            var from = value(piece.From);
            var to = value(piece.To);
            var expression = !piece.Moves || piece.EndMs == long.MaxValue ? Number(to)
                : $"({Number(from)}+({Number(to - from)})*(t-{a})/{Seconds(piece.EndMs - piece.StartMs)})";
            terms.Add($"{condition}*{expression}");
        }
        return string.Join("+", terms);
    }

    /// <summary>
    /// FFmpeg filter that applies the camera to the composed 1280×720 frame: each frame is scaled so the
    /// window fills the output and then cropped (both evaluated per frame, so moves are smooth).
    /// </summary>
    public string PreviewFilter()
    {
        string X(Func<CameraWindow, double> value) => Expression(value);
        var width = X(w => w.Width);
        var scaledWidth = $"trunc(1280*1280/({width})/2)*2";
        var scaledHeight = $"trunc(720*1280/({width})/2)*2";
        // crop keeps the input size it was configured with (iw/ih do not follow the per-frame scale),
        // so the scaled size is computed here instead of read from iw/ih.
        return $"scale=w='{scaledWidth}':h='{scaledHeight}':eval=frame:flags=bicubic," +
            $"crop=w=1280:h=720:x='min({scaledWidth}-1280,max(0,({X(w => w.X)})*({scaledWidth})/1280))':" +
            $"y='min({scaledHeight}-720,max(0,({X(w => w.Y)})*({scaledWidth})/1280))',setsar=1";
    }

    private static string Seconds(long ms) => (ms / 1000d).ToString("0.###", CultureInfo.InvariantCulture);
    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Builds the path from framing requests applied in order: at <c>Time</c> the camera moves
    /// from wherever it is to <c>Target</c> in <c>MoveMs</c> (0 = cut). A later request interrupts a move.</summary>
    public static CameraPath FromRequests(IEnumerable<(long Time, CameraWindow Target, long MoveMs)> requests)
    {
        var pieces = new List<CameraPiece> { new(0, long.MaxValue, CameraWindow.Full, CameraWindow.Full) };
        foreach (var (time, target, move) in requests)
        {
            var t = Math.Max(0, time);
            var current = new CameraPath(pieces).At(t);
            // Keep what happened before t, frozen where the camera is at t.
            var kept = pieces.Where(x => x.StartMs < t).ToList();
            if (kept.Count > 0)
            {
                var last = kept[^1];
                kept[^1] = last with { EndMs = t, To = last.At(t) };
            }
            if (move > 0 && !current.Near(target))
            {
                kept.Add(new CameraPiece(t, t + move, current, target));
                kept.Add(new CameraPiece(t + move, long.MaxValue, target, target));
            }
            else kept.Add(new CameraPiece(t, long.MaxValue, target, target));
            pieces = Merge(kept);
        }
        return new CameraPath(pieces);
    }

    /// <summary>Joins consecutive still pieces with the same framing (fewer keyframes and filter terms).</summary>
    private static List<CameraPiece> Merge(List<CameraPiece> pieces)
    {
        var merged = new List<CameraPiece>();
        foreach (var piece in pieces.Where(x => x.EndMs > x.StartMs))
        {
            if (merged.Count > 0 && !merged[^1].Moves && !piece.Moves && merged[^1].To.Near(piece.From) &&
                merged[^1].EndMs == piece.StartMs)
                merged[^1] = merged[^1] with { EndMs = piece.EndMs };
            else merged.Add(piece);
        }
        return merged;
    }
}

/// <summary>
/// Turns the camera blocks of a scene into a <see cref="CameraPath"/>. Rules:
/// <list type="bullet">
/// <item>The scene starts on the whole frame. Each camera block holds until the next one or the end.</item>
/// <item>«personaje»: frames the character's visible render (face or body) at the zoom asked for. If the
/// character changes render or position, the camera goes with them (same move time); if the render
/// moves (animar), the camera follows it; if the character leaves, the camera goes back to the whole
/// frame, and returns to them if they come back before the next camera block. Not on screen: whole frame.</item>
/// <item>«habla»: each dialogue line frames its speaker if they are on screen; otherwise it stays.</item>
/// <item>«punto»: the frame centre moved by x/y, at the zoom asked for. «general»: the whole frame.</item>
/// <item>The window never leaves the frame (it slides against the edges) and zoom is 1–3×.</item>
/// <item>With camera blocks in a scene, the automatic «primer plano» zoom is off for that scene.</item>
/// </list>
/// </summary>
public static class CameraPlanner
{
    public static async Task<CameraPath> BuildAsync(SceneComposition scene, IReadOnlyList<CameraShot> automatic,
        CancellationToken token = default)
    {
        var cues = (scene.CameraCues ?? []).OrderBy(x => x.StartMs).ToArray();
        if (cues.Length == 0)
        {
            var requests = new List<(long, CameraWindow, long)>();
            foreach (var shot in automatic)
            {
                requests.Add((shot.StartMs, CameraWindow.Around(shot.CenterX, shot.CenterY, shot.Zoom), 0));
                requests.Add((shot.EndMs, CameraWindow.Full, 0));
            }
            return requests.Count == 0 ? CameraPath.Still : CameraPath.FromRequests(requests.OrderBy(x => x.Item1));
        }

        var characters = VegasBridge.BuildClips(scene)
            .Where(x => x.Kind == nameof(ScriptBlockKind.CharacterShow) && x.CharacterId is not null)
            .OrderBy(x => x.StartMs).ToArray();
        var layouts = new Dictionary<Guid, LayerLayout?>();
        foreach (var clip in characters)
        {
            token.ThrowIfCancellationRequested();
            var size = await SceneComposer.ProbeDimensionsAsync(clip.Path, token);
            layouts[clip.BlockId] = LayerGeometry.Place(LayerSpec.From(clip), size.Width, size.Height, 1280, 720);
        }
        return Plan(scene, cues, characters, clip => layouts.GetValueOrDefault(clip.BlockId));
    }

    /// <summary>The planning itself, without file access (see <see cref="BuildAsync"/>).</summary>
    public static CameraPath Plan(SceneComposition scene, IReadOnlyList<CameraCue> cues,
        IReadOnlyList<VegasBridge.Clip> characters, Func<VegasBridge.Clip, LayerLayout?> layoutOf)
    {
        var requests = new List<(long Time, CameraWindow Target, long MoveMs)>();
        VegasBridge.Clip? VisibleAt(Guid character, long time) => characters.LastOrDefault(x =>
            x.CharacterId == character && x.StartMs <= time && time < x.StartMs + x.DurationMs);
        CameraWindow? Framing(VegasBridge.Clip clip, long time, CameraSettings settings) =>
            FocusPoint(clip, layoutOf(clip), time, settings.Focus) is { } point
                ? CameraWindow.Around(point.X + settings.OffsetX, point.Y + settings.OffsetY, settings.Zoom) : null;

        for (var i = 0; i < cues.Count; i++)
        {
            var cue = cues[i];
            var settings = cue.Settings;
            var until = i + 1 < cues.Count ? cues[i + 1].StartMs : long.MaxValue;
            switch (settings.Mode)
            {
                case "punto":
                    requests.Add((cue.StartMs, CameraWindow.Around(640 + settings.OffsetX, 360 + settings.OffsetY, settings.Zoom), settings.MoveMs));
                    break;
                case "personaje" when cue.CharacterId is Guid character:
                {
                    var own = characters.Where(x => x.CharacterId == character &&
                        x.StartMs + x.DurationMs > cue.StartMs && x.StartMs < until).ToArray();
                    if (VisibleAt(character, cue.StartMs) is null)
                        requests.Add((cue.StartMs, CameraWindow.Full, settings.MoveMs));
                    for (var k = 0; k < own.Length; k++)
                    {
                        var clip = own[k];
                        var start = Math.Max(clip.StartMs, cue.StartMs);
                        var clipEnd = Math.Min(clip.StartMs + clip.DurationMs, until);
                        var arrive = Math.Min(start + settings.MoveMs, clipEnd);
                        if (Framing(clip, arrive, settings) is not { } target) continue;
                        requests.Add((start, target, arrive - start));
                        // Follow a render that moves: linear motion, so one more request to where it stops.
                        var motionEnd = clip.StartMs + MotionSpan(clip);
                        var followEnd = Math.Min(motionEnd, clipEnd);
                        if (HasMotion(clip) && followEnd > arrive && Framing(clip, followEnd, settings) is { } followed)
                            requests.Add((arrive, followed, followEnd - arrive));
                        // Leaves the scene (not replaced by another render of the same character).
                        var replaced = k + 1 < own.Length && own[k + 1].StartMs <= clip.StartMs + clip.DurationMs;
                        if (!replaced && clip.StartMs + clip.DurationMs < until && clip.StartMs + clip.DurationMs < scene.DurationMs)
                            requests.Add((clip.StartMs + clip.DurationMs, CameraWindow.Full, settings.MoveMs));
                    }
                    break;
                }
                case "habla":
                {
                    foreach (var line in scene.Media.Where(x => x.Kind == ScriptBlockKind.Dialogue && x.CharacterId is not null &&
                                 x.StartMs >= cue.StartMs && x.StartMs < until).OrderBy(x => x.StartMs))
                    {
                        if (VisibleAt(line.CharacterId!.Value, line.StartMs) is not { } clip) continue;
                        var arrive = Math.Min(line.StartMs + settings.MoveMs, clip.StartMs + clip.DurationMs);
                        if (Framing(clip, arrive, settings) is { } target)
                            requests.Add((line.StartMs, target, arrive - line.StartMs));
                    }
                    break;
                }
                default:
                    requests.Add((cue.StartMs, CameraWindow.Full, settings.MoveMs));
                    break;
            }
        }
        // Stable by time: requests of the same instant keep their order (the last one wins).
        return CameraPath.FromRequests(requests.Select((x, index) => (x, index)).OrderBy(x => x.x.Time)
            .ThenBy(x => x.index).Select(x => x.x));
    }

    private static bool HasMotion(VegasBridge.Clip clip) =>
        clip.MotionOffsetX != 0 || clip.MotionOffsetY != 0 || Math.Abs(clip.MotionRotationDegrees) > 0.0001;

    /// <summary>How long a render's own motion lasts: its «animar ms», or all of its visible time.</summary>
    public static long MotionSpan(VegasBridge.Clip clip) => Math.Max(40, clip.MotionDurationMs > 0 ? clip.MotionDurationMs : clip.DurationMs);

    /// <summary>Where the camera aims on a render at a time: its face (upper part) or body centre,
    /// following its motion and rotation.</summary>
    public static (double X, double Y)? FocusPoint(VegasBridge.Clip clip, LayerLayout? layout, long time, string focus)
    {
        if (layout is null) return null;
        var progress = HasMotion(clip) ? Math.Clamp((time - clip.StartMs) / (double)MotionSpan(clip), 0, 1) : 0;
        var centerX = layout.CenterX + clip.MotionOffsetX * progress;
        var centerY = layout.CenterY + clip.MotionOffsetY * progress;
        var dy = focus == "cuerpo" ? 0 : -0.2 * layout.Height; // face: about 30 % from the top
        var angle = (clip.RotationDegrees + clip.MotionRotationDegrees * progress) * Math.PI / 180;
        return (centerX - dy * Math.Sin(angle), centerY + dy * Math.Cos(angle));
    }
}
