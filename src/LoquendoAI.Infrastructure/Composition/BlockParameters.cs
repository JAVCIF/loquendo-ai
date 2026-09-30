using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using LoquendoAI.Core.Models;

namespace LoquendoAI.Infrastructure.Composition;

/// <summary>
/// What a camera block asks for. Mode: "personaje" (the block's character), "habla" (whoever speaks,
/// line by line), "punto" (the frame centre moved by OffsetX/Y) or "general" (the whole frame).
/// Focus: "cara" (upper part of the render) or "cuerpo" (its centre).
/// </summary>
public sealed record CameraSettings(string Mode, double Zoom, long MoveMs, string Focus, int OffsetX, int OffsetY);

/// <summary>
/// What a cinema block asks for: show or remove the bars, which style ("cerrado": wide bars,
/// "abierto": thin bars), how long the bars take to move (0 = at once) and on which layers the VEGAS
/// Cookie Cutter goes: "todos" (every visual), "personajes" (every render) or "lista" (the renders of
/// <see cref="Characters"/>). A layer drawn above one with the effect gets the effect too (CinemaPlan.Resolve).
/// </summary>
public sealed record CinemaSettings(bool Show, string Style, long MoveMs, string Layers, IReadOnlyList<Guid> Characters);

/// <summary>
/// What a gesture block asks for (1.0.0-beta.7). Mode: "personaje" (the block's character, once), "habla"
/// (from here on, whoever starts a line does it) or "quitar" (ends «habla»). Steps: moves done one after
/// another; the moves inside one step happen at the same time ("balanceo", "rebote"). Angle in degrees,
/// Side "auto" (toward the centre, alternating), "derecha" or "izquierda"; Stretch in percent (negative
/// squashes); Speed multiplies the tempo; Pivot "pies", "cintura" or "centro".
/// </summary>
public sealed record GestureSettings(string Mode, IReadOnlyList<IReadOnlyList<string>> Steps, double Angle, string Side,
    double StretchPercent, double Speed, string Pivot)
{
    /// <summary>"balanceo+rebote, rebote" → [[balanceo, rebote], [rebote]]; unknown words are dropped.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> ParseSteps(string? value)
    {
        var steps = new List<IReadOnlyList<string>>();
        foreach (var step in (value ?? "").Split([',', '>', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var moves = step.Split(['+', '&', '/'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(Move).OfType<string>().Distinct().ToArray();
            if (moves.Length > 0) steps.Add(moves);
        }
        return steps;
    }

    /// <summary>Canonical move name, or null.</summary>
    public static string? Move(string word) => Normalize(word) switch
    {
        "BALANCEO" or "MOVIMIENTO" or "MENEO" or "GIRO" or "INCLINAR" or "BALANCEAR" => "balanceo",
        "REBOTE" or "ESTIRAR" or "ESTIRAMIENTO" or "VARIACION" or "BOTE" or "REBOTAR" => "rebote",
        "AMBOS" or "LOS DOS" => "ambos",
        _ => null
    };

    public static string StepsText(IReadOnlyList<IReadOnlyList<string>> steps) =>
        string.Join(", ", steps.Select(x => string.Join("+", x)));

    private static string Normalize(string value)
    {
        var decomposed = value.Trim().Normalize(System.Text.NormalizationForm.FormD);
        var text = new System.Text.StringBuilder();
        foreach (var c in decomposed)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                text.Append(char.ToUpperInvariant(c));
        return text.ToString();
    }
}

/// <summary>
/// What a blur block asks for (1.0.0-beta.8). Target: "fondo", "personaje" (the block's character), "personajes",
/// "imagenes", "videos" or "todos". Amount: the VEGAS «Desenfoque gaussiano» range (0,02 «Suavizar», 0,01
/// «Desenfoque ligero», 0 = sharp again). MoveMs: how long the change takes (0 = at once).
/// </summary>
public sealed record BlurSettings(string Target, double Amount, long MoveMs);

/// <summary>A voice recording kept while a take is regenerated with TTS («Restaurar grabación»).</summary>
public sealed record RecordedTakeReference(string? Path, string? Hash, long? DurationMs);

/// <summary>Default values shared by the editor, the Director, the preview and the VEGAS export.</summary>
public static class BlockDefaults
{
    public const int TransitionDurationMs = 500;
    public const string KeyColor = "00FF00";
    public const double KeyTolerance = 0.30;
    public const int CharacterMargin = 80;
    public const int ImageBottomMargin = 30;

    /// <summary>Maximum size of a visual in the 1280×720 composition.</summary>
    public static (int Width, int Height) VisualBox(ScriptBlockKind kind) => kind switch
    {
        ScriptBlockKind.Background => (1280, 720),
        ScriptBlockKind.CharacterShow => (1280, 610),
        ScriptBlockKind.Image => (500, 400),
        _ => (960, 700)
    };

    /// <summary>
    /// Largest size of a visual. A background can be larger than the frame (1.4.0, «zoom» up to 3×: 3840×2160), so it
    /// can pan or be offset without showing its edges and keep the same scale while still and while moving;
    /// everything else fits in the 1280×720 frame.
    /// </summary>
    public static (int Width, int Height) VisualMaxBox(ScriptBlockKind kind) =>
        kind == ScriptBlockKind.Background ? (3840, 2160) : (1280, 720);

    public const double BackgroundZoomMax = 3.0;

    /// <summary>Camera: zoom on a character or point, how long the move takes (0 = cut) and limits.</summary>
    public const double CameraZoom = 1.4;
    /// <summary>Zoom of «[CAMARA] punto» when none is given.</summary>
    public const double CameraPointZoom = 1.3;
    public const double CameraZoomMin = 1.0;
    public const double CameraZoomMax = 3.0;
    public const int CameraMoveMs = 300;
    public const int CameraMoveMaxMs = 5000;
    public const int CinemaMoveMs = 800;
    /// <summary>Gestures (Residents96 technique): tilt angle, stretch and tempo limits.</summary>
    public const double GestureAngle = 5;
    public const double GestureAngleMax = 20;
    public const double GestureStretch = 8;
    public const double GestureStretchMin = -30;
    public const double GestureStretchMax = 40;
    public const double GestureSpeedMin = 0.5;
    public const double GestureSpeedMax = 3;

    public static int VolumePercent(ScriptBlockKind kind) => kind switch
    {
        ScriptBlockKind.Music => 25,
        ScriptBlockKind.Video => 0,
        _ => 100
    };
}

/// <summary>
/// The typed form of <see cref="SceneScriptBlock.ParametersJson"/>. Every reader goes through
/// <see cref="Of"/> (parsed once per JSON string) and the validated values with their defaults
/// come from the methods below, so a default or a valid range is defined in exactly one place.
/// Writers build an instance and call <see cref="ToJson"/>; keys this version does not model
/// (for example "stt") are preserved as they were.
/// </summary>
public sealed record BlockParameters
{
    public static readonly BlockParameters Empty = new();

    // Placement and appearance of visuals.
    public string? Position { get; init; }
    public string? FramingPreset { get; init; }
    public Guid? FolderSourceId { get; init; }
    public string? FolderRelativePath { get; init; }
    public bool? IncludeSubfolders { get; init; }
    public int? VisualMaxWidth { get; init; }
    public int? VisualMaxHeight { get; init; }
    public int? VisualOffsetX { get; init; }
    public int? VisualOffsetY { get; init; }
    public bool? FlipHorizontal { get; init; }
    public bool? FlipVertical { get; init; }
    /// <summary>«Cambiar dirección» (1.4.3): the render looks the other way and stays where it is (the picture turns
    /// around its own centre). «Invertir horizontal» instead mirrors the whole layer across the frame: picture and place.</summary>
    public bool? ChangeDirection { get; init; }
    public double? RotationDegrees { get; init; }
    public int? MotionOffsetX { get; init; }
    public int? MotionOffsetY { get; init; }
    public double? MotionRotationDegrees { get; init; }
    public long? MotionDurationMs { get; init; }
    public long? VisualDurationMs { get; init; }
    public bool? AutoTrimBorders { get; init; }

    // Video.
    public string? VideoLayer { get; init; }
    public bool? GreenScreen { get; init; }
    /// <summary>"on"/"off" when chosen explicitly; absent = decide from the file name.</summary>
    public string? GreenScreenMode { get; init; }
    public string? KeyColor { get; init; }
    public double? KeyTolerance { get; init; }

    // Audio.
    public bool? WaitForEnd { get; init; }
    public long? AudioDurationMs { get; init; }
    public string? AudioDurationMode { get; init; }
    public int? VolumePercent { get; init; }

    // Transitions.
    public string? TransitionStyle { get; init; }
    public long? TransitionDurationMs { get; init; }
    public int? TransitionTargets { get; init; }
    public string? TransitionOverride { get; init; }
    public string? VegasEffect { get; init; }
    public string? VegasPluginId { get; init; }
    public string? VegasPluginPreset { get; init; }

    // Voices.
    public string? DirectVoiceProvider { get; init; }
    public string? DirectVoiceId { get; init; }
    public bool? TranscriptReviewed { get; init; }
    public string? TranscriptOrigin { get; init; }
    public RecordedTakeReference? RecordedTake { get; init; }

    // Cinema bars (1.0.0-beta.5).
    public string? CinemaMode { get; init; }
    public string? CinemaStyle { get; init; }
    public long? CinemaMoveMs { get; init; }
    public string? CinemaLayers { get; init; }
    public IReadOnlyList<Guid>? CinemaCharacters { get; init; }

    // Blur (1.0.0-beta.8).
    public string? BlurTarget { get; init; }
    public double? BlurAmount { get; init; }
    public long? BlurMoveMs { get; init; }

    // Gestures (1.0.0-beta.7).
    public string? GestureMode { get; init; }
    public string? GestureSteps { get; init; }
    public double? GestureAngle { get; init; }
    public string? GestureSide { get; init; }
    public double? GestureStretch { get; init; }
    public double? GestureSpeed { get; init; }
    public string? GesturePivot { get; init; }

    // Camera (1.0.0-beta.5).
    public string? CameraMode { get; init; }
    public double? CameraZoom { get; init; }
    public long? CameraMoveMs { get; init; }
    public string? CameraFocus { get; init; }
    public int? CameraOffsetX { get; init; }
    public int? CameraOffsetY { get; init; }

    /// <summary>Keys not modelled here, kept verbatim when the block is saved again.</summary>
    public IReadOnlyDictionary<string, JsonElement> Extra { get; init; } = new Dictionary<string, JsonElement>();

    private static readonly ConditionalWeakTable<string, BlockParameters> Cache = new();

    /// <summary>The parameters of a block, parsed once per distinct JSON string.</summary>
    public static BlockParameters Of(SceneScriptBlock block) => Parse(block.ParametersJson);

    public static BlockParameters Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return Empty;
        return Cache.GetValue(json, ParseUncached);
    }

    private static BlockParameters ParseUncached(string json)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(json); }
        catch (JsonException) { return Empty; }
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object) return Empty;
            var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject()) values[property.Name] = property.Value.Clone();
            JsonElement? Take(string name) => values.Remove(name, out var value) ? value : null;
            string? Text(string name) => Take(name) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;
            bool? Flag(string name) => Take(name) is { } v && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
            int? Int(string name) => Take(name) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt32(out var n) ? n : null;
            long? Long(string name) => Take(name) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt64(out var n) ? n : null;
            double? Real(string name) => Take(name) is { ValueKind: JsonValueKind.Number } v && v.TryGetDouble(out var n) && double.IsFinite(n) ? n : null;
            // Versions before v11 stored video sizes as videoMax…/videoOffset….
            int? Visual(string name, string legacy)
            {
                var primary = values.ContainsKey(name);
                var value = Int(name);
                var old = Int(legacy);
                return primary ? value : old;
            }
            Guid? Id(string name) => Text(name) is { } text && Guid.TryParse(text, out var id) ? id : null;
            RecordedTakeReference? Recording(string name)
            {
                if (Take(name) is not { ValueKind: JsonValueKind.Object } take) return null;
                string? S(string key) => take.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                long? L(string key) => take.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;
                return new RecordedTakeReference(S("path"), S("hash"), L("durationMs"));
            }
            var result = new BlockParameters
            {
                Position = Text("position"),
                FramingPreset = Text("framingPreset"),
                FolderSourceId = Id("folderSourceId"),
                FolderRelativePath = Text("folderRelativePath"),
                IncludeSubfolders = Flag("includeSubfolders"),
                VisualMaxWidth = Visual("visualMaxWidth", "videoMaxWidth"),
                VisualMaxHeight = Visual("visualMaxHeight", "videoMaxHeight"),
                VisualOffsetX = Visual("visualOffsetX", "videoOffsetX"),
                VisualOffsetY = Visual("visualOffsetY", "videoOffsetY"),
                FlipHorizontal = Flag("flipHorizontal"),
                FlipVertical = Flag("flipVertical"),
                ChangeDirection = Flag("changeDirection"),
                RotationDegrees = Real("rotationDegrees"),
                MotionOffsetX = Int("motionOffsetX"),
                MotionOffsetY = Int("motionOffsetY"),
                MotionRotationDegrees = Real("motionRotationDegrees"),
                MotionDurationMs = Long("motionDurationMs"),
                VisualDurationMs = Long("visualDurationMs"),
                AutoTrimBorders = Flag("autoTrimBorders"),
                VideoLayer = Text("videoLayer"),
                GreenScreen = Flag("greenScreen"),
                // Any stored value (even a malformed one) means the user chose; only absence is automatic.
                GreenScreenMode = values.ContainsKey("greenScreenMode") ? Text("greenScreenMode") ?? "" : null,
                KeyColor = Text("keyColor"),
                KeyTolerance = Real("keyTolerance"),
                WaitForEnd = Flag("waitForEnd"),
                AudioDurationMs = Long("audioDurationMs"),
                AudioDurationMode = Text("audioDurationMode"),
                VolumePercent = Int("volumePercent"),
                TransitionStyle = Text("transitionStyle"),
                TransitionDurationMs = Long("transitionDurationMs"),
                TransitionTargets = Int("transitionTargets"),
                TransitionOverride = Text("transitionOverride"),
                VegasEffect = Text("vegasEffect"),
                VegasPluginId = Text("vegasPluginId"),
                VegasPluginPreset = Text("vegasPluginPreset"),
                DirectVoiceProvider = Text("directVoiceProvider"),
                DirectVoiceId = Text("directVoiceId"),
                TranscriptReviewed = Flag("transcriptReviewed"),
                TranscriptOrigin = Text("transcriptOrigin"),
                RecordedTake = Recording("recordedTake"),
                CinemaMode = Text("cinemaMode"),
                CinemaStyle = Text("cinemaStyle"),
                CinemaMoveMs = Long("cinemaMoveMs"),
                CinemaLayers = Text("cinemaLayers"),
                CinemaCharacters = Take("cinemaCharacters") is { ValueKind: JsonValueKind.Array } list
                    ? list.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String && Guid.TryParse(x.GetString(), out var id) ? id : Guid.Empty)
                        .Where(x => x != Guid.Empty).ToArray()
                    : null,
                BlurTarget = Text("blurTarget"),
                BlurAmount = Real("blurAmount"),
                BlurMoveMs = Long("blurMoveMs"),
                GestureMode = Text("gestureMode"),
                GestureSteps = Text("gestureSteps"),
                GestureAngle = Real("gestureAngle"),
                GestureSide = Text("gestureSide"),
                GestureStretch = Real("gestureStretch"),
                GestureSpeed = Real("gestureSpeed"),
                GesturePivot = Text("gesturePivot"),
                CameraMode = Text("cameraMode"),
                CameraZoom = Real("cameraZoom"),
                CameraMoveMs = Long("cameraMoveMs"),
                CameraFocus = Text("cameraFocus"),
                CameraOffsetX = Int("cameraOffsetX"),
                CameraOffsetY = Int("cameraOffsetY")
            };
            // Explicit nulls written by older versions carry no information.
            foreach (var key in values.Where(x => x.Value.ValueKind == JsonValueKind.Null).Select(x => x.Key).ToArray())
                values.Remove(key);
            return result with { Extra = values };
        }
    }

    public string ToJson()
    {
        var json = new JsonObject();
        void Put(string name, JsonNode? value) { if (value is not null) json[name] = value; }
        Put("position", Position);
        Put("framingPreset", FramingPreset);
        Put("folderSourceId", FolderSourceId?.ToString("D"));
        Put("folderRelativePath", FolderRelativePath);
        Put("includeSubfolders", IncludeSubfolders);
        Put("visualMaxWidth", VisualMaxWidth);
        Put("visualMaxHeight", VisualMaxHeight);
        Put("visualOffsetX", VisualOffsetX);
        Put("visualOffsetY", VisualOffsetY);
        Put("flipHorizontal", FlipHorizontal);
        Put("flipVertical", FlipVertical);
        Put("changeDirection", ChangeDirection);
        Put("rotationDegrees", RotationDegrees);
        Put("motionOffsetX", MotionOffsetX);
        Put("motionOffsetY", MotionOffsetY);
        Put("motionRotationDegrees", MotionRotationDegrees);
        Put("motionDurationMs", MotionDurationMs);
        Put("visualDurationMs", VisualDurationMs);
        Put("autoTrimBorders", AutoTrimBorders);
        Put("videoLayer", VideoLayer);
        Put("greenScreen", GreenScreen);
        Put("greenScreenMode", GreenScreenMode);
        Put("keyColor", KeyColor);
        Put("keyTolerance", KeyTolerance);
        Put("waitForEnd", WaitForEnd);
        Put("audioDurationMs", AudioDurationMs);
        Put("audioDurationMode", AudioDurationMode);
        Put("volumePercent", VolumePercent);
        Put("transitionStyle", TransitionStyle);
        Put("transitionDurationMs", TransitionDurationMs);
        Put("transitionTargets", TransitionTargets);
        Put("transitionOverride", TransitionOverride);
        Put("vegasEffect", VegasEffect);
        Put("vegasPluginId", VegasPluginId);
        Put("vegasPluginPreset", VegasPluginPreset);
        Put("directVoiceProvider", DirectVoiceProvider);
        Put("directVoiceId", DirectVoiceId);
        Put("transcriptReviewed", TranscriptReviewed);
        Put("transcriptOrigin", TranscriptOrigin);
        Put("cinemaMode", CinemaMode);
        Put("cinemaStyle", CinemaStyle);
        Put("cinemaMoveMs", CinemaMoveMs);
        Put("cinemaLayers", CinemaLayers);
        if (CinemaCharacters is { Count: > 0 } cinemaCharacters)
            json["cinemaCharacters"] = new JsonArray(cinemaCharacters.Select(x => (JsonNode?)JsonValue.Create(x.ToString("D"))).ToArray());
        Put("blurTarget", BlurTarget);
        Put("blurAmount", BlurAmount);
        Put("blurMoveMs", BlurMoveMs);
        Put("gestureMode", GestureMode);
        Put("gestureSteps", GestureSteps);
        Put("gestureAngle", GestureAngle);
        Put("gestureSide", GestureSide);
        Put("gestureStretch", GestureStretch);
        Put("gestureSpeed", GestureSpeed);
        Put("gesturePivot", GesturePivot);
        Put("cameraMode", CameraMode);
        Put("cameraZoom", CameraZoom);
        Put("cameraMoveMs", CameraMoveMs);
        Put("cameraFocus", CameraFocus);
        Put("cameraOffsetX", CameraOffsetX);
        Put("cameraOffsetY", CameraOffsetY);
        if (RecordedTake is { } take)
            json["recordedTake"] = new JsonObject { ["path"] = take.Path, ["hash"] = take.Hash, ["durationMs"] = take.DurationMs };
        foreach (var (key, value) in Extra)
            if (!json.ContainsKey(key)) json[key] = JsonNode.Parse(value.GetRawText());
        return json.ToJsonString();
    }

    /// <summary>A copy with an unmodelled key set (or removed with null).</summary>
    public BlockParameters WithExtra(string key, JsonNode? value)
    {
        var extra = new Dictionary<string, JsonElement>(Extra, StringComparer.Ordinal);
        if (value is null) extra.Remove(key);
        else
        {
            using var document = JsonDocument.Parse(value.ToJsonString());
            extra[key] = document.RootElement.Clone();
        }
        return this with { Extra = extra };
    }

    public BlockParameters WithDirectVoice(string provider, string? voiceId) => string.IsNullOrWhiteSpace(voiceId)
        ? this with { DirectVoiceProvider = null, DirectVoiceId = null }
        : this with { DirectVoiceProvider = provider, DirectVoiceId = voiceId };

    // ----- Validated values with their defaults (the only place they are defined) -----

    public static bool IsVisual(ScriptBlockKind kind) => kind is ScriptBlockKind.Background or
        ScriptBlockKind.CharacterShow or ScriptBlockKind.Image or ScriptBlockKind.Video;

    private static bool IsTimedAudio(ScriptBlockKind kind) =>
        kind is ScriptBlockKind.Narration or ScriptBlockKind.SoundEffect or ScriptBlockKind.Music;

    public string EffectivePosition => Position is "izquierda" or "derecha" or "auto" ? Position : "centro";

    public string Framing(ScriptBlockKind kind) => kind == ScriptBlockKind.CharacterShow &&
        FramingPreset is "auto" or "entero" or "medio" or "detalle" ? FramingPreset : "original";

    public bool TrimBorders(ScriptBlockKind kind) => kind == ScriptBlockKind.Background && AutoTrimBorders != false;

    public long? VisualDuration(ScriptBlockKind kind) => IsVisual(kind) && VisualDurationMs > 0 ? VisualDurationMs : null;

    public long? AudioDuration(ScriptBlockKind kind) => IsTimedAudio(kind) && AudioDurationMs > 0 ? AudioDurationMs : null;

    public bool StretchAudio(ScriptBlockKind kind) => IsTimedAudio(kind) && AudioDurationMode == "tempo";

    public bool WaitsForSound(ScriptBlockKind kind) => kind == ScriptBlockKind.SoundEffect && WaitForEnd == true;

    public int Volume(ScriptBlockKind kind) =>
        kind is ScriptBlockKind.SoundEffect or ScriptBlockKind.Music or ScriptBlockKind.Video && VolumePercent is >= 0 and <= 200
            ? VolumePercent.Value : BlockDefaults.VolumePercent(kind);

    public bool AutoGreenScreen(ScriptBlockKind kind) => kind == ScriptBlockKind.Video && GreenScreenMode is null;

    public (string Layer, bool GreenScreen, string KeyColor, double Tolerance) Video
    {
        get
        {
            // v10 saved "fondo" even when the user had not deliberately chosen a layer: that legacy
            // default means script order; an explicit background layer is "fondo-fijo".
            var layer = VideoLayer switch { "fondo-fijo" => "fondo", "sobre" => "sobre", _ => "guion" };
            var color = KeyColor is { Length: 6 } key && key.All(Uri.IsHexDigit) ? key : BlockDefaults.KeyColor;
            return (layer, GreenScreen == true, color,
                KeyTolerance is >= 0.01 and <= 1 ? KeyTolerance.Value : BlockDefaults.KeyTolerance);
        }
    }

    public VisualTransformOptions Transform(ScriptBlockKind kind)
    {
        var box = BlockDefaults.VisualBox(kind);
        var max = BlockDefaults.VisualMaxBox(kind);
        static int Range(int? value, int fallback, int min, int max) => value is { } v && v >= min && v <= max ? v : fallback;
        // A size over the limit of its kind (e.g. a Director «ancho=1920» on a render) is capped, not dropped.
        static int Size(int? value, int fallback, int max) => value is { } v && v >= 64 ? Math.Min(v, max) : fallback;
        static double Angle(double? value, double limit) => value is { } v && v >= -limit && v <= limit ? v : 0;
        return new VisualTransformOptions(
            Size(VisualMaxWidth, box.Width, max.Width), Size(VisualMaxHeight, box.Height, max.Height),
            Range(VisualOffsetX, 0, -1280, 1280), Range(VisualOffsetY, 0, -720, 720),
            FlipHorizontal == true, FlipVertical == true, Angle(RotationDegrees, 180),
            Range(MotionOffsetX, 0, -1280, 1280), Range(MotionOffsetY, 0, -720, 720),
            Angle(MotionRotationDegrees, 720),
            MotionDurationMs is >= 0 and <= 86_400_000 ? MotionDurationMs.Value : 0,
            ChangeDirection == true);
    }

    public int Targets => TransitionTargets is >= 1 and <= SceneComposer.TargetAll ? TransitionTargets.Value : SceneComposer.TargetAll;

    public (string Style, long DurationMs) Transition => TransitionStyle is "entrada" or "salida" or "cambio" or "cruce"
        ? (TransitionStyle, Math.Clamp(TransitionDurationMs ?? BlockDefaults.TransitionDurationMs, 80, 10_000))
        : ("entrada", 0);

    public string Effect => VegasEffect is "disolvente" or "flash" or "barrido" or "plugin" ? VegasEffect : "ninguno";

    public string VisualTransition(ScriptBlockKind kind) => IsVisual(kind) &&
        TransitionOverride is "corte" or "fundido" or "disolvente" or "flash" or "barrido" or "plugin" ? TransitionOverride : "heredar";

    public string? DirectVoice(string provider) =>
        DirectVoiceProvider == provider && !string.IsNullOrWhiteSpace(DirectVoiceId) ? DirectVoiceId : null;

    /// <summary>The request of a cinema block, validated (see <see cref="CinemaSettings"/>).</summary>
    public CinemaSettings Cinema()
    {
        var characters = CinemaCharacters ?? [];
        var layers = CinemaLayers is "personajes" ? "personajes"
            : CinemaLayers is "lista" && characters.Count > 0 ? "lista" : "todos";
        return new CinemaSettings(CinemaMode != "quitar", CinemaStyle == "abierto" ? "abierto" : "cerrado",
            CinemaMoveMs is >= 0 and <= BlockDefaults.CameraMoveMaxMs ? CinemaMoveMs.Value : BlockDefaults.CinemaMoveMs,
            layers, layers == "lista" ? characters : []);
    }

    /// <summary>The camera request of a camera block, validated. A block with a character and no mode
    /// frames that character; without either it goes back to the whole frame.</summary>
    public CameraSettings Camera(bool hasCharacter)
    {
        var mode = CameraMode is "personaje" or "habla" or "punto" or "general" ? CameraMode
            : hasCharacter ? "personaje" : "general";
        if (mode == "personaje" && !hasCharacter) mode = "general";
        var zoom = mode == "general" ? 1 : CameraZoom is { } z && z >= BlockDefaults.CameraZoomMin && z <= BlockDefaults.CameraZoomMax
            ? z : BlockDefaults.CameraZoom;
        var move = CameraMoveMs is >= 0 and <= BlockDefaults.CameraMoveMaxMs ? CameraMoveMs.Value : BlockDefaults.CameraMoveMs;
        return new CameraSettings(mode, zoom, move, CameraFocus == "cuerpo" ? "cuerpo" : "cara",
            CameraOffsetX is >= -640 and <= 640 ? CameraOffsetX.Value : 0,
            CameraOffsetY is >= -360 and <= 360 ? CameraOffsetY.Value : 0);
    }

    /// <summary>The blur request of a blur block, validated. A block with a character and no target blurs that
    /// character; without either it blurs the background.</summary>
    public BlurSettings Blur(bool hasCharacter)
    {
        var target = BlurTarget is "fondo" or "personajes" or "imagenes" or "videos" or "todos" ? BlurTarget
            : hasCharacter ? "personaje" : "fondo";
        if (target == "personaje" && !hasCharacter) target = "fondo";
        return new BlurSettings(target,
            BlurAmount is { } amount && amount >= 0 && amount <= BlurPlan.Max ? amount : BlurPlan.Soft,
            BlurMoveMs is >= 0 and <= BlockDefaults.CameraMoveMaxMs ? BlurMoveMs.Value : 0);
    }

    /// <summary>The gesture request of a gesture block, validated. «ambos» is balanceo+rebote at once;
    /// no valid move means a single balanceo.</summary>
    public GestureSettings Gesture()
    {
        var mode = GestureMode is "habla" or "quitar" ? GestureMode : "personaje";
        var steps = GestureSettings.ParseSteps(GestureSteps)
            .Select(x => (IReadOnlyList<string>)x.SelectMany(m => m == "ambos" ? new[] { "balanceo", "rebote" } : [m]).Distinct().ToArray())
            .ToArray();
        if (steps.Length == 0) steps = [["balanceo"]];
        var stretch = GestureStretch is { } s && s >= BlockDefaults.GestureStretchMin && s <= BlockDefaults.GestureStretchMax && Math.Abs(s) >= 1
            ? s : BlockDefaults.GestureStretch;
        return new GestureSettings(mode, steps,
            GestureAngle is { } a && a >= 0.5 && a <= BlockDefaults.GestureAngleMax ? a : BlockDefaults.GestureAngle,
            GestureSide is "derecha" or "izquierda" ? GestureSide : "auto", stretch,
            GestureSpeed is { } v && v >= BlockDefaults.GestureSpeedMin && v <= BlockDefaults.GestureSpeedMax ? v : 1,
            GesturePivot is "cintura" or "centro" ? GesturePivot : "pies");
    }

    public override string ToString() => ToJson();
}
