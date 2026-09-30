using System.Globalization;
using LoquendoAI.Core.Models;

namespace LoquendoAI.Infrastructure.Composition;

public enum FramingProblem
{
    /// <summary>The render (or prop) ends (or stays) partly outside the frame.</summary>
    OutOfFrame,
    /// <summary>A character much smaller than the others (or than a normal character).</summary>
    Small,
    /// <summary>A prop so small it can barely be seen.</summary>
    TinyProp
}

/// <summary>A way to fix an issue: the parameters to set on the block (null fields stay as they are).
/// Id: «limitar», «entrada» or «tamano».</summary>
public sealed record FramingFix(string Id, string Label, BlockParameters Changes);

/// <summary>A framing issue of one block. <see cref="Scale"/>: for a size issue, the proposed enlargement
/// (1.4 = 40 % bigger), which the user may change before applying (<see cref="FramingAudit.ScaleFix"/>).</summary>
public sealed record FramingIssue(Guid BlockId, FramingProblem Problem, string Message, IReadOnlyList<FramingFix> Fixes,
    double? Scale = null);

/// <summary>
/// «Revisar encuadre» (1.4.4): finds renders and props that the AI (or a hand edit) left badly framed, with the same
/// placement as the preview and VEGAS (LayerGeometry) and the visible part of each image (without transparent borders):
/// <list type="bullet">
/// <item>Out of the frame: less than 85 % of the visible part inside at the end of its animation (or, without animation,
/// where it stays). «Limitar al cuadro» shortens the move (and, if that is not enough, the offset) so it stays inside;
/// it keeps the direction, so a push or a fight still reads. «Era una entrada» is offered only for a first appearance
/// that moves outwards: it starts outside and comes in to the place it had. A move that ends right before the
/// character is hidden is an exit and is left alone.</item>
/// <item>Small characters: visibly shorter than the others (all characters are people, ponies included: they are
/// compared by visible height). Wide renders (sitting, with furniture) get a safer, smaller enlargement.</item>
/// <item>Tiny props: 64 px or less.</item>
/// </list>
/// </summary>
public static class FramingAudit
{
    public const double MinInside = 0.85;
    private const double TargetInside = 0.97;
    private const double Frame = 720;

    private sealed record Placed(SceneMedia Clip, SceneScriptBlock Block, string Name, LayerLayout Layout,
        (double X0, double Y0, double X1, double Y1) Visible);

    public static async Task<IReadOnlyList<FramingIssue>> AuditAsync(IReadOnlyList<SceneScriptBlock> blocks, SceneComposition planned,
        Func<SceneScriptBlock, string> name, CancellationToken token = default)
    {
        var prepared = await CharacterFraming.ApplyAsync(planned, token);
        var byId = blocks.ToDictionary(x => x.Id);
        var placed = new List<Placed>();
        foreach (var clip in prepared.Media.Where(x => x.Kind is ScriptBlockKind.CharacterShow or ScriptBlockKind.Image))
        {
            token.ThrowIfCancellationRequested();
            if (!byId.TryGetValue(clip.BlockId, out var block) || string.IsNullOrWhiteSpace(clip.Path) || !File.Exists(clip.Path)) continue;
            int width, height;
            try { (width, height) = await SceneComposer.ProbeDimensionsAsync(clip.Path, token); }
            catch (InvalidDataException) { continue; }
            if (LayerGeometry.Place(LayerSpec.From(clip), width, height, 1280, 720) is not { } layout) continue;
            var bounds = await CharacterFraming.VisibleBoundsAsync(clip.Path, token);
            placed.Add(new Placed(clip, block, name(block), layout, VisibleRect(layout, bounds, width, height)));
        }
        var issues = new List<FramingIssue>();
        foreach (var item in placed) if (OutOfFrame(item, prepared) is { } issue) issues.Add(issue);
        issues.AddRange(Sizes(placed));
        return issues.OrderBy(x => byId[x.BlockId].OrderIndex).ThenBy(x => x.Problem).ToArray();
    }

    /// <summary>The fields a fix changes, written over the block's parameters.</summary>
    public static BlockParameters Apply(BlockParameters current, BlockParameters changes) => current with
    {
        VisualOffsetX = changes.VisualOffsetX ?? current.VisualOffsetX,
        VisualOffsetY = changes.VisualOffsetY ?? current.VisualOffsetY,
        MotionOffsetX = changes.MotionOffsetX ?? current.MotionOffsetX,
        MotionOffsetY = changes.MotionOffsetY ?? current.MotionOffsetY,
        VisualMaxWidth = changes.VisualMaxWidth ?? current.VisualMaxWidth,
        VisualMaxHeight = changes.VisualMaxHeight ?? current.VisualMaxHeight,
        FramingPreset = changes.FramingPreset ?? current.FramingPreset
    };

    /// <summary>The size fix with another enlargement (the user's percentage): the block's box times <paramref name="scale"/>,
    /// within the limits of its kind.</summary>
    public static BlockParameters ScaleFix(SceneScriptBlock block, double scale)
    {
        var box = BlockParameters.Of(block).Transform(block.Kind);
        var max = BlockDefaults.VisualMaxBox(block.Kind);
        scale = Math.Clamp(scale, 0.2, 5);
        return new BlockParameters
        {
            VisualMaxWidth = Math.Clamp((int)Math.Round(box.MaxWidth * scale), 64, max.Width),
            VisualMaxHeight = Math.Clamp((int)Math.Round(box.MaxHeight * scale), 64, max.Height)
        };
    }

    // ------------------------------------------------------------------ geometry

    /// <summary>The visible (non-transparent) part of the layer in the frame, before rotation and motion.</summary>
    private static (double, double, double, double) VisibleRect(LayerLayout layout, (double X, double Y, double W, double H, double Aspect) bounds,
        int width, int height)
    {
        var sx0 = Math.Max(bounds.X * width, layout.SourceX);
        var sy0 = Math.Max(bounds.Y * height, layout.SourceY);
        var sx1 = Math.Min((bounds.X + bounds.W) * width, layout.SourceX + layout.SourceWidth);
        var sy1 = Math.Min((bounds.Y + bounds.H) * height, layout.SourceY + layout.SourceHeight);
        if (sx1 <= sx0 || sy1 <= sy0) return (layout.X, layout.Y, layout.X + layout.Width, layout.Y + layout.Height);
        return (layout.X + (sx0 - layout.SourceX) * layout.Scale, layout.Y + (sy0 - layout.SourceY) * layout.Scale,
            layout.X + (sx1 - layout.SourceX) * layout.Scale, layout.Y + (sy1 - layout.SourceY) * layout.Scale);
    }

    /// <summary>Share (0–1) of the visible part inside the frame, moved by (dx, dy) and turned by <paramref name="degrees"/>
    /// around the layer's centre (clockwise, like the preview). The turned part is taken by its bounding box.</summary>
    private static double Inside(Placed item, double dx, double dy, double degrees)
    {
        var (x0, y0, x1, y1) = item.Visible;
        var cx = item.Layout.CenterX + dx;
        var cy = item.Layout.CenterY + dy;
        (x0, y0, x1, y1) = (x0 + dx, y0 + dy, x1 + dx, y1 + dy);
        if (Math.Abs(degrees) > 0.01)
        {
            var r = degrees * Math.PI / 180;
            var (cos, sin) = (Math.Cos(r), Math.Sin(r));
            var corners = new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) }
                .Select(p => (X: cx + (p.Item1 - cx) * cos - (p.Item2 - cy) * sin, Y: cy + (p.Item1 - cx) * sin + (p.Item2 - cy) * cos)).ToArray();
            (x0, y0, x1, y1) = (corners.Min(p => p.X), corners.Min(p => p.Y), corners.Max(p => p.X), corners.Max(p => p.Y));
        }
        var area = (x1 - x0) * (y1 - y0);
        if (area <= 0) return 1;
        var w = Math.Max(0, Math.Min(x1, 1280) - Math.Max(x0, 0));
        var h = Math.Max(0, Math.Min(y1, 720) - Math.Max(y0, 0));
        return w * h / area;
    }

    // ------------------------------------------------------------------ out of frame

    private static FramingIssue? OutOfFrame(Placed item, SceneComposition scene)
    {
        var clip = item.Clip;
        var (mx, my) = (clip.MotionOffsetX, clip.MotionOffsetY);
        var moves = mx != 0 || my != 0;
        var start = Inside(item, 0, 0, clip.RotationDegrees);
        var end = Inside(item, mx, my, clip.RotationDegrees + clip.MotionRotationDegrees);
        if (end >= MinInside) return null;
        if (moves && IsExit(clip, scene)) return null;
        var mirror = BlockParameters.Of(item.Block).FlipHorizontal == true;
        int BlockX(int mediaX) => mirror ? -mediaX : mediaX;
        var fixes = new List<FramingFix>();

        // «Limitar al cuadro»: the same direction, a shorter move; if the place itself is out, the offset too.
        double t = 1, u = 1;
        if (moves)
        {
            t = Search(k => Inside(item, mx * k, my * k, clip.RotationDegrees + clip.MotionRotationDegrees) >= TargetInside);
            if (t <= 0.001) t = 0;
        }
        if (Inside(item, mx * t, my * t, clip.RotationDegrees + clip.MotionRotationDegrees) < TargetInside)
        {
            // The offset puts it out: bring the offset and the move back together towards the position
            // (offset·k and move·k; k = 0 is the plain position).
            var (ox, oy) = (clip.VisualOffsetX, clip.VisualOffsetY);
            u = Search(k => Inside(item, (k - 1) * ox + k * mx, (k - 1) * oy + k * my,
                clip.RotationDegrees + clip.MotionRotationDegrees) >= TargetInside);
            t = moves ? u : 0;
        }
        var limited = new BlockParameters
        {
            VisualOffsetX = BlockX((int)Math.Round(clip.VisualOffsetX * u)),
            VisualOffsetY = (int)Math.Round(clip.VisualOffsetY * u),
            MotionOffsetX = BlockX((int)Math.Round(mx * t)),
            MotionOffsetY = (int)Math.Round(my * t)
        };
        fixes.Add(new FramingFix("limitar", "Limitar al cuadro", limited));

        // «Era una entrada»: a first appearance that starts inside and moves out → starts outside and comes in.
        if (moves && start >= 0.9 && FirstAppearance(clip, scene))
            fixes.Add(new FramingFix("entrada", "Era una entrada", new BlockParameters
            {
                VisualOffsetX = BlockX(clip.VisualOffsetX + mx),
                VisualOffsetY = clip.VisualOffsetY + my,
                MotionOffsetX = BlockX(-mx),
                MotionOffsetY = -my
            }));
        var outside = (int)Math.Round((1 - end) * 100);
        var message = moves
            ? $"{item.Name} termina {outside} % fuera del cuadro"
            : $"{item.Name} queda {outside} % fuera del cuadro";
        return new FramingIssue(clip.BlockId, FramingProblem.OutOfFrame, message, fixes);
    }

    /// <summary>Largest k in [0, 1] with ok(k) (ok(0) is assumed; ok is monotonic enough for a bisection).</summary>
    private static double Search(Func<double, bool> ok)
    {
        if (ok(1)) return 1;
        double lo = 0, hi = 1;
        for (var i = 0; i < 24; i++)
        {
            var mid = (lo + hi) / 2;
            if (ok(mid)) lo = mid; else hi = mid;
        }
        return lo;
    }

    /// <summary>The move ends right before this render leaves (hidden, not replaced): an exit.</summary>
    private static bool IsExit(SceneMedia clip, SceneComposition scene)
    {
        var key = SceneComposer.RenderKey(clip);
        var next = scene.Media.SkipWhile(x => x.BlockId != clip.BlockId).Skip(1)
            .FirstOrDefault(x => x.Kind is ScriptBlockKind.CharacterShow or ScriptBlockKind.CharacterHide && SceneComposer.RenderKey(x) == key);
        if (next is null || next.Kind != ScriptBlockKind.CharacterHide) return false;
        var motionEnd = clip.StartMs + (clip.MotionDurationMs > 0 ? clip.MotionDurationMs : next.StartMs - clip.StartMs);
        return next.StartMs - motionEnd <= 1500;
    }

    /// <summary>Nobody with this render/character is on screen when it appears (the scene start, or after a hide).</summary>
    private static bool FirstAppearance(SceneMedia clip, SceneComposition scene)
    {
        var key = SceneComposer.RenderKey(clip);
        var before = scene.Media.TakeWhile(x => x.BlockId != clip.BlockId)
            .LastOrDefault(x => x.Kind is ScriptBlockKind.CharacterShow or ScriptBlockKind.CharacterHide && SceneComposer.RenderKey(x) == key);
        return before is null || before.Kind == ScriptBlockKind.CharacterHide;
    }

    // ------------------------------------------------------------------ sizes

    private static IEnumerable<FramingIssue> Sizes(List<Placed> placed)
    {
        var fmt = CultureInfo.InvariantCulture;
        var characters = placed.Where(x => x.Clip.Kind == ScriptBlockKind.CharacterShow).ToArray();
        double Height(Placed x) => x.Visible.Y1 - x.Visible.Y0;
        double Width(Placed x) => x.Visible.X1 - x.Visible.X0;
        bool Special(Placed x) => Width(x) > Height(x) * 1.4; // very wide: sitting, lying, with furniture (a standing pony is not)
        if (characters.Length > 0)
        {
            // All characters are people (ponies too): the reference is the usual height of the well-sized, ordinary
            // renders of the scene; without any, a normal character (80 % of the frame). Between half and 95 % of the frame.
            var normal = characters.Where(x => !Special(x) && Height(x) >= Frame * 0.35).Select(Height).OrderBy(x => x).ToArray();
            var reference = Math.Clamp(normal.Length > 0 ? normal[normal.Length / 2] : Frame * 0.8, Frame * 0.5, Frame * 0.95);
            foreach (var item in characters)
            {
                var visible = Height(item);
                var special = Special(item);
                if (visible >= reference * 0.8 && visible >= Frame * 0.35) continue;
                // Already as wide as its place in the scene allows (automatic framing shares the width between the
                // characters on screen): enlarging the block would change nothing.
                var box = BlockParameters.Of(item.Block).Transform(ScriptBlockKind.CharacterShow);
                if (item.Clip.VisualMaxWidth < box.MaxWidth && item.Layout.Width >= item.Clip.VisualMaxWidth - 2) continue;
                var target = reference * (special ? 0.85 : 1);
                var scale = Math.Min(3, target / Math.Max(1, visible));
                if (scale < 1.08) continue;
                var fix = ScaleFix(item.Block, scale);
                var framing = BlockParameters.Of(item.Block).Framing(ScriptBlockKind.CharacterShow);
                var padded = item.Layout.Height > visible * 1.25;
                if (framing == "original" && padded) fix = fix with { FramingPreset = "auto" }; // trim the empty canvas
                var smaller = (int)Math.Round((1 - visible / reference) * 100);
                var message = smaller >= 10
                    ? $"{item.Name} se ve {smaller} % más pequeño que el resto ({Math.Round(visible).ToString(fmt)} px de alto)"
                    : $"{item.Name} se ve muy pequeño ({Math.Round(visible).ToString(fmt)} px de alto)";
                if (special) message += " · render ancho: ajuste prudente";
                yield return new FramingIssue(item.Clip.BlockId, FramingProblem.Small, message,
                    [new FramingFix("tamano", $"Agrandar {Math.Round((scale - 1) * 100).ToString(fmt)} %", fix)], scale);
            }
        }
        foreach (var item in placed.Where(x => x.Clip.Kind == ScriptBlockKind.Image))
        {
            var size = Math.Max(Height(item), Width(item));
            if (size > 64) continue; // 64 px: the smallest box the editor allows
            var box = BlockDefaults.VisualBox(ScriptBlockKind.Image);
            var current = BlockParameters.Of(item.Block).Transform(ScriptBlockKind.Image);
            var scale = Math.Max(box.Width / (double)current.MaxWidth, box.Height / (double)current.MaxHeight);
            yield return new FramingIssue(item.Clip.BlockId, FramingProblem.TinyProp,
                $"{item.Name} mide {Math.Round(size).ToString(fmt)} px: casi no se ve",
                [new FramingFix("tamano", "Tamaño normal de prop", new BlockParameters { VisualMaxWidth = box.Width, VisualMaxHeight = box.Height })],
                scale);
        }
    }
}
