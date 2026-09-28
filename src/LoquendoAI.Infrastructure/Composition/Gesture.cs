using System.Globalization;
using LoquendoAI.Core.Models;

namespace LoquendoAI.Infrastructure.Composition;

/// <summary>A render's gesture pose: tilt in degrees (positive = the top leans right) around its pivot,
/// and vertical stretch (1 = none) anchored at the pivot.</summary>
public readonly record struct GesturePose(double Degrees, double Stretch)
{
    public static readonly GesturePose Rest = new(0, 1);

    public static GesturePose Lerp(GesturePose a, GesturePose b, double p) =>
        new(a.Degrees + (b.Degrees - a.Degrees) * p, a.Stretch + (b.Stretch - a.Stretch) * p);
}

/// <summary>A gesture keyframe. <see cref="Fast"/>: the change from this key to the next one uses the VEGAS
/// «Rápido» (Fast) curve, quick at first and landing softly; otherwise it is linear.</summary>
public sealed record GestureKey(long AtMs, GesturePose Pose, bool Fast);

/// <summary>A gesture block on the scene clock: <see cref="CharacterId"/> is the block's character.</summary>
public sealed record GestureCue(long StartMs, GestureSettings Settings, Guid? CharacterId);

/// <summary>The gestures of one render (one visual block), as keyframes on the scene clock.</summary>
public sealed record GestureTrack(string Pivot, IReadOnlyList<GestureKey> Keys)
{
    /// <summary>Pivot below the layer centre, as a fraction of half the layer height: feet, waist or centre.</summary>
    public double PivotFraction => Pivot switch { "centro" => 0, "cintura" => 0.2, _ => 1 };

    public bool Rotates => Keys.Any(x => Math.Abs(x.Pose.Degrees) > 0.0001);
    public bool Stretches => Keys.Any(x => Math.Abs(x.Pose.Stretch - 1) > 0.0001);
    public double MaxDegrees => Keys.Select(x => Math.Abs(x.Pose.Degrees)).DefaultIfEmpty(0).Max();
    public double MaxStretch => Math.Max(1, Keys.Select(x => x.Pose.Stretch).DefaultIfEmpty(1).Max());
    public double MinStretch => Math.Min(1, Keys.Select(x => x.Pose.Stretch).DefaultIfEmpty(1).Min());

    public GesturePose At(long timeMs)
    {
        if (Keys.Count == 0) return GesturePose.Rest;
        if (timeMs <= Keys[0].AtMs) return Keys[0].Pose;
        for (var i = 0; i + 1 < Keys.Count; i++)
        {
            var (a, b) = (Keys[i], Keys[i + 1]);
            if (timeMs >= b.AtMs || b.AtMs <= a.AtMs) continue;
            var p = (timeMs - a.AtMs) / (double)(b.AtMs - a.AtMs);
            return GesturePose.Lerp(a.Pose, b.Pose, a.Fast ? GesturePlan.Ease(p) : p);
        }
        return Keys[^1].Pose;
    }

    /// <summary>Whether the change going on at <paramref name="timeMs"/> uses the Fast curve.</summary>
    public bool FastAt(long timeMs)
    {
        for (var i = 0; i + 1 < Keys.Count; i++)
            if (Keys[i].AtMs <= timeMs && timeMs < Keys[i + 1].AtMs) return Keys[i].Fast;
        return false;
    }

    /// <summary>FFmpeg expression (of t, in seconds on the scene clock) of one pose value.</summary>
    public string Expression(Func<GesturePose, double> value)
    {
        if (Keys.Count == 0) return Number(value(GesturePose.Rest));
        var terms = new List<string> { $"lt(t,{Seconds(Keys[0].AtMs)})*{Number(value(Keys[0].Pose))}" };
        for (var i = 0; i + 1 < Keys.Count; i++)
        {
            var (a, b) = (Keys[i], Keys[i + 1]);
            if (b.AtMs <= a.AtMs) continue;
            var from = value(a.Pose);
            var delta = value(b.Pose) - from;
            var p = $"(t-{Seconds(a.AtMs)})/{Seconds(b.AtMs - a.AtMs)}";
            var eased = a.Fast ? $"({p})*(2-({p}))" : p;
            terms.Add(Math.Abs(delta) < 0.00001
                ? $"gte(t,{Seconds(a.AtMs)})*lt(t,{Seconds(b.AtMs)})*{Number(from)}"
                : $"gte(t,{Seconds(a.AtMs)})*lt(t,{Seconds(b.AtMs)})*({Number(from)}+({Number(delta)})*{eased})");
        }
        terms.Add($"gte(t,{Seconds(Keys[^1].AtMs)})*{Number(value(Keys[^1].Pose))}");
        return string.Join("+", terms);
    }

    /// <summary>
    /// The part of the track inside [start, end) with times relative to <paramref name="startMs"/> (a VEGAS event
    /// cut out of the clip), sampled from the whole curve: one linear key per frame (40 ms from each key) while the
    /// pose changes, plus the pose where the part starts and ends. A cut in the middle of a gesture therefore keeps
    /// the curve instead of starting a new ease at the cut. Null when the render stays at rest all that time.
    /// </summary>
    public GestureTrack? Slice(long startMs, long endMs)
    {
        if (Keys.Count == 0 || endMs <= startMs) return null;
        var times = new SortedSet<long> { startMs };
        for (var i = 0; i + 1 < Keys.Count; i++)
        {
            var (a, b) = (Keys[i], Keys[i + 1]);
            if (a.AtMs > startMs && a.AtMs < endMs) times.Add(a.AtMs);
            if (b.AtMs <= a.AtMs || a.Pose == b.Pose) continue;
            for (var t = a.AtMs + GesturePlan.FrameMs; t < b.AtMs; t += GesturePlan.FrameMs)
                if (t > startMs && t < endMs) times.Add(t);
        }
        if (Keys[^1].AtMs > startMs && Keys[^1].AtMs < endMs) times.Add(Keys[^1].AtMs);
        // Still moving when the part ends: its pose at the end.
        if (Keys[0].AtMs < endMs && Keys[^1].AtMs >= endMs) times.Add(endMs);
        var keys = times.Select(t => new GestureKey(t - startMs, At(t), false)).ToArray();
        if (keys.All(x => Math.Abs(x.Pose.Degrees) < 0.0001 && Math.Abs(x.Pose.Stretch - 1) < 0.0001)) return null;
        return this with { Keys = keys };
    }

    internal static string Seconds(long ms) => (ms / 1000d).ToString("0.###", CultureInfo.InvariantCulture);
    internal static string Number(double value) => value.ToString("0.#####", CultureInfo.InvariantCulture);
}

/// <summary>
/// Character gestures, after the Residents96 VEGAS technique: the Pan/Crop pivot goes to the render's feet and
/// the moves follow a «Rápido»-like curve (fast start, soft landing). VEGAS gets one keyframe per frame of that
/// curve, not «Rápido» keyframes: its own curve and box interpolation looked stiffer than the preview.
/// <list type="bullet">
/// <item>«balanceo»: from its current tilt, 4 frames to lean to one side, 5 more frames to lean slightly to
/// the other side (40 % of the angle), where it stays. The next balanceo starts from there.</item>
/// <item>«rebote»: the render stretches (or squashes) from the feet at frame 2 and is back at frame 4.</item>
/// <item>Moves in one step happen together (balanceo+rebote); steps go one after the other. A gesture that
/// starts while another is going on takes over from the current pose.</item>
/// <item>«auto» side: toward the centre of the screen, then alternating. «habla»: each line from here on
/// makes its speaker (if on screen) do the gesture as the line starts, until [GESTO] habla | quitar.</item>
/// </list>
/// Frames are 40 ms (25 fps) divided by the speed. The preview draws the same keyframes and curve.
/// </summary>
public static class GesturePlan
{
    public const int FrameMs = 40;

    /// <summary>VEGAS «Rápido» (Fast) keyframe curve, approximated as an ease-out.</summary>
    public static double Ease(double p) => p * (2 - p);

    /// <summary>Keys of one step starting at <paramref name="startMs"/> from <paramref name="start"/>.
    /// <paramref name="direction"/>: +1 leans right first, −1 left.</summary>
    public static (IReadOnlyList<GestureKey> Keys, GesturePose End, long EndMs) Step(long startMs, GesturePose start,
        IReadOnlyList<string> moves, GestureSettings settings, int direction)
    {
        var frame = FrameMs / settings.Speed;
        long F(int n) => (long)Math.Round(n * frame);
        (long At, double Value)[] tilt = moves.Contains("balanceo")
            ? [(0, start.Degrees), (F(4), direction * settings.Angle), (F(9), -direction * settings.Angle * 0.4)]
            : [(0, start.Degrees)];
        (long At, double Value)[] stretch = moves.Contains("rebote")
            ? [(0, start.Stretch), (F(2), 1 + settings.StretchPercent / 100), (F(4), 1)]
            : [(0, start.Stretch)];
        var times = tilt.Select(x => x.At).Concat(stretch.Select(x => x.At)).Distinct().Order().ToArray();
        var keys = times.Select((at, i) => new GestureKey(startMs + at, new GesturePose(Curve(tilt, at), Curve(stretch, at)),
            i < times.Length - 1)).ToArray();
        return (keys, keys[^1].Pose, keys[^1].AtMs);
    }

    private static double Curve((long At, double Value)[] keys, long at)
    {
        if (at <= keys[0].At) return keys[0].Value;
        for (var i = 0; i + 1 < keys.Length; i++)
            if (at < keys[i + 1].At)
                return keys[i].Value + (keys[i + 1].Value - keys[i].Value) *
                    Ease((at - keys[i].At) / (double)(keys[i + 1].At - keys[i].At));
        return keys[^1].Value;
    }

    /// <summary>+1 when a render on this position leans toward the centre by leaning right.</summary>
    public static int TowardCentre(string position)
    {
        if (position == "derecha") return -1;
        if (position.StartsWith("slot:", StringComparison.Ordinal) && position.Split(':') is { Length: 3 } parts &&
            int.TryParse(parts[1], out var slot) && int.TryParse(parts[2], out var lanes) && lanes > 0)
            return slot + 0.5 > lanes / 2d ? -1 : 1;
        return 1;
    }

    private sealed class Building(string pivot)
    {
        public string Pivot { get; } = pivot;
        public List<GestureKey> Keys { get; } = [];
        public int Autos { get; set; }
    }

    /// <summary>Turns the gesture blocks of a scene into keyframes per render (idempotent). Call once the
    /// renders have their final position (CharacterFraming.ApplyAsync).</summary>
    public static SceneComposition Resolve(SceneComposition scene)
    {
        if (scene.Gestures is not null || scene.GestureCues is not { Count: > 0 } cues) return scene;
        var renders = VegasBridge.BuildClips(scene)
            .Where(x => x.Layer >= 0 && x.Kind == nameof(ScriptBlockKind.CharacterShow) && x.CharacterId is not null).ToArray();
        var lines = scene.Media.Where(x => x.Kind == ScriptBlockKind.Dialogue && x.CharacterId is not null)
            .OrderBy(x => x.StartMs).ToArray();
        var ordered = cues.OrderBy(x => x.StartMs).ToArray();
        var requests = new List<(long At, GestureSettings Settings, Guid Character)>();
        for (var i = 0; i < ordered.Length; i++)
        {
            var cue = ordered[i];
            if (cue.Settings.Mode == "personaje" && cue.CharacterId is Guid character)
                requests.Add((cue.StartMs, cue.Settings, character));
            else if (cue.Settings.Mode == "habla")
            {
                var until = ordered.Skip(i + 1).Where(x => x.Settings.Mode is "habla" or "quitar")
                    .Select(x => x.StartMs).DefaultIfEmpty(long.MaxValue).First();
                requests.AddRange(lines.Where(x => x.StartMs >= cue.StartMs && x.StartMs < until)
                    .Select(x => (x.StartMs, cue.Settings, x.CharacterId!.Value)));
            }
        }
        var tracks = new Dictionary<Guid, Building>();
        foreach (var (at, settings, character) in requests.OrderBy(x => x.At))
        {
            var render = renders.FirstOrDefault(x => x.CharacterId == character && x.StartMs <= at && at < x.StartMs + x.DurationMs);
            if (render is null) continue;
            if (!tracks.TryGetValue(render.BlockId, out var track)) tracks[render.BlockId] = track = new Building(settings.Pivot);
            var pose = new GestureTrack(track.Pivot, track.Keys).At(at);
            track.Keys.RemoveAll(x => x.AtMs >= at);
            var direction = settings.Side switch
            {
                "derecha" => 1,
                "izquierda" => -1,
                _ => TowardCentre(render.Position) * (track.Autos++ % 2 == 0 ? 1 : -1)
            };
            var time = at;
            foreach (var moves in settings.Steps)
            {
                var (keys, end, endMs) = Step(time, pose, moves, settings, direction);
                track.Keys.AddRange(keys);
                (pose, time) = (end, endMs);
                if (settings.Side == "auto" && moves.Contains("balanceo")) direction = -direction;
            }
            // Where a step ends and the next begins there are two keys at the same time: keep the later one.
            var merged = new List<GestureKey>();
            foreach (var key in track.Keys)
                if (merged.Count > 0 && merged[^1].AtMs == key.AtMs) merged[^1] = key;
                else merged.Add(key);
            track.Keys.Clear();
            track.Keys.AddRange(merged);
        }
        return scene with
        {
            Gestures = tracks.Where(x => x.Value.Keys.Count > 1)
                .ToDictionary(x => x.Key, x => new GestureTrack(x.Value.Pivot, x.Value.Keys.ToArray()))
        };
    }

    /// <summary>
    /// FFmpeg filters for a render layer (its stream centred on the layer centre, times on the scene clock):
    /// the stream is padded so the pivot is its centre, rotated and stretched there per frame. Returns the
    /// filters and how far below the layer centre the pivot is (the overlay places the pivot there).
    /// </summary>
    public static (string Filters, double PivotBelowCentre) PreviewFilter(GestureTrack track, int layerWidth, int layerHeight,
        bool rotatedStream)
    {
        var below = track.PivotFraction * layerHeight / 2d;
        // Content box relative to the pivot, then its extent at every angle the gesture reaches.
        double left, right, top, bottom;
        if (rotatedStream)
        {
            var radius = Math.Sqrt((double)layerWidth * layerWidth + (double)layerHeight * layerHeight) / 2 + Math.Abs(below);
            (left, right, top, bottom) = (-radius, radius, -radius, radius);
        }
        else (left, right, top, bottom) = (-layerWidth / 2d, layerWidth / 2d, -(layerHeight / 2d + below), layerHeight / 2d - below);
        double halfWidth = 0, halfHeight = 0;
        var maxAngle = track.MaxDegrees * Math.PI / 180;
        for (var i = -8; i <= 8; i++)
        {
            var angle = maxAngle * i / 8;
            foreach (var (x, y) in new[] { (left, top), (right, top), (right, bottom), (left, bottom) })
            {
                halfWidth = Math.Max(halfWidth, Math.Abs(x * Math.Cos(angle) - y * Math.Sin(angle)));
                halfHeight = Math.Max(halfHeight, Math.Abs(x * Math.Sin(angle) + y * Math.Cos(angle)));
            }
        }
        var smax = track.MaxStretch;
        var smin = track.MinStretch;
        var width = Even(2 * halfWidth + 4);
        var height = Even(2 * halfHeight * (track.Stretches ? smax / smin : 1) + 4);
        var filters = $",pad={width}:{height}:(ow-iw)/2:oh/2-ih/2-{Number(below)}:color=black@0";
        if (track.Rotates)
            filters += $",rotate=a='({track.Expression(x => x.Degrees)})*PI/180':ow={width}:oh={height}:c=none";
        if (track.Stretches)
        {
            // Scaled up only (÷ the smallest stretch) so the per-frame crop always has room; a final fixed
            // scale brings the squash back. The scaled height is computed here: crop keeps its first iw/ih.
            var scaled = $"trunc({height}*({track.Expression(x => x.Stretch)})/{Number(smin)}/2)*2";
            filters += $",scale=w={width}:h='{scaled}':eval=frame:flags=bicubic" +
                $",crop=w={width}:h={height}:x=0:y='({scaled}-{height})/2'";
            if (smin < 0.9999) filters += $",scale={width}:{Even(height * smin)}";
        }
        return (filters, below);
    }

    private static int Even(double value) => (int)Math.Ceiling(value / 2) * 2;
    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>2-D affine map: x' = A·x + B·y + E, y' = C·x + D·y + F (image coordinates, y down).</summary>
internal readonly record struct Affine(double A, double B, double C, double D, double E, double F)
{
    public static readonly Affine Identity = new(1, 0, 0, 1, 0, 0);
    public static Affine Translate(double x, double y) => new(1, 0, 0, 1, x, y);
    public static Affine Scale(double x, double y) => new(x, 0, 0, y, 0, 0);

    /// <summary>Clockwise on screen for positive degrees (FFmpeg rotate and the preview).</summary>
    public static Affine Rotate(double degrees)
    {
        var a = degrees * Math.PI / 180;
        return new(Math.Cos(a), -Math.Sin(a), Math.Sin(a), Math.Cos(a), 0, 0);
    }

    public static Affine About(double x, double y, Affine map) => Translate(-x, -y).Then(map).Then(Translate(x, y));

    /// <summary>This map followed by <paramref name="next"/>.</summary>
    public Affine Then(Affine next) => new(
        next.A * A + next.B * C, next.A * B + next.B * D,
        next.C * A + next.D * C, next.C * B + next.D * D,
        next.A * E + next.B * F + next.E, next.C * E + next.D * F + next.F);

    public (double X, double Y) Apply(double x, double y) => (A * x + B * y + E, C * x + D * y + F);

    public Affine Inverse()
    {
        var det = A * D - B * C;
        var (a, b, c, d) = (D / det, -B / det, -C / det, A / det);
        return new(a, b, c, d, -(a * E + b * F), -(c * E + d * F));
    }
}
