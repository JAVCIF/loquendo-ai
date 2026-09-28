using System.Globalization;
using LoquendoAI.Core.Models;

namespace LoquendoAI.Infrastructure.Composition;

/// <summary>
/// One of the two letterbox configurations of VEGAS «Cortador de galletas» (Cookie Cutter): black
/// rectangle, «cortar todo excepto la sección», with a border and a size. Size <see cref="OpenSize"/>
/// shows the bars, <see cref="ClosedSize"/> removes them; animating the size opens or closes them.
/// In between, the visible band is Size / ClosedSize of the frame height (calibrated on VEGAS: at the
/// «cerrado» open size the bars cover about 12.5 % of the height each).
/// </summary>
public sealed record CinemaStyle(string Name, double Border, double OpenSize, double ClosedSize)
{
    public static readonly CinemaStyle Closed = new("cerrado", 1.0, 0.748, 1.0);
    public static readonly CinemaStyle Open = new("abierto", 0.56, 0.639, 0.747);

    public static CinemaStyle Of(string name) => name == "abierto" ? Open : Closed;

    /// <summary>Height of EACH bar as a fraction of the frame height, for a Cookie Cutter size.</summary>
    public double BarFraction(double size) => Math.Clamp((1 - size / ClosedSize) / 2, 0, 0.5);
}

/// <summary>A cinema block on the scene clock.</summary>
public sealed record CinemaCue(long StartMs, CinemaSettings Settings);

/// <summary>
/// Bars on screen from <see cref="StartMs"/> to <see cref="EndMs"/> (exclusive; long.MaxValue = until the
/// end), with the Cookie Cutter size moving linearly from <see cref="FromSize"/> to <see cref="ToSize"/>,
/// on the layers named by <see cref="Layers"/>/<see cref="Characters"/>.
/// </summary>
/// After <see cref="CinemaPlan.Resolve"/>, bars on some layers only name the exact visual blocks that carry
/// the effect in <see cref="Blocks"/>.
public sealed record CinemaSegment(long StartMs, long EndMs, string Style, double FromSize, double ToSize,
    string Layers, IReadOnlyList<Guid> Characters, IReadOnlyList<Guid>? Blocks = null)
{
    public CinemaStyle Config => CinemaStyle.Of(Style);

    public double SizeAt(long timeMs) => EndMs == long.MaxValue || EndMs <= StartMs ? ToSize
        : FromSize + (ToSize - FromSize) * Math.Clamp((timeMs - StartMs) / (double)(EndMs - StartMs), 0, 1);

    public bool Affects(ScriptBlockKind kind, Guid? characterId, Guid? blockId = null) => Blocks is not null
        ? blockId is Guid block && Blocks.Contains(block)
        : Layers switch
    {
        "personajes" => kind == ScriptBlockKind.CharacterShow,
        "lista" => kind == ScriptBlockKind.CharacterShow && characterId is Guid id && Characters.Contains(id),
        _ => kind is ScriptBlockKind.Background or ScriptBlockKind.CharacterShow or ScriptBlockKind.Image or ScriptBlockKind.Video
    };
}

/// <summary>Cookie Cutter border and size of an event <see cref="AtMs"/> after it starts (VEGAS keyframes).</summary>
public sealed record CinemaKey(long AtMs, double Border, double Size);

/// <summary>Bars state at the start of a scene (the end of the previous scene of the episode).</summary>
public sealed record CinemaState(string Style, string Layers, IReadOnlyList<Guid> Characters);

/// <summary>
/// Rules of the cinematic bars:
/// <list type="bullet">
/// <item>[CINE] mostrar opens the bars in «duracion» ms (0 = at once); [CINE] quitar closes them.</item>
/// <item>They stay until [CINE] quitar, also into the next scenes of the episode: a whole episode can be
/// «in cinema» with a single [CINE] mostrar in its first scene.</item>
/// <item>A new [CINE] mostrar with another style switches at once; with other layers it only changes
/// which layers carry the effect.</item>
/// <item>In VEGAS the effect goes on the events of the chosen layers with Border/Size keyframes (<see cref="Keys"/>);
/// the bars never cut an event (so they cannot break a «cruce»). Layers drawn in front of a chosen one get the
/// effect too (<see cref="Resolve"/>).</item>
/// </list>
/// </summary>
public static class CinemaPlan
{
    public static IReadOnlyList<CinemaSegment> Build(IReadOnlyList<CinemaCue> cues, CinemaState? initial)
    {
        var segments = new List<CinemaSegment>();
        if (initial is not null)
        {
            var style = CinemaStyle.Of(initial.Style);
            segments.Add(new CinemaSegment(0, long.MaxValue, style.Name, style.OpenSize, style.OpenSize, initial.Layers, initial.Characters));
        }
        foreach (var cue in cues.OrderBy(x => x.StartMs))
        {
            var t = Math.Max(0, cue.StartMs);
            var current = segments.LastOrDefault(x => x.StartMs <= t && t < x.EndMs);
            var style = CinemaStyle.Of(cue.Settings.Style);
            // Cut whatever was planned from t on.
            segments = segments.Where(x => x.StartMs < t).Select(x => x.EndMs > t
                ? x with { EndMs = t, ToSize = x.SizeAt(t) } : x).ToList();
            var move = cue.Settings.MoveMs;
            if (cue.Settings.Show)
            {
                var from = current is not null && current.Style == style.Name ? current.SizeAt(t) : style.ClosedSize;
                if (current is not null && current.Style != style.Name) from = style.OpenSize; // style switch: at once
                if (move > 0 && Math.Abs(from - style.OpenSize) > 0.0001)
                {
                    segments.Add(new CinemaSegment(t, t + move, style.Name, from, style.OpenSize, cue.Settings.Layers, cue.Settings.Characters));
                    segments.Add(new CinemaSegment(t + move, long.MaxValue, style.Name, style.OpenSize, style.OpenSize, cue.Settings.Layers, cue.Settings.Characters));
                }
                else segments.Add(new CinemaSegment(t, long.MaxValue, style.Name, style.OpenSize, style.OpenSize, cue.Settings.Layers, cue.Settings.Characters));
            }
            else if (current is not null)
            {
                var config = current.Config;
                var from = current.SizeAt(t);
                if (move > 0 && Math.Abs(from - config.ClosedSize) > 0.0001)
                    segments.Add(current with { StartMs = t, EndMs = t + move, FromSize = from, ToSize = config.ClosedSize });
            }
        }
        // Consecutive still segments with the same look are one (fewer cuts in VEGAS).
        return Merge(segments);
    }

    private static bool SameBlocks(IReadOnlyList<Guid>? a, IReadOnlyList<Guid>? b) =>
        a is null ? b is null : b is not null && a.Count == b.Count && a.All(b.Contains);

    private static IReadOnlyList<CinemaSegment> Merge(IEnumerable<CinemaSegment> segments)
    {
        var merged = new List<CinemaSegment>();
        foreach (var segment in segments.Where(x => x.EndMs > x.StartMs))
        {
            if (merged.Count > 0 && merged[^1] is var last && last.EndMs == segment.StartMs && last.Style == segment.Style &&
                Math.Abs(last.FromSize - last.ToSize) < 0.0001 && Math.Abs(segment.FromSize - segment.ToSize) < 0.0001 &&
                Math.Abs(last.ToSize - segment.FromSize) < 0.0001 && last.Layers == segment.Layers &&
                last.Characters.SequenceEqual(segment.Characters) && SameBlocks(last.Blocks, segment.Blocks))
                merged[^1] = last with { EndMs = segment.EndMs };
            else merged.Add(segment);
        }
        return merged;
    }

    /// <summary>
    /// Bars on some layers only («personajes» or a list of characters), resolved to the exact blocks that
    /// carry the effect, per stretch of time. The effect covers everything drawn below the layer that carries
    /// it; any visual layer drawn ABOVE it (a character or prop in front) gets the same effect, or it would
    /// show over the bars. While none of the chosen layers is on screen there are no bars. Bars on every
    /// layer («todos») are left as they are.
    /// </summary>
    public static SceneComposition Resolve(SceneComposition scene)
    {
        if (scene.Cinema is not { Count: > 0 } cinema || cinema.All(x => x.Layers == "todos" || x.Blocks is not null)) return scene;
        var layers = VegasBridge.BuildClips(scene).Where(x => x.Layer >= 0)
            .Select(x => (x.BlockId, Kind: Enum.Parse<ScriptBlockKind>(x.Kind), x.CharacterId, x.StartMs, EndMs: x.StartMs + x.DurationMs, x.Layer))
            .ToArray();
        var resolved = new List<CinemaSegment>();
        foreach (var segment in cinema)
        {
            if (segment.Layers == "todos" || segment.Blocks is not null) { resolved.Add(segment); continue; }
            var end = Math.Min(segment.EndMs, scene.DurationMs);
            var times = layers.SelectMany(x => new[] { x.StartMs, x.EndMs }).Where(t => t > segment.StartMs && t < end)
                .Append(segment.StartMs).Append(end).Distinct().Order().ToArray();
            for (var i = 0; i + 1 < times.Length; i++)
            {
                long a = times[i], b = times[i + 1];
                var visible = layers.Where(x => x.StartMs < b && x.EndMs > a).ToArray();
                var chosen = visible.Where(x => segment.Affects(x.Kind, x.CharacterId)).ToArray();
                if (chosen.Length == 0) continue;
                var lowest = chosen.Min(x => x.Layer);
                var blocks = visible.Where(x => x.Layer >= lowest).Select(x => x.BlockId).Distinct().ToArray();
                resolved.Add(segment with
                {
                    StartMs = a, EndMs = b == end && segment.EndMs > end ? segment.EndMs : b,
                    FromSize = segment.SizeAt(a), ToSize = segment.SizeAt(b), Blocks = blocks
                });
            }
        }
        return scene with { Cinema = Merge(resolved.OrderBy(x => x.StartMs)) };
    }

    /// <summary>Bars state when a scene ends: what the next scene of the episode starts with.</summary>
    public static CinemaState? EndState(IEnumerable<SceneScriptBlock> blocks, CinemaState? initial)
    {
        var state = initial;
        foreach (var block in blocks.Where(x => x.Kind == ScriptBlockKind.Cinema).OrderBy(x => x.OrderIndex))
        {
            var settings = BlockParameters.Of(block).Cinema();
            state = settings.Show ? new CinemaState(settings.Style, settings.Layers, settings.Characters) : null;
        }
        return state;
    }

    /// <summary>
    /// Cookie Cutter keyframes for the part [start, end) of a layer (times relative to start), from the segments
    /// that affect it. No event cuts are needed: while there are no bars the effect sits at the size that removes
    /// them (1,0 «cerrado», 0,747 «abierto»), a jump (bars at once, a style change) is a keyframe 1 ms before
    /// plus one at the jump, and a move is a linear ramp. Null when no bars touch that part.
    /// </summary>
    public static IReadOnlyList<CinemaKey>? Keys(IReadOnlyList<CinemaSegment> segments, long startMs, long endMs)
    {
        var inside = segments.Where(x => x.StartMs < endMs && x.EndMs > startMs).ToArray();
        if (inside.Length == 0 || endMs <= startMs) return null;
        CinemaKey Value(long at, bool before)
        {
            var probe = before ? at - 1 : at;
            var segment = segments.LastOrDefault(x => x.StartMs <= probe && probe < x.EndMs);
            if (segment is not null) return new CinemaKey(at - startMs, segment.Config.Border, segment.SizeAt(at));
            // No bars: the size that removes them, in the style of the nearest bars (no visible change).
            var nearest = segments.Where(x => x.StartMs > probe).OrderBy(x => x.StartMs).FirstOrDefault() ??
                segments.Where(x => x.EndMs <= probe).OrderByDescending(x => x.EndMs).First();
            return new CinemaKey(at - startMs, nearest.Config.Border, nearest.Config.ClosedSize);
        }
        var keys = new List<CinemaKey> { Value(startMs, false) };
        foreach (var at in inside.SelectMany(x => new[] { x.StartMs, x.EndMs }).Where(x => x > startMs && x < endMs).Distinct().Order())
        {
            var (left, right) = (Value(at, true), Value(at, false));
            if (Math.Abs(left.Border - right.Border) > 0.00001 || Math.Abs(left.Size - right.Size) > 0.00001)
                keys.Add(left with { AtMs = left.AtMs - 1 });
            keys.Add(right);
        }
        keys.Add(Value(endMs, true));
        // Keys on a straight line (a hold, or the middle of one ramp) add nothing.
        var result = new List<CinemaKey>();
        foreach (var key in keys.Where(x => x.AtMs >= 0).DistinctBy(x => x.AtMs))
        {
            if (result.Count >= 2 && Linear(result[^2], result[^1], key)) result[^1] = key;
            else result.Add(key);
        }
        return result;
    }

    private static bool Linear(CinemaKey a, CinemaKey b, CinemaKey c)
    {
        if (c.AtMs <= a.AtMs || Math.Abs(a.Border - b.Border) > 0.00001 || Math.Abs(b.Border - c.Border) > 0.00001) return false;
        var expected = a.Size + (c.Size - a.Size) * (b.AtMs - a.AtMs) / (double)(c.AtMs - a.AtMs);
        return Math.Abs(expected - b.Size) < 0.00001;
    }

    /// <summary>The segment of <paramref name="segments"/> on screen at a time, if any, that affects a layer.</summary>
    public static CinemaSegment? At(IReadOnlyList<CinemaSegment> segments, long timeMs, ScriptBlockKind kind, Guid? characterId,
        Guid? blockId = null) =>
        segments.LastOrDefault(x => x.StartMs <= timeMs && timeMs < x.EndMs && x.Affects(kind, characterId, blockId));

    /// <summary>
    /// FFmpeg filters that draw both bars over <paramref name="input"/> for the given segments (a black
    /// band above and one below whose height follows the Cookie Cutter size per frame).
    /// </summary>
    /// <param name="camera">When the bars are drawn BEFORE the camera zooms the composition (bars on some
    /// layers only), the camera path: the bars are placed so that, once zoomed, they keep their size on screen
    /// (VEGAS applies the Cookie Cutter after the event Pan/Crop).</param>
    public static IEnumerable<string> PreviewFilters(IReadOnlyList<CinemaSegment> segments, string input, string output,
        string tag, long sceneMs, CameraPath? camera = null)
    {
        if (segments.Count == 0) yield break;
        var terms = new List<string>();
        foreach (var segment in segments)
        {
            var config = segment.Config;
            var a = Seconds(segment.StartMs);
            var condition = segment.EndMs == long.MaxValue ? $"gte(t,{a})" : $"gte(t,{a})*lt(t,{Seconds(segment.EndMs)})";
            var from = config.BarFraction(segment.FromSize) * 720;
            var to = config.BarFraction(segment.ToSize) * 720;
            var value = segment.EndMs == long.MaxValue || Math.Abs(to - from) < 0.001 ? Number(to)
                : $"({Number(from)}+({Number(to - from)})*(t-{a})/{Seconds(segment.EndMs - segment.StartMs)})";
            terms.Add($"{condition}*{value}");
        }
        var bar = string.Join("+", terms);
        var duration = Seconds(Math.Max(40, sceneMs));
        yield return $"color=c=black:s=1280x720:r=25:d={duration},format=rgba,split=2[{tag}_top][{tag}_bottom]";
        string top = "(" + bar + ")-720", bottom = "720-(" + bar + ")";
        if (camera is { IsStill: false })
        {
            // Screen row r of the zoomed picture is row Y + r·W/1280 of the composition.
            var y = camera.Expression(w => w.Y);
            var width = camera.Expression(w => w.Width);
            top = $"({y})+({bar})*({width})/1280-720";
            bottom = $"({y})+(720-({bar}))*({width})/1280";
        }
        yield return $"{input}[{tag}_top]overlay=x=0:y='{top}':eval=frame:format=auto[{tag}_mid]";
        yield return $"[{tag}_mid][{tag}_bottom]overlay=x=0:y='{bottom}':eval=frame:format=auto{output}";
    }

    private static string Seconds(long ms) => (ms / 1000d).ToString("0.###", CultureInfo.InvariantCulture);
    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
