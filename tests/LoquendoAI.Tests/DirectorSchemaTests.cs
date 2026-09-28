using System.Text.Json;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;
using LoquendoAI.Infrastructure.Director;

namespace LoquendoAI.Tests;

/// <summary>The typed schema the AI Director answers with, and its translation to Director lines.</summary>
internal static class DirectorSchemaTests
{
    private static readonly Dictionary<string, Guid> Ids =
        Enumerable.Range(1, 12).ToDictionary(i => $"A{i}", _ => Guid.NewGuid(), StringComparer.OrdinalIgnoreCase);

    private static DirectorSchemaContext Context(bool recorded = false, IReadOnlyList<string>? plugins = null) =>
        new(["A1", "A2"], ["A3", "A2"], ["A4"], ["A5", "A6"], ["A7", "A6"],
            new Dictionary<string, IReadOnlyList<string>> { ["Bart"] = ["A8", "A9"], ["Fluttershy"] = ["A10"] },
            ["Bart", "Fluttershy"], ["Bart", "Fluttershy"], ["B1", "B2"], recorded, plugins);

    private static string[] Actions(object schema)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(schema));
        return document.RootElement.GetProperty("properties").GetProperty("steps").GetProperty("items").GetProperty("anyOf")
            .EnumerateArray().Select(branch => branch.GetProperty("properties").GetProperty("accion").GetProperty("enum")[0].GetString()!)
            .ToArray();
    }

    [Test("Esquema: una rama «mostrar» por personaje; «conservar» solo con tomas grabadas")]
    public static void Branches()
    {
        var story = Actions(DirectorAiSchema.Build(Context()));
        Assert.Equal(2, story.Count(x => x == "mostrar"), "ramas mostrar");
        Assert.False(story.Contains("conservar"), "conservar en modo historia");
        Assert.True(Actions(DirectorAiSchema.Build(Context(recorded: true))).Contains("conservar"), "conservar con tomas grabadas");
        var json = JsonSerializer.Serialize(DirectorAiSchema.Build(Context()));
        using var document = JsonDocument.Parse(json);
        var first = document.RootElement.GetProperty("properties").GetProperty("steps").GetProperty("items")
            .GetProperty("anyOf")[0].GetProperty("properties").EnumerateObject().First().Name;
        Assert.Equal(DirectorAiSchema.ActionField, first, "la acción va primero");
    }

    private static DirectorSpec Line(string stepJson, ScriptBlockKind kind, IReadOnlyDictionary<string, DirectorPluginRef>? plugins = null)
    {
        using var document = JsonDocument.Parse(stepJson);
        var (line, _) = DirectorAiSchema.ToLine(document.RootElement, plugins);
        var specs = DirectorScript.ParseDirectorPrompt(DirectorScript.ResolveAiAssetAliases(line, Ids));
        Assert.Equal(1, specs.Count, "líneas de " + line);
        Assert.Equal(null, specs[0].Error, "error de " + line);
        Assert.Equal(kind, specs[0].Kind, "tipo de " + line);
        return specs[0];
    }

    [Test("Cada acción del esquema produce una línea válida para el Director")]
    public static void StepsToLines()
    {
        Assert.Equal(Ids["A1"].ToString("D"), Line("""{"accion":"fondo","recurso":"A1","transicion":"corte"}""", ScriptBlockKind.Background).ResourceQuery);
        var show = Line("""{"accion":"mostrar","personaje":"Fluttershy","recurso":"A10","posicion":"derecha","encuadre":"medio cuerpo","transicion":"fundido","animar_x":-100,"animar_y":0,"animar_ms":2000}""",
            ScriptBlockKind.CharacterShow);
        Assert.Equal("derecha", show.Position, "posición");
        Assert.Equal("medio", show.FramingPreset, "encuadre");
        Assert.Equal("-100", show.Options!["ANIMAR X"], "animación");
        var still = Line("""{"accion":"mostrar","personaje":"Bart","recurso":"A9","posicion":"centro","encuadre":"cuerpo entero","transicion":"heredar","animar_x":0,"animar_y":0,"animar_ms":1500}""",
            ScriptBlockKind.CharacterShow);
        Assert.Equal(0, still.Options!.Count, "sin desplazamiento no se escribe una duración de animación suelta");
        var dialogue = Line("""{"accion":"dialogo","personaje":"Bart","texto":"¡Mira esto! Vamos | a cocinar\nla pizza."}""", ScriptBlockKind.Dialogue);
        Assert.False(dialogue.Text.Contains('|') || dialogue.Text.Contains('\n'), "el texto no rompe la línea");
        Assert.Equal("200", Line("""{"accion":"sfx","recurso":"A6","volumen":250,"esperar":true}""", ScriptBlockKind.SoundEffect).Options!["VOLUMEN"], "volumen limitado");
        Assert.Equal(800, Line("""{"accion":"pausa","ms":800}""", ScriptBlockKind.Pause).PauseMs, "pausa");
        Assert.Equal("80", Line("""{"accion":"transicion","estilo":"cambio","duracion_ms":20,"vegas":"ninguno","capas":"todos"}""",
            ScriptBlockKind.Transition).Options!["DURACION"], "duración mínima");
        Assert.Equal(1, Line("""{"accion":"transicion","estilo":"entrada","duracion_ms":600,"vegas":"flash","capas":"fondo"}""",
            ScriptBlockKind.Transition).Options!.Count, "entrada no lleva efecto ni capas");
        Line("""{"accion":"video","recurso":"A4","capa":"sobre","duracion_ms":3000,"volumen":65}""", ScriptBlockKind.Video);
        Line("""{"accion":"ocultar","personaje":"Bart"}""", ScriptBlockKind.CharacterHide);

        using var keep = JsonDocument.Parse("""{"accion":"conservar","bloque":"B2"}""");
        Assert.Equal(("", "B2"), DirectorAiSchema.ToLine(keep.RootElement), "conservar devuelve la toma");
    }

    [Test("Transiciones del catálogo de VEGAS ofrecidas como T1, T2…")]
    public static void PluginRefs()
    {
        var options = VegasTransitionCatalog.All.Where(x => x.Presets.Length > 0).Take(3).ToArray();
        var map = options.Select((o, i) => ($"T{i + 1}", new DirectorPluginRef(o.Id, o.Presets[0])))
            .ToDictionary(x => x.Item1, x => x.Item2, StringComparer.OrdinalIgnoreCase);
        var json = JsonSerializer.Serialize(DirectorAiSchema.Build(Context(plugins: map.Keys.ToArray())));
        Assert.Contains("\"T3\"", json, "enum de transiciones");
        var background = Line("""{"accion":"fondo","recurso":"A1","transicion":"T1"}""", ScriptBlockKind.Background, map);
        Assert.Equal(options[0].Id, background.Options!["VEGAS"], "plugin del fondo");
        Assert.Equal("plugin", background.Options!["TRANSICION"], "modo plugin");
        var cross = Line("""{"accion":"transicion","estilo":"cruce","duracion_ms":700,"vegas":"T2","capas":"todos"}""", ScriptBlockKind.Transition, map);
        Assert.True(VegasTransitionCatalog.TryResolve(cross.Options!["VEGAS"], cross.Options!["PRESET"], out var plugin, out _) &&
                    plugin!.Id == options[1].Id, "el cruce resuelve el plugin y su preset");
        var unknown = Line("""{"accion":"fondo","recurso":"A1","transicion":"T99"}""", ScriptBlockKind.Background, map);
        Assert.False(unknown.Options!.ContainsKey("VEGAS"), "una referencia inexistente no inventa un plugin");
    }
}
