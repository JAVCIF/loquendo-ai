using System.Globalization;
using System.Text.Json;

namespace LoquendoAI.Infrastructure.Director;

/// <summary>What the model may reference in this draft. Every list becomes an enum in the
/// schema, so constrained decoding (Ollama grammar / Gemini JSON schema) cannot produce a
/// render of the wrong character, a SFX pointing at a background, or an invented ID.</summary>
public sealed record DirectorSchemaContext(
    IReadOnlyList<string> BackgroundRefs,
    IReadOnlyList<string> ImageRefs,
    IReadOnlyList<string> VideoRefs,
    IReadOnlyList<string> MusicRefs,
    IReadOnlyList<string> SfxRefs,
    IReadOnlyDictionary<string, IReadOnlyList<string>> RendersByCharacter,
    IReadOnlyList<string> Speakers,
    IReadOnlyList<string> Characters,
    IReadOnlyList<string> BlockRefs,
    bool Recorded,
    IReadOnlyList<string>? PluginRefs = null);

/// <summary>A VEGAS catalog transition offered to the model as T1, T2…</summary>
public sealed record DirectorPluginRef(string Id, string Preset);

/// <summary>
/// Typed Director output: {"steps":[{ "accion": "...", ...fields }]}. Each action is one
/// anyOf branch with closed enums and numeric ranges, and <see cref="ToLine"/> turns it into
/// the existing text instruction so validation, the review grid and "Copiar borrador" keep
/// working unchanged. The syntax no longer has to be taught in the prompt.
/// </summary>
public static class DirectorAiSchema
{
    public const string ActionField = "accion";
    public static readonly string[] Transitions = ["heredar", "corte", "fundido", "disolvente", "flash", "barrido"];
    public static readonly string[] Positions = ["izquierda", "derecha", "centro", "auto"];
    public static readonly string[] Framings = ["auto", "original", "cuerpo entero", "medio cuerpo", "primer plano"];
    public static readonly string[] SceneTransitions = ["entrada", "salida", "cambio", "cruce"];
    public static readonly string[] VegasEffects = ["ninguno", "disolvente", "flash", "barrido"];
    public static readonly string[] Layers = ["todos", "fondo", "personajes", "imagenes", "videos"];
    public static readonly string[] VideoLayers = ["guion", "fondo", "sobre"];
    /// <summary>Camera targets besides a character name.</summary>
    public const string CameraGeneral = "general";
    public const string CameraSpeaker = "quien habla";
    public static readonly string[] CameraFocus = ["cara", "cuerpo"];
    /// <summary>Gesture moves offered to the model («quitar» ends the «quien habla» gestures).</summary>
    public static readonly string[] GestureMoves = ["balanceo", "rebote", "balanceo+rebote", "quitar"];
    /// <summary>Blur targets besides a character name, and its styles («quitar» = sharp again).</summary>
    public static readonly string[] BlurGroups = ["fondo", "todos", "personajes"];
    public static readonly string[] BlurStyles = ["suavizar", "ligero", "quitar"];
    /// <summary>
    /// Renders without a registered character (extras, NPCs) travel under this key of RendersByCharacter (1.4.1):
    /// «mostrar» with personaje "NPC" shows one, «ocultar_npc» hides it again by the same render. They are not
    /// camera, gesture or blur targets (those follow a character).
    /// </summary>
    public const string NpcKey = "NPC";

    private static bool IsNpc(string key) => key.Equals(NpcKey, StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, object> Str(IEnumerable<string> values) =>
        new() { ["type"] = "string", ["enum"] = values.ToArray() };

    private static Dictionary<string, object> Int(int min, int max) =>
        new() { ["type"] = "integer", ["minimum"] = min, ["maximum"] = max };

    private static Dictionary<string, object> Action(string name, params (string Key, object Schema)[] fields)
    {
        // "accion" goes first so the model commits to the action type before its details.
        var properties = new Dictionary<string, object> { [ActionField] = Str([name]) };
        foreach (var (key, schema) in fields) properties[key] = schema;
        return new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = properties.Keys.ToArray(),
            ["additionalProperties"] = false
        };
    }

    /// <summary>
    /// A reference field without an enum (compact schema): the app checks it with <see cref="CheckRefs"/>.
    /// </summary>
    private static Dictionary<string, object> Ref(string what) =>
        new() { ["type"] = "string", ["description"] = what };

    /// <param name="compact">
    /// Claude compiles the schema into a grammar with a size limit («The compiled grammar is too large»).
    /// Every catalog reference as an enum, times one «mostrar» branch per character, exceeds it with a
    /// normal library. The compact schema keeps the action branches and the short enums (options,
    /// characters, transitions, recorded blocks) but writes asset references as plain strings; the
    /// catalog in the prompt lists them and <see cref="CheckRefs"/> rejects any reference that does not fit.
    /// </param>
    public static object Build(DirectorSchemaContext context, bool compact = false)
    {
        var actions = new List<object>();
        Dictionary<string, object> Refs(IReadOnlyList<string> refs, string what) =>
            compact ? Ref($"Referencia A… del catálogo con uso={what}.") : Str(refs);
        // Catalog transitions (T1…) extend the built-in choices: on a visual they become
        // transicion=plugin, on a layer crossfade they become the VEGAS effect.
        var plugins = context.PluginRefs ?? [];
        var transitions = Transitions.Concat(plugins).ToArray();
        var vegasEffects = VegasEffects.Concat(plugins).ToArray();
        if (context.BackgroundRefs.Count > 0)
            actions.Add(Action("fondo", ("recurso", Refs(context.BackgroundRefs, "fondo")), ("transicion", Str(transitions))));
        var withRenders = context.RendersByCharacter.Where(x => x.Value.Count > 0).ToArray();
        var characterRenders = withRenders.Where(x => !IsNpc(x.Key)).ToArray();
        var npcRenders = withRenders.FirstOrDefault(x => IsNpc(x.Key)).Value ?? [];
        if (compact && withRenders.Length > 0)
            actions.Add(Action("mostrar", ("personaje", Str(withRenders.Select(x => x.Key))),
                ("recurso", Ref("Referencia A… de un render de ESE personaje (lista «Renders por personaje»).")),
                ("posicion", Str(Positions)), ("encuadre", Str(Framings)), ("transicion", Str(transitions)),
                ("animar_x", Int(-640, 640)), ("animar_y", Int(-360, 360)), ("animar_ms", Int(0, 10000))));
        // One branch per character: its enum only lists that character's renders.
        else foreach (var (character, renders) in withRenders)
            actions.Add(Action("mostrar", ("personaje", Str([character])), ("recurso", Str(renders)),
                ("posicion", Str(Positions)), ("encuadre", Str(Framings)), ("transicion", Str(transitions)),
                ("animar_x", Int(-640, 640)), ("animar_y", Int(-360, 360)), ("animar_ms", Int(0, 10000))));
        if (context.Characters.Count > 0)
            actions.Add(Action("ocultar", ("personaje", Str(context.Characters))));
        if (npcRenders.Count > 0)
            actions.Add(Action("ocultar_npc", ("recurso", compact
                ? Ref("Referencia A… del render NPC que se mostró con «mostrar» personaje NPC.") : Str(npcRenders))));
        if (!context.Recorded && context.Speakers.Count > 0)
            actions.Add(Action("dialogo", ("personaje", Str(context.Speakers)), ("texto", new Dictionary<string, object> { ["type"] = "string" })));
        if (context.ImageRefs.Count > 0)
            actions.Add(Action("imagen", ("recurso", Refs(context.ImageRefs, "imagen")), ("duracion_ms", Int(0, 20000)),
                ("transicion", Str(transitions))));
        if (context.VideoRefs.Count > 0)
            actions.Add(Action("video", ("recurso", Refs(context.VideoRefs, "video")), ("capa", Str(VideoLayers)),
                ("duracion_ms", Int(0, 60000)), ("volumen", Int(0, 200))));
        if (context.MusicRefs.Count > 0)
            actions.Add(Action("musica", ("recurso", Refs(context.MusicRefs, "musica")), ("volumen", Int(0, 200))));
        if (context.SfxRefs.Count > 0)
            actions.Add(Action("sfx", ("recurso", Refs(context.SfxRefs, "sfx")), ("volumen", Int(0, 200)),
                ("esperar", new Dictionary<string, object> { ["type"] = "boolean" })));
        actions.Add(Action("pausa", ("ms", Int(50, 10000))));
        // Cinematic black bars (VEGAS Cookie Cutter): on or off, thick or thin, how long they take.
        actions.Add(Action("cine", ("modo", Str(["mostrar", "quitar"])), ("estilo", Str(["cerrado", "abierto"])),
            ("duracion_ms", Int(0, 3000))));
        // Camera: a character on screen, whoever speaks next, or back to the whole frame.
        if (characterRenders.Length > 0)
            actions.Add(Action("camara",
                ("objetivo", Str(characterRenders.Select(x => x.Key).Append(CameraGeneral).Append(CameraSpeaker))),
                ("zoom", Int(110, 250)), ("duracion_ms", Int(0, 3000)), ("enfoque", Str(CameraFocus))));
        // Gestures (VEGAS Pan/Crop around the feet): one character, or whoever speaks from now on.
        if (characterRenders.Length > 0)
            actions.Add(Action("gesto",
                ("personaje", Str(characterRenders.Select(x => x.Key).Append(CameraSpeaker))),
                ("movimiento", Str(GestureMoves))));
        // Blur (VEGAS Gaussian blur): background, everything, the renders or one character; blur or sharpen again.
        actions.Add(Action("desenfoque",
            ("objetivo", Str(BlurGroups.Concat(characterRenders.Select(x => x.Key)))),
            ("estilo", Str(BlurStyles)), ("duracion_ms", Int(0, 3000))));
        actions.Add(Action("transicion", ("estilo", Str(SceneTransitions)), ("duracion_ms", Int(80, 10000)),
            ("vegas", Str(vegasEffects)), ("capas", Str(Layers))));
        if (context.Recorded && context.BlockRefs.Count > 0)
            actions.Add(Action("conservar", ("bloque", Str(context.BlockRefs))));

        return new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["steps"] = new Dictionary<string, object>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object> { ["anyOf"] = actions }
                }
            },
            ["required"] = new[] { "steps" },
            ["additionalProperties"] = false
        };
    }

    /// <summary>Converts one typed action into the text instruction understood by
    /// ParseDirectorPrompt. Returns the recorded block reference (B1…) for "conservar".
    /// <paramref name="plugins"/> maps T1… to the catalog transition and preset offered.</summary>
    public static (string Line, string BlockRef) ToLine(JsonElement step,
        IReadOnlyDictionary<string, DirectorPluginRef>? plugins = null)
    {
        if (step.ValueKind != JsonValueKind.Object) return ("[COMENTARIO] acción no válida", "");
        string S(string key) => step.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? OneLine(value.GetString()) : "";
        long N(string key) => step.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt64(out var number) ? number : 0;
        bool B(string key) => step.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;
        string Num(long value) => value.ToString(CultureInfo.InvariantCulture);
        DirectorPluginRef? Plugin(string value) =>
            plugins is not null && plugins.TryGetValue(value, out var plugin) ? plugin : null;
        string PluginOptions(DirectorPluginRef plugin) => $" | vegas={plugin.Id} | preset={plugin.Preset}";
        string Transition(string value) => value.Length == 0 || value == "heredar" ? ""
            : Plugin(value) is { } plugin ? " | transicion=plugin" + PluginOptions(plugin)
            // An unknown T-ref cannot come from constrained output; drop it instead of failing the row.
            : Transitions.Contains(value) ? " | transicion=" + value : "";

        switch (S(ActionField))
        {
            case "fondo":
                return ($"[FONDO] {S("recurso")}{Transition(S("transicion"))}", "");
            case "mostrar":
            {
                // "auto" is both a framing and a position word; the parser takes the first "auto"
                // as the position, so an automatic framing is omitted (it is the default anyway).
                var framing = Fallback(S("encuadre"), "auto");
                var line = $"[MOSTRAR] {S("personaje")} | {S("recurso")}" +
                    (framing == "auto" ? "" : " | " + framing) + $" | {Fallback(S("posicion"), "auto")}" +
                    Transition(S("transicion"));
                var dx = N("animar_x"); var dy = N("animar_y"); var ms = N("animar_ms");
                if (dx != 0) line += " | animar x=" + Num(dx);
                if (dy != 0) line += " | animar y=" + Num(dy);
                if ((dx != 0 || dy != 0) && ms > 0) line += " | animar ms=" + Num(ms);
                return (line, "");
            }
            case "ocultar":
                return ($"[OCULTAR] {S("personaje")}", "");
            case "ocultar_npc":
                return ($"[OCULTAR] {NpcKey} | {S("recurso")}", "");
            case "dialogo":
                return ($"{S("personaje")}: {S("texto")}", "");
            case "imagen":
            {
                var ms = N("duracion_ms");
                return ($"[IMAGEN] {S("recurso")}" + (ms > 0 ? " | duracion=" + Num(ms) : "") + Transition(S("transicion")), "");
            }
            case "video":
            {
                var ms = N("duracion_ms");
                return ($"[VIDEO] {S("recurso")} | capa={Fallback(S("capa"), "guion")}" + (ms > 0 ? " | duracion=" + Num(ms) : "") +
                    $" | volumen={Num(Math.Clamp(N("volumen"), 0, 200))}", "");
            }
            case "musica":
                return ($"[MUSICA] {S("recurso")} | volumen={Num(Math.Clamp(N("volumen"), 0, 200))}", "");
            case "sfx":
                return ($"[SFX] {S("recurso")} | esperar={(B("esperar") ? "si" : "no")} | volumen={Num(Math.Clamp(N("volumen"), 0, 200))}", "");
            case "pausa":
                return ($"[PAUSA] {Num(Math.Clamp(N("ms"), 1, 86_400_000))}", "");
            case "transicion":
            {
                var style = Fallback(S("estilo"), "cruce");
                var line = $"[TRANSICION] {style} | duracion={Num(Math.Clamp(N("duracion_ms"), 80, 10_000))}";
                if (style == "cruce")
                {
                    var vegas = S("vegas");
                    if (Plugin(vegas) is { } plugin) line += PluginOptions(plugin);
                    else if (vegas.Length > 0 && vegas != "ninguno" && VegasEffects.Contains(vegas)) line += " | vegas=" + vegas;
                    var layers = S("capas");
                    if (layers.Length > 0 && layers != "todos") line += " | capas=" + layers;
                }
                return (line, "");
            }
            case "conservar":
                return ("", S("bloque"));
            case "cine":
            {
                var ms = Num(Math.Clamp(N("duracion_ms"), 0, 3000));
                return S("modo") == "quitar"
                    ? ($"[CINE] quitar | duracion={ms}", "")
                    : ($"[CINE] mostrar | estilo={Fallback(S("estilo"), "cerrado")} | duracion={ms}", "");
            }
            case "camara":
            {
                var target = S("objetivo");
                var ramp = Num(Math.Clamp(N("duracion_ms"), 0, 3000));
                if (target.Length == 0 || target.Equals(CameraGeneral, StringComparison.OrdinalIgnoreCase))
                    return ($"[CAMARA] general | duracion={ramp}", "");
                var zoom = (Math.Clamp(N("zoom"), 110, 250) / 100d).ToString("0.##", CultureInfo.InvariantCulture);
                var focus = Fallback(S("enfoque"), "cara");
                var who = target.Equals(CameraSpeaker, StringComparison.OrdinalIgnoreCase) ? "habla" : target;
                return ($"[CAMARA] {who} | zoom={zoom} | duracion={ramp} | enfoque={focus}", "");
            }
            case "desenfoque":
            {
                var target = Fallback(S("objetivo"), "fondo");
                var style = BlurStyles.Contains(S("estilo")) ? S("estilo") : "suavizar";
                return ($"[DESENFOQUE] {target} | {style} | duracion={Num(Math.Clamp(N("duracion_ms"), 0, 3000))}", "");
            }
            case "gesto":
            {
                var move = Fallback(S("movimiento"), "balanceo");
                if (move == "quitar") return ("[GESTO] habla | quitar", "");
                var who = S("personaje");
                who = who.Length == 0 || who.Equals(CameraSpeaker, StringComparison.OrdinalIgnoreCase) ? "habla" : who;
                return ($"[GESTO] {who} | {(GestureMoves.Contains(move) ? move : "balanceo")}", "");
            }
            default:
                return ("[COMENTARIO] acción desconocida: " + S(ActionField), "");
        }
    }

    /// <summary>
    /// Whether the references of one typed action are among those offered for that use (always checked;
    /// essential with the compact schema, where they are plain strings). Null when they fit.
    /// </summary>
    public static string? CheckRefs(JsonElement step, DirectorSchemaContext context)
    {
        if (step.ValueKind != JsonValueKind.Object) return "Acción no válida";
        string S(string key) => step.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? (value.GetString() ?? "").Trim() : "";
        static bool In(IEnumerable<string> list, string value) => list.Contains(value, StringComparer.OrdinalIgnoreCase);
        string? Resource(IReadOnlyList<string> offered, string use) =>
            In(offered, S("recurso")) ? null : $"«{S("recurso")}» no es un recurso de tipo {use} del catálogo enviado";
        switch (S(ActionField).ToLowerInvariant())
        {
            case "fondo": return Resource(context.BackgroundRefs, "fondo");
            case "imagen": return Resource(context.ImageRefs, "imagen");
            case "video": return Resource(context.VideoRefs, "vídeo");
            case "musica": return Resource(context.MusicRefs, "música");
            case "sfx": return Resource(context.SfxRefs, "efecto");
            case "mostrar":
            {
                var character = context.RendersByCharacter.Keys.FirstOrDefault(x => x.Equals(S("personaje"), StringComparison.OrdinalIgnoreCase));
                if (character is null) return $"«{S("personaje")}» no tiene renders en el catálogo enviado";
                return In(context.RendersByCharacter[character], S("recurso")) ? null
                    : $"«{S("recurso")}» no es un render de {character} (la IA eligió uno que no le corresponde)";
            }
            case "ocultar":
                return In(context.Characters, S("personaje")) ? null : $"«{S("personaje")}» no es un personaje de la escena";
            case "ocultar_npc":
                return context.RendersByCharacter.FirstOrDefault(x => IsNpc(x.Key)).Value is { } npc && In(npc, S("recurso")) ? null
                    : $"«{S("recurso")}» no es un render NPC del catálogo enviado";
            case "dialogo":
                return context.Recorded || In(context.Speakers, S("personaje")) ? null : $"«{S("personaje")}» no tiene voz configurada";
            case "camara":
            {
                var target = S("objetivo");
                return target.Length == 0 || In([CameraGeneral, CameraSpeaker], target) || In(CharacterKeys(context), target)
                    ? null : $"«{target}» no es un objetivo de cámara válido";
            }
            case "desenfoque":
            {
                var target = S("objetivo");
                return target.Length == 0 || In(BlurGroups, target) || In(CharacterKeys(context), target)
                    ? null : $"«{target}» no es un objetivo de desenfoque válido";
            }
            case "gesto":
            {
                var who = S("personaje");
                return who.Length == 0 || In([CameraSpeaker], who) || In(CharacterKeys(context), who)
                    ? null : $"«{who}» no es un personaje con renders para el gesto";
            }
            default: return null;
        }
    }

    private static IEnumerable<string> CharacterKeys(DirectorSchemaContext context) =>
        context.RendersByCharacter.Keys.Where(x => !IsNpc(x));

    private static string Fallback(string value, string fallback) => value.Length == 0 ? fallback : value;

    // Instructions are single-line and "|" separates options: keep model text from breaking either.
    private static string OneLine(string? value) =>
        (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace('|', '/').Trim();
}
