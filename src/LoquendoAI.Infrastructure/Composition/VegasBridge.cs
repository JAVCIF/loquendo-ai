using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LoquendoAI.Core.Models;

namespace LoquendoAI.Infrastructure.Composition;

public enum VegasExportMode { Legacy, Normal }

/// <summary>Legacy: every audio clip on its own track (previous behaviour). PerCharacter: each
/// speaker's voice lines share one track across the whole export (a second track only when two
/// lines of the same speaker overlap); music, SFX and video audio are packed into the fewest
/// tracks with the same volume and no overlapping events, so VEGAS never auto-crossfades them.</summary>
public enum VegasAudioTrackMode { Legacy, PerCharacter }

/// <summary>One scene of an export. Scenes are laid out one after another on the timeline.</summary>
public sealed record VegasSceneInput(string Name, SceneComposition Scene, IReadOnlyDictionary<Guid, string>? OriginalPaths = null);

/// <summary>Exports a scene as a VEGAS script plus an inspectable source manifest.
/// VEGAS itself writes the native .veg file when the script is run.</summary>
public static class VegasBridge
{
    /// <summary>Pan/Crop of an original source: Left…Bottom select the source pixels shown in the frame;
    /// PivotX/PivotY (source pixels) is the point rotation and motion turn around, the layer's center.</summary>
    public sealed record NativeFrame(double Left, double Top, double Right, double Bottom,
        double CenterX, double CenterY, double MotionUnitsX, double MotionUnitsY, double? PivotX = null, double? PivotY = null)
    {
        public double RotationCenterX => PivotX ?? CenterX;
        public double RotationCenterY => PivotY ?? CenterY;
    }

    /// <summary>VEGAS «Cortador de galletas» on an event: border and size keyframes (CinemaPlan.Keys).</summary>
    public sealed record LetterboxFx(IReadOnlyList<CinemaKey> Keys);

    /// <summary>Pan/Crop of one keyframe of a native clip, <see cref="AtMs"/> after the event starts.</summary>
    public sealed record NativeKey(long AtMs, NativeFrame Frame);

    public sealed record Clip(string Kind, Guid BlockId, long StartMs, long DurationMs, string Path,
        int Layer, string Position, int MaxWidth, int MaxHeight, int OffsetX, int OffsetY,
        bool FlipHorizontal, bool FlipVertical, bool GreenScreen, string KeyColor,
        double KeyTolerance, long SourceDurationMs, bool StretchAudio, string VideoLayer,
        bool ExplicitAudioDuration, string? ImportPath = null, bool SourceHasAlpha = false,
        string FramingPreset = "original", string VisualCrop = "", IReadOnlyList<CameraSample>? CameraKeys = null,
        long SourceOffsetMs = 0, double RotationDegrees = 0, long FadeInMs = 0,
        string VegasTransitionId = "", string VegasTransitionPreset = "", string TrackKey = "",
        int VolumePercent = 100, int MotionOffsetX = 0, int MotionOffsetY = 0,
        double MotionRotationDegrees = 0, long MotionDurationMs = 0,
        string? OriginalPath = null, NativeFrame? Native = null, Guid? CharacterId = null,
        double? MediaPivotX = null, double? MediaPivotY = null, IReadOnlyList<NativeKey>? NativeKeys = null,
        LetterboxFx? Cinema = null, IReadOnlyList<GestureKey>? GestureKeys = null, double GesturePivot = 1,
        LayerLayout? Layout = null, IReadOnlyList<BlurKey>? BlurKeys = null);

    public static IReadOnlyList<Clip> BuildClips(SceneComposition scene)
    {
        var clips = new List<Clip>();
        var all = scene.Media.ToArray();
        var visualOrder = 0;
        var videoKeys = new Dictionary<Guid, string>();
        foreach (var video in all.Where(x => x.Kind == ScriptBlockKind.Video))
        {
            var previous = SceneComposer.VideoReplacement(video, all);
            videoKeys[video.BlockId] = previous is not null && videoKeys.TryGetValue(previous.BlockId, out var key)
                ? key : "video:" + video.BlockId.ToString("N");
        }
        void AddVisual(SceneMedia item, long end)
        {
            end = Math.Min(scene.DurationMs, end);
            if (end <= item.StartMs) return;
            var trackKey = item.Kind switch
            {
                ScriptBlockKind.Background => "fondo",
                ScriptBlockKind.CharacterShow => "personaje:" + (item.CharacterId ?? item.BlockId).ToString("N"),
                ScriptBlockKind.Image => "imagenes",
                ScriptBlockKind.Video => videoKeys[item.BlockId],
                _ => "visual:" + item.BlockId.ToString("N")
            };
            clips.Add(new Clip(item.Kind.ToString(), item.BlockId, item.StartMs, end - item.StartMs,
                item.Path, visualOrder++, item.Position, item.VisualMaxWidth, item.VisualMaxHeight,
                item.VisualOffsetX, item.VisualOffsetY, item.FlipHorizontal, item.FlipVertical,
                item.GreenScreen, item.KeyColor, item.KeyTolerance, 0, false, item.VideoLayer, false,
                FramingPreset: item.FramingPreset, VisualCrop: item.VisualCrop,
                RotationDegrees: item.RotationDegrees, FadeInMs: item.FadeInMs,
                VegasTransitionId: item.VegasTransitionId, VegasTransitionPreset: item.VegasTransitionPreset,
                TrackKey: trackKey, VolumePercent: item.VolumePercent,
                MotionOffsetX: item.MotionOffsetX, MotionOffsetY: item.MotionOffsetY,
                MotionRotationDegrees: item.MotionRotationDegrees,
                MotionDurationMs: item.MotionDurationMs > 0 ? item.MotionDurationMs : end - item.StartMs,
                CharacterId: item.Kind == ScriptBlockKind.CharacterShow ? item.CharacterId : null));
        }
        var backgrounds = all.Where(x => x.Kind == ScriptBlockKind.Background).ToArray();
        for (var i = 0; i < backgrounds.Length; i++)
        {
            var item = backgrounds[i];
            AddVisual(item, Math.Min(item.DurationMs > 0 ? item.StartMs + item.DurationMs : scene.DurationMs,
                i + 1 < backgrounds.Length ? SceneComposer.VisualCutoff(backgrounds[i + 1]) : scene.DurationMs));
        }
        foreach (var item in all.Where(x => x.Kind == ScriptBlockKind.Video && x.VideoLayer == "fondo"))
            AddVisual(item, Math.Min(item.StartMs + item.DurationMs,
                Math.Min(all.SkipWhile(x => x.BlockId != item.BlockId).Skip(1)
                    .Where(x => x.Kind == ScriptBlockKind.Background).Select(SceneComposer.VisualCutoff)
                    .DefaultIfEmpty(scene.DurationMs).Min(),
                    SceneComposer.VideoCutoff(item, all, scene.DurationMs))));

        var layers = new List<(SceneMedia Item, long End)>();
        var visible = new Dictionary<Guid, SceneMedia>();
        foreach (var item in all.Where(x => x.Kind is ScriptBlockKind.CharacterShow or ScriptBlockKind.CharacterHide))
        {
            if (SceneComposer.RenderKey(item) is not Guid key) continue;
            if (visible.Remove(key, out var previous) && item.StartMs > previous.StartMs)
                layers.Add((previous, SceneComposer.VisualCutoff(item)));
            if (item.Kind == ScriptBlockKind.CharacterShow) visible[key] = item;
        }
        layers.AddRange(visible.Values.Select(x => (x, scene.DurationMs)));
        var props = all.Where(x => x.Kind == ScriptBlockKind.Image).ToArray();
        for (var i = 0; i < props.Length; i++)
            layers.Add((props[i], i + 1 < props.Length ? SceneComposer.VisualCutoff(props[i + 1]) : scene.DurationMs));
        layers.AddRange(all.Where(x => x.Kind == ScriptBlockKind.Video && x.VideoLayer != "fondo")
            .Select(x => (x, Math.Min(x.StartMs + x.DurationMs, SceneComposer.VideoCutoff(x, all, scene.DurationMs)))));
        var order = all.Select((item, index) => (item.BlockId, index)).ToDictionary(x => x.BlockId, x => x.index);
        foreach (var (item, end) in layers.OrderBy(x => x.Item.VideoLayer == "sobre" && x.Item.Kind == ScriptBlockKind.Video
                     ? int.MaxValue : order[x.Item.BlockId]).ThenBy(x => order[x.Item.BlockId]))
            AddVisual(item, Math.Min(end, item.DurationMs > 0 ? item.StartMs + item.DurationMs : scene.DurationMs));

        foreach (var item in all.Where(x => x.Kind is ScriptBlockKind.Dialogue or ScriptBlockKind.Narration or ScriptBlockKind.SoundEffect or ScriptBlockKind.Music))
        {
            var duration = item.Kind == ScriptBlockKind.Music && !item.ExplicitAudioDuration
                ? scene.DurationMs - item.StartMs : item.DurationMs;
            duration = Math.Min(duration, scene.DurationMs - item.StartMs);
            if (duration <= 0) continue;
            clips.Add(new Clip(item.Kind.ToString(), item.BlockId, item.StartMs, duration,
                item.Path, -1, item.Position, 0, 0, 0, 0, false, false, false, "", 0,
                item.SourceDurationMs, item.StretchAudio, "", item.ExplicitAudioDuration,
                VolumePercent: item.VolumePercent, CharacterId: item.CharacterId));
        }
        foreach (var video in all.Where(x => x.Kind == ScriptBlockKind.Video && x.HasVideoAudio && x.VolumePercent > 0))
        {
            var end = SceneComposer.VideoAudioEnd(video, all, scene.DurationMs);
            if (end <= video.StartMs) continue;
            clips.Add(new Clip(nameof(ScriptBlockKind.Video), video.BlockId, video.StartMs, end - video.StartMs,
                video.Path, -1, video.Position, 0, 0, 0, 0, false, false, false, "", 0,
                video.SourceDurationMs, false, video.VideoLayer, true, VolumePercent: video.VolumePercent));
        }
        return clips;
    }

    public static Task ExportAsync(SceneComposition scene, string directory, string sceneName,
        int exportHeight = 720, CancellationToken token = default,
        IReadOnlyDictionary<Guid, string>? originalPaths = null,
        VegasExportMode mode = VegasExportMode.Legacy,
        VegasAudioTrackMode audioMode = VegasAudioTrackMode.Legacy,
        IReadOnlyDictionary<Guid, string>? characterNames = null) =>
        ExportEpisodeAsync([new VegasSceneInput(sceneName, scene, originalPaths)], directory, sceneName,
            exportHeight, token, mode, audioMode, characterNames);

    /// <summary>Exports one or more scenes into a single VEGAS project: each scene starts where the
    /// previous one ends, tracks with the same key (background, each character, each speaker…)
    /// are shared across scenes, and every scene gets a named region.</summary>
    public static async Task ExportEpisodeAsync(IReadOnlyList<VegasSceneInput> scenes, string directory, string projectName,
        int exportHeight = 720, CancellationToken token = default,
        VegasExportMode mode = VegasExportMode.Legacy,
        VegasAudioTrackMode audioMode = VegasAudioTrackMode.Legacy,
        IReadOnlyDictionary<Guid, string>? characterNames = null)
    {
        if (scenes.Count == 0) throw new ArgumentException("No hay escenas para exportar.", nameof(scenes));
        if (exportHeight is not (720 or 1080)) throw new ArgumentOutOfRangeException(nameof(exportHeight));
        var exportWidth = exportHeight * 16 / 9;
        var allClips = new List<Clip>();
        var allTransitions = new List<SceneTransition>();
        var regions = new List<(long StartMs, long DurationMs, string Name)>();
        long offset = 0;
        foreach (var input in scenes)
        {
            token.ThrowIfCancellationRequested();
            var scene = GesturePlan.Resolve(CinemaPlan.Resolve(await CharacterFraming.ApplyAsync(input.Scene, token)));
            var start = offset;
            allClips.AddRange(SplitForCamera(BuildClips(scene), scene.Camera ?? CameraPath.Still,
                    scene.Transitions ?? Array.Empty<SceneTransition>(), scene.Cinema ?? [], scene.Gestures, scene.BlurCues)
                .Select(clip => clip with
                {
                    StartMs = clip.StartMs + start,
                    OriginalPath = input.OriginalPaths is not null &&
                        input.OriginalPaths.TryGetValue(clip.BlockId, out var original) ? original : null
                }));
            allTransitions.AddRange((scene.Transitions ?? []).Select(x => x with { StartMs = x.StartMs + start }));
            regions.Add((start, scene.DurationMs, input.Name));
            offset += scene.DurationMs;
        }
        var clips = allClips.ToArray();
        foreach (var clip in clips)
            if (!File.Exists(clip.Path)) throw new FileNotFoundException($"Falta un medio para VEGAS: {clip.Kind}", clip.Path);
        Directory.CreateDirectory(directory);
        var dimensions = new Dictionary<string, (int Width, int Height)>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < clips.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var clip = clips[i];
            if (clip.Layer < 0) continue;
            // Motion is applied later by native VEGAS event Pan/Crop keyframes.
            // An animated still therefore remains a PNG, not a rendered ProRes movie.
            var animated = clip.Kind == nameof(ScriptBlockKind.Video) ||
                Path.GetExtension(clip.Path).Equals(".gif", StringComparison.OrdinalIgnoreCase);
            var hasAlpha = clip.Kind == nameof(ScriptBlockKind.Video) && await HasAlphaAsync(clip.Path, token);
            if (!dimensions.TryGetValue(clip.Path, out var size))
                dimensions[clip.Path] = size = await SceneComposer.ProbeDimensionsAsync(clip.Path, token);
            // Gestures are written as whole Pan/Crop keyframes computed from the layer placement.
            if (clip.GestureKeys is not null)
                clips[i] = clip = clip with
                {
                    Layout = LayerGeometry.Place(LayerSpec.From(clip), size.Width, size.Height, exportWidth, exportHeight)
                };
            var keys = mode == VegasExportMode.Normal && !clip.GreenScreen && !hasAlpha
                ? NativeKeysFor(clip, size.Width, size.Height, exportWidth, exportHeight) : null;
            if (keys is not null)
            {
                clips[i] = clip with { Native = keys[0].Frame, NativeKeys = keys };
                continue;
            }
            // The camera is not rendered into the media (it is Pan/Crop keyframes on the event), so a still
            // with the same look is rendered once and reused by every event, scene and block that shows it.
            var mediaKey = clip with
            {
                BlockId = animated ? clip.BlockId : Guid.Empty, StartMs = animated ? clip.StartMs : 0,
                DurationMs = animated ? clip.DurationMs : 0, SourceOffsetMs = animated ? clip.SourceOffsetMs : 0,
                CameraKeys = null, NativeKeys = null, Cinema = null, GestureKeys = null, GesturePivot = 1, Layout = null, BlurKeys = null,
                FadeInMs = 0, VegasTransitionId = "", VegasTransitionPreset = "",
                TrackKey = "", VolumePercent = 0, OriginalPath = null, CharacterId = null
            };
            var settingsHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(mediaKey))))[..12];
            var path = Path.Combine(directory, "medios_normalizados", animated
                ? $"clip_{clip.BlockId:N}_{clip.StartMs}_{exportHeight}p_native_{settingsHash}.mov"
                : $"clip_{settingsHash}_{exportHeight}p.png");
            var layout = LayerGeometry.Place(LayerSpec.From(clip), size.Width, size.Height, exportWidth, exportHeight);
            var pivot = await CreateVisualCanvasAsync(clip, path, exportWidth, exportHeight, animated, layout, token);
            clips[i] = clip with { ImportPath = path, SourceHasAlpha = hasAlpha, MediaPivotX = pivot.X, MediaPivotY = pivot.Y };
        }
        var name = SafeName(projectName);
        var transitions = allTransitions.ToArray();
        var blackPath = Path.Combine(directory, $"fundidos_{exportHeight}p.png");
        if (transitions.Any(x => x.Style != "cruce"))
            await CreateBlackTransitionMediaAsync(blackPath, exportWidth, exportHeight, token);
        var names = characterNames ?? new Dictionary<Guid, string>();
        var manifest = new { Format = "LoquendoAI.VegasBridge.v15", Mode = mode.ToString(), AudioTracks = audioMode.ToString(),
            Scene = projectName, DurationMs = offset, ProjectWidth = exportWidth,
            ProjectHeight = exportHeight, FrameRate = 25,
            Scenes = regions.Select(x => new { x.Name, x.StartMs, x.DurationMs }).ToArray(),
            Clips = clips, Transitions = transitions };
        File.WriteAllText(Path.Combine(directory, "escena.json"), JsonSerializer.Serialize(manifest,
            new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        var sceneRegions = scenes.Count > 1 ? regions : [];
        var modern = CreateScript(clips, name, false, exportWidth, exportHeight, transitions, blackPath, audioMode, names, sceneRegions);
        File.WriteAllText(Path.Combine(directory, "Abrir_en_VEGAS_14_o_superior.cs"), modern, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "Abrir_en_VEGAS_12_13.cs"),
            CreateScript(clips, name, true, exportWidth, exportHeight, transitions, blackPath, audioMode, names, sceneRegions),
            new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "LEEME.txt"),
            "Abre un proyecto NUEVO y vacío en VEGAS. Menú Herramientas > Scripts > Ejecutar script.\r\n" +
            "Usa el .cs de tu versión. El script inserta pistas y guarda " + name + ".veg en esta carpeta.\r\n" +
            (scenes.Count > 1 ? "Episodio: " + scenes.Count + " escenas seguidas; cada una tiene su región con nombre en la línea de tiempo.\r\n" : "") +
            (audioMode == VegasAudioTrackMode.PerCharacter
                ? "Audio por personaje: una pista de voz por personaje (y Narrador) en todo el proyecto; música, SFX y audio de vídeo\r\n" +
                  "comparten pistas con el mismo volumen sin eventos solapados. Si dos líneas del mismo personaje se solapan, va una pista (2).\r\n"
                : "Audio Legacy: cada clip de audio en su propia pista.\r\n") +
            (mode == VegasExportMode.Normal
                ? "Modo Normal: fondo, render, imagen, GIF y vídeo sin alfa/croma usan el medio original.\r\n" +
                  "Pan/Crop conserva encuadre, posición, giro y movimiento por evento.\r\n" +
                  "Vídeos con alfa o croma, y recortes no compatibles, conservan medio Legacy.\r\n" +
                  "Comprueba la reproducción de GIF animados en tu versión de VEGAS.\r\n"
                : "Modo Legacy: visuales normalizados en lienzo " + exportWidth + " x " + exportHeight + ".\r\n" +
                  "PNG para imágenes estáticas, MOV ProRes 4444 para GIF y vídeo.\r\n") +
            "Cada medio normalizado incluye el original como segunda toma editable.\r\n" +
            "X/Y y giro animados tienen keyframes editables en Panoramización/Recorte del evento.\r\n" +
            "El tamaño, encuadre, posición inicial y rotación inicial se conservan en el evento o medio normalizado.\r\n" +
            "La cámara divide eventos visuales para conservar capas editables. Si activas la toma\r\n" +
            "original de un video dividido, revisa su punto de entrada manualmente en VEGAS.\r\n" +
            "Conserva esta carpeta y las rutas de la Biblioteca: el .veg depende de ambos.\r\n" +
            "escena.json conserva todos los parámetros para futuras mejoras del bridge.\r\n" +
            (mode == VegasExportMode.Normal
                ? "Visuales originales: el Pan/Crop del evento contiene tamaño/posición en el proyecto.\r\n"
                : "Cada visual Legacy ya trae su tamaño/posición y cubre el lienzo del proyecto.\r\n") +
            "Para vídeo con croma, el medio procesado copia el croma del compositor;\r\n" +
            "VEGAS puede mostrar distintos los bordes.\r\n" +
            "para cambiar una composición, edítala en Loquendo y exporta de nuevo.\r\n" +
            "Entrada, salida y cambio a mitad usan una pista superior editable.\r\n" +
            "Cruce superpone eventos del mismo fondo en UNA pista: la transición nativa vive sobre su cruce.\r\n" +
            "Cada medio entrante puede heredar la selección de capas, cortar o elegir su fundido/plugin.\r\n" +
            "Los visuales que heredan y están excluidos se cortan a mitad del cruce.\r\n" +
            "Cada personaje y cada medio reemplazado comparten pista: los demás clips conservan sus capas.\r\n" +
            "La preview aproxima Flash con un destello blanco; los plugins exactos se ven en VEGAS.\r\n" +
            "Texto en pantalla aún no forma parte del compositor.\r\n", new UTF8Encoding(false));
    }

    private static async Task CreateBlackTransitionMediaAsync(string path, int width, int height, CancellationToken token)
    {
        if (File.Exists(path) && new FileInfo(path).Length > 1024) return;
        var processInfo = new ProcessStartInfo("ffmpeg")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi",
                     "-i", $"color=c=black:s={width}x{height}", "-frames:v", "1", path })
            processInfo.ArgumentList.Add(arg);
        using var process = Process.Start(processInfo) ?? throw new InvalidOperationException("No se pudo iniciar FFmpeg.");
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var error = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        if (process.ExitCode != 0) throw new InvalidOperationException($"No se pudo crear el medio del fundido: {await error}");
    }

    /// <summary>
    /// Cuts visual clips where the camera jumps (a camera move of 0 ms) and gives every piece the camera
    /// keyframes it needs. Motion keeps its original span across the pieces.
    /// </summary>
    private static IEnumerable<Clip> SplitForCamera(IReadOnlyList<Clip> clips, CameraPath camera,
        IReadOnlyList<SceneTransition> transitions, IReadOnlyList<CinemaSegment> cinema,
        IReadOnlyDictionary<Guid, GestureTrack>? gestures = null, IReadOnlyList<BlurCue>? blurCues = null)
    {
        var cuts = camera.IsStill ? [] : camera.Cuts.ToArray();
        foreach (var clip in clips)
        {
            var kind = Enum.Parse<ScriptBlockKind>(clip.Kind);
            // Cinema bars: the Cookie Cutter goes on this layer's events with keyframes; the bars cut no event.
            var bars = clip.Layer < 0 ? [] : cinema.Where(x => x.Affects(kind, clip.CharacterId, clip.BlockId)).ToArray();
            var gesture = clip.Layer < 0 ? null : gestures?.GetValueOrDefault(clip.BlockId);
            // Blur: the level of this layer over the scene; each event gets its part as OFX keyframes.
            var blur = clip.Layer < 0 ? [] : BlurPlan.Curve(blurCues, kind, clip.VideoLayer, clip.CharacterId);
            if (clip.Layer < 0 || camera.IsStill && bars.Length == 0 && gesture is null && blur.Count == 0)
            {
                yield return clip;
                continue;
            }
            var end = clip.StartMs + clip.DurationMs;
            var boundaries = new[] { clip.StartMs, end }
                .Concat(cuts
                    .Concat(camera.IsStill ? [] : transitions.Where(x => x.Style == "cruce").Select(x => x.StartMs + x.DurationMs))
                    .Where(x => x >= clip.StartMs + clip.FadeInMs && x > clip.StartMs && x < end)
                    .Where(x => !transitions.Any(t => t.Style == "cruce" &&
                        x >= t.StartMs && x < t.StartMs + t.DurationMs)))
                .Distinct().OrderBy(x => x).ToArray();
            var motionSpan = clip.MotionOffsetX != 0 || clip.MotionOffsetY != 0 || Math.Abs(clip.MotionRotationDegrees) > 0.0001
                ? CameraPlanner.MotionSpan(clip) : clip.MotionDurationMs;
            for (var i = 0; i + 1 < boundaries.Length; i++)
            {
                var start = boundaries[i];
                var duration = boundaries[i + 1] - start;
                var keys = camera.IsStill ? [] : camera.Samples(start, duration);
                yield return clip with
                {
                    StartMs = start,
                    DurationMs = duration,
                    CameraKeys = keys.Count == 0 || keys.All(x => x.Window.IsFull) ? null : keys,
                    Cinema = CinemaPlan.Keys(bars, start, start + duration) is { } cinemaKeys ? new LetterboxFx(cinemaKeys) : null,
                    MotionDurationMs = motionSpan,
                    GestureKeys = gesture?.Slice(start, start + duration)?.Keys,
                    BlurKeys = BlurPlan.Slice(blur, start, start + duration),
                    GesturePivot = gesture?.PivotFraction ?? 1,
                    SourceOffsetMs = start - clip.StartMs,
                    FadeInMs = start == clip.StartMs ? clip.FadeInMs : 0,
                    VegasTransitionId = start == clip.StartMs ? clip.VegasTransitionId : "",
                    VegasTransitionPreset = start == clip.StartMs ? clip.VegasTransitionPreset : ""
                };
            }
        }
    }

    /// <summary>Camera window at <paramref name="atMs"/> into the clip (linear between its camera keys).</summary>
    internal static CameraWindow CameraAt(Clip clip, long atMs)
    {
        if (clip.CameraKeys is not { Count: > 0 } keys) return CameraWindow.Full;
        if (atMs <= keys[0].AtMs) return keys[0].Window;
        for (var i = 1; i < keys.Count; i++)
            if (atMs <= keys[i].AtMs)
                return CameraWindow.Lerp(keys[i - 1].Window, keys[i].Window,
                    (atMs - keys[i - 1].AtMs) / (double)Math.Max(1, keys[i].AtMs - keys[i - 1].AtMs));
        return keys[^1].Window;
    }

    /// <summary>
    /// Keyframe times of a clip: its start, where the camera changes speed and where its own motion
    /// stops, plus its end while either is still moving. Both are linear between these points, so linear
    /// VEGAS keyframes reproduce the preview exactly.
    /// </summary>
    internal static IReadOnlyList<long> KeyTimes(Clip clip)
    {
        var times = new SortedSet<long> { 0 };
        foreach (var key in clip.CameraKeys ?? []) times.Add(Math.Clamp(key.AtMs, 0, clip.DurationMs));
        foreach (var key in clip.GestureKeys ?? []) times.Add(Math.Clamp(key.AtMs, 0, clip.DurationMs));
        if (clip.MotionOffsetX != 0 || clip.MotionOffsetY != 0 || Math.Abs(clip.MotionRotationDegrees) > 0.0001)
        {
            var remaining = Math.Max(40, clip.MotionDurationMs > 0 ? clip.MotionDurationMs : clip.DurationMs) - clip.SourceOffsetMs;
            if (remaining > 0) times.Add(Math.Min(clip.DurationMs, remaining));
        }
        return times.ToArray();
    }

    /// <summary>Motion progress (0–1) of a clip's own animation at <paramref name="atMs"/> into the event.</summary>
    internal static double MotionProgress(Clip clip, long atMs)
    {
        var span = Math.Max(40, clip.MotionDurationMs > 0 ? clip.MotionDurationMs : clip.DurationMs);
        return Math.Clamp((clip.SourceOffsetMs + atMs) / (double)span, 0, 1);
    }

    private static IReadOnlyList<NativeKey>? NativeKeysFor(Clip clip, int sourceWidth, int sourceHeight, int width, int height)
    {
        var keys = new List<NativeKey>();
        foreach (var at in KeyTimes(clip))
        {
            if (!TryNativeFrame(clip, sourceWidth, sourceHeight, width, height, CameraAt(clip, at), out var frame)) return null;
            keys.Add(new NativeKey(at, frame!));
        }
        return keys;
    }

    private static async Task<bool> HasAlphaAsync(string path, CancellationToken token)
    {
        if (!MediaProbeCache.TryGet("pixfmt", path, out var format))
        {
            format = await PixelFormatAsync(path, token);
            MediaProbeCache.Set("pixfmt", path, format);
        }
        return format.StartsWith("yuva", StringComparison.OrdinalIgnoreCase) ||
            format.StartsWith("gbrap", StringComparison.OrdinalIgnoreCase) ||
            format.StartsWith("rgba", StringComparison.OrdinalIgnoreCase) ||
            format.StartsWith("bgra", StringComparison.OrdinalIgnoreCase) ||
            format.StartsWith("argb", StringComparison.OrdinalIgnoreCase) ||
            format.StartsWith("abgr", StringComparison.OrdinalIgnoreCase) ||
            format.StartsWith("ya", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> PixelFormatAsync(string path, CancellationToken token)
    {
        var psi = new ProcessStartInfo("ffprobe") { RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in new[] { "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=pix_fmt",
                     "-of", "default=noprint_wrappers=1:nokey=1", path }) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo iniciar ffprobe.");
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        if (process.ExitCode != 0) throw new InvalidOperationException($"No se pudo analizar el video {Path.GetFileName(path)}: {await error}");
        return (await output).Trim();
    }

    private static bool TryNativeFrame(Clip clip, int sourceWidth, int sourceHeight,
        int width, int height, CameraWindow camera, out NativeFrame? frame)
    {
        frame = null;
        // Same placement as the preview (LayerGeometry). A covering layer whose box cuts the image
        // inside the frame cannot be expressed with Pan/Crop: it is exported as normalized media.
        if (LayerGeometry.Place(LayerSpec.From(clip), sourceWidth, sourceHeight, width, height) is not { } layout ||
            LayerGeometry.NeedsBoxCrop(layout, width, height)) return false;
        var factor = height / 720d;
        var scale = layout.Scale;
        // The camera window (1280×720 units) in export pixels: the frame shows only that part of the scene.
        var cameraX = camera.X * factor;
        var cameraY = camera.Y * factor;
        var cameraW = camera.Width * factor;
        var cameraH = camera.Height * factor;
        // Inverse map: each corner of the project frame selects source pixels.
        // Its width/height follow the output aspect, like "Match Output Aspect".
        var boundsW = cameraW / scale;
        var boundsH = boundsW * height / width;
        var left = layout.SourceX + (cameraX - layout.X) / scale;
        var top = layout.SourceY + (cameraY - layout.Y + (cameraH - boundsH * scale) / 2) / scale;
        // Rotation and motion pivot on the layer's own center, as in the preview.
        var pivotX = layout.SourceX + (layout.CenterX - layout.X) / scale;
        var pivotY = layout.SourceY + (layout.CenterY - layout.Y) / scale;
        frame = new NativeFrame(left, top, left + boundsW, top + boundsH,
            left + boundsW / 2, top + boundsH / 2, factor / scale, factor / scale, pivotX, pivotY);
        return true;
    }

    private static (int Width, int Height, int Left, int Top) NativeCanvas(Clip clip, int width, int height)
    {
        // Backgrounds need pixels beyond the viewport when native Pan/Crop moves
        // or rotates the frame. The camera only zooms in, so it never needs more.
        var background = clip.Kind == nameof(ScriptBlockKind.Background) ||
            clip.Kind == nameof(ScriptBlockKind.Video) && clip.VideoLayer == "fondo";
        if (!background ||
            clip.MotionOffsetX == 0 && clip.MotionOffsetY == 0 &&
            Math.Abs(clip.MotionRotationDegrees) < 0.0001)
            return (width, height, 0, 0);

        var factor = height / 720d;
        double horizontal = 0, vertical = 0;
        var steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(clip.MotionRotationDegrees) / 3));
        for (var i = 0; i <= steps; i++)
        {
            var angle = clip.MotionRotationDegrees * i / steps * Math.PI / 180;
            var cosine = Math.Abs(Math.Cos(angle));
            var sine = Math.Abs(Math.Sin(angle));
            horizontal = Math.Max(horizontal, (width * cosine + height * sine - width) / 2);
            vertical = Math.Max(vertical, (height * cosine + width * sine - height) / 2);
        }
        var left = (int)Math.Ceiling(Math.Abs(clip.MotionOffsetX * factor) + horizontal + 16);
        var top = (int)Math.Ceiling(Math.Abs(clip.MotionOffsetY * factor) + vertical + 16);
        return (width + 2 * left, height + 2 * top, left, top);
    }

    /// <summary>Renders the visual on a transparent project-sized canvas and returns the point (in canvas
    /// pixels) its VEGAS rotation and motion must turn around: the layer's center, as in the preview.</summary>
    private static async Task<(double X, double Y)> CreateVisualCanvasAsync(Clip clip, string output, int width, int height,
        bool animated, LayerLayout? layout, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var canvas = NativeCanvas(clip, width, height);
        var pivotFactor = height / 720d;
        (double X, double Y) pivot = layout is { Fill: false }
            ? (layout.CenterX + canvas.Left, layout.CenterY + canvas.Top)
            : (canvas.Width / 2d + (clip.Kind == nameof(ScriptBlockKind.Background) ? clip.OffsetX * pivotFactor : 0),
               canvas.Height / 2d + (clip.Kind == nameof(ScriptBlockKind.Background) ? clip.OffsetY * pivotFactor : 0));
        if (File.Exists(output) && new FileInfo(output).Length > 1024) return pivot;
        var temp = Path.Combine(Path.GetDirectoryName(output)!, $".{Guid.NewGuid():N}" +
            (animated ? ".mov" : ".png"));
        var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true };
        void Args(params string[] args) { foreach (var arg in args) psi.ArgumentList.Add(arg); }
        Args("-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i",
            $"color=c=black:s={canvas.Width}x{canvas.Height}:r=25");
        var sourceAnimated = clip.Kind == nameof(ScriptBlockKind.Video) ||
            Path.GetExtension(clip.Path).Equals(".gif", StringComparison.OrdinalIgnoreCase);
        if (sourceAnimated) Args("-stream_loop", "-1");
        else Args("-loop", "1", "-framerate", "25");
        if (sourceAnimated && clip.SourceOffsetMs > 0)
            Args("-ss", (clip.SourceOffsetMs / 1000d).ToString("0.###", CultureInfo.InvariantCulture));
        Args("-i", clip.Path);
        // The scene compositor uses 1280x720 coordinates. A 1080p export scales
        // the visual bounds and offsets together so the framing stays the same.
        var factor = height / 720d;
        var maxWidth = (int)Math.Round(clip.MaxWidth * factor);
        var maxHeight = (int)Math.Round(clip.MaxHeight * factor);
        var background = clip.Kind == nameof(ScriptBlockKind.Background);
        var fixedVideo = clip.Kind == nameof(ScriptBlockKind.Video) && clip.VideoLayer == "fondo";
        var moving = clip.MotionOffsetX != 0 || clip.MotionOffsetY != 0 || clip.MotionRotationDegrees != 0;
        var offsetX = (int)Math.Round(clip.OffsetX * factor);
        var offsetY = (int)Math.Round(clip.OffsetY * factor);
        var moveX = (int)Math.Round(clip.MotionOffsetX * factor);
        var moveY = (int)Math.Round(clip.MotionOffsetY * factor);
        // A background larger than the frame (zoom, 1.4.0) keeps the preview's box (LayerGeometry.FillExtent): it only
        // grows if its travel needs it, so the frame VEGAS shows stays covered and the scale matches the preview.
        // A spinning background keeps the older rule: its canvas term carries the rotation margin.
        var zoomedBox = background && clip.MotionRotationDegrees == 0;
        var backgroundWidth = zoomedBox && maxWidth > width
            ? (int)Math.Round(moving ? LayerGeometry.FillExtent(maxWidth, width, offsetX, moveX, 8 * factor, true) : maxWidth)
            : Math.Max(canvas.Width + Math.Abs(offsetX) * 2,
                (fixedVideo ? width : maxWidth) +
                (moving ? (Math.Abs(background ? offsetX : 0) + Math.Abs(moveX) + 8) * 2 : 0));
        var backgroundHeight = zoomedBox && maxHeight > height
            ? (int)Math.Round(moving ? LayerGeometry.FillExtent(maxHeight, height, offsetY, moveY, 8 * factor, true) : maxHeight)
            : Math.Max(canvas.Height + Math.Abs(offsetY) * 2,
                (fixedVideo ? height : maxHeight) +
                (moving ? (Math.Abs(background ? offsetY : 0) + Math.Abs(moveY) + 8) * 2 : 0));
        if (!moving && layout is { Fill: true })
        {
            // Static covering layer: the same box as the preview (LayerGeometry.FillBox), so a box
            // smaller than the frame is cut exactly like the preview and the offset moves that box.
            backgroundWidth = Math.Max(2, (int)Math.Round(layout.BoxWidth));
            backgroundHeight = Math.Max(2, (int)Math.Round(layout.BoxHeight));
        }
        var scale = fixedVideo ? $"scale={backgroundWidth}:{backgroundHeight}:force_original_aspect_ratio=increase,crop={backgroundWidth}:{backgroundHeight}"
            : background ? $"scale={backgroundWidth}:{backgroundHeight}:force_original_aspect_ratio=increase,crop={backgroundWidth}:{backgroundHeight}"
            : $"scale={maxWidth}:{maxHeight}:force_original_aspect_ratio=decrease";
        var flips = (clip.FlipHorizontal ? ",hflip" : "") + (clip.FlipVertical ? ",vflip" : "");
        var key = clip.GreenScreen ? $",colorkey=0x{clip.KeyColor}:{clip.KeyTolerance.ToString("0.###", CultureInfo.InvariantCulture)}:0.12" : "";
        var spin = clip.MotionRotationDegrees != 0;
        // Normalize only the initial composition. Animated translation/rotation
        // belong to the VEGAS event, so the source remains constant in time.
        var rotation = background || fixedVideo
            ? SceneComposer.RotationFillFilter(clip.RotationDegrees, backgroundWidth, backgroundHeight)
            : spin ? StaticRotationOnMotionCanvas(clip.RotationDegrees, maxWidth, maxHeight)
                : SceneComposer.RotationFilter(clip.RotationDegrees);
        var margin = (int)Math.Round(80 * factor);
        var bottom = clip.Kind == nameof(ScriptBlockKind.Image) ? (int)Math.Round(30 * factor) : 0;
        var x = background ? $"(W-w)/2+({offsetX})" : fixedVideo ? "(W-w)/2" :
            $"({CharacterFraming.XPosition(clip.Position, margin)})+({offsetX})";
        var y = background ? $"(H-h)/2+({offsetY})" : fixedVideo ? "(H-h)/2" :
            $"H-h-{bottom}+({offsetY})";
        if (!background && !fixedVideo && layout is { Fill: false })
        {
            // Same size and center as the preview (LayerGeometry); rotation turns around the center.
            var layerWidth = (int)layout.Width;
            var layerHeight = (int)layout.Height;
            scale = $"scale={layerWidth}:{layerHeight}";
            rotation = spin ? StaticRotationOnMotionCanvas(clip.RotationDegrees, layerWidth, layerHeight)
                : SceneComposer.RotationFilter(clip.RotationDegrees);
            x = $"{LayerGeometry.Number(pivot.X)}-w/2";
            y = $"{LayerGeometry.Number(pivot.Y)}-h/2";
        }
        else if (spin && !background && !fixedVideo)
        {
            var side = (int)Math.Ceiling(Math.Sqrt((double)maxWidth * maxWidth +
                (double)maxHeight * maxHeight) / 2) * 2;
            var correction = (side - maxWidth) / 2d;
            if (clip.Position == "izquierda") x += "-" + correction.ToString("0.###", CultureInfo.InvariantCulture);
            if (clip.Position == "derecha") x += "+" + correction.ToString("0.###", CultureInfo.InvariantCulture);
            y += "+" + ((side - maxHeight) / 2d).ToString("0.###", CultureInfo.InvariantCulture);
        }
        var filter = $"[0:v]format=rgba,colorchannelmixer=aa=0[canvas];" +
            $"[1:v]{clip.VisualCrop}{scale},setsar=1,format=rgba{flips}{key}{rotation}[layer];" +
            $"[canvas][layer]overlay=x='{x}':y='{y}':eval=frame:format=auto:shortest=1,format=rgba[out]";
        Args("-filter_complex", filter, "-map", "[out]");
        if (animated)
            Args("-r", "25", "-t", (clip.DurationMs / 1000d).ToString("0.###", CultureInfo.InvariantCulture),
                "-an", "-c:v", "prores_ks", "-profile:v", "4", "-pix_fmt", "yuva444p10le", "-alpha_bits", "16", temp);
        else Args("-frames:v", "1", "-c:v", "png", "-pix_fmt", "rgba", temp);
        try
        {
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo iniciar FFmpeg.");
            using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
            var error = process.StandardError.ReadToEndAsync(token);
            var outputTask = process.StandardOutput.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);
            if (process.ExitCode != 0) throw new InvalidOperationException($"No se pudo encuadrar {Path.GetFileName(clip.Path)} para VEGAS: {await error}");
            await outputTask;
            File.Move(temp, output, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return pivot;
    }

    private static string StaticRotationOnMotionCanvas(double degrees, int width, int height)
    {
        // Keep the same square canvas and placement used by the animated preview.
        var side = (int)Math.Ceiling(Math.Sqrt((double)width * width + (double)height * height) / 2) * 2;
        var angle = degrees.ToString("0.###", CultureInfo.InvariantCulture) + "*PI/180";
        return $",pad={width}:{height}:(ow-iw)/2:(oh-ih)/2:color=black@0," +
            $"pad={side}:{side}:(ow-iw)/2:(oh-ih)/2:color=black@0," +
            $"rotate=angle='{angle}':ow=iw:oh=ih:c=none";
    }

    private static string SafeName(string name)
    {
        var chars = name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray();
        var value = new string(chars).Trim().Trim('.');
        return string.IsNullOrWhiteSpace(value) ? "Escena_Loquendo" : value;
    }

    private static string Literal(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"")
        .Replace("\r", "\\r").Replace("\n", "\\n") + "\"";

    private static string Number(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);

    /// <summary>Starts keyframe <paramref name="index"/> of the event (key0 always exists).</summary>
    private static string Keyframe(StringBuilder sb, int index, long atMs)
    {
        var name = "key" + index.ToString(CultureInfo.InvariantCulture);
        if (index == 0) sb.AppendLine("    VideoMotionKeyframe key0 = ev.VideoMotion.Keyframes[0];");
        else
        {
            sb.AppendLine("    VideoMotionKeyframe " + name + " = new VideoMotionKeyframe(Timecode.FromMilliseconds(" + atMs + "));");
            // VEGAS rejects Bounds/Center setters until the keyframe belongs to the event.
            sb.AppendLine("    ev.VideoMotion.Keyframes.Add(" + name + ");");
        }
        return name;
    }

    private static string Bounds(double left, double top, double right, double bottom) =>
        "new VideoMotionBounds(" + Number(left) + "f, " + Number(top) + "f, " + Number(right) + "f, " + Number(top) + "f, " +
        Number(right) + "f, " + Number(bottom) + "f, " + Number(left) + "f, " + Number(bottom) + "f)";

    /// <summary>Normalized media (the layer already placed on a project-sized canvas): each keyframe shows
    /// the camera window of that canvas and applies the layer's own motion.</summary>
    private static void AppendEditableMotion(StringBuilder sb, Clip clip, int width, int height)
    {
        var moves = clip.MotionOffsetX != 0 || clip.MotionOffsetY != 0 || Math.Abs(clip.MotionRotationDegrees) >= 0.0001;
        if (!moves && clip.CameraKeys is null) return;
        var factor = height / 720d;
        var canvas = NativeCanvas(clip, width, height);
        var times = KeyTimes(clip);
        for (var i = 0; i < times.Count; i++)
        {
            var key = Keyframe(sb, i, times[i]);
            var window = CameraAt(clip, times[i]);
            var left = canvas.Left + window.X * factor;
            var top = canvas.Top + window.Y * factor;
            sb.AppendLine("    " + key + ".Bounds = " + Bounds(left, top, left + window.Width * factor, top + window.Height * factor) + ";");
            sb.AppendLine("    " + key + ".Center = new VideoMotionVertex(" + Number(clip.MediaPivotX ?? canvas.Width / 2d) + "f, " +
                Number(clip.MediaPivotY ?? canvas.Height / 2d) + "f);");
            sb.AppendLine("    " + key + ".Type = VideoKeyframeType.Linear;");
            if (moves) AppendMotionTransform(sb, key, clip, factor, MotionProgress(clip, times[i]));
        }
    }

    /// <summary>Original media with native Pan/Crop: one keyframe per <see cref="NativeKey"/> (camera and
    /// motion breakpoints), each with its own source rectangle.</summary>
    private static void AppendNativeMotion(StringBuilder sb, Clip clip)
    {
        var keys = clip.NativeKeys ?? [new NativeKey(0, clip.Native ?? throw new InvalidOperationException("Falta el encuadre nativo."))];
        sb.AppendLine("    ev.VideoMotion.ScaleToFill = true;");
        for (var i = 0; i < keys.Count; i++)
        {
            var (at, frame) = keys[i];
            var key = Keyframe(sb, i, at);
            // Source-space Pan/Crop vertices. Their rectangle has the output aspect and
            // maps the untouched original onto its requested position in the project.
            var left = clip.FlipHorizontal ? frame.Right : frame.Left;
            var right = clip.FlipHorizontal ? frame.Left : frame.Right;
            var top = clip.FlipVertical ? frame.Bottom : frame.Top;
            var bottom = clip.FlipVertical ? frame.Top : frame.Bottom;
            sb.AppendLine("    " + key + ".Bounds = " + Bounds(left, top, right, bottom) + ";");
            sb.AppendLine("    " + key + ".Center = new VideoMotionVertex(" + Number(frame.RotationCenterX) + "f, " +
                Number(frame.RotationCenterY) + "f);");
            sb.AppendLine("    " + key + ".Type = VideoKeyframeType.Linear;");
            AppendNativeTransform(sb, key, clip, frame, MotionProgress(clip, at));
        }
    }

    /// <summary>
    /// Pan/Crop of a clip with gestures at <paramref name="atMs"/> into the event: the source quadrilateral
    /// the frame shows (top-left, top-right, bottom-right, bottom-left) and the pivot: the render's feet (or
    /// waist or centre, as asked) in source pixels, like the Residents96 setup, so VEGAS also turns around the
    /// feet between keyframes and when the rotation is edited by hand. Placement, rotation and motion as in the preview, then the gesture around the feet
    /// (turn, then stretch along the screen vertical), then the camera; the quad is that map's inverse image
    /// of the frame corners.
    /// </summary>
    internal static ((double X, double Y)[] Quad, (double X, double Y) Center) GestureQuad(Clip clip, int width, int height, long atMs)
    {
        var layout = clip.Layout ?? throw new InvalidOperationException("Falta la colocación de la capa.");
        var factor = height / 720d;
        var progress = MotionProgress(clip, atMs);
        Affine toScreen;
        (double X, double Y) center;
        double rotation;
        if (clip.Native is not null)
        {
            toScreen = Affine.Scale(layout.Scale, layout.Scale)
                .Then(Affine.Translate(layout.X - layout.SourceX * layout.Scale, layout.Y - layout.SourceY * layout.Scale));
            center = (layout.CenterX, layout.CenterY);
            rotation = clip.RotationDegrees + clip.MotionRotationDegrees * progress;
        }
        else
        {
            // Normalized media: the layer is already placed (and statically rotated) on the canvas.
            var canvas = NativeCanvas(clip, width, height);
            toScreen = Affine.Translate(-canvas.Left, -canvas.Top);
            center = ((clip.MediaPivotX ?? canvas.Width / 2d) - canvas.Left, (clip.MediaPivotY ?? canvas.Height / 2d) - canvas.Top);
            rotation = clip.MotionRotationDegrees * progress;
        }
        var dx = clip.MotionOffsetX * factor * progress;
        var dy = clip.MotionOffsetY * factor * progress;
        var pose = new GestureTrack("", clip.GestureKeys ?? []).At(atMs);
        var feetX = center.X + dx;
        var feetY = center.Y + dy + clip.GesturePivot * layout.Height / 2;
        var camera = CameraAt(clip, atMs);
        var map = toScreen
            .Then(Affine.About(center.X, center.Y, Affine.Rotate(rotation)))
            .Then(Affine.Translate(dx, dy))
            .Then(Affine.About(feetX, feetY, Affine.Rotate(pose.Degrees).Then(Affine.Scale(1, pose.Stretch))))
            .Then(Affine.Translate(-camera.X * factor, -camera.Y * factor))
            .Then(Affine.Scale(1280 / camera.Width, 1280 / camera.Width));
        var inverse = map.Inverse();
        var pivot = toScreen.Inverse().Apply(center.X, center.Y + clip.GesturePivot * layout.Height / 2);
        return ([inverse.Apply(0, 0), inverse.Apply(width, 0), inverse.Apply(width, height), inverse.Apply(0, height)], pivot);
    }

    /// <summary>
    /// Clip with gestures: the whole Pan/Crop of each keyframe computed by <see cref="GestureQuad"/>, written as
    /// an upright rectangle turned with RotateBy (as VEGAS shows it), with the pivot at the feet. While a gesture
    /// moves there is one linear keyframe per frame (40 ms) following the preview curve exactly: VEGAS's own
    /// «Rápido» curve and its interpolation of the box between distant keyframes do not match it (tested in
    /// VEGAS: stiffer and cut short). The stretch deforms the box, so the event must not keep its aspect ratio
    /// (with «Mantener relación de aspecto» on, VEGAS fits the box instead: the render moves away and back).
    /// </summary>
    private static void AppendGestureMotion(StringBuilder sb, Clip clip, int width, int height)
    {
        var track = new GestureTrack("", clip.GestureKeys ?? []);
        sb.AppendLine("    ev.VideoMotion.ScaleToFill = true;");
        if (track.Stretches) sb.AppendLine("    ev.MaintainAspectRatio = false;");
        var times = new SortedSet<long>(KeyTimes(clip));
        for (var k = 0; k + 1 < track.Keys.Count; k++)
        {
            var (a, b) = (track.Keys[k], track.Keys[k + 1]);
            if (a.Pose == b.Pose) continue;
            for (var t = a.AtMs + GesturePlan.FrameMs; t < b.AtMs; t += GesturePlan.FrameMs)
                if (t > 0 && t < clip.DurationMs) times.Add(t);
        }
        var index = 0;
        foreach (var at in times)
        {
            var key = Keyframe(sb, index++, at);
            var (quad, center) = GestureQuad(clip, width, height, at);
            var turn = Math.Atan2(quad[1].Y - quad[0].Y, quad[1].X - quad[0].X);
            var upright = Affine.About(center.X, center.Y, Affine.Rotate(-turn * 180 / Math.PI));
            var corners = quad.Select(x => upright.Apply(x.X, x.Y)).ToArray();
            double left = corners.Min(x => x.X), right = corners.Max(x => x.X);
            double top = corners.Min(x => x.Y), bottom = corners.Max(x => x.Y);
            // Native media: the flips are the swapped edges, as in AppendNativeMotion (normalized media has them baked in).
            var native = clip.Native is not null;
            if (native && clip.FlipHorizontal) (left, right) = (right, left);
            if (native && clip.FlipVertical) (top, bottom) = (bottom, top);
            sb.AppendLine("    " + key + ".Bounds = " + Bounds(left, top, right, bottom) + ";");
            sb.AppendLine("    " + key + ".Center = new VideoMotionVertex(" + Number(center.X) + "f, " + Number(center.Y) + "f);");
            sb.AppendLine("    " + key + ".Type = VideoKeyframeType.Linear;");
            if (Math.Abs(turn) > 0.000001) sb.AppendLine("    " + key + ".RotateBy(" + Number(turn) + ");");
        }
    }

    private static void AppendNativeTransform(StringBuilder sb, string key, Clip clip,
        NativeFrame frame, double progress)
    {
        var degrees = clip.RotationDegrees + clip.MotionRotationDegrees * progress;
        if (Math.Abs(degrees) > 0.0001)
            sb.AppendLine("    " + key + ".RotateBy(" + Number(-degrees * Math.PI / 180) + ");");
        var dx = clip.MotionOffsetX * frame.MotionUnitsX * progress;
        var dy = clip.MotionOffsetY * frame.MotionUnitsY * progress;
        if (Math.Abs(dx) > 0.0001 || Math.Abs(dy) > 0.0001)
            sb.AppendLine("    " + key + ".MoveBy(new VideoMotionVertex(" + Number(-dx) +
                "f, " + Number(-dy) + "f));");
    }

    private static void AppendMotionTransform(StringBuilder sb, string key, Clip clip,
        double factor, double progress)
    {
        if (Math.Abs(clip.MotionRotationDegrees * progress) > 0.0001)
            sb.AppendLine("    " + key + ".RotateBy(" + Number(-clip.MotionRotationDegrees * progress * Math.PI / 180) + ");");
        var dx = clip.MotionOffsetX * factor * progress;
        var dy = clip.MotionOffsetY * factor * progress;
        if (Math.Abs(dx) > 0.0001 || Math.Abs(dy) > 0.0001)
            sb.AppendLine("    " + key + ".MoveBy(new VideoMotionVertex(" + Number(-dx) + "f, " + Number(-dy) + "f));");
    }

    /// <summary>
    /// Script helpers for the cinema bars. VEGAS «Cortador de galletas» (Cookie Cutter) is found by name or
    /// ID in any language, and its parameters by name or label: rectangle, «cortar todo excepto la sección»,
    /// black, no feather, the style's border and the size (keyframed while the bars move). Classic C#
    /// syntax: VEGAS compiles the script with an old compiler. Problems are listed at the end, not fatal.
    /// </summary>
    private static void AppendLetterboxHelpers(StringBuilder sb)
    {
        sb.AppendLine("""
  static PlugInNode cookieCutter; static bool cookieCutterSearched;
  static PlugInNode gaussian; static bool gaussianSearched;
  // Always VEGAS's own OFX effect: its ID is {Svfx:com.vegascreativesoftware:…} (sonycreativesoftware in VEGAS 12/13).
  // Third-party packs (BCC, Sapphire, NewBlue…) have effects with similar names but other parameters («BCC Gaussian
  // Blur» took the place of «Desenfoque gaussiano» and did nothing), so they are never taken; if an installation
  // gives VEGAS's effect another ID, only its exact built-in name is accepted. Otherwise the export says so.
  static PlugInNode FindVegasPlugIn(PlugInNode node, string[] idWords, string[] exactNames) {
    PlugInNode found = FindPlugIn(node, idWords, exactNames, true);
    return found != null ? found : FindPlugIn(node, idWords, exactNames, false);
  }
  static PlugInNode FindPlugIn(PlugInNode node, string[] idWords, string[] exactNames, bool byId) {
    foreach (PlugInNode child in node) {
      if (child.IsContainer) { PlugInNode found = FindPlugIn(child, idWords, exactNames, byId); if (found != null) return found; continue; }
      string name = (child.Name == null ? "" : child.Name).Trim().ToLowerInvariant();
      string id = (child.UniqueID == null ? "" : child.UniqueID).ToLowerInvariant();
      if (byId) {
        if (!id.Contains("vegascreativesoftware") && !id.Contains("sonycreativesoftware")) continue;
        foreach (string w in idWords) if (id.Contains(w)) return child;
      }
      else foreach (string n in exactNames) if (name == n) return child;
    }
    return null;
  }
  // Horizontal and vertical range with the same values, animated per frame while they change (0 = sharp).
  static string AddBlur(Vegas vegas, VideoEvent ev, double[] times, double[] values) {
    if (!gaussianSearched) { gaussianSearched = true; gaussian = FindVegasPlugIn(vegas.VideoFX, new string[] { "gaussianblur", "gaussian" }, new string[] { "desenfoque gaussiano", "gaussian blur", "vegas desenfoque gaussiano", "vegas gaussian blur" }); }
    if (gaussian == null) return "Desenfoque gaussiano no encontrado: desenfoque sin aplicar";
    try {
      Effect fx = new Effect(gaussian);
      ev.Effects.Add(fx);
      OFXEffect ofx = fx.OFXEffect;
      if (ofx == null) return "Desenfoque gaussiano sin parámetros OFX";
      OFXDoubleParameter horizontal = FindParameter(ofx, new string[] { "horizontal", "horiz" }, typeof(OFXDoubleParameter)) as OFXDoubleParameter;
      OFXDoubleParameter vertical = FindParameter(ofx, new string[] { "vertical", "vert" }, typeof(OFXDoubleParameter)) as OFXDoubleParameter;
      if (horizontal == null || vertical == null) return "Desenfoque gaussiano: no se encontraron los rangos horizontal y vertical";
      SetCurve(horizontal, times, values);
      SetCurve(vertical, times, values);
      return null;
    } catch (Exception ex) { return "Desenfoque gaussiano: " + ex.Message; }
  }
  static void SetCurve(OFXDoubleParameter parameter, double[] times, double[] values) {
    bool constant = true;
    for (int i = 1; i < values.Length; i++) if (Math.Abs(values[i] - values[0]) > 0.0000001) constant = false;
    if (constant) { parameter.Value = values[0]; return; }
    parameter.IsAnimated = true;
    for (int i = 0; i < times.Length; i++) parameter.SetValueAtTime(Timecode.FromMilliseconds(times[i]), values[i]);
  }
  // Exact name/label first, then "contains"; only parameters of the wanted kind (e.g. «Borde» is a number,
  // a «Color de borde» must not be taken for it).
  static OFXParameter FindParameter(OFXEffect ofx, string[] words, Type kind) {
    for (int pass = 0; pass < 2; pass++) {
      foreach (OFXParameter p in ofx.Parameters) {
        if (!kind.IsInstanceOfType(p)) continue;
        string name = (p.Name == null ? "" : p.Name).Trim().ToLowerInvariant();
        string label = (p.Label == null ? "" : p.Label).Trim().ToLowerInvariant();
        foreach (string w in words)
          if (pass == 0 ? (name == w || label == w) : (name.Contains(w) || label.Contains(w))) return p;
      }
    }
    return null;
  }
  static void SetChoice(OFXEffect ofx, string[] parameter, string[] choice) {
    OFXChoiceParameter c = FindParameter(ofx, parameter, typeof(OFXChoiceParameter)) as OFXChoiceParameter; if (c == null) return;
    foreach (OFXChoice o in c.Choices) {
      string n = (o.Name == null ? "" : o.Name).ToLowerInvariant();
      foreach (string w in choice) if (n.Contains(w)) { c.Value = o; return; }
    }
  }
  // Black border. The colour parameter is RGB or RGBA depending on the VEGAS version, so its value is set by
  // reflection with whatever colour type it uses (0, 0, 0 and alpha 1).
  static bool SetBlack(OFXEffect ofx) {
    foreach (OFXParameter p in ofx.Parameters) {
      string text = ((p.Name == null ? "" : p.Name) + "|" + (p.Label == null ? "" : p.Label)).ToLowerInvariant();
      if (!text.Contains("color") && !text.Contains("colour")) continue;
      try {
        System.Reflection.PropertyInfo value = p.GetType().GetProperty("Value");
        if (value == null || !value.CanWrite) continue;
        object black = MakeBlack(value.PropertyType);
        if (black == null) continue;
        System.Reflection.PropertyInfo animated = p.GetType().GetProperty("IsAnimated");
        if (animated != null && animated.CanWrite) animated.SetValue(p, false, null);
        value.SetValue(p, black, null);
        return true;
      } catch (Exception) { }
    }
    return false;
  }
  static object MakeBlack(Type type) {
    object[][] tries = new object[][] {
      new object[] { 0.0, 0.0, 0.0, 1.0 }, new object[] { 0.0f, 0.0f, 0.0f, 1.0f },
      new object[] { 0.0, 0.0, 0.0 }, new object[] { 0.0f, 0.0f, 0.0f } };
    foreach (object[] args in tries) {
      try { return Activator.CreateInstance(type, args); } catch (Exception) { }
    }
    // No such constructor: a colour with R, G, B (and A) fields or properties.
    try {
      object color = Activator.CreateInstance(type);
      string[] names = new string[] { "R", "G", "B", "A" };
      for (int i = 0; i < names.Length; i++) {
        double v = i == 3 ? 1.0 : 0.0;
        System.Reflection.FieldInfo f = type.GetField(names[i]);
        if (f != null) { f.SetValue(color, Convert.ChangeType(v, f.FieldType)); continue; }
        System.Reflection.PropertyInfo pr = type.GetProperty(names[i]);
        if (pr != null && pr.CanWrite) pr.SetValue(color, Convert.ChangeType(v, pr.PropertyType), null);
      }
      return color;
    } catch (Exception) { }
    return null;
  }
  // The preset the letterbox is made from: «Cuadrado, centro, bordes blancos» (English: square, center, white border).
  static void ApplyBasePreset(Effect fx) {
    try {
      foreach (EffectPreset preset in cookieCutter.Presets) {
        string n = (preset.Name == null ? "" : preset.Name).ToLowerInvariant();
        if ((n.Contains("cuadrado") && n.Contains("centro")) || (n.Contains("square") && n.Contains("cent"))) { fx.Preset = preset.Name; return; }
      }
    } catch (Exception) { }
  }
  static string AddLetterbox(Vegas vegas, VideoEvent ev, double[] times, double[] borders, double[] sizes) {
    if (!cookieCutterSearched) { cookieCutterSearched = true; cookieCutter = FindVegasPlugIn(vegas.VideoFX, new string[] { "cookiecutter", "cookie" }, new string[] { "cortador de galletas", "cookie cutter", "vegas cortador de galletas", "vegas cookie cutter" }); }
    if (cookieCutter == null) return "Cortador de galletas no encontrado: barras de cine sin aplicar";
    try {
      Effect fx = new Effect(cookieCutter);
      ev.Effects.Add(fx);
      ApplyBasePreset(fx);
      OFXEffect ofx = fx.OFXEffect;
      if (ofx == null) return "Cortador de galletas sin parámetros OFX";
      string problem = null;
      try { SetChoice(ofx, new string[] { "shape", "forma" }, new string[] { "rect" }); } catch (Exception) { problem = "Cortador de galletas: revisa la forma (rectángulo)"; }
      try { SetChoice(ofx, new string[] { "method", "método", "metodo" }, new string[] { "except", "excepto" }); } catch (Exception) { problem = "Cortador de galletas: revisa el método"; }
      if (!SetBlack(ofx)) problem = "Cortador de galletas: no se pudo poner el color en negro (cámbialo en el efecto)";
      try {
        OFXDoubleParameter feather = FindParameter(ofx, new string[] { "feather", "pluma", "difumin" }, typeof(OFXDoubleParameter)) as OFXDoubleParameter;
        if (feather != null) feather.Value = 0;
      } catch (Exception) { }
      OFXDoubleParameter edge = FindParameter(ofx, new string[] { "border", "borde" }, typeof(OFXDoubleParameter)) as OFXDoubleParameter;
      if (edge == null) return "Cortador de galletas: no se encontró «Borde»";
      SetCurve(edge, times, borders);
      OFXDoubleParameter size = FindParameter(ofx, new string[] { "size", "tamaño", "tamano" }, typeof(OFXDoubleParameter)) as OFXDoubleParameter;
      if (size == null) return "Cortador de galletas: no se encontró «Tamaño»";
      SetCurve(size, times, sizes);
      return problem;
    } catch (Exception ex) { return "Cortador de galletas: " + ex.Message; }
  }
""");
    }

    internal sealed record AudioTrackPlan(string Name, int VolumePercent, IReadOnlyList<Clip> Clips);

    /// <summary>Groups audio clips into tracks. Track volume carries the clip volume, so a shared
    /// track only holds clips with the same volume (no per-event gain is needed).</summary>
    internal static IReadOnlyList<AudioTrackPlan> AudioTracks(IReadOnlyList<Clip> audio, VegasAudioTrackMode mode,
        IReadOnlyDictionary<Guid, string> characterNames)
    {
        var kindOrder = new[] { nameof(ScriptBlockKind.Music), nameof(ScriptBlockKind.SoundEffect),
            nameof(ScriptBlockKind.Video), nameof(ScriptBlockKind.Narration), nameof(ScriptBlockKind.Dialogue) };
        if (mode == VegasAudioTrackMode.Legacy)
            return kindOrder.SelectMany(kind => audio.Where(x => x.Kind == kind))
                .Select(clip => new AudioTrackPlan(clip.Kind + " | " + Path.GetFileName(clip.Path), clip.VolumePercent, [clip]))
                .ToArray();

        static bool IsVoice(Clip clip) => clip.Kind is nameof(ScriptBlockKind.Dialogue) or nameof(ScriptBlockKind.Narration);
        string Speaker(Clip clip) => clip.CharacterId is Guid id && characterNames.TryGetValue(id, out var name) ? name
            : clip.CharacterId is null ? "Narrador" : "Personaje " + clip.CharacterId.Value.ToString("N")[..6];
        string GroupName(Clip clip) => IsVoice(clip)
            ? "Voz · " + Speaker(clip) + (clip.VolumePercent == 100 ? "" : $" · {clip.VolumePercent}%")
            : clip.Kind switch
            {
                nameof(ScriptBlockKind.Music) => "Música",
                nameof(ScriptBlockKind.SoundEffect) => "SFX",
                _ => "Audio de vídeo"
            } + $" · {clip.VolumePercent}%";
        // Voices first (by first line), then music, SFX and video audio.
        var groups = audio.OrderBy(x => x.StartMs).GroupBy(GroupName)
            .OrderBy(group => IsVoice(group.First()) ? 0 : 1)
            .ThenBy(group => IsVoice(group.First()) ? group.First().StartMs : Array.IndexOf(kindOrder, group.First().Kind))
            .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase);
        var tracks = new List<AudioTrackPlan>();
        foreach (var group in groups)
        {
            // Interval packing: reuse the first lane that is free when the clip starts.
            var lanes = new List<(long End, List<Clip> Clips)>();
            foreach (var clip in group)
            {
                var lane = lanes.FindIndex(x => x.End <= clip.StartMs);
                if (lane < 0) { lanes.Add((clip.StartMs + clip.DurationMs, [clip])); continue; }
                lanes[lane].Clips.Add(clip);
                lanes[lane] = (clip.StartMs + clip.DurationMs, lanes[lane].Clips);
            }
            for (var i = 0; i < lanes.Count; i++)
                tracks.Add(new AudioTrackPlan(group.Key + (i == 0 ? "" : $" ({i + 1})"), group.First().VolumePercent, lanes[i].Clips));
        }
        return tracks;
    }

    private static string CreateScript(IReadOnlyList<Clip> clips, string name, bool legacy,
        int width, int height, IReadOnlyList<SceneTransition> transitions, string blackPath,
        VegasAudioTrackMode audioMode, IReadOnlyDictionary<Guid, string> characterNames,
        IReadOnlyList<(long StartMs, long DurationMs, string Name)> regions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// Generado por Loquendo AI. Abrir sobre un proyecto VACÍO en VEGAS.");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.IO;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Windows.Forms;");
        sb.AppendLine(legacy ? "using Sony.Vegas;" : "using ScriptPortal.Vegas;");
        sb.AppendLine("public class EntryPoint { public void FromVegas(Vegas vegas) {");
        sb.AppendLine("  if (vegas.Project.Tracks.Count != 0) { MessageBox.Show(\"Abre un proyecto vacío antes de importar la escena.\"); return; }");
        sb.AppendLine("  string destination = Path.Combine(Path.GetDirectoryName(" +
            (legacy ? "Sony.Vegas.Script.File" : "ScriptPortal.Vegas.Script.File") + "), " + Literal(name + ".veg") + ");");
        sb.AppendLine("  if (File.Exists(destination)) { MessageBox.Show(\"Ya existe el proyecto VEGAS. Muévelo o usa otra exportación para conservarlo.\"); return; }");
        sb.AppendLine($"  vegas.Project.Video.Width = {width}; vegas.Project.Video.Height = {height};");
        sb.AppendLine("  vegas.Project.Video.FrameRate = 25.0; vegas.Project.Video.PixelAspectRatio = 1.0;");
        sb.AppendLine("  List<string> unavailableTransitions = new List<string>();");
        sb.AppendLine("  Dictionary<string, VideoTrack> videoTracks = new Dictionary<string, VideoTrack>();");
        // AddVideoTrack inserts at the top: create from background up to foreground.
        foreach (var clip in clips.Where(x => x.Layer >= 0).OrderBy(x => x.Layer))
        {
            var label = clip.Kind + " | " + Path.GetFileName(clip.OriginalPath ?? clip.Path);
            sb.AppendLine("  {");
            sb.AppendLine("    Media media = new Media(" + Literal(clip.ImportPath ?? clip.OriginalPath ?? clip.Path) + ");");
            sb.AppendLine("    VideoTrack track;");
            sb.AppendLine("    if (!videoTracks.TryGetValue(" + Literal(clip.TrackKey) + ", out track)) {");
            sb.AppendLine("      track = vegas.Project.AddVideoTrack(); track.Name = " + Literal(label) + ";");
            sb.AppendLine("      videoTracks.Add(" + Literal(clip.TrackKey) + ", track);");
            sb.AppendLine("    }");
            sb.AppendLine("    VideoEvent ev = track.AddVideoEvent(Timecode.FromMilliseconds(" + clip.StartMs + "), Timecode.FromMilliseconds(" + clip.DurationMs + "));");
            if (clip.ImportPath is not null)
            {
                sb.AppendLine("    media.GetVideoStreamByIndex(0).AlphaChannel = VideoAlphaType.Straight;");
                sb.AppendLine("    Take clean = ev.AddTake(media.GetVideoStreamByIndex(0));");
                sb.AppendLine("    Media original = new Media(" + Literal(clip.OriginalPath ?? clip.Path) + ");");
                if (clip.SourceHasAlpha) sb.AppendLine("    original.GetVideoStreamByIndex(0).AlphaChannel = VideoAlphaType.Straight;");
                sb.AppendLine("    ev.AddTake(original.GetVideoStreamByIndex(0)); ev.ActiveTake = clean;");
            }
            else
            {
                sb.AppendLine("    Take sourceTake = ev.AddTake(media.GetVideoStreamByIndex(0));");
                if (clip.SourceOffsetMs > 0 && (clip.Kind == nameof(ScriptBlockKind.Video) ||
                    Path.GetExtension(clip.Path).Equals(".gif", StringComparison.OrdinalIgnoreCase)))
                    sb.AppendLine("    sourceTake.Offset = Timecode.FromMilliseconds(" + clip.SourceOffsetMs + ");");
            }
            sb.AppendLine("    ev.Name = " + Literal(label) + ";");
            sb.AppendLine("    ev.Loop = true;");
            if (clip.GestureKeys is { Count: > 0 } && clip.Layout is not null) AppendGestureMotion(sb, clip, width, height);
            else if (clip.Native is not null) AppendNativeMotion(sb, clip);
            else AppendEditableMotion(sb, clip, width, height);
            // Blur before the Cookie Cutter: the bars stay sharp.
            if (clip.BlurKeys is { Count: > 0 } blurKeys)
                sb.AppendLine("    { string problem = AddBlur(vegas, ev, new double[] { " +
                    string.Join(", ", blurKeys.Select(x => x.AtMs.ToString(CultureInfo.InvariantCulture))) + " }, new double[] { " +
                    string.Join(", ", blurKeys.Select(x => Number(Math.Round(x.Amount, 6)))) + " }); if (problem != null && " +
                    "!unavailableTransitions.Contains(problem)) unavailableTransitions.Add(problem); }");
            if (clip.Cinema is { } bars)
                sb.AppendLine("    { string problem = AddLetterbox(vegas, ev, new double[] { " +
                    string.Join(", ", bars.Keys.Select(x => x.AtMs.ToString(CultureInfo.InvariantCulture))) + " }, new double[] { " +
                    string.Join(", ", bars.Keys.Select(x => Number(Math.Round(x.Border, 6)))) + " }, new double[] { " +
                    string.Join(", ", bars.Keys.Select(x => Number(Math.Round(x.Size, 6)))) + " }); if (problem != null && " +
                    "!unavailableTransitions.Contains(problem)) unavailableTransitions.Add(problem); }");
            if (clip.FadeInMs > 0)
            {
                sb.AppendLine("    ev.FadeIn.Length = Timecode.FromMilliseconds(" +
                    Math.Min(clip.FadeInMs, clip.DurationMs) + ");");
                if (clip.VegasTransitionId.Length > 0)
                {
                    sb.AppendLine("    try {");
                    sb.AppendLine("      PlugInNode node = null;");
                    sb.AppendLine("      try { node = vegas.Transitions.FindChildByUniqueID(" +
                        Literal(clip.VegasTransitionId) + "); } catch (Exception) { }");
                    var pluginName = VegasTransitionCatalog.Find(clip.VegasTransitionId)?.Name ?? clip.VegasTransitionId;
                    sb.AppendLine("      if (node == null) node = vegas.Transitions.GetChildByName(" + Literal(pluginName) + ");");
                    sb.AppendLine("      if (node == null) node = vegas.Transitions.GetChildByName(" + Literal("VEGAS " + pluginName) + ");");
                    sb.AppendLine("      if (node == null) unavailableTransitions.Add(" + Literal(pluginName + " (no instalado)") + ");");
                    sb.AppendLine("      else { Effect fx = new Effect(node); ev.FadeIn.Transition = fx;");
                    // The host supplies its default preset; the localized inventory label
                    // '(Predeterminado)' is not guaranteed to be assignable via Effect.Preset.
                    if (clip.VegasTransitionPreset.Length > 0 && clip.VegasTransitionPreset != "(Predeterminado)")
                        sb.AppendLine("        fx.Preset = " + Literal(clip.VegasTransitionPreset) + ";");
                    sb.AppendLine("      }");
                    sb.AppendLine("    } catch (Exception ex) { unavailableTransitions.Add(" + Literal(pluginName + ": ") + " + ex.Message); }");
                }
            }
            sb.AppendLine("  }");
        }
        if (transitions.Any(x => x.Style != "cruce"))
        {
            sb.AppendLine("  {");
            sb.AppendLine("    Media black = new Media(" + Literal(blackPath) + ");");
            sb.AppendLine("    VideoTrack fades = vegas.Project.AddVideoTrack(); fades.Name = \"Fundidos de escena\";");
            foreach (var transition in transitions)
            {
                if (transition.Style == "cruce") continue;
                sb.AppendLine("    {");
                sb.AppendLine("      VideoEvent ev = fades.AddVideoEvent(Timecode.FromMilliseconds(" + transition.StartMs +
                    "), Timecode.FromMilliseconds(" + transition.DurationMs + "));");
                sb.AppendLine("      ev.AddTake(black.GetVideoStreamByIndex(0));");
                sb.AppendLine("      ev.Loop = true;");
                if (transition.Style == "cambio")
                {
                    sb.AppendLine("      ev.FadeIn.Length = Timecode.FromMilliseconds(" + transition.DurationMs / 2 + ");");
                    sb.AppendLine("      ev.FadeOut.Length = Timecode.FromMilliseconds(" +
                        (transition.DurationMs - transition.DurationMs / 2) + ");");
                }
                else sb.AppendLine("      ev.Fade" + (transition.Style == "salida" ? "In" : "Out") +
                    ".Length = Timecode.FromMilliseconds(" + transition.DurationMs + ");");
                sb.AppendLine("      ev.Name = " + Literal("Fundido " + transition.Style) + ";");
                sb.AppendLine("    }");
            }
            sb.AppendLine("  }");
        }
        foreach (var track in AudioTracks(clips.Where(x => x.Layer < 0).ToArray(), audioMode, characterNames))
        {
            // One block per track; events share it. Legacy produces one track per clip as before.
            sb.AppendLine("  {");
            sb.AppendLine("    AudioTrack track = vegas.Project.AddAudioTrack(); track.Name = " + Literal(track.Name) + ";");
            sb.AppendLine("    track.Volume = " + Number(track.VolumePercent / 100d) + "f;");
            foreach (var clip in track.Clips)
            {
                var label = clip.Kind + " | " + Path.GetFileName(clip.Path);
                sb.AppendLine("    {");
                sb.AppendLine("      Media media = new Media(" + Literal(clip.Path) + ");");
                sb.AppendLine("      AudioEvent ev = track.AddAudioEvent(Timecode.FromMilliseconds(" + clip.StartMs + "), Timecode.FromMilliseconds(" + clip.DurationMs + "));");
                sb.AppendLine("      ev.AddTake(media.GetAudioStreamByIndex(0)); ev.Name = " + Literal(label) + ";");
                if (clip.StretchAudio && clip.SourceDurationMs > 0)
                {
                    sb.AppendLine("      ev.PlaybackRate = " + Number(clip.SourceDurationMs / (double)clip.DurationMs) + ";");
                    sb.AppendLine("      ev.PitchLock = true;");
                }
                else sb.AppendLine("      ev.Loop = " + (clip.DurationMs > clip.SourceDurationMs ? "true" : "false") + ";");
                sb.AppendLine("    }");
            }
            sb.AppendLine("  }");
        }
        foreach (var region in regions)
            sb.AppendLine("  vegas.Project.Regions.Add(new Region(Timecode.FromMilliseconds(" + region.StartMs +
                "), Timecode.FromMilliseconds(" + Math.Max(1, region.DurationMs) + "), " + Literal(region.Name) + "));");
        sb.AppendLine("  vegas.Project.SaveProject(destination);");
        sb.AppendLine("  MessageBox.Show(\"Proyecto creado: \" + destination +" +
            " (unavailableTransitions.Count == 0 ? \"\" : \"\\nEfectos VEGAS no aplicados: \" +" +
            " String.Join(\"; \", unavailableTransitions.ToArray()) + \". Revisa los presets.\"));");
        sb.AppendLine("}");
        AppendLetterboxHelpers(sb);
        sb.AppendLine("}");
        return sb.ToString();
    }
}
