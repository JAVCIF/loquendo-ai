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
    TinyProp,
    /// <summary>A render or prop without transparency: its background shows as a box (1.4.7).</summary>
    OpaqueBackground,
    /// <summary>Strict review (1.4.7): a render or prop without warnings, listed so its size can be changed too.</summary>
    Review
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
/// <item>Small characters (1.4.7), by the height of their opaque body: against the frame (under 60 % of its height,
/// 45 % for a wide render), alone or with others; and against whoever is on screen at the same time (20 % shorter,
/// 35 % for a wide render). All characters are people, ponies included. The fix is the render's «Escala», which also
/// enlarges what the automatic framing gives (past its lane if needed). Wide renders get a safer enlargement.</item>
/// <item>Tiny props: 64 px or less, or under 100 px and much smaller than the characters around.</item>
/// <item>Images without transparency: the background shows as a box.</item>
/// </list>
/// One issue per block: when a block has several problems, they go together.
/// </summary>
public static class FramingAudit
{
    public const double MinInside = 0.85;
    private const double TargetInside = 0.97;
    private const double Frame = 720;

    private sealed record Placed(SceneMedia Clip, SceneScriptBlock Block, string Name, LayerLayout Layout,
        (double X0, double Y0, double X1, double Y1) Visible, RenderSurface Surface, long Start, long End, int Order);

    /// <param name="strict">Strict review (1.4.7): besides the warnings, every render and prop of the scene, each with
    /// «Cambiar tamaño», so any of them can be adjusted by hand.</param>
    public static async Task<IReadOnlyList<FramingIssue>> AuditAsync(IReadOnlyList<SceneScriptBlock> blocks, SceneComposition planned,
        Func<SceneScriptBlock, string> name, CancellationToken token = default, bool strict = false)
    {
        var prepared = await CharacterFraming.ApplyAsync(planned, token);
        var byId = blocks.ToDictionary(x => x.Id);
        var spans = VegasBridge.BuildClips(prepared).GroupBy(x => x.BlockId)
            .ToDictionary(x => x.Key, x => (Start: x.Min(y => y.StartMs), End: x.Max(y => y.StartMs + y.DurationMs)));
        var placed = new List<Placed>();
        var drawOrder = prepared.Media.Select((x, i) => (x.BlockId, i)).ToDictionary(x => x.BlockId, x => x.i); // later = on top
        foreach (var clip in prepared.Media.Where(x => x.Kind is ScriptBlockKind.CharacterShow or ScriptBlockKind.Image))
        {
            token.ThrowIfCancellationRequested();
            if (!byId.TryGetValue(clip.BlockId, out var block) || string.IsNullOrWhiteSpace(clip.Path) || !File.Exists(clip.Path)) continue;
            int width, height;
            try { (width, height) = await SceneComposer.ProbeDimensionsAsync(clip.Path, token); }
            catch (InvalidDataException) { continue; }
            if (LayerGeometry.Place(LayerSpec.From(clip), width, height, 1280, 720) is not { } layout) continue;
            var surface = await CharacterFraming.SurfaceAsync(clip.Path, token);
            var span = spans.GetValueOrDefault(clip.BlockId, (clip.StartMs, clip.StartMs + Math.Max(1, clip.DurationMs)));
            placed.Add(new Placed(clip, block, name(block), layout, VisibleRect(layout, surface.Solid, width, height), surface,
                span.Item1, span.Item2, drawOrder[clip.BlockId]));
        }
        var issues = new List<FramingIssue>();
        foreach (var item in placed) if (OutOfFrame(item, prepared) is { } issue) issues.Add(issue);
        issues.AddRange(Sizes(placed, CharacterFraming.MaximumVisibleCharacters(prepared)));
        foreach (var item in placed.Where(x => x.Surface.Opaque && !x.Clip.GreenScreen))
            issues.Add(new FramingIssue(item.Clip.BlockId, FramingProblem.OpaqueBackground,
                $"{item.Name}: la imagen no tiene transparencia" + (item.Surface.Background is { } color ? $" (fondo #{color})" : "") +
                ", se verá el recuadro; usa un render con fondo transparente", []));
        if (strict)
            foreach (var item in placed)
            {
                var height = item.Visible.Y1 - item.Visible.Y0;
                issues.Add(new FramingIssue(item.Clip.BlockId, FramingProblem.Review,
                    $"{item.Name}: {Math.Round(height).ToString(CultureInfo.InvariantCulture)} px de alto, el {(int)Math.Round(height / Frame * 100)} % del cuadro",
                    [new FramingFix("tamano", "Cambiar tamaño", ScaleFix(item.Block, 1))], 1));
            }
        return issues.GroupBy(x => x.BlockId).Select(Merge)
            .OrderBy(x => byId[x.BlockId].OrderIndex).ThenBy(x => x.Problem).ToArray();
    }

    /// <summary>Several problems of one block become one issue (the review and its memory handle one per block).</summary>
    private static FramingIssue Merge(IEnumerable<FramingIssue> group)
    {
        var all = group.OrderBy(x => x.Problem).ToArray();
        if (all.Length == 1) return all[0];
        var warnings = all.Where(x => x.Problem != FramingProblem.Review).ToArray();
        return all[0] with
        {
            Message = string.Join(" · ", (warnings.Length > 0 ? warnings : all).Select(x => x.Message)),
            Fixes = all.SelectMany(x => x.Fixes).DistinctBy(x => x.Id).ToArray(),
            Scale = all.Select(x => x.Scale).FirstOrDefault(x => x is not null)
        };
    }

    /// <summary>The fields a fix changes, written over the block's parameters.</summary>
    public static BlockParameters Apply(BlockParameters current, BlockParameters changes) => current with
    {
        VisualOffsetX = changes.VisualOffsetX ?? current.VisualOffsetX,
        VisualOffsetY = changes.VisualOffsetY ?? current.VisualOffsetY,
        MotionOffsetX = changes.MotionOffsetX ?? current.MotionOffsetX,
        MotionOffsetY = changes.MotionOffsetY ?? current.MotionOffsetY,
        Scale = changes.Scale ?? current.Scale,
        VisualMaxWidth = changes.VisualMaxWidth ?? current.VisualMaxWidth,
        VisualMaxHeight = changes.VisualMaxHeight ?? current.VisualMaxHeight,
        FramingPreset = changes.FramingPreset ?? current.FramingPreset
    };

    /// <summary>The size fix with another enlargement (the user's percentage): a render's «Escala» times
    /// <paramref name="scale"/> (1.4.7: it also enlarges the automatic framing); a prop's box times it, within the limits
    /// of its kind.</summary>
    public static BlockParameters ScaleFix(SceneScriptBlock block, double scale)
    {
        if (block.Kind == ScriptBlockKind.CharacterShow)
            return new BlockParameters
            {
                Scale = Math.Round(Math.Clamp(BlockParameters.Of(block).ScaleFactor(block.Kind) * scale,
                    BlockParameters.ScaleMin, BlockParameters.ScaleMax), 3)
            };
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
    private static (double, double, double, double) VisibleRect(LayerLayout layout, (double X, double Y, double W, double H) bounds,
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

    private const double MinShare = 0.6, MinShareWide = 0.45, MinShareBehind = 0.4, PartnerShare = 0.8, PartnerShareWide = 0.65,
        TargetShare = 0.78;

    private static IEnumerable<FramingIssue> Sizes(List<Placed> placed, int lanes)
    {
        var fmt = CultureInfo.InvariantCulture;
        var characters = placed.Where(x => x.Clip.Kind == ScriptBlockKind.CharacterShow).ToArray();
        double Height(Placed x) => x.Visible.Y1 - x.Visible.Y0;
        double Width(Placed x) => x.Visible.X1 - x.Visible.X0;
        bool Special(Placed x) => Width(x) > Height(x) * 1.4; // very wide: sitting, lying, with furniture (a standing pony is not)
        bool Together(Placed a, Placed b) => a.Start < b.End && b.Start < a.End;
        Guid Who(Placed x) => x.Clip.CharacterId ?? x.Clip.BlockId;
        // Behind another character (a back row): drawn under one on screen at the same time that covers part of it,
        // with the feet higher up. Smaller by design: not compared with the front row, only a lower floor.
        Placed? InFront(Placed item) => characters.Where(x => Who(x) != Who(item) && Together(x, item) && x.Order > item.Order &&
                Math.Min(x.Visible.X1, item.Visible.X1) - Math.Max(x.Visible.X0, item.Visible.X0) >= Width(item) * 0.2 &&
                x.Visible.Y1 > item.Visible.Y1 + 15)
            .MaxBy(Height);
        foreach (var item in characters)
        {
            var visible = Height(item);
            var special = Special(item);
            var front = InFront(item);
            // Whoever is on screen at the same time (another character: the render it replaces in a cross-fade is not).
            var tallest = front is not null ? null : characters.Where(x => Who(x) != Who(item) && Together(x, item)).MaxBy(Height);
            var partner = tallest is null ? 0 : Height(tallest);
            var floor = Frame * (front is not null ? MinShareBehind : special ? MinShareWide : MinShare);
            var shorter = partner > 0 && visible < partner * (special ? PartnerShareWide : PartnerShare);
            if (visible >= floor && !shorter) continue;
            // As big as a normal character (or as the tallest beside it), never past the frame; behind: just visible enough.
            var target = front is not null ? Frame * MinShare
                : Math.Min(Frame * 0.95, Math.Max(partner, Frame * TargetShare)) * (special ? 0.85 : 1);
            var scale = Math.Min(3, target / Math.Max(1, visible));
            if (scale < 1.08) continue;
            var fix = ScaleFix(item.Block, scale);
            var framing = BlockParameters.Of(item.Block).Framing(ScriptBlockKind.CharacterShow);
            var padded = item.Layout.Height > visible * 1.25;
            if (framing == "original" && padded) fix = fix with { FramingPreset = "auto" }; // trim the empty canvas
            var message = shorter && partner > 0
                ? $"{item.Name} se ve {(int)Math.Round((1 - visible / partner) * 100)} % más pequeño que {tallest!.Name} " +
                  $"({Math.Round(visible).ToString(fmt)} px de alto)"
                : $"{item.Name} se ve pequeño: {Math.Round(visible).ToString(fmt)} px de alto, el {(int)Math.Round(visible / Frame * 100)} % del cuadro";
            if (front is not null) message += $" · está detrás de {front.Name}";
            if (special) message += " · render ancho: ajuste prudente";
            // The automatic framing shares the width between the characters on screen: tell why it is narrow.
            if (framing != "original" && lanes > 1 && item.Layout.Width >= item.Clip.VisualMaxWidth - 2 &&
                item.Clip.VisualMaxWidth < BlockParameters.Of(item.Block).Transform(ScriptBlockKind.CharacterShow).MaxWidth)
                message += $" · lo limita el ancho de su carril ({lanes} personajes a la vez)";
            if (!item.Surface.Opaque && item.Surface.Bounds.H > item.Surface.Solid.H * 1.4)
                message += " · la imagen tiene un halo casi transparente alrededor";
            yield return new FramingIssue(item.Clip.BlockId, FramingProblem.Small, message,
                [new FramingFix("tamano", $"Agrandar {Math.Round((scale - 1) * 100).ToString(fmt)} %", fix)], scale);
        }
        foreach (var item in placed.Where(x => x.Clip.Kind == ScriptBlockKind.Image))
        {
            var size = Math.Max(Height(item), Width(item));
            // 64 px is the smallest box the editor allows; up to 100 px it is tiny only beside much bigger characters.
            var beside = characters.Where(x => Together(x, item)).Select(Height).DefaultIfEmpty(0).Max();
            if (size > 64 && (size >= 100 || size >= beside * 0.15)) continue;
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
