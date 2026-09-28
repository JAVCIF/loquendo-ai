using System.Globalization;
using LoquendoAI.Core.Models;

namespace LoquendoAI.Infrastructure.Composition;

/// <summary>A blur block on the scene clock. <see cref="CharacterId"/>: the character when the target is one.</summary>
public sealed record BlurCue(long StartMs, BlurSettings Settings, Guid? CharacterId);

/// <summary>Blur of a layer (the VEGAS «Desenfoque gaussiano» range, the same horizontally and vertically)
/// at <see cref="AtMs"/>; linear between keys.</summary>
public sealed record BlurKey(long AtMs, double Amount);

/// <summary>
/// Blur ([DESENFOQUE]), after the VEGAS «Desenfoque gaussiano» presets: «Suavizar» (range 0,0200) and
/// «Desenfoque ligero» (0,0100), horizontal and vertical always equal, 0 = sharp.
/// <list type="bullet">
/// <item>A blur block moves the level of its target (fondo, a character, personajes, imagenes, videos or todos)
/// from where it is to the new one in «duracion» ms (0 = at once). The level stays until another block changes it.</item>
/// <item>It is a state of the target, not of one file: a background or render that appears later is blurred too.</item>
/// <item>VEGAS: the effect goes on the events after Pan/Crop and before the Cookie Cutter, so the camera does not
/// change how blurred things look and the cinema bars stay sharp; the range is keyframed per frame while it moves.</item>
/// </list>
/// </summary>
public static class BlurPlan
{
    public const double Soft = 0.02;
    public const double Light = 0.01;
    public const double Max = 0.1;

    /// <summary>FFmpeg gblur sigma (1280-wide pixels) per unit of VEGAS range: the range is taken as a fraction of the
    /// frame width covering about three sigmas. Calibrated by eye; adjust if VEGAS looks stronger or softer.</summary>
    public const double SigmaPerRange = 1280 / 3d;

    public static string StyleName(double amount) => amount <= 0 ? "quitar"
        : Math.Abs(amount - Soft) < 0.0001 ? "suavizar" : Math.Abs(amount - Light) < 0.0001 ? "ligero"
        : amount.ToString("0.####", CultureInfo.InvariantCulture);

    public static bool Affects(BlurCue cue, ScriptBlockKind kind, string videoLayer, Guid? characterId) => cue.Settings.Target switch
    {
        "fondo" => kind == ScriptBlockKind.Background || kind == ScriptBlockKind.Video && videoLayer == "fondo",
        "personajes" => kind == ScriptBlockKind.CharacterShow,
        "personaje" => kind == ScriptBlockKind.CharacterShow && cue.CharacterId is not null && characterId == cue.CharacterId,
        "imagenes" => kind == ScriptBlockKind.Image,
        "videos" => kind == ScriptBlockKind.Video && videoLayer != "fondo",
        _ => kind is ScriptBlockKind.Background or ScriptBlockKind.CharacterShow or ScriptBlockKind.Image or ScriptBlockKind.Video
    };

    /// <summary>Blur level over the scene of a layer of this kind/character (empty = never blurred).</summary>
    public static IReadOnlyList<BlurKey> Curve(IReadOnlyList<BlurCue>? cues, ScriptBlockKind kind, string videoLayer, Guid? characterId)
    {
        var keys = new List<BlurKey>();
        foreach (var cue in (cues ?? []).OrderBy(x => x.StartMs))
        {
            if (!Affects(cue, kind, videoLayer, characterId)) continue;
            var at = Math.Max(0, cue.StartMs);
            var current = At(keys, at);
            keys.RemoveAll(x => x.AtMs > at);
            keys.Add(new BlurKey(at, current));
            keys.Add(new BlurKey(at + Math.Max(0, cue.Settings.MoveMs), Math.Clamp(cue.Settings.Amount, 0, Max)));
        }
        return keys.All(x => x.Amount <= 0) ? [] : keys;
    }

    /// <summary>Level at a time: linear between keys; at a jump (two keys at one time) the later one.</summary>
    public static double At(IReadOnlyList<BlurKey> keys, long timeMs)
    {
        for (var i = keys.Count - 1; i >= 0; i--)
        {
            if (keys[i].AtMs > timeMs) continue;
            if (i + 1 < keys.Count && keys[i + 1].AtMs > keys[i].AtMs && keys[i + 1].AtMs > timeMs)
                return keys[i].Amount + (keys[i + 1].Amount - keys[i].Amount) *
                    (timeMs - keys[i].AtMs) / (double)(keys[i + 1].AtMs - keys[i].AtMs);
            return keys[i].Amount;
        }
        return 0;
    }

    /// <summary>
    /// The level from <paramref name="startMs"/> to <paramref name="endMs"/> as keys relative to the start: one
    /// per frame while it changes, one per change otherwise. Null when it stays at 0.
    /// </summary>
    public static IReadOnlyList<BlurKey>? Slice(IReadOnlyList<BlurKey> keys, long startMs, long endMs)
    {
        if (keys.Count == 0 || endMs <= startMs) return null;
        var times = new SortedSet<long> { startMs };
        for (var i = 0; i + 1 < keys.Count; i++)
        {
            var (a, b) = (keys[i], keys[i + 1]);
            if (b.AtMs > startMs && b.AtMs < endMs) times.Add(b.AtMs);
            if (a.AtMs > startMs && a.AtMs < endMs) times.Add(a.AtMs);
            if (b.AtMs <= a.AtMs || Math.Abs(b.Amount - a.Amount) < 0.000001) continue;
            for (var t = a.AtMs + GesturePlan.FrameMs; t < b.AtMs; t += GesturePlan.FrameMs)
                if (t > startMs && t < endMs) times.Add(t);
        }
        var result = times.Select(t => new BlurKey(t - startMs, At(keys, t))).ToList();
        // Jumps: the level just before the jump, so VEGAS does not ramp from the old keyframe.
        foreach (var jump in keys.Where((k, i) => i > 0 && keys[i - 1].AtMs == k.AtMs && keys[i - 1].Amount != k.Amount &&
                     k.AtMs > startMs && k.AtMs < endMs))
        {
            var index = result.FindIndex(x => x.AtMs == jump.AtMs - startMs);
            if (index >= 0) result.Insert(index, new BlurKey(jump.AtMs - startMs - 1, At(keys, jump.AtMs - 1)));
        }
        return result.All(x => x.Amount <= 0) ? null : result;
    }

    /// <summary>
    /// FFmpeg filters for a layer stream whose timestamps are on the scene clock: premultiplied Gaussian blur whose
    /// sigma follows the level (per frame, through sendcmd) divided by the camera zoom, because VEGAS blurs after
    /// Pan/Crop, i.e. after the camera, while the preview zooms the finished picture. <paramref name="pad"/> adds
    /// transparent room so the blur is not cut at the layer edges (centred, so centre-based placement holds).
    /// </summary>
    public static string PreviewFilter(IReadOnlyList<BlurKey> keys, long startMs, long endMs, CameraPath? camera, string name, bool pad)
    {
        if (keys.Count == 0) return "";
        var commands = new List<string>();
        double? last = null;
        double maxSigma = 0;
        for (var t = startMs; t <= endMs; t += GesturePlan.FrameMs)
        {
            var zoom = camera is { IsStill: false } ? camera.At(t).Zoom : 1;
            var sigma = At(keys, t) * SigmaPerRange / zoom;
            maxSigma = Math.Max(maxSigma, sigma);
            if (last is { } previous && Math.Abs(previous - sigma) < 0.05) continue;
            // Both sigmas: gblur copies sigma to sigmaV only when it starts, so a «sigma» command alone blurs sideways.
            commands.Add($"{(t / 1000d).ToString("0.###", CultureInfo.InvariantCulture)} gblur@{name} sigma {Number(sigma)}," +
                $"gblur@{name} sigmaV {Number(sigma)}");
            last = sigma;
        }
        if (maxSigma < 0.05) return "";
        var room = pad ? (int)Math.Ceiling(maxSigma * 3 / 2) * 2 : 0;
        return (room > 0 ? $",pad=iw+{2 * room}:ih+{2 * room}:{room}:{room}:color=black@0" : "") +
            $",format=gbrap,premultiply=inplace=1,sendcmd=c='{string.Join(";", commands)}'," +
            $"gblur@{name}=sigma=0:sigmaV=0,unpremultiply=inplace=1,format=rgba";
    }

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
