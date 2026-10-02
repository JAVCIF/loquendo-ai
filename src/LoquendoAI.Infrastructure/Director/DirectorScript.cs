using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;

namespace LoquendoAI.Infrastructure.Director;

/// <summary>One parsed Director line: what to do, with whom, which resource and its options.</summary>
public sealed record DirectorSpec(ScriptBlockKind Kind, string CharacterName, string ResourceQuery, string Text,
    string Position, bool Wait, int PauseMs, string? Error, string FramingPreset = "auto",
    Dictionary<string, string>? Options = null)
{
    /// <summary>The instruction this spec came from (one line of the prompt). A group hide
    /// («[OCULTAR] Bart, Lisa», «[OCULTAR] todos») becomes one spec per character, each with its own line.</summary>
    public string SourceLine { get; init; } = "";

    public string Summary => Kind is ScriptBlockKind.Camera or ScriptBlockKind.Cinema or ScriptBlockKind.Gesture or ScriptBlockKind.Blur
        ? (Kind switch
          {
              ScriptBlockKind.Camera => "cámara: ", ScriptBlockKind.Cinema => "cine: ",
              ScriptBlockKind.Blur => "desenfoque: ", _ => "gesto: "
          }) + Text +
          (Options is { Count: > 0 } ? " · " + string.Join(", ", Options.Select(x => x.Key.ToLowerInvariant() + "=" + x.Value)) : "")
        : Position + " · " + FramingPreset +
        (Wait && (Options is null || !Options.ContainsKey("ESPERAR")) ? " · esperar" : "") +
        (Options is { Count: > 0 } ? " · " + string.Join(", ", Options.Select(x => x.Key.ToLowerInvariant() + "=" + x.Value)) : "");
}

/// <summary>
/// The non-AI Director language ("[FONDO] cocina | x=40", "Bart: hola"): parsing, option
/// validation and the block parameters each line produces. Shared by the manual Director, the
/// AI Director (its typed output is turned into these lines) and the tests. Moved out of the
/// WPF window in 1.0.0-beta.4 so it can be tested; the App calls it through a global using static.
/// </summary>
public static class DirectorScript
{
    /// <param name="onScreenAtStart">Characters already on screen before the prompt (a continuation of a scene):
    /// «[OCULTAR] todos» hides them too.</param>
    public static List<DirectorSpec> ParseDirectorPrompt(string prompt, IEnumerable<string>? onScreenAtStart = null)
    {
        var result = new List<DirectorSpec>();
        var sources = new List<(int Start, string Line)>();
        foreach (var raw in prompt.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            sources.Add((result.Count, line));
            if (line.StartsWith('[') && line.IndexOf(']') is var closing && closing > 1)
            {
                var command = NormalizeDirectorName(line[1..closing]);
                var parts = line[(closing + 1)..].Trim().Split('|', StringSplitOptions.TrimEntries);
                var value = parts[0];
                var kind = command switch
                {
                    "FONDO" => ScriptBlockKind.Background,
                    "MOSTRAR" or "MOSTRAR PERSONAJE" => ScriptBlockKind.CharacterShow,
                    "OCULTAR" or "OCULTAR PERSONAJE" => ScriptBlockKind.CharacterHide,
                    "SFX" or "EFECTO" or "EFECTO DE SONIDO" => ScriptBlockKind.SoundEffect,
                    "MUSICA" => ScriptBlockKind.Music,
                    "IMAGEN" or "PROP" => ScriptBlockKind.Image,
                    "VIDEO" => ScriptBlockKind.Video,
                    "PAUSA" => ScriptBlockKind.Pause,
                    "NARRACION" => ScriptBlockKind.Narration,
                    "DIALOGO" => ScriptBlockKind.Dialogue,
                    "TRANSICION" => ScriptBlockKind.Transition,
                    "CAMARA" => ScriptBlockKind.Camera,
                    "CINE" or "BARRAS" or "ENCUADRE CINE" or "ENCUADRE CINEMATOGRAFICO" => ScriptBlockKind.Cinema,
                    "GESTO" or "BALANCEO" or "REBOTE" or "ANIMACION" or "ANIMAR RENDER" => ScriptBlockKind.Gesture,
                    "DESENFOQUE" or "DESENFOCAR" or "BLUR" or "ENFOCAR" or "ENFOQUE" => ScriptBlockKind.Blur,
                    _ => ScriptBlockKind.Comment
                };
                var position = parts.FirstOrDefault(x => SameDirectorName(x, "izquierda") || SameDirectorName(x, "derecha") ||
                    SameDirectorName(x, "centro") || SameDirectorName(x, "auto")) ??
                    (kind == ScriptBlockKind.CharacterShow ? "auto" : "centro");
                var framing = parts.Skip(1).FirstOrDefault(IsDirectorFramingOption);
                var framingPreset = framing is null ? "auto" : NormalizeDirectorName(framing) switch
                {
                    "CUERPO ENTERO" => "entero", "MEDIO CUERPO" => "medio", "PRIMER PLANO" => "detalle",
                    "ORIGINAL" => "original", _ => "auto"
                };
                var pause = 0;
                string? error = null;
                if (kind == ScriptBlockKind.Comment) error = "Instrucción desconocida";
                else if (kind == ScriptBlockKind.Pause && (!int.TryParse(value.Replace("ms", "", StringComparison.OrdinalIgnoreCase).Trim(), out pause) || pause < 1 || pause > 86_400_000))
                    error = "Pausa inválida (ms)";
                else if (value.Length == 0 && kind != ScriptBlockKind.Pause)
                    error = "Falta el texto, personaje o recurso";
                else if (kind == ScriptBlockKind.Transition && NormalizeDirectorName(value) is not
                    ("FUNDIDO ENTRADA" or "FUNDIDO SALIDA" or "ENTRADA" or "SALIDA" or
                     "CAMBIO" or "CAMBIO A MITAD" or "CRUCE" or "CRUCE DE CAPAS"))
                    error = "Transición válida: entrada, salida, cambio o cruce";
                var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string? gestureMoves = null;
                string? blurStyle = null;
                foreach (var option in parts.Skip(1))
                {
                    if (kind == ScriptBlockKind.Blur && !option.Contains('=') && BlurStyle(option) is { } style)
                    {
                        if (blurStyle is not null) error ??= $"Estilo duplicado: {option}";
                        blurStyle = style;
                        continue;
                    }
                    if (kind == ScriptBlockKind.Gesture && !option.Contains('=') &&
                        (GestureSettings.ParseSteps(option).Count > 0 || SameDirectorName(option, "quitar")))
                    {
                        if (gestureMoves is not null) error ??= $"Movimientos duplicados: {option}";
                        gestureMoves = option;
                        continue;
                    }
                    if (SameDirectorName(option, position) || IsDirectorFramingOption(option) ||
                        kind == ScriptBlockKind.SoundEffect && SameDirectorName(option, "esperar")) continue;
                    var equal = option.IndexOf('=');
                    if (equal < 1)
                    {
                        // [MOSTRAR] Bart | render and [OCULTAR] NPC | render (1.4.1): one bare field names the render.
                        if (!(kind == ScriptBlockKind.CharacterShow && SameDirectorName(option, value)) &&
                            !((kind == ScriptBlockKind.CharacterShow || kind == ScriptBlockKind.CharacterHide && SameDirectorName(value, "NPC")) &&
                                options.Count == 0 && parts.Skip(1).FirstOrDefault(x =>
                                !x.Contains('=') && !SameDirectorName(x, position) && !IsDirectorFramingOption(x)) == option))
                            error ??= $"Opción no reconocida: {option}";
                        continue;
                    }
                    var key = NormalizeDirectorName(option[..equal]) switch
                    {
                        "GIRO" or "INCLINACION" or "GRADOS" when kind == ScriptBlockKind.Gesture => "ANGULO",
                        "ESTIRAMIENTO" or "REBOTE" or "DEFORMAR" when kind == ScriptBlockKind.Gesture => "ESTIRAR",
                        "PIVOTE" or "PUNTO DE EJE" or "PUNTO" when kind == ScriptBlockKind.Gesture => "EJE",
                        "DIRECCION" or "HACIA" when kind == ScriptBlockKind.Gesture => "LADO",
                        "RAPIDEZ" or "TEMPO" when kind == ScriptBlockKind.Gesture => "VELOCIDAD",
                        "CANTIDAD" or "RANGO" or "INTENSIDAD" or "NIVEL" when kind == ScriptBlockKind.Blur => "VALOR",
                        "INVERTIR HORIZONTAL" or "VOLTEAR HORIZONTAL" => "VOLTEAR H",
                        // «Cambiar dirección» (1.4.3): looks the other way without moving (voltear h mirrors the place too).
                        "DIRECCION" or "CAMBIAR DE DIRECCION" or "MIRAR AL OTRO LADO" or "DAR LA VUELTA" => "CAMBIAR DIRECCION",
                        "INVERTIR VERTICAL" or "VOLTEAR VERTICAL" => "VOLTEAR V",
                        "GIRO" => "ROTACION",
                        "MOVER X" => "X",
                        "MOVER Y" => "Y",
                        "PANTALLA VERDE" => "CROMA",
                        "COLOR CROMA" => "COLOR",
                        "VIDEO EN" or "CAPA VIDEO" => "CAPA",
                        "DURACION AUDIO" when kind is ScriptBlockKind.Narration or ScriptBlockKind.SoundEffect or ScriptBlockKind.Music => "DURACION",
                        "PAUSA DESPUES" or "PAUSA MS" => "PAUSA",
                        "ACERCAR" or "AUMENTO" => "ZOOM",
                        "FOCO" => "ENFOQUE",
                        "MOVIMIENTO" or "MS" when kind is ScriptBlockKind.Camera or ScriptBlockKind.Cinema or ScriptBlockKind.Blur => "DURACION",
                        "PERSONAJES" or "CAPA" when kind == ScriptBlockKind.Cinema => "CAPAS",
                        _ => NormalizeDirectorName(option[..equal])
                    };
                    var val = option[(equal + 1)..].Trim();
                    if (!DirectorOptionAllowed(kind, key) ||
                        !(kind == ScriptBlockKind.Camera ? CameraOptionValid(key, val)
                            : kind == ScriptBlockKind.Cinema ? CinemaOptionValid(key, val)
                            : kind == ScriptBlockKind.Gesture ? GestureOptionValid(key, val)
                            : kind == ScriptBlockKind.Blur ? BlurOptionValid(key, val) : DirectorOptionValid(key, val)) ||
                        kind == ScriptBlockKind.Transition && key == "DURACION" &&
                        (!long.TryParse(val, out var fadeMs) || fadeMs is < 80 or > 10_000))
                        error ??= $"Opción inválida o aún no disponible: {option}";
                    else if (!options.TryAdd(key, val))
                        error ??= $"Opción duplicada: {key}";
                }
                if (kind == ScriptBlockKind.Transition && options.ContainsKey("VEGAS") &&
                    NormalizeDirectorName(value) is not ("CRUCE" or "CRUCE DE CAPAS"))
                    error ??= "El efecto VEGAS se aplica sobre el fundido de Cruce de capas";
                if (kind == ScriptBlockKind.Transition && options.ContainsKey("CAPAS") &&
                    NormalizeDirectorName(value) is not ("CRUCE" or "CRUCE DE CAPAS"))
                    error ??= "La selección de capas se aplica sobre Cruce de capas";
                var pluginSelector = DirectorPluginSelector(kind, options);
                if (SceneComposer.IsVisualBlock(kind) && options.ContainsKey("VEGAS") &&
                    !SameDirectorName(options.GetValueOrDefault("TRANSICION") ?? "", "plugin"))
                    error ??= "Para elegir VEGAS en este recurso usa transicion=plugin";
                if (SceneComposer.IsVisualBlock(kind) &&
                    SameDirectorName(options.GetValueOrDefault("TRANSICION") ?? "", "plugin") && pluginSelector is null)
                    error ??= "Falta vegas=nombre del plugin o su ID";
                if (options.ContainsKey("PRESET") && pluginSelector is null)
                    error ??= "El preset necesita una transición del catálogo";
                if (pluginSelector is not null && !VegasTransitionCatalog.TryResolve(pluginSelector,
                        options.GetValueOrDefault("PRESET"), out _, out _))
                    error ??= "Plugin o preset no encontrado en el catálogo; usa su nombre/ID y un preset exacto";
                if (kind != ScriptBlockKind.Pause && options.TryGetValue("PAUSA", out var pauseValue) &&
                    int.TryParse(pauseValue, out var pauseAfter)) pause = pauseAfter;
                if (kind == ScriptBlockKind.Cinema)
                {
                    var show = CinemaShow(value);
                    if (show is null) error ??= "Usa [CINE] mostrar o [CINE] quitar";
                    result.Add(new DirectorSpec(kind, "", "", show == false ? "quitar" : "mostrar", "centro", false, pause,
                        error, Options: options));
                    continue;
                }
                if (kind == ScriptBlockKind.Blur)
                {
                    var target = BlurTargetName(value);
                    blurStyle ??= (options.TryGetValue("ESTILO", out var named) ? BlurStyle(named) : null) ??
                        (command == "ENFOCAR" || command == "ENFOQUE" ? "quitar" : "suavizar");
                    if (blurStyle == "quitar" && options.ContainsKey("VALOR")) error ??= "«valor» no va con quitar/enfocar";
                    options["ESTILO"] = blurStyle;
                    result.Add(new DirectorSpec(kind, target == "personaje" ? value : "", "", target, "centro", false, pause,
                        error, Options: options));
                    continue;
                }
                if (kind == ScriptBlockKind.Gesture)
                {
                    var speaker = CameraMode(value) == "habla";
                    gestureMoves ??= command switch { "BALANCEO" => "balanceo", "REBOTE" => "rebote", _ => "balanceo" };
                    var stop = SameDirectorName(gestureMoves, "quitar");
                    if (stop && !speaker) error ??= "«quitar» solo con [GESTO] habla (deja de hacerlo al hablar)";
                    if (!stop) options["MOVIMIENTOS"] = GestureSettings.StepsText(GestureSettings.ParseSteps(gestureMoves));
                    result.Add(new DirectorSpec(kind, speaker ? "" : value, "", speaker ? stop ? "quitar" : "habla" : "personaje",
                        "centro", false, pause, error, Options: options));
                    continue;
                }
                if (kind == ScriptBlockKind.Camera)
                {
                    var mode = CameraMode(value);
                    if (mode == "punto" && options.ContainsKey("ZOOM") is false)
                        options["ZOOM"] = BlockDefaults.CameraPointZoom.ToString(CultureInfo.InvariantCulture);
                    result.Add(new DirectorSpec(kind, mode == "personaje" ? value : "", "", mode, "centro", false, pause,
                        error, Options: options));
                    continue;
                }
                if (kind == ScriptBlockKind.Dialogue)
                {
                    var separator = value.IndexOf(':');
                    if (separator <= 0 || separator == value.Length - 1) error = "Usa [DIÁLOGO] Personaje: texto";
                    result.Add(new DirectorSpec(kind, separator > 0 ? value[..separator].Trim() : value, "",
                        separator > 0 ? value[(separator + 1)..].Trim() : "", position, false, pause, error, Options: options));
                }
                else
                    result.Add(new DirectorSpec(kind, kind is ScriptBlockKind.CharacterShow or ScriptBlockKind.CharacterHide ? value : "",
                        kind == ScriptBlockKind.CharacterShow ? parts.Skip(1).FirstOrDefault(x => !x.Contains('=') && !SameDirectorName(x, position) &&
                            !SameDirectorName(x, "esperar") && !IsDirectorFramingOption(x)) ?? value
                        // [OCULTAR] NPC | render: the render to hide (a character's name alone needs none).
                        : kind == ScriptBlockKind.CharacterHide ? SameDirectorName(value, "NPC") ? parts.Skip(1).FirstOrDefault(x =>
                            !x.Contains('=') && !SameDirectorName(x, position) && !IsDirectorFramingOption(x)) ?? "" : ""
                        : value,
                        kind is ScriptBlockKind.Narration or ScriptBlockKind.Transition ? value : "", position,
                        parts.Any(x => SameDirectorName(x, "esperar")), pause, error, framingPreset, options));
                continue;
            }
            var colon = line.IndexOf(':');
            if (colon > 0 && colon < 70 && colon < line.Length - 1)
            {
                var speaker = line[..colon].Trim();
                if (speaker.EndsWith(" dice", StringComparison.OrdinalIgnoreCase)) speaker = speaker[..^5].Trim();
                result.Add(new DirectorSpec(ScriptBlockKind.Dialogue, speaker, "", line[(colon + 1)..].Trim(), "centro", false, 0, null));
            }
            else result.Add(new DirectorSpec(ScriptBlockKind.Comment, "", "", line, "centro", false, 0,
                "No entendido; usa [FONDO], [MOSTRAR], [SFX] o Personaje: texto"));
        }
        for (var s = 0; s < sources.Count; s++)
        {
            var end = s + 1 < sources.Count ? sources[s + 1].Start : result.Count;
            for (var i = sources[s].Start; i < end; i++) result[i] = result[i] with { SourceLine = sources[s].Line };
        }
        return ExpandGroupHides(result, onScreenAtStart ?? []);
    }

    /// <summary>
    /// «[OCULTAR] Bart, Lisa» and «[OCULTAR] todos» (1.4.4): one hide per character, one after the other with no time
    /// between them, so they leave at the same moment (hides take no time; a pause goes after the last one). «todos»
    /// are the characters and NPC renders on screen at that point of the prompt (plus <paramref name="onScreenAtStart"/>).
    /// «[OCULTAR] Bart | pausa=400 » then «[OCULTAR] Lisa» still staggers them.
    /// </summary>
    private static List<DirectorSpec> ExpandGroupHides(List<DirectorSpec> specs, IEnumerable<string> onScreenAtStart)
    {
        // On screen, in order of appearance: a character's name, or «NPC» + the render.
        var onScreen = onScreenAtStart.Where(x => x.Length > 0).Select(x => (Name: x, Render: "")).ToList();
        static bool Same((string Name, string Render) a, (string Name, string Render) b) =>
            SameDirectorName(a.Name, b.Name) && (a.Render.Length == 0 && b.Render.Length == 0 || SameDirectorName(a.Render, b.Render));
        (string, string) Key(DirectorSpec spec) =>
            (spec.CharacterName, SameDirectorName(spec.CharacterName, "NPC") ? spec.ResourceQuery : "");
        var result = new List<DirectorSpec>(specs.Count);
        foreach (var spec in specs)
        {
            if (spec.Kind == ScriptBlockKind.CharacterShow && spec.Error is null)
            {
                var key = Key(spec);
                if (!onScreen.Any(x => Same(x, key))) onScreen.Add(key);
            }
            if (spec.Kind != ScriptBlockKind.CharacterHide || spec.Error is not null)
            {
                result.Add(spec);
                continue;
            }
            var all = SameDirectorName(spec.CharacterName, "todos") || SameDirectorName(spec.CharacterName, "todo") ||
                SameDirectorName(spec.CharacterName, "todos los personajes");
            if (!all && !spec.CharacterName.Contains(','))
            {
                var key = Key(spec);
                onScreen.RemoveAll(x => Same(x, key));
                result.Add(spec);
                continue;
            }
            var targets = all ? onScreen.ToList()
                : spec.CharacterName.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => (Name: x, Render: "")).ToList();
            targets = targets.Where((x, i) => !targets.Take(i).Any(y => Same(x, y))).ToList();
            if (targets.Count == 0)
            {
                result.Add(spec with { Error = all ? "No hay personajes en pantalla para ocultar" : "Falta el personaje" });
                continue;
            }
            if (targets.Any(x => SameDirectorName(x.Name, "NPC") && x.Render.Length == 0))
            {
                result.Add(spec with { Error = "Para ocultar un NPC escribe [OCULTAR] NPC | recurso (uno por línea)" });
                continue;
            }
            var options = spec.Options is null ? null : new Dictionary<string, string>(spec.Options);
            options?.Remove("PAUSA");
            for (var t = 0; t < targets.Count; t++)
            {
                var (name, render) = targets[t];
                var last = t == targets.Count - 1;
                var npc = render.Length > 0;
                result.Add(spec with
                {
                    CharacterName = npc ? "NPC" : name,
                    ResourceQuery = npc ? render : "",
                    PauseMs = last ? spec.PauseMs : 0,
                    Options = last ? spec.Options : options,
                    SourceLine = "[OCULTAR] " + (npc ? "NPC | " + render : name) + (last && spec.PauseMs > 0 ? " | pausa=" + spec.PauseMs : "")
                });
                onScreen.RemoveAll(x => Same(x, (name, render)));
            }
        }
        return result;
    }

    public static int DirectorNameDistance(string left, string right)
    {
        left = NormalizeDirectorName(left);
        right = NormalizeDirectorName(right);
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var i = 1; i <= left.Length; i++)
        {
            var current = new int[right.Length + 1];
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1),
                    previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            previous = current;
        }
        return previous[right.Length];
    }

    public static bool SameDirectorName(string left, string right) => NormalizeDirectorName(left) == NormalizeDirectorName(right);
    public static string NormalizeDirectorName(string value)
    {
        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var text = new StringBuilder();
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) text.Append(char.ToUpperInvariant(c));
        return text.ToString().Normalize(NormalizationForm.FormC);
    }

    public static bool IsDirectorFramingOption(string value) => NormalizeDirectorName(value) is
        "AUTO" or "AUTOMATICO" or "ORIGINAL" or "CUERPO ENTERO" or "MEDIO CUERPO" or "PRIMER PLANO";

    public static int DirectorTransitionTargets(string value)
    {
        if (NormalizeDirectorName(value) is "TODOS" or "TODAS") return SceneComposer.TargetAll;
        var targets = 0;
        foreach (var entry in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            targets |= NormalizeDirectorName(entry) switch
            {
                "FONDO" => SceneComposer.TargetBackground,
                "PERSONAJES" or "PERSONAJE" or "RENDERS" => SceneComposer.TargetCharacter,
                "IMAGENES" or "IMAGEN" or "GIF" => SceneComposer.TargetImage,
                "VIDEOS" or "VIDEO" => SceneComposer.TargetVideo,
                _ => 0
            };
            if (NormalizeDirectorName(entry) is not ("FONDO" or "PERSONAJES" or "PERSONAJE" or
                "RENDERS" or "IMAGENES" or "IMAGEN" or "GIF" or "VIDEOS" or "VIDEO")) return 0;
        }
        return targets;
    }

    public static bool DirectorOptionAllowed(ScriptBlockKind kind, string key) => key switch
    {
        "X" or "Y" when kind == ScriptBlockKind.Camera => true,
        "ESTILO" or "DURACION" or "CAPAS" when kind == ScriptBlockKind.Cinema => true,
        "ANGULO" or "LADO" or "ESTIRAR" or "VELOCIDAD" or "EJE" => kind == ScriptBlockKind.Gesture,
        "DURACION" or "VALOR" or "ESTILO" when kind == ScriptBlockKind.Blur => true,
        "DURACION" when kind == ScriptBlockKind.Camera => true,
        "ANCHO" or "ALTO" or "X" or "Y" or "VOLTEAR H" or "VOLTEAR V" or "CAMBIAR DIRECCION" or "ROTACION" or
        "ANIMAR X" or "ANIMAR Y" or "ANIMAR GIRO" or "ANIMAR MS" =>
            kind is ScriptBlockKind.Background or ScriptBlockKind.CharacterShow or ScriptBlockKind.Image or ScriptBlockKind.Video,
        "DURACION" => kind is ScriptBlockKind.Background or ScriptBlockKind.CharacterShow or
            ScriptBlockKind.Image or ScriptBlockKind.Video or ScriptBlockKind.Transition or
            ScriptBlockKind.Narration or ScriptBlockKind.SoundEffect or ScriptBlockKind.Music,
        "PAUSA" => kind is not (ScriptBlockKind.Pause or ScriptBlockKind.Comment),
        "ZOOM" => kind is ScriptBlockKind.Camera or ScriptBlockKind.Background,
        "ENFOQUE" => kind == ScriptBlockKind.Camera,
        "ESPERAR" => kind == ScriptBlockKind.SoundEffect,
        "VOLUMEN" => kind is ScriptBlockKind.SoundEffect or ScriptBlockKind.Music or ScriptBlockKind.Video,
        "VEGAS" or "PRESET" => kind == ScriptBlockKind.Transition || SceneComposer.IsVisualBlock(kind),
        "CAPAS" => kind == ScriptBlockKind.Transition,
        "TRANSICION" => SceneComposer.IsVisualBlock(kind),
        "BORDES" => kind == ScriptBlockKind.Background,
        "CAPA" or "CROMA" or "COLOR" or "TOLERANCIA" => kind == ScriptBlockKind.Video,
        "MODO AUDIO" => kind is ScriptBlockKind.Narration or ScriptBlockKind.SoundEffect or ScriptBlockKind.Music,
        // «escala=130» (1.4.7): the render 30 % bigger than the automatic framing (or its box) gives it.
        "ESCALA" => kind == ScriptBlockKind.CharacterShow,
        _ => false
    };

    public static bool DirectorOptionValid(string key, string value) => key switch
    {
        // Up to 3× the frame: only backgrounds use more than 1280×720 (zoom, 1.4.0); other visuals are capped to the frame.
        "ANCHO" => int.TryParse(value, out var w) && w is >= 64 and <= 3840,
        "ESCALA" => int.TryParse(value.TrimEnd('%').Trim(), out var scale) &&
            scale >= BlockParameters.ScaleMin * 100 && scale <= BlockParameters.ScaleMax * 100,
        "ZOOM" => ParseZoom(value) is >= 1 and <= BlockDefaults.BackgroundZoomMax, // [FONDO] only (DirectorOptionAllowed)
        "ALTO" => int.TryParse(value, out var h) && h is >= 64 and <= 2160,
        "X" => int.TryParse(value, out var x) && x is >= -1280 and <= 1280,
        "Y" => int.TryParse(value, out var y) && y is >= -720 and <= 720,
        "ROTACION" => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var angle) &&
            double.IsFinite(angle) && angle is >= -180 and <= 180,
        "ANIMAR X" => int.TryParse(value, out var dx) && dx is >= -1280 and <= 1280,
        "ANIMAR Y" => int.TryParse(value, out var dy) && dy is >= -720 and <= 720,
        "ANIMAR GIRO" => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var spin) &&
            double.IsFinite(spin) && spin is >= -720 and <= 720,
        "ANIMAR MS" => long.TryParse(value, out var animation) && animation is >= 0 and <= 86_400_000,
        "DURACION" => long.TryParse(value, out var d) && d is >= 1 and <= 86_400_000,
        "PAUSA" => int.TryParse(value, out var pause) && pause is >= 0 and <= 86_400_000,
        "VOLUMEN" => int.TryParse(value, out var volume) && volume is >= 0 and <= 200,
        "VOLTEAR H" or "VOLTEAR V" or "CAMBIAR DIRECCION" or "BORDES" or "CROMA" or "ESPERAR" => NormalizeDirectorName(value) is "SI" or "NO",
        "CAPA" => NormalizeDirectorName(value) is "GUION" or "FONDO" or "SOBRE" or
            "FONDO FIJO" or "FONDO-FIJO" or "ORDEN DEL GUION" or "ENCIMA DE TODO",
        "MODO AUDIO" => NormalizeDirectorName(value) is "LOOP" or "TEMPO",
        "VEGAS" => NormalizeDirectorName(value) is "NINGUNO" or "DISOLVENTE" or "FLASH" or "BARRIDO" ||
            VegasTransitionCatalog.Find(value) is not null,
        "PRESET" => value.Trim().Length > 0,
        "TRANSICION" => NormalizeDirectorName(value) is "HEREDAR" or "CORTE" or "FUNDIDO" or
            "DISOLVENTE" or "FLASH" or "BARRIDO" or "PLUGIN" || VegasTransitionCatalog.Find(value) is not null,
        "CAPAS" => DirectorTransitionTargets(value) != 0,
        "COLOR" => value.TrimStart('#') is var color && color.Length == 6 && color.All(Uri.IsHexDigit),
        "TOLERANCIA" => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var t) && t is >= 0.01 and <= 1,
        _ => false
    };

    public static string? DirectorPluginSelector(ScriptBlockKind kind, Dictionary<string, string> options)
    {
        if (kind == ScriptBlockKind.Transition)
        {
            var name = options.GetValueOrDefault("VEGAS");
            return NormalizeDirectorName(name ?? "") is "NINGUNO" or "DISOLVENTE" or "FLASH" or "BARRIDO"
                ? null : VegasTransitionCatalog.Find(name) is not null ? name : null;
        }
        if (!SceneComposer.IsVisualBlock(kind)) return null;
        var transition = options.GetValueOrDefault("TRANSICION");
        if (SameDirectorName(transition ?? "", "plugin")) return options.GetValueOrDefault("VEGAS");
        if (NormalizeDirectorName(transition ?? "") is "HEREDAR" or "CORTE" or "FUNDIDO" or
            "DISOLVENTE" or "FLASH" or "BARRIDO") return null;
        return VegasTransitionCatalog.Find(transition) is not null ? transition : null;
    }

    /// <param name="characterId">Resolves the character names of «[CINE] … | capas=Bart,Lisa».</param>
    public static string DirectorParameters(DirectorSpec spec, Func<string, Guid?>? characterId = null)
    {
        var options = spec.Options ?? new Dictionary<string, string>();
        if (spec.Kind == ScriptBlockKind.Blur)
        {
            var style = options.GetValueOrDefault("ESTILO") ?? "suavizar";
            return new BlockParameters
            {
                BlurTarget = spec.Text,
                BlurAmount = style == "quitar" ? 0 : GestureNumber(options.GetValueOrDefault("VALOR")) ??
                    (style == "ligero" ? BlurPlan.Light : BlurPlan.Soft),
                BlurMoveMs = long.TryParse(options.GetValueOrDefault("DURACION"), out var blurMs) ? blurMs : null
            }.ToJson();
        }
        if (spec.Kind == ScriptBlockKind.Gesture)
            return new BlockParameters
            {
                GestureMode = spec.Text,
                GestureSteps = options.GetValueOrDefault("MOVIMIENTOS"),
                GestureAngle = GestureNumber(options.GetValueOrDefault("ANGULO")),
                GestureSide = options.GetValueOrDefault("LADO") is { } side ? GestureSide(side) : null,
                GestureStretch = GestureNumber(options.GetValueOrDefault("ESTIRAR")),
                GestureSpeed = GestureSpeed(options.GetValueOrDefault("VELOCIDAD")),
                GesturePivot = options.GetValueOrDefault("EJE") is { } pivot ? GesturePivot(pivot) : null
            }.ToJson();
        if (spec.Kind == ScriptBlockKind.Cinema)
        {
            var layers = CinemaLayers(options.GetValueOrDefault("CAPAS"));
            var ids = layers.Names.Select(name => characterId?.Invoke(name)).OfType<Guid>().Distinct().ToArray();
            return new BlockParameters
            {
                CinemaMode = spec.Text == "quitar" ? "quitar" : "mostrar",
                CinemaStyle = options.GetValueOrDefault("ESTILO") is { } style ? CinemaStyleName(style) : null,
                CinemaMoveMs = long.TryParse(options.GetValueOrDefault("DURACION"), out var ms) ? ms : null,
                CinemaLayers = layers.Mode == "lista" && ids.Length == 0 ? null : layers.Mode,
                CinemaCharacters = layers.Mode == "lista" && ids.Length > 0 ? ids : null
            }.ToJson();
        }
        if (spec.Kind == ScriptBlockKind.Camera)
            return new BlockParameters
            {
                CameraMode = spec.Text,
                CameraZoom = spec.Text == "general" ? null : ParseZoom(options.GetValueOrDefault("ZOOM")),
                CameraMoveMs = long.TryParse(options.GetValueOrDefault("DURACION"), out var move) ? move : null,
                CameraFocus = options.GetValueOrDefault("ENFOQUE") is { } focus ? NormalizeDirectorName(focus) == "CUERPO" ? "cuerpo" : "cara" : null,
                CameraOffsetX = int.TryParse(options.GetValueOrDefault("X"), out var cx) ? cx : null,
                CameraOffsetY = int.TryParse(options.GetValueOrDefault("Y"), out var cy) ? cy : null
            }.ToJson();
        string? Get(string key) => options.GetValueOrDefault(key);
        int? Integer(string key) => int.TryParse(Get(key), out var number) ? number : null;
        long? Long(string key) => long.TryParse(Get(key), out var number) ? number : null;
        bool? Flag(string key) => Get(key) is { } value ? SameDirectorName(value, "si") : null;
        var kind = spec.Kind;
        double? BackgroundZoom() => kind == ScriptBlockKind.Background && ParseZoom(Get("ZOOM")) is double zoom
            ? Math.Clamp(zoom, 1, BlockDefaults.BackgroundZoomMax) : null;
        var pluginSelector = DirectorPluginSelector(kind, options);
        VegasTransitionCatalog.TryResolve(pluginSelector, Get("PRESET"), out var plugin, out var pluginPreset);
        var box = BlockDefaults.VisualBox(kind);
        // Same keys as before 1.0.0-beta.4 (the editor and old drafts read them), now typed.
        return new BlockParameters
        {
            Position = spec.Position,
            FramingPreset = spec.FramingPreset,
            WaitForEnd = Flag("ESPERAR") ?? spec.Wait,
            // [FONDO] … | zoom=1.5 → a background 1.5× the frame (1920×720·1.5), unless ancho/alto say otherwise.
            VisualMaxWidth = Integer("ANCHO") ?? (BackgroundZoom() is double zx ? (int)Math.Round(box.Width * zx) : box.Width),
            VisualMaxHeight = Integer("ALTO") ?? (BackgroundZoom() is double zy ? (int)Math.Round(box.Height * zy) : box.Height),
            VisualOffsetX = Integer("X") ?? 0,
            VisualOffsetY = Integer("Y") ?? 0,
            VisualDurationMs = SceneComposer.IsVisualBlock(kind) ? Long("DURACION") : null,
            FlipHorizontal = Flag("VOLTEAR H") ?? false,
            FlipVertical = Flag("VOLTEAR V") ?? false,
            ChangeDirection = Flag("CAMBIAR DIRECCION") ?? false,
            RotationDegrees = double.TryParse(Get("ROTACION"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var rotation) ? rotation : 0,
            MotionOffsetX = Integer("ANIMAR X") ?? 0,
            MotionOffsetY = Integer("ANIMAR Y") ?? 0,
            MotionRotationDegrees = double.TryParse(Get("ANIMAR GIRO"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var spin) ? spin : 0,
            MotionDurationMs = Long("ANIMAR MS") ?? 0,
            Scale = kind == ScriptBlockKind.CharacterShow && int.TryParse(Get("ESCALA")?.TrimEnd('%').Trim(), out var percent) && percent != 100
                ? percent / 100d : null,
            AutoTrimBorders = Flag("BORDES") ?? true,
            VideoLayer = NormalizeDirectorName(Get("CAPA") ?? "guion") switch
            {
                "FONDO" or "FONDO FIJO" or "FONDO-FIJO" => "fondo-fijo",
                "SOBRE" or "ENCIMA DE TODO" => "sobre", _ => "guion"
            },
            GreenScreen = Flag("CROMA") ?? false,
            GreenScreenMode = Flag("CROMA") == true ? "on" : "off",
            KeyColor = (Get("COLOR") ?? BlockDefaults.KeyColor).TrimStart('#').ToUpperInvariant(),
            KeyTolerance = double.TryParse(Get("TOLERANCIA"), NumberStyles.Float, CultureInfo.InvariantCulture, out var tolerance)
                ? tolerance : BlockDefaults.KeyTolerance,
            AudioDurationMs = kind is ScriptBlockKind.Narration or ScriptBlockKind.SoundEffect or ScriptBlockKind.Music
                ? Long("DURACION") : null,
            AudioDurationMode = SameDirectorName(Get("MODO AUDIO") ?? "loop", "tempo") ? "tempo" : "loop",
            VolumePercent = Integer("VOLUMEN"),
            TransitionStyle = NormalizeDirectorName(spec.Text) switch
            {
                "FUNDIDO SALIDA" or "SALIDA" => "salida",
                "CAMBIO" or "CAMBIO A MITAD" => "cambio",
                "CRUCE" or "CRUCE DE CAPAS" => "cruce",
                _ => "entrada"
            },
            TransitionDurationMs = Long("DURACION") ?? BlockDefaults.TransitionDurationMs,
            TransitionTargets = DirectorTransitionTargets(Get("CAPAS") ?? "todos"),
            TransitionOverride = NormalizeDirectorName(Get("TRANSICION") ?? "heredar") switch
            {
                "CORTE" => "corte", "FUNDIDO" => "fundido", "DISOLVENTE" => "disolvente",
                "FLASH" => "flash", "BARRIDO" => "barrido", _ when plugin is not null => "plugin", _ => "heredar"
            },
            VegasEffect = NormalizeDirectorName(Get("VEGAS") ?? "ninguno") switch
            {
                "DISOLVENTE" => "disolvente", "FLASH" => "flash", "BARRIDO" => "barrido",
                _ when kind == ScriptBlockKind.Transition && plugin is not null => "plugin", _ => "ninguno"
            },
            VegasPluginId = plugin?.Id,
            VegasPluginPreset = pluginPreset is { Length: > 0 } ? pluginPreset : null
        }.ToJson();
    }

    /// <summary>Finds a registered character named in the file ("03_Bart_hola.wav",
    /// "Pinkie Pie - toma 2.wav"). Longest name wins; two different speakers means no guess.</summary>
    public static CharacterDefinition? CharacterFromFileName(string file, IReadOnlyList<CharacterDefinition> characters)
    {
        static string Tokens(string value)
        {
            // Accent/case-insensitive words; letters and digits split too ("bart01" → "BART 01").
            var words = new List<string>();
            var current = new System.Text.StringBuilder();
            foreach (var c in NormalizeDirectorName(value))
            {
                var boundary = !char.IsLetterOrDigit(c) ||
                    current.Length > 0 && char.IsDigit(c) != char.IsDigit(current[^1]);
                if (boundary && current.Length > 0) { words.Add(current.ToString()); current.Clear(); }
                if (char.IsLetterOrDigit(c)) current.Append(c);
            }
            if (current.Length > 0) words.Add(current.ToString());
            return " " + string.Join(' ', words) + " ";
        }
        var stem = Tokens(Path.GetFileNameWithoutExtension(file));
        var matches = characters.Where(x => x.Name.Trim().Length > 2 && stem.Contains(Tokens(x.Name), StringComparison.Ordinal))
            .OrderByDescending(x => x.Name.Length).ToArray();
        if (matches.Length == 0) return null;
        var best = matches[0];
        // "Bart" inside "Bart Simpson" is the same speaker; any other name means an ambiguous file.
        return matches.Skip(1).All(x => Tokens(best.Name).Contains(Tokens(x.Name), StringComparison.Ordinal)) ? best : null;
    }

    // ---------- Cinema bars ----------

    /// <summary>[CINE] mostrar/abrir (bars on) or quitar/cerrar (bars off); null when it is neither.</summary>
    public static bool? CinemaShow(string value) => NormalizeDirectorName(value) switch
    {
        "MOSTRAR" or "ABRIR" or "PONER" or "ON" or "SI" or "ENTRAR" or "ACTIVAR" => true,
        "QUITAR" or "CERRAR" or "OFF" or "NO" or "SALIR" or "DESACTIVAR" => false,
        _ => null
    };

    /// <summary>"cerrado" (wide bars, VEGAS border 1.0) or "abierto" (thin bars, border 0.56).</summary>
    public static string CinemaStyleName(string value) => NormalizeDirectorName(value) is "ABIERTO" or "FINO" or "DELGADO" or "SUAVE"
        ? "abierto" : "cerrado";

    /// <summary>capas= todos | personajes | a comma list of character names.</summary>
    public static (string Mode, IReadOnlyList<string> Names) CinemaLayers(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || NormalizeDirectorName(value) is "TODOS" or "TODO" or "TODAS") return ("todos", []);
        if (NormalizeDirectorName(value) is "PERSONAJES" or "RENDERS") return ("personajes", []);
        return ("lista", value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool CinemaOptionValid(string key, string value) => key switch
    {
        "ESTILO" => NormalizeDirectorName(value) is "CERRADO" or "ABIERTO" or "ANCHO" or "GRUESO" or "FINO" or "DELGADO" or "SUAVE",
        "DURACION" => long.TryParse(value, out var ms) && ms is >= 0 and <= BlockDefaults.CameraMoveMaxMs,
        "CAPAS" => value.Trim().Length > 0,
        _ => false
    };

    // ---------- Blur ----------

    /// <summary>What a [DESENFOQUE] target means: a group of layers, or else a character name ("personaje").</summary>
    public static string BlurTargetName(string target) => NormalizeDirectorName(target) switch
    {
        "" or "FONDO" or "FONDOS" or "ESCENARIO" => "fondo",
        "TODO" or "TODOS" or "TODAS" or "ESCENA" or "PANTALLA" => "todos",
        "PERSONAJES" or "RENDERS" => "personajes",
        "IMAGENES" or "IMAGEN" or "PROPS" or "PROP" => "imagenes",
        "VIDEOS" or "VIDEO" => "videos",
        _ => "personaje"
    };

    /// <summary>«suavizar» (range 0,02), «ligero» (0,01) or «quitar» (sharp again); null for other words.</summary>
    public static string? BlurStyle(string word) => NormalizeDirectorName(word) switch
    {
        "SUAVIZAR" or "SUAVE" or "SUAVIZADO" or "FUERTE" => "suavizar",
        "LIGERO" or "DESENFOQUE LIGERO" or "LEVE" or "POCO" => "ligero",
        "QUITAR" or "ENFOCAR" or "NITIDO" or "NORMAL" or "NADA" or "0" => "quitar",
        _ => null
    };

    private static bool BlurOptionValid(string key, string value) => key switch
    {
        "DURACION" => long.TryParse(value, out var ms) && ms is >= 0 and <= BlockDefaults.CameraMoveMaxMs,
        "VALOR" => GestureNumber(value) is > 0 and <= BlurPlan.Max,
        "ESTILO" => BlurStyle(value) is not null,
        _ => false
    };

    // ---------- Gestures ----------

    /// <summary>A number such as 8, 8.5, 8,5 or 8 % (degrees or percent). Null when it is not one.</summary>
    public static double? GestureNumber(string? value) =>
        value is not null && double.TryParse(value.Trim().TrimEnd('%', '°').Trim().Replace(',', '.'), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? number : null;

    /// <summary>velocidad=1.5, 0,75, «rapido» (1.5) or «lento» (0.7).</summary>
    public static double? GestureSpeed(string? value) => NormalizeDirectorName(value ?? "") switch
    {
        "RAPIDO" or "RAPIDA" => 1.5,
        "LENTO" or "LENTA" => 0.7,
        "NORMAL" => 1,
        _ => GestureNumber(value)
    };

    public static string GestureSide(string value) => NormalizeDirectorName(value) switch
    {
        "DERECHA" or "DER" or "D" => "derecha",
        "IZQUIERDA" or "IZQ" or "I" => "izquierda",
        _ => "auto"
    };

    public static string GesturePivot(string value) => NormalizeDirectorName(value) switch
    {
        "CINTURA" => "cintura",
        "CENTRO" => "centro",
        _ => "pies"
    };

    private static bool GestureOptionValid(string key, string value) => key switch
    {
        "ANGULO" => GestureNumber(value) is >= 0.5 and <= BlockDefaults.GestureAngleMax,
        "ESTIRAR" => GestureNumber(value) is { } stretch && stretch >= BlockDefaults.GestureStretchMin &&
            stretch <= BlockDefaults.GestureStretchMax && Math.Abs(stretch) >= 1,
        "VELOCIDAD" => GestureSpeed(value) is >= BlockDefaults.GestureSpeedMin and <= BlockDefaults.GestureSpeedMax,
        "LADO" => NormalizeDirectorName(value) is "DERECHA" or "IZQUIERDA" or "DER" or "IZQ" or "D" or "I" or "AUTO" or "CENTRO",
        "EJE" => NormalizeDirectorName(value) is "PIES" or "CINTURA" or "CENTRO",
        _ => false
    };

    // ---------- Camera ----------

    /// <summary>What a [CAMARA] target means: "general" (whole frame), "habla" (whoever speaks),
    /// "punto" (a point of the frame) or "personaje" (a character name).</summary>
    public static string CameraMode(string target) => NormalizeDirectorName(target) switch
    {
        "" or "GENERAL" or "NORMAL" or "PLANO GENERAL" or "TODO" or "TODOS" or "ABRIR" => "general",
        "HABLA" or "QUIEN HABLA" or "EL QUE HABLA" or "HABLANTE" => "habla",
        "CENTRO" or "PUNTO" => "punto",
        _ => "personaje"
    };

    /// <summary>Zoom as 1.4, 1,4, x1.4 or 140 %. Null when it is not a number.</summary>
    public static double? ParseZoom(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim().TrimStart('x', 'X').Replace(',', '.');
        var percent = text.EndsWith('%');
        if (!double.TryParse(text.TrimEnd('%').Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
            !double.IsFinite(number)) return null;
        return percent || number >= 10 ? number / 100 : number;
    }

    private static bool CameraOptionValid(string key, string value) => key switch
    {
        "ZOOM" => ParseZoom(value) is >= BlockDefaults.CameraZoomMin and <= BlockDefaults.CameraZoomMax,
        "DURACION" => long.TryParse(value, out var ms) && ms is >= 0 and <= BlockDefaults.CameraMoveMaxMs,
        "ENFOQUE" => NormalizeDirectorName(value) is "CARA" or "CUERPO",
        "X" => int.TryParse(value, out var x) && x is >= -640 and <= 640,
        "Y" => int.TryParse(value, out var y) && y is >= -360 and <= 360,
        _ => false
    };

    /// <summary>
    /// Checks the camera lines of a script in order. A [CAMARA] on a character that is not on screen is
    /// fine if the character appears before the next [CAMARA] (the camera goes to them then); if they
    /// never appear in that stretch it is an error. A [GESTO] on a character that is not on screen is an
    /// error. Returns, by spec index, (message, isError).
    /// </summary>
    public static IReadOnlyDictionary<int, (string Message, bool IsError)> CameraChecks(IReadOnlyList<DirectorSpec?> specs)
    {
        var result = new Dictionary<int, (string, bool)>();
        var onScreen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < specs.Count; i++)
        {
            if (specs[i] is not { } spec) continue;
            var name = NormalizeDirectorName(spec.CharacterName);
            if (spec.Kind == ScriptBlockKind.CharacterShow) onScreen.Add(name);
            else if (spec.Kind == ScriptBlockKind.CharacterHide) onScreen.Remove(name);
            else if (spec.Kind == ScriptBlockKind.Gesture && spec.Text == "personaje" && !onScreen.Contains(name))
                result[i] = ($"{spec.CharacterName} no está en pantalla: el gesto necesita un [MOSTRAR] antes", true);
            else if (spec.Kind == ScriptBlockKind.Camera && spec.Text == "personaje" && !onScreen.Contains(name))
            {
                var appears = specs.Skip(i + 1).TakeWhile(x => x?.Kind != ScriptBlockKind.Camera)
                    .Any(x => x?.Kind == ScriptBlockKind.CharacterShow && NormalizeDirectorName(x.CharacterName) == name);
                result[i] = appears
                    ? ($"{spec.CharacterName} aún no está en pantalla: la cámara irá a él/ella cuando aparezca", false)
                    : ($"{spec.CharacterName} no está en pantalla mientras dura esta cámara (usa [MOSTRAR] antes)", true);
            }
        }
        return result;
    }

    /// <summary>Expands catalog aliases (A12) the AI wrote in resource positions to asset IDs.</summary>
    public static string ResolveAiAssetAliases(string line, IReadOnlyDictionary<string, Guid> aliases)
    {
        // Only resource positions can be expanded: never rewrite dialogue, descriptions or plugin names.
        string Replace(Match match) => aliases.TryGetValue(match.Groups["alias"].Value, out var id)
            ? match.Groups["head"].Value + id.ToString("D") : match.Value;
        line = Regex.Replace(line,
            @"^(?<head>\[(?:FONDO|IMAGEN|VIDEO|MUSICA|SFX|MOSTRAR)\]\s*)(?<alias>A\d+)(?=\s*(?:\||$))",
            Replace, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return Regex.Replace(line,
            @"^(?<head>\[(?:MOSTRAR|OCULTAR)\]\s*[^|\r\n]+\|\s*)(?<alias>A\d+)(?=\s*(?:\||$))",
            Replace, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
