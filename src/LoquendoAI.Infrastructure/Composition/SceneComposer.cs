using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using LoquendoAI.Core.Models;

namespace LoquendoAI.Infrastructure.Composition;

public sealed record SceneMedia(Guid BlockId, ScriptBlockKind Kind, long StartMs, long DurationMs, string Path,
    string Position = "centro", Guid? CharacterId = null, string VideoLayer = "guion", bool GreenScreen = false,
    string KeyColor = "00FF00", double KeyTolerance = 0.30, long SourceDurationMs = 0, bool StretchAudio = false,
    bool ExplicitAudioDuration = false, int VisualMaxWidth = 960, int VisualMaxHeight = 700,
    int VisualOffsetX = 0, int VisualOffsetY = 0, bool FlipHorizontal = false, bool FlipVertical = false,
    string FramingPreset = "original", string VisualCrop = "", bool AutoTrimBorders = true,
    double RotationDegrees = 0, long FadeInMs = 0, string VegasTransitionId = "",
    string VegasTransitionPreset = "", bool CutVideo = false,
    int VolumePercent = 100, bool HasVideoAudio = false,
    int MotionOffsetX = 0, int MotionOffsetY = 0, double MotionRotationDegrees = 0,
    long MotionDurationMs = 0, Guid? HideBlockId = null, bool MirrorPlacement = false);
/// <summary>A planned scene. <see cref="CameraCues"/> are the camera blocks on the clock; <see cref="Camera"/> is the
/// resulting path, filled in by CharacterFraming.ApplyAsync once the renders have their final placement.</summary>
public sealed record SceneComposition(long DurationMs, IReadOnlyList<SceneMedia> Media,
    CameraPath? Camera = null, IReadOnlyList<SceneTransition>? Transitions = null,
    IReadOnlyList<CameraCue>? CameraCues = null, IReadOnlyList<CinemaSegment>? Cinema = null,
    IReadOnlyList<GestureCue>? GestureCues = null, IReadOnlyDictionary<Guid, GestureTrack>? Gestures = null,
    IReadOnlyList<BlurCue>? BlurCues = null);
public sealed record SceneTransition(long StartMs, long DurationMs, string Style,
    string VegasEffect = "ninguno", int Targets = 15,
    string VegasTransitionId = "", string VegasTransitionPreset = "");
public sealed record VisualTransformOptions(int MaxWidth, int MaxHeight, int OffsetX = 0, int OffsetY = 0,
    bool FlipHorizontal = false, bool FlipVertical = false, double RotationDegrees = 0,
    int MotionOffsetX = 0, int MotionOffsetY = 0, double MotionRotationDegrees = 0,
    long MotionDurationMs = 0, bool ChangeDirection = false);

public static class SceneComposer
{
    /// <summary>
    /// What an «Ocultar personaje» block hides (1.4.1). With a character: that character. Without one, the block names
    /// a render by its asset (a render shown without character: an NPC, an extra): the latest earlier «Mostrar» of that
    /// asset without character that an earlier «Ocultar» has not hidden yet (two extras with the same render are hidden
    /// one by one). If that render was never shown without character but was shown with one (it got a character after
    /// the «Ocultar» was written), the block hides that character. A redundant hide of NPCs already hidden does nothing.
    /// </summary>
    public static (Guid? CharacterId, Guid? RenderBlockId) HideTarget(IEnumerable<SceneScriptBlock> blocks, SceneScriptBlock hide)
    {
        if (hide.CharacterId is Guid character) return (character, null);
        if (hide.AssetId is not Guid asset) return (null, null);
        var open = new List<Guid>();
        var anyNpc = false;
        Guid? owner = null;
        foreach (var block in blocks.Where(x => x.OrderIndex < hide.OrderIndex && x.AssetId == asset).OrderBy(x => x.OrderIndex))
        {
            if (block.Kind == ScriptBlockKind.CharacterShow)
            {
                if (block.CharacterId is Guid who) owner = who;
                else
                {
                    open.Add(block.Id);
                    anyNpc = true;
                }
            }
            else if (block.Kind == ScriptBlockKind.CharacterHide && block.CharacterId is null && open.Count > 0)
                open.RemoveAt(open.Count - 1);
        }
        return open.Count > 0 ? (null, open[^1]) : !anyNpc && owner is Guid shown ? (shown, null) : (null, null);
    }

    /// <summary>
    /// «Ocultar: todos los que están en escena» of the editor (1.4.4): who is on screen right before the block at
    /// <paramref name="order"/>, in order of appearance: each character (by id) and each render shown without a character
    /// (an NPC, by its asset; the same render shown twice counts twice). Hiding them with one «Ocultar» each, one after
    /// the other and without a pause between them, makes them all leave at the same moment.
    /// </summary>
    public static IReadOnlyList<(Guid? CharacterId, Guid? RenderAssetId)> OnScreenBefore(IReadOnlyList<SceneScriptBlock> blocks, int order)
    {
        var ordered = blocks.Where(x => x.OrderIndex < order).OrderBy(x => x.OrderIndex).ToArray();
        var shown = new List<(Guid? Character, Guid? Asset, Guid Block)>();
        foreach (var block in ordered)
        {
            if (block.Kind == ScriptBlockKind.CharacterShow)
            {
                if (block.CharacterId is Guid character)
                {
                    shown.RemoveAll(x => x.Character == character);
                    shown.Add((character, null, block.Id));
                }
                else if (block.AssetId is Guid asset) shown.Add((null, asset, block.Id));
            }
            else if (block.Kind == ScriptBlockKind.CharacterHide)
            {
                var (character, renderBlock) = HideTarget(ordered, block);
                if (character is Guid who) shown.RemoveAll(x => x.Character == who);
                else if (renderBlock is Guid render) shown.RemoveAll(x => x.Block == render);
            }
        }
        return shown.Select(x => (x.Character, x.Asset)).ToArray();
    }

    /// <summary>Which on-screen render a show/hide event is about: the character, or the render itself when it has
    /// no character (a «Mostrar» block, or the «Mostrar» an «Ocultar» points at). Null: nothing.</summary>
    public static Guid? RenderKey(SceneMedia item) => item.Kind == ScriptBlockKind.CharacterHide
        ? item.CharacterId ?? item.HideBlockId
        : item.CharacterId ?? item.BlockId;

    public const int TargetBackground = 1;
    public const int TargetCharacter = 2;
    public const int TargetImage = 4;
    public const int TargetVideo = 8;
    public const int TargetAll = TargetBackground | TargetCharacter | TargetImage | TargetVideo;
    public static int TransitionTargets(SceneScriptBlock block) => BlockParameters.Of(block).Targets;
    public static bool AppliesTo(SceneTransition transition, ScriptBlockKind kind)
    {
        var bit = kind switch
        {
            ScriptBlockKind.Background => TargetBackground,
            ScriptBlockKind.CharacterShow or ScriptBlockKind.CharacterHide => TargetCharacter,
            ScriptBlockKind.Image => TargetImage,
            ScriptBlockKind.Video => TargetVideo,
            _ => 0
        };
        return bit != 0 && (transition.Targets & bit) != 0;
    }
    public static (string Style, long DurationMs) TransitionOptions(SceneScriptBlock block) => BlockParameters.Of(block).Transition;
    public static string TransitionEffect(SceneScriptBlock block) => BlockParameters.Of(block).Effect;

    public static (string Id, string Preset) VegasEffect(string effect) => effect switch
    {
        "disolvente" => ("{Svfx:com.vegascreativesoftware:dissolve}", "Disolvente aditivo"),
        "flash" => ("{Svfx:com.vegascreativesoftware:flash}", "Flash suave"),
        "barrido" => ("{Svfx:com.vegascreativesoftware:linearwipe}", "Izquierda-derecha, borde suave"),
        _ => ("", "")
    };
    public static (string Id, string Preset) VegasPlugin(SceneScriptBlock block)
    {
        var parameters = BlockParameters.Of(block);
        return VegasTransitionCatalog.TryResolve(parameters.VegasPluginId, parameters.VegasPluginPreset, out var plugin, out var resolved)
            ? (plugin!.Id, resolved) : ("", "");
    }
    public static bool IsVisualBlock(ScriptBlockKind kind) => BlockParameters.IsVisual(kind);
    public static string VisualTransitionMode(SceneScriptBlock block) => BlockParameters.Of(block).VisualTransition(block.Kind);
    public static bool Crossfades(SceneTransition transition, SceneScriptBlock block)
    {
        if (transition.Style != "cruce" || !IsVisualBlock(block.Kind)) return false;
        return VisualTransitionMode(block) switch
        {
            "corte" => false,
            "heredar" => AppliesTo(transition, block.Kind),
            _ => true
        };
    }
    public static long VisualStart(SceneTransition transition, SceneScriptBlock block) =>
        transition.Style == "cambio" || transition.Style == "cruce" && !Crossfades(transition, block)
            ? transition.StartMs + transition.DurationMs / 2 : transition.StartMs;
    public static long HideStart(SceneTransition transition) =>
        transition.Style == "cambio" || transition.Style == "cruce" && !AppliesTo(transition, ScriptBlockKind.CharacterShow)
            ? transition.StartMs + transition.DurationMs / 2 : transition.StartMs + transition.DurationMs;
    public static long VisualCutoff(SceneMedia successor) => successor.StartMs + successor.FadeInMs;
    // An incoming video replaces the most recent active video in its own layer.
    // Independent videos in the same layer keep separate tracks and can coexist.
    public static SceneMedia? VideoReplacement(SceneMedia incoming, IReadOnlyList<SceneMedia> media)
    {
        if (incoming.Kind != ScriptBlockKind.Video || incoming.FadeInMs <= 0 && !incoming.CutVideo) return null;
        return media.TakeWhile(x => x.BlockId != incoming.BlockId)
            .Where(x => x.Kind == ScriptBlockKind.Video && x.VideoLayer == incoming.VideoLayer &&
                x.StartMs < incoming.StartMs && x.StartMs + x.DurationMs > incoming.StartMs)
            .LastOrDefault();
    }
    public static long VideoCutoff(SceneMedia current, IReadOnlyList<SceneMedia> media, long end) =>
        media.Where(x => x.Kind == ScriptBlockKind.Video &&
                VideoReplacement(x, media)?.BlockId == current.BlockId)
            .Select(VisualCutoff).DefaultIfEmpty(end).Min();
    public static long VideoAudioEnd(SceneMedia video, IReadOnlyList<SceneMedia> media, long sceneEnd)
    {
        var end = Math.Min(sceneEnd, Math.Min(video.StartMs + video.DurationMs,
            VideoCutoff(video, media, sceneEnd)));
        if (video.VideoLayer == "fondo")
            end = Math.Min(end, media.SkipWhile(x => x.BlockId != video.BlockId).Skip(1)
                .Where(x => x.Kind == ScriptBlockKind.Background)
                .Select(VisualCutoff).DefaultIfEmpty(sceneEnd).Min());
        return end;
    }
    // Positive angles rotate clockwise in FFmpeg's image coordinate system.
    public static string RotationFilter(double degrees)
    {
        if (Math.Abs(degrees) < 0.0001) return "";
        var angle = degrees.ToString("0.###", CultureInfo.InvariantCulture) + "*PI/180";
        return $",rotate={angle}:ow=rotw({angle}):oh=roth({angle}):c=none";
    }
    public static string RotationFillFilter(double degrees, int width, int height)
    {
        if (Math.Abs(degrees) < 0.0001) return "";
        // Enlarge before rotating so the final crop never exposes an empty corner (same factor as VEGAS).
        var factor = LayerGeometry.RotationOverscan(width, height, degrees, 0);
        var scaledWidth = (int)Math.Ceiling(width * factor / 2) * 2;
        var scaledHeight = (int)Math.Ceiling(height * factor / 2) * 2;
        return $",scale={scaledWidth}:{scaledHeight}{RotationFilter(degrees)},crop={width}:{height}";
    }
    public static string MotionProgress(long durationMs, long requestedMs, long offsetMs = 0)
    {
        var duration = Seconds(Math.Max(40, requestedMs > 0 ? requestedMs : durationMs));
        var offset = Seconds(offsetMs);
        return $"min(1,max(0,(t+({offset}))/{duration}))";
    }
    public static bool HasMotion(SceneMedia clip) =>
        clip.MotionOffsetX != 0 || clip.MotionOffsetY != 0 || clip.MotionRotationDegrees != 0;

    // A rotating full-frame image must have enough overscan at every angle,
    // including angles between the two endpoints.
    public static string RotationFillMotionFilter(double start, double delta, long durationMs,
        long requestedMs, int width, int height, long offsetMs = 0)
    {
        if (Math.Abs(delta) < 0.0001) return RotationFillFilter(start, width, height);
        var factor = LayerGeometry.RotationOverscan(width, height, start, delta);
        var scaledWidth = (int)Math.Ceiling(width * factor / 2) * 2;
        var scaledHeight = (int)Math.Ceiling(height * factor / 2) * 2;
        var angleExpression = $"({start.ToString("0.###", CultureInfo.InvariantCulture)}+({delta.ToString("0.###", CultureInfo.InvariantCulture)})*" +
            $"{MotionProgress(durationMs, requestedMs, offsetMs)})*PI/180";
        return $",scale={scaledWidth}:{scaledHeight},rotate=angle='{angleExpression}':ow=iw:oh=ih:c=none,crop={width}:{height}";
    }
    public static string RotationLayerMotionFilter(double start, double delta, long durationMs,
        long requestedMs, int width, int height, long offsetMs = 0)
    {
        if (Math.Abs(delta) < 0.0001) return RotationFilter(start);
        var side = (int)Math.Ceiling(Math.Sqrt((double)width * width + (double)height * height) / 2) * 2;
        var angleExpression = $"({start.ToString("0.###", CultureInfo.InvariantCulture)}+({delta.ToString("0.###", CultureInfo.InvariantCulture)})*" +
            $"{MotionProgress(durationMs, requestedMs, offsetMs)})*PI/180";
        return $",pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:color=black@0," +
            $"pad={side}:{side}:(ow-iw)/2:(oh-ih)/2:color=black@0," +
            $"rotate=angle='{angleExpression}':ow=iw:oh=ih:c=none";
    }
    public static bool WaitForSound(SceneScriptBlock block) => BlockParameters.Of(block).WaitsForSound(block.Kind);

    public static string Position(SceneScriptBlock block) => BlockParameters.Of(block).EffectivePosition;

    /// <summary>The same place seen in a mirror: left and right swap, the centre stays. «auto» is mirrored when its
    /// lane is assigned (CharacterFraming).</summary>
    public static string MirrorPosition(string position) => position switch
    {
        "izquierda" => "derecha",
        "derecha" => "izquierda",
        _ => position
    };

    public static long? VisualDuration(SceneScriptBlock block) => BlockParameters.Of(block).VisualDuration(block.Kind);

    public static string FramingPreset(SceneScriptBlock block) => BlockParameters.Of(block).Framing(block.Kind);

    public static bool AutoTrimBorders(SceneScriptBlock block) => BlockParameters.Of(block).TrimBorders(block.Kind);

    public static long? AudioDuration(SceneScriptBlock block) => BlockParameters.Of(block).AudioDuration(block.Kind);

    public static int DefaultVolumePercent(ScriptBlockKind kind) => BlockDefaults.VolumePercent(kind);

    public static int VolumePercent(SceneScriptBlock block) => BlockParameters.Of(block).Volume(block.Kind);

    public static async Task<bool> HasAudioStreamAsync(string path, CancellationToken token = default)
    {
        if (MediaProbeCache.TryGet("audio", path, out var cached)) return cached == "1";
        var result = await HasAudioStreamUncachedAsync(path, token);
        MediaProbeCache.Set("audio", path, result ? "1" : "0");
        return result;
    }

    private static async Task<bool> HasAudioStreamUncachedAsync(string path, CancellationToken token)
    {
        var psi = new ProcessStartInfo("ffprobe")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (var arg in new[] { "-v", "error", "-select_streams", "a:0", "-show_entries",
                     "stream=index", "-of", "default=noprint_wrappers=1:nokey=1", path })
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo iniciar ffprobe.");
        using var registration = token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(true); }
            catch (InvalidOperationException) { }
        });
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"No se pudo analizar el audio de {Path.GetFileName(path)}: {await error}");
        return !string.IsNullOrWhiteSpace(await output);
    }

    public static bool StretchAudio(SceneScriptBlock block) => BlockParameters.Of(block).StretchAudio(block.Kind);

    public static bool LooksLikeGreenScreen(string name) =>
        name.Contains("pantalla verde", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("green screen", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("greenscreen", StringComparison.OrdinalIgnoreCase);

    public static bool AutoGreenScreen(SceneScriptBlock block) => BlockParameters.Of(block).AutoGreenScreen(block.Kind);

    public static (string Layer, bool GreenScreen, string KeyColor, double Tolerance) VideoOptions(SceneScriptBlock block) => BlockParameters.Of(block).Video;

    public static VisualTransformOptions VisualTransform(SceneScriptBlock block) => BlockParameters.Of(block).Transform(block.Kind);

    /// <summary>Width × height of an image or video (first video stream), from the probe cache or ffprobe.</summary>
    public static async Task<(int Width, int Height)> ProbeDimensionsAsync(string path, CancellationToken token = default)
    {
        if (MediaProbeCache.TryGet("dim", path, out var cached) && cached.Split('x') is [var w, var h] &&
            int.TryParse(w, out var cachedWidth) && int.TryParse(h, out var cachedHeight) && cachedWidth > 0 && cachedHeight > 0)
            return (cachedWidth, cachedHeight);
        var psi = new ProcessStartInfo("ffprobe") { RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in new[] { "-v", "error", "-select_streams", "v:0", "-show_entries",
                     "stream=width,height", "-of", "csv=p=0:s=x", path }) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo iniciar ffprobe.");
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        var parts = (await output).Trim().Split('x');
        if (process.ExitCode != 0 || parts.Length != 2 || !int.TryParse(parts[0], out var width) ||
            !int.TryParse(parts[1], out var height) || width <= 0 || height <= 0)
            throw new InvalidDataException($"No se pudieron medir las dimensiones de {Path.GetFileName(path)}: {await error}");
        MediaProbeCache.Set("dim", path, $"{width}x{height}");
        return (width, height);
    }

    /// <summary>Duration in ms: WAV header when possible, then the probe cache, then ffprobe.</summary>
    public static async Task<long> ProbeDurationAsync(string path, CancellationToken token = default)
    {
        if (MediaProbeCache.WaveDurationMs(path) is long wave) return wave;
        if (MediaProbeCache.TryGet("duration", path, out var cached) &&
            long.TryParse(cached, NumberStyles.Integer, CultureInfo.InvariantCulture, out var known) && known > 0) return known;
        var measured = await ProbeDurationUncachedAsync(path, token);
        MediaProbeCache.Set("duration", path, measured.ToString(CultureInfo.InvariantCulture));
        return measured;
    }

    private static async Task<long> ProbeDurationUncachedAsync(string path, CancellationToken token)
    {
        var psi = new ProcessStartInfo("ffprobe") { RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in new[] { "-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", path }) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo iniciar ffprobe.");
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        if (process.ExitCode != 0 || !double.TryParse((await output).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || !double.IsFinite(seconds))
            throw new InvalidOperationException($"No se pudo medir {Path.GetFileName(path)}: {await error}");
        return Math.Max(1, (long)Math.Ceiling(seconds * 1000));
    }

    /// <param name="cinemaStart">Bars the scene starts with (how the previous scene of the episode ended).</param>
    public static async Task<SceneComposition> PlanAsync(IReadOnlyList<SceneScriptBlock> blocks,
        Func<SceneScriptBlock, string?> resolve, CancellationToken token = default,
        Func<SceneScriptBlock, bool>? suggestedGreenScreen = null, CinemaState? cinemaStart = null)
    {
        var cinemaCues = new List<CinemaCue>();
        var media = new List<SceneMedia>();
        var transitions = new List<SceneTransition>();
        var cameraCues = new List<CameraCue>();
        var gestureCues = new List<GestureCue>();
        var blurCues = new List<BlurCue>();
        SceneTransition? pendingVisual = null;
        long clock = 0, end = 0;
        foreach (var block in blocks.OrderBy(x => x.OrderIndex))
        {
            token.ThrowIfCancellationRequested();
            var kind = block.Kind;
            if (kind == ScriptBlockKind.Transition)
            {
                pendingVisual = null;
                var (style, durationMs) = TransitionOptions(block);
                if (durationMs > 0)
                {
                    var plugin = VegasPlugin(block);
                    var transition = new SceneTransition(clock, durationMs, style,
                        TransitionEffect(block), TransitionTargets(block), plugin.Id, plugin.Preset);
                    transitions.Add(transition);
                    pendingVisual = style is "cambio" or "cruce" ? transition : null;
                    clock += durationMs;
                    end = Math.Max(end, clock);
                }
                // Transition duration is already part of the clock. PauseAfterMs, if set,
                // remains an additional explicit pause below.
            }
            var incoming = IsVisualBlock(kind) ? pendingVisual : null;
            if (kind == ScriptBlockKind.Cinema)
                cinemaCues.Add(new CinemaCue(clock, BlockParameters.Of(block).Cinema()));
            if (kind == ScriptBlockKind.Blur)
                blurCues.Add(new BlurCue(clock, BlockParameters.Of(block).Blur(block.CharacterId is not null), block.CharacterId));
            if (kind == ScriptBlockKind.Gesture)
                gestureCues.Add(new GestureCue(clock, BlockParameters.Of(block).Gesture(), block.CharacterId));
            if (kind == ScriptBlockKind.Camera)
                cameraCues.Add(new CameraCue(clock, BlockParameters.Of(block).Camera(block.CharacterId is not null), block.CharacterId));
            if (!IsVisualBlock(kind) && kind is not (ScriptBlockKind.Transition or ScriptBlockKind.Comment or
                    ScriptBlockKind.CharacterHide or ScriptBlockKind.Camera or ScriptBlockKind.Cinema or ScriptBlockKind.Gesture or ScriptBlockKind.Blur))
                pendingVisual = null;
            if (kind is ScriptBlockKind.Dialogue or ScriptBlockKind.Narration or ScriptBlockKind.Background or ScriptBlockKind.CharacterShow or ScriptBlockKind.Image or ScriptBlockKind.Video or ScriptBlockKind.SoundEffect or ScriptBlockKind.Music)
            {
                var path = resolve(block);
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    throw new FileNotFoundException($"Falta el audio o asset del bloque #{block.OrderIndex + 1}. Genera las voces o revisa la fuente.", path);
                var sourceDuration = kind is ScriptBlockKind.Dialogue or ScriptBlockKind.Narration or ScriptBlockKind.SoundEffect or ScriptBlockKind.Music or ScriptBlockKind.Video
                    ? await ProbeDurationAsync(path, token) : 0;
                var duration = kind == ScriptBlockKind.Dialogue ? sourceDuration
                    : kind is ScriptBlockKind.Narration or ScriptBlockKind.SoundEffect or ScriptBlockKind.Music ? AudioDuration(block) ?? sourceDuration
                    : VisualDuration(block) ?? (kind == ScriptBlockKind.Video ? sourceDuration : 0);
                var volumePercent = VolumePercent(block);
                var hasVideoAudio = kind == ScriptBlockKind.Video && volumePercent > 0 &&
                    await HasAudioStreamAsync(path, token);
                var video = VideoOptions(block);
                var transform = VisualTransform(block);
                var greenScreen = video.GreenScreen || kind == ScriptBlockKind.Video && AutoGreenScreen(block) &&
                    (LooksLikeGreenScreen(Path.GetFileNameWithoutExtension(path)) || suggestedGreenScreen?.Invoke(block) == true);
                var fadeIn = incoming is not null && Crossfades(incoming, block)
                    ? incoming.DurationMs : 0;
                var overrideMode = VisualTransitionMode(block);
                var effect = fadeIn == 0 ? ("", "") : overrideMode switch
                {
                    "plugin" => VegasPlugin(block),
                    "heredar" when incoming!.VegasEffect == "plugin" =>
                        (incoming.VegasTransitionId, incoming.VegasTransitionPreset),
                    _ => VegasEffect(overrideMode switch
                    {
                        "heredar" => incoming!.VegasEffect,
                        "fundido" => "ninguno",
                        _ => overrideMode
                    })
                };
                // «Invertir horizontal» mirrors the whole layer across the frame (1.4.3): the other side, the offset,
                // the turn and the horizontal motion are mirrored, and the picture is flipped in place. «Cambiar
                // dirección» only flips the picture in place; both together leave it looking the same way.
                var mirror = transform.FlipHorizontal;
                media.Add(new SceneMedia(block.Id, kind, incoming is null ? clock : VisualStart(incoming, block),
                    duration, path, mirror ? MirrorPosition(Position(block)) : Position(block), block.CharacterId,
                    video.Layer, greenScreen, video.KeyColor, video.Tolerance, sourceDuration,
                    StretchAudio(block) && AudioDuration(block).HasValue, AudioDuration(block).HasValue,
                    transform.MaxWidth, transform.MaxHeight, mirror ? -transform.OffsetX : transform.OffsetX, transform.OffsetY,
                    transform.FlipHorizontal != transform.ChangeDirection, transform.FlipVertical, FramingPreset(block), "", AutoTrimBorders(block),
                    mirror ? -transform.RotationDegrees : transform.RotationDegrees, fadeIn, effect.Item1, effect.Item2,
                    kind == ScriptBlockKind.Video && incoming?.Style == "cruce" && fadeIn == 0,
                    volumePercent, hasVideoAudio,
                    mirror ? -transform.MotionOffsetX : transform.MotionOffsetX, transform.MotionOffsetY,
                    mirror ? -transform.MotionRotationDegrees : transform.MotionRotationDegrees,
                    transform.MotionDurationMs, MirrorPlacement: mirror));
                if (kind is ScriptBlockKind.Dialogue or ScriptBlockKind.Narration || (kind == ScriptBlockKind.SoundEffect && WaitForSound(block))) clock += duration;
                if (kind is ScriptBlockKind.Dialogue or ScriptBlockKind.Narration or ScriptBlockKind.SoundEffect ||
                    (kind == ScriptBlockKind.Music && AudioDuration(block).HasValue))
                    end = Math.Max(end, media[^1].StartMs + duration);
            }
            if (kind == ScriptBlockKind.CharacterHide)
            {
                var target = HideTarget(blocks, block);
                media.Add(new SceneMedia(block.Id, kind, pendingVisual is { Style: "cambio" or "cruce" }
                    ? HideStart(pendingVisual) : clock, 0, "", CharacterId: target.CharacterId, HideBlockId: target.RenderBlockId));
            }
            else if (kind is not (ScriptBlockKind.Dialogue or ScriptBlockKind.Narration or ScriptBlockKind.Background or ScriptBlockKind.CharacterShow or ScriptBlockKind.Image or ScriptBlockKind.Video or ScriptBlockKind.SoundEffect or ScriptBlockKind.Music))
                media.Add(new SceneMedia(block.Id, kind, clock, 0, ""));
            clock += Math.Max(0, block.PauseAfterMs);
            if (block.PauseAfterMs > 0) pendingVisual = null;
            end = Math.Max(end, clock);
        }
        foreach (var clip in media.Where(x => (x.DurationMs > 0 || x.MotionDurationMs > 0) &&
            x.Kind is (ScriptBlockKind.Background or ScriptBlockKind.CharacterShow or ScriptBlockKind.Image or ScriptBlockKind.Video)))
        {
            var successors = media.SkipWhile(x => x.BlockId != clip.BlockId).Skip(1);
            var cutoff = successors.Where(x => clip.Kind switch
            {
                ScriptBlockKind.Background => x.Kind == ScriptBlockKind.Background,
                ScriptBlockKind.CharacterShow => x.Kind is (ScriptBlockKind.CharacterShow or ScriptBlockKind.CharacterHide) &&
                    x.CharacterId == clip.CharacterId && clip.CharacterId is not null ||
                    x.Kind == ScriptBlockKind.CharacterHide && clip.CharacterId is null && x.HideBlockId == clip.BlockId,
                ScriptBlockKind.Image => x.Kind == ScriptBlockKind.Image,
                ScriptBlockKind.Video => clip.VideoLayer == "fondo" && x.Kind == ScriptBlockKind.Background ||
                    x.Kind == ScriptBlockKind.Video && VideoReplacement(x, media)?.BlockId == clip.BlockId,
                _ => false
            }).Select(VisualCutoff).DefaultIfEmpty(long.MaxValue).Min();
            end = Math.Max(end, Math.Min(clip.StartMs +
                (clip.DurationMs > 0 ? clip.DurationMs : clip.MotionDurationMs), cutoff));
        }
        var cinema = CinemaPlan.Build(cinemaCues, cinemaStart);
        return new SceneComposition(Math.Max(1000, end), media, Transitions: transitions,
            CameraCues: cameraCues.Count == 0 ? null : cameraCues, Cinema: cinema.Count == 0 ? null : cinema,
            GestureCues: gestureCues.Count == 0 ? null : gestureCues, BlurCues: blurCues.Count == 0 ? null : blurCues);
    }

    public static async Task RenderAsync(SceneComposition scene, string outputPath, CancellationToken token = default)
    {
        scene = GesturePlan.Resolve(CinemaPlan.Resolve(await CharacterFraming.ApplyAsync(scene, token)));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        var temp = Path.Combine(Path.GetDirectoryName(outputPath)!, $".{Guid.NewGuid():N}.mp4");
        var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        void Args(params string[] args) { foreach (var arg in args) psi.ArgumentList.Add(arg); }
        Args("-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "color=c=black:s=1280x720:r=25");
        var visual = scene.Media.Where(x => x.Kind is ScriptBlockKind.Background or ScriptBlockKind.CharacterShow or ScriptBlockKind.Image or ScriptBlockKind.Video).ToArray();
        var audible = scene.Media.Where(x => x.Kind is (ScriptBlockKind.Dialogue or ScriptBlockKind.Narration or ScriptBlockKind.SoundEffect or ScriptBlockKind.Music) ||
            x.Kind == ScriptBlockKind.Video && x.HasVideoAudio && x.VolumePercent > 0).ToArray();
        var indexes = new Dictionary<Guid, int>();
        var input = 1;
        foreach (var clip in visual)
        {
            var extension = Path.GetExtension(clip.Path);
            if (extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".avi", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".mov", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".webm", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".wmv", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".mpg", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".mpeg", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".ts", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".m2ts", StringComparison.OrdinalIgnoreCase))
                Args("-stream_loop", "-1");
            else Args("-loop", "1", "-framerate", "25");
            Args("-i", clip.Path);
            indexes[clip.BlockId] = input++;
        }
        foreach (var clip in audible.Where(x => x.Kind != ScriptBlockKind.Video))
        {
            if ((clip.Kind == ScriptBlockKind.Music && !clip.StretchAudio) ||
                (clip.Kind is (ScriptBlockKind.SoundEffect or ScriptBlockKind.Narration) &&
                 clip.DurationMs > clip.SourceDurationMs && !clip.StretchAudio))
                Args("-stream_loop", "-1");
            Args("-i", clip.Path); indexes[clip.BlockId] = input++;
        }
        // Overlays (characters, props, videos over the scene) are placed with LayerGeometry, the same
        // numbers the VEGAS export uses; that needs each file's size (cached after the first probe).
        var layouts = new Dictionary<Guid, LayerLayout>();
        foreach (var clip in visual.Where(x => !LayerSpec.From(x).Fills))
        {
            try
            {
                var (sourceWidth, sourceHeight) = await ProbeDimensionsAsync(clip.Path, token);
                if (LayerGeometry.Place(LayerSpec.From(clip), sourceWidth, sourceHeight, 1280, 720) is { } layout)
                    layouts[clip.BlockId] = layout;
            }
            catch (InvalidDataException) { /* Unreadable size: the layer keeps the size-relative placement below. */ }
        }
        var filters = new List<string>();
        var previous = "[0:v]";
        var n = 0;
        var blurs = 0;
        string Timing(SceneMedia clip, long end, long fadeOutMs = 0)
        {
            var flash = clip.FadeInMs > 0 && clip.VegasTransitionId.Contains(":flash}", StringComparison.OrdinalIgnoreCase) &&
                clip.Kind != ScriptBlockKind.Background && !(clip.Kind == ScriptBlockKind.Video && clip.VideoLayer == "fondo")
                ? $",format=yuva444p,eq=brightness='0.95*max(0,1-abs(2*t/{Seconds(clip.FadeInMs)}-1))':eval=frame,format=rgba"
                : "";
            var fade = clip.FadeInMs > 0
                ? $",fade=t=in:st=0:d={Seconds(Math.Min(clip.FadeInMs, end - clip.StartMs))}:alpha=1" : "";
            var outgoing = fadeOutMs > 0
                ? $",fade=t=out:st={Seconds(end - clip.StartMs - fadeOutMs)}:d={Seconds(fadeOutMs)}:alpha=1" : "";
            return $"trim=duration={Seconds(end - clip.StartMs)},setpts=PTS-STARTPTS{flash}{fade}{outgoing}," +
                $"setpts=PTS+{Seconds(clip.StartMs)}/TB";
        }
        // Blur (VEGAS Gaussian blur after Pan/Crop): per-frame sigma, compensated for the camera zoom.
        string BlurFor(SceneMedia clip, long end, bool pad) => BlurPlan.PreviewFilter(
            BlurPlan.Curve(scene.BlurCues, clip.Kind, clip.VideoLayer, clip.CharacterId), clip.StartMs, end, scene.Camera,
            $"blur{blurs++}", pad);
        long FadeOutFor(SceneMedia clip, long end)
        {
            SceneMedia? successor = clip.Kind switch
            {
                ScriptBlockKind.CharacterShow => scene.Media.SkipWhile(x => x.BlockId != clip.BlockId).Skip(1)
                    .FirstOrDefault(x => x.Kind == ScriptBlockKind.CharacterShow &&
                        x.CharacterId == clip.CharacterId && clip.CharacterId is not null),
                ScriptBlockKind.Image => scene.Media.SkipWhile(x => x.BlockId != clip.BlockId).Skip(1)
                    .FirstOrDefault(x => x.Kind == ScriptBlockKind.Image),
                ScriptBlockKind.Video when clip.VideoLayer != "fondo" => scene.Media
                    .FirstOrDefault(x => x.Kind == ScriptBlockKind.Video &&
                        VideoReplacement(x, scene.Media)?.BlockId == clip.BlockId),
                _ => null
            };
            return successor is not null && successor.FadeInMs > 0 && successor.StartMs < end
                ? Math.Min(successor.FadeInMs, end - Math.Max(clip.StartMs, successor.StartMs)) : 0;
        }
        string Window(SceneMedia clip, long end) => $"enable='gte(t,{Seconds(clip.StartMs)})*lt(t,{Seconds(end)})':eof_action=pass:repeatlast=0:format=auto";
        string Move(SceneMedia clip, long end, int delta) => delta == 0 ? "" :
            $"+({delta})*{MotionProgress(end - clip.StartMs, clip.MotionDurationMs, -clip.StartMs)}";
        var bg = visual.Where(x => x.Kind == ScriptBlockKind.Background).OrderBy(x => x.StartMs).ToArray();
        for (var bgIndex = 0; bgIndex < bg.Length; bgIndex++)
        {
            var clip = bg[bgIndex];
            var nextStart = bg.Skip(bgIndex + 1).Select(VisualCutoff).DefaultIfEmpty(scene.DurationMs).Min();
            var end = Math.Min(nextStart, Math.Min(scene.DurationMs, clip.DurationMs > 0 ? clip.StartMs + clip.DurationMs : scene.DurationMs));
            if (end <= clip.StartMs) continue;
            var label = $"v{n++}";
            var flips = (clip.FlipHorizontal ? ",hflip" : "") + (clip.FlipVertical ? ",vflip" : "");
            // Same box as LayerGeometry.FillBox (VEGAS): room for the motion, or the zoom when it already covers it.
            var width = HasMotion(clip) ? (int)Math.Round(LayerGeometry.FillExtent(clip.VisualMaxWidth, 1280,
                clip.VisualOffsetX, clip.MotionOffsetX, 8, true)) : clip.VisualMaxWidth;
            var height = HasMotion(clip) ? (int)Math.Round(LayerGeometry.FillExtent(clip.VisualMaxHeight, 720,
                clip.VisualOffsetY, clip.MotionOffsetY, 8, true)) : clip.VisualMaxHeight;
            var rotation = clip.MotionRotationDegrees != 0
                ? ",setpts=PTS-STARTPTS" + RotationFillMotionFilter(clip.RotationDegrees, clip.MotionRotationDegrees,
                    end - clip.StartMs, clip.MotionDurationMs, width, height)
                : RotationFillFilter(clip.RotationDegrees, width, height);
            filters.Add($"[{indexes[clip.BlockId]}:v]{clip.VisualCrop}scale={width}:{height}:force_original_aspect_ratio=increase," +
                $"crop={width}:{height},setsar=1,format=rgba{flips}{rotation},{Timing(clip, end)}{BlurFor(clip, end, false)}[bg{n}]");
            filters.Add($"{previous}[bg{n}]overlay=x='(W-w)/2+({clip.VisualOffsetX}){Move(clip, end, clip.MotionOffsetX)}':" +
                $"y='(H-h)/2+({clip.VisualOffsetY}){Move(clip, end, clip.MotionOffsetY)}':{Window(clip, end)}[{label}]");
            previous = $"[{label}]";
        }
        foreach (var clip in visual.Where(x => x.Kind == ScriptBlockKind.Video && x.VideoLayer == "fondo").OrderBy(x => x.StartMs))
        {
            var nextBg = scene.Media.SkipWhile(x => x.BlockId != clip.BlockId).Skip(1)
                .Where(x => x.Kind == ScriptBlockKind.Background).Select(VisualCutoff).DefaultIfEmpty(scene.DurationMs).Min();
            var end = Math.Min(Math.Min(scene.DurationMs, clip.StartMs + clip.DurationMs),
                Math.Min(nextBg, VideoCutoff(clip, scene.Media, scene.DurationMs)));
            if (end <= clip.StartMs) continue;
            var label = $"v{n++}";
            var video = $"video{n}";
            var key = clip.GreenScreen ? $",colorkey=0x{clip.KeyColor}:{clip.KeyTolerance.ToString("0.###", CultureInfo.InvariantCulture)}:0.12" : "";
            var flips = (clip.FlipHorizontal ? ",hflip" : "") + (clip.FlipVertical ? ",vflip" : "");
            var width = 1280 + (HasMotion(clip) ? (Math.Abs(clip.MotionOffsetX) + 8) * 2 : 0);
            var height = 720 + (HasMotion(clip) ? (Math.Abs(clip.MotionOffsetY) + 8) * 2 : 0);
            var rotation = clip.MotionRotationDegrees != 0
                ? ",setpts=PTS-STARTPTS" + RotationFillMotionFilter(clip.RotationDegrees, clip.MotionRotationDegrees,
                    end - clip.StartMs, clip.MotionDurationMs, width, height)
                : RotationFillFilter(clip.RotationDegrees, width, height);
            filters.Add($"[{indexes[clip.BlockId]}:v]scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height},setsar=1,format=rgba{flips}{key}{rotation},{Timing(clip, end)}{BlurFor(clip, end, false)}[{video}]");
            filters.Add($"{previous}[{video}]overlay=x='(W-w)/2{Move(clip, end, clip.MotionOffsetX)}':" +
                $"y='(H-h)/2{Move(clip, end, clip.MotionOffsetY)}':{Window(clip, end)}[{label}]");
            previous = $"[{label}]";
        }
        // Flash on the background is composited before foreground layers: an unchanged
        // character stays visible and does not inherit the background transition.
        foreach (var flashing in scene.Media.Where(x => x.FadeInMs > 0 &&
            x.VegasTransitionId.Contains(":flash}", StringComparison.OrdinalIgnoreCase) &&
            (x.Kind == ScriptBlockKind.Background || x.Kind == ScriptBlockKind.Video && x.VideoLayer == "fondo")))
        {
            var flashEnd = Math.Min(scene.DurationMs, flashing.StartMs + flashing.FadeInMs);
            if (flashEnd <= flashing.StartMs) continue;
            var flashMs = flashEnd - flashing.StartMs;
            var half = Seconds(flashMs / 2);
            var otherHalf = Seconds(flashMs - flashMs / 2);
            var label = $"v{n++}";
            var layer = $"backgroundFlash{n}";
            filters.Add($"color=c=white:s=1280x720:r=25:d={Seconds(flashMs)},format=rgba," +
                $"fade=t=in:st=0:d={half}:alpha=1,fade=t=out:st={half}:d={otherHalf}:alpha=1," +
                $"setpts=PTS-STARTPTS+{Seconds(flashing.StartMs)}/TB[{layer}]");
            filters.Add($"{previous}[{layer}]overlay=0:0:enable='gte(t,{Seconds(flashing.StartMs)})*" +
                $"lt(t,{Seconds(flashEnd)})':eof_action=pass:repeatlast=0:format=auto[{label}]");
            previous = $"[{label}]";
        }
        var visible = new Dictionary<Guid, SceneMedia>();
        var segments = new List<(SceneMedia clip, long end)>();
        foreach (var eventItem in scene.Media.Where(x => x.Kind is ScriptBlockKind.CharacterShow or ScriptBlockKind.CharacterHide).OrderBy(x => x.StartMs))
        {
            if (RenderKey(eventItem) is not Guid visualId) continue;
            if (visible.Remove(visualId, out var old) && eventItem.StartMs > old.StartMs)
                segments.Add((old, Math.Max(eventItem.StartMs, VisualCutoff(eventItem))));
            if (eventItem.Kind == ScriptBlockKind.CharacterShow) visible[visualId] = eventItem;
        }
        segments.AddRange(visible.Values.Select(x => (x, scene.DurationMs)));
        var layers = new List<(SceneMedia clip, long end)>();
        layers.AddRange(segments.Select(item => (item.clip, Math.Min(item.end,
            item.clip.DurationMs > 0 ? item.clip.StartMs + item.clip.DurationMs : scene.DurationMs))));
        var props = visual.Where(x => x.Kind == ScriptBlockKind.Image).OrderBy(x => x.StartMs).ToArray();
        for (var i = 0; i < props.Length; i++)
        {
            var clip = props[i];
            var end = Math.Min(i + 1 < props.Length ? VisualCutoff(props[i + 1]) : scene.DurationMs,
                clip.DurationMs > 0 ? clip.StartMs + clip.DurationMs : scene.DurationMs);
            layers.Add((clip, end));
        }
        layers.AddRange(visual.Where(x => x.Kind == ScriptBlockKind.Video && x.VideoLayer != "fondo")
            .Select(x => (clip: x, end: Math.Min(scene.DurationMs,
                Math.Min(x.StartMs + x.DurationMs, VideoCutoff(x, scene.Media, scene.DurationMs))))));
        var order = scene.Media.Select((clip, index) => (clip.BlockId, index)).ToDictionary(x => x.BlockId, x => x.index);
        foreach (var (clip, end) in layers.OrderBy(x => x.clip.VideoLayer == "sobre" && x.clip.Kind == ScriptBlockKind.Video
            ? int.MaxValue : order[x.clip.BlockId]).ThenBy(x => order[x.clip.BlockId]))
        {
            if (end <= clip.StartMs) continue;
            var label = $"v{n++}";
            var layer = $"layer{n}";
            var key = clip.Kind == ScriptBlockKind.Video && clip.GreenScreen
                ? $",colorkey=0x{clip.KeyColor}:{clip.KeyTolerance.ToString("0.###", CultureInfo.InvariantCulture)}:0.12" : "";
            var flips = (clip.FlipHorizontal ? ",hflip" : "") + (clip.FlipVertical ? ",vflip" : "");
            var spinning = clip.MotionRotationDegrees != 0;
            string x, y, scale;
            int layerWidth, layerHeight;
            var gesture = "";
            if (layouts.TryGetValue(clip.BlockId, out var placed))
            {
                // Exact size and center from LayerGeometry. Rotation and motion pivot on the layer's
                // own center ("-w/2" uses the rotated size), exactly like the VEGAS Pan/Crop.
                layerWidth = (int)placed.Width;
                layerHeight = (int)placed.Height;
                scale = $"scale={layerWidth}:{layerHeight}";
                x = $"{LayerGeometry.Number(placed.CenterX)}-w/2{Move(clip, end, clip.MotionOffsetX)}";
                y = $"{LayerGeometry.Number(placed.CenterY)}-h/2{Move(clip, end, clip.MotionOffsetY)}";
                // Gestures turn and stretch the layer around its feet: the stream is re-centred on that
                // pivot, so the overlay places the pivot instead of the centre.
                if (scene.Gestures is not null && scene.Gestures.TryGetValue(clip.BlockId, out var track))
                {
                    (gesture, var below) = GesturePlan.PreviewFilter(track, layerWidth, layerHeight,
                        spinning || Math.Abs(clip.RotationDegrees) > 0.0001);
                    y = $"{LayerGeometry.Number(placed.CenterY + below)}-h/2{Move(clip, end, clip.MotionOffsetY)}";
                }
            }
            else
            {
                layerWidth = clip.VisualMaxWidth;
                layerHeight = clip.VisualMaxHeight;
                scale = $"scale={clip.VisualMaxWidth}:{clip.VisualMaxHeight}:force_original_aspect_ratio=decrease";
                x = $"({CharacterFraming.XPosition(clip.Position, BlockDefaults.CharacterMargin)})+({clip.VisualOffsetX}){Move(clip, end, clip.MotionOffsetX)}";
                y = $"({(clip.Kind == ScriptBlockKind.Image ? "H-h-" + BlockDefaults.ImageBottomMargin : "H-h")})+({clip.VisualOffsetY}){Move(clip, end, clip.MotionOffsetY)}";
            }
            var rotation = spinning
                ? ",setpts=PTS-STARTPTS" + RotationLayerMotionFilter(clip.RotationDegrees, clip.MotionRotationDegrees,
                    end - clip.StartMs, clip.MotionDurationMs, layerWidth, layerHeight)
                : RotationFilter(clip.RotationDegrees);
            filters.Add($"[{indexes[clip.BlockId]}:v]{clip.VisualCrop}{scale},setsar=1,format=rgba{flips}{key}{rotation},{Timing(clip, end, FadeOutFor(clip, end))}{gesture}{BlurFor(clip, end, placed is not null)}[{layer}]");
            filters.Add($"{previous}[{layer}]overlay=x='{x}':y='{y}':{Window(clip, end)}[{label}]");
            previous = $"[{label}]";
            // Bars on some layers only: drawn right above this layer, so later layers stay in front
            // (VEGAS: Cookie Cutter on this event, after its Pan/Crop, so the bars stay put when the camera zooms).
            var ownBars = (scene.Cinema ?? []).Where(x => x.Layers != "todos" && x.Affects(clip.Kind, clip.CharacterId, clip.BlockId) &&
                    x.StartMs < end && x.EndMs > clip.StartMs)
                .Select(x => x with { StartMs = Math.Max(x.StartMs, clip.StartMs), EndMs = Math.Min(x.EndMs, end),
                    FromSize = x.SizeAt(Math.Max(x.StartMs, clip.StartMs)), ToSize = x.SizeAt(Math.Min(x.EndMs, end)) })
                .ToArray();
            if (ownBars.Length > 0)
            {
                filters.AddRange(CinemaPlan.PreviewFilters(ownBars, previous, $"[bars{n}]", $"bars{n}", scene.DurationMs, scene.Camera));
                previous = $"[bars{n}]";
            }
        }
        // Camera (automatic «primer plano» zoom or camera blocks): the same path VEGAS writes as keyframes.
        if (scene.Camera is { IsStill: false } camera)
        {
            filters.Add($"{previous}{camera.PreviewFilter()}[camera_out]");
            previous = "[camera_out]";
        }
        // Bars on every layer: fixed on screen, over the camera (VEGAS applies them after Pan/Crop).
        if ((scene.Cinema ?? []).Where(x => x.Layers == "todos").ToArray() is { Length: > 0 } fullBars)
        {
            filters.AddRange(CinemaPlan.PreviewFilters(fullBars, previous, "[bars_all]", "bars_all", scene.DurationMs));
            previous = "[bars_all]";
        }
        foreach (var transition in scene.Transitions ?? [])
        {
            var transitionEnd = Math.Min(scene.DurationMs, transition.StartMs + transition.DurationMs);
            if (transitionEnd <= transition.StartMs) continue;
            var duration = Seconds(transitionEnd - transition.StartMs);
            var label = $"v{n++}";
            var layer = $"transition{n}";
            if (transition.Style == "cruce") continue;
            var effect = transition.Style is "salida" or "cambio" ? "in" : "out";
            var fade = transition.Style == "cambio"
                ? $",fade=t=out:st={Seconds(transition.DurationMs / 2)}:d={Seconds(transition.DurationMs - transition.DurationMs / 2)}:alpha=1"
                : "";
            var fadeInDuration = transition.Style == "cambio" ? Seconds(transition.DurationMs / 2) : duration;
            filters.Add($"color=c=black:s=1280x720:r=25:d={duration},format=rgba," +
                $"fade=t={effect}:st=0:d={fadeInDuration}:alpha=1{fade}," +
                $"setpts=PTS-STARTPTS+{Seconds(transition.StartMs)}/TB[{layer}]");
            filters.Add($"{previous}[{layer}]overlay=0:0:enable='gte(t,{Seconds(transition.StartMs)})*" +
                $"lt(t,{Seconds(transitionEnd)})':eof_action=pass:repeatlast=0:format=auto[{label}]");
            previous = $"[{label}]";
        }
        filters.Add($"{previous}format=yuv420p[vout]");
        var audioLabels = new List<string>();
        foreach (var clip in audible)
        {
            var label = $"a{audioLabels.Count}";
            var maxLength = clip.Kind == ScriptBlockKind.Music && !clip.ExplicitAudioDuration
                ? scene.DurationMs - clip.StartMs : clip.Kind == ScriptBlockKind.Video
                    ? VideoAudioEnd(clip, scene.Media, scene.DurationMs) - clip.StartMs : clip.DurationMs;
            if (maxLength <= 0) continue;
            var tempo = clip.StretchAudio && clip.SourceDurationMs > 0
                ? TempoFilters(clip.SourceDurationMs / (double)maxLength) : "";
            filters.Add($"[{indexes[clip.BlockId]}:a]aresample=48000,asetpts=PTS-STARTPTS{tempo},apad,atrim=duration={Seconds(maxLength)},asetpts=PTS-STARTPTS,adelay={clip.StartMs}:all=1,volume={(clip.VolumePercent / 100d).ToString("0.##", CultureInfo.InvariantCulture)}[{label}]");
            audioLabels.Add($"[{label}]");
        }
        if (audioLabels.Count > 0) filters.Add($"{string.Join("", audioLabels)}amix=inputs={audioLabels.Count}:duration=longest:normalize=0,apad,atrim=duration={Seconds(scene.DurationMs)}[aout]");
        else filters.Add($"anullsrc=r=48000:cl=stereo,atrim=duration={Seconds(scene.DurationMs)}[aout]");
        var graph = string.Join(";", filters);
        var inputArgs = psi.ArgumentList.ToArray();
        string[] outputArgs = ["-map", "[vout]", "-map", "[aout]", "-t", Seconds(scene.DurationMs),
            "-c:v", "libx264", "-preset", "veryfast", "-pix_fmt", "yuv420p", "-c:a", "aac", "-movflags", "+faststart", temp];
        string? graphFile = null;
        try
        {
            if (!NeedsFilterGraphFile(inputArgs, graph, outputArgs))
                await RunFfmpegAsync([.. inputArgs, "-filter_complex", graph, .. outputArgs], token);
            else
            {
                // Windows caps a command line at 32 767 characters and long scenes exceed it.
                // FFmpeg 7+ reads an option value from a file with "-/option"; older builds only
                // know -filter_complex_script (deprecated in 7.1), so fall back to it.
                graphFile = Path.Combine(Path.GetDirectoryName(outputPath)!, $".{Guid.NewGuid():N}.filtergraph.txt");
                await File.WriteAllTextAsync(graphFile, graph, new UTF8Encoding(false), token);
                try { await RunFfmpegAsync([.. inputArgs, "-/filter_complex", graphFile, .. outputArgs], token); }
                catch (FfmpegOptionException)
                {
                    await RunFfmpegAsync([.. inputArgs, "-filter_complex_script", graphFile, .. outputArgs], token);
                }
            }
            File.Move(temp, outputPath, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
            if (graphFile is not null && File.Exists(graphFile)) File.Delete(graphFile);
        }
    }

    /// <summary>Leaves a wide margin under the 32 767-character Windows limit (the executable path
    /// and quoting also count).</summary>
    internal const int InlineCommandLineBudget = 24_000;

    internal static bool NeedsFilterGraphFile(IEnumerable<string> inputArgs, string graph, IEnumerable<string> outputArgs) =>
        inputArgs.Concat(outputArgs).Sum(arg => arg.Length + 3) + graph.Length + 3 > InlineCommandLineBudget;

    private sealed class FfmpegOptionException(string message) : InvalidOperationException(message);

    private static async Task RunFfmpegAsync(IEnumerable<string> arguments, CancellationToken token)
    {
        var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo iniciar FFmpeg.");
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var error = process.StandardError.ReadToEndAsync(token);
        var standard = process.StandardOutput.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        var details = await error;
        await standard;
        if (process.ExitCode == 0) return;
        if (details.Contains("filter_complex", StringComparison.OrdinalIgnoreCase) &&
            (details.Contains("Unrecognized option", StringComparison.OrdinalIgnoreCase) ||
             details.Contains("Option not found", StringComparison.OrdinalIgnoreCase)))
            throw new FfmpegOptionException(details);
        throw new InvalidOperationException($"FFmpeg no pudo generar la escena: {details}");
    }

    private static string Seconds(long milliseconds) => (milliseconds / 1000d).ToString("0.###", CultureInfo.InvariantCulture);

    private static string TempoFilters(double ratio)
    {
        var filters = new List<string>();
        while (ratio < 0.5) { filters.Add(",atempo=0.5"); ratio /= 0.5; }
        while (ratio > 2) { filters.Add(",atempo=2"); ratio /= 2; }
        if (Math.Abs(ratio - 1) > 0.0001) filters.Add(",atempo=" + ratio.ToString("0.######", CultureInfo.InvariantCulture));
        return string.Concat(filters);
    }
}
