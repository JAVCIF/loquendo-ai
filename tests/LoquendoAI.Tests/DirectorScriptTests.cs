using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;
using LoquendoAI.Infrastructure.Director;

namespace LoquendoAI.Tests;

/// <summary>The Director language without AI: parsing, validation and the block parameters it produces.</summary>
internal static class DirectorScriptTests
{
    private static DirectorSpec One(string line)
    {
        var specs = DirectorScript.ParseDirectorPrompt(line);
        Assert.Equal(1, specs.Count, $"líneas interpretadas de «{line}»");
        return specs[0];
    }

    [Test("Fondo con opciones: recurso, desplazamiento y transición")]
    public static void Background()
    {
        var spec = One("[FONDO] cocina | x=40 | transicion=corte");
        Assert.Equal(ScriptBlockKind.Background, spec.Kind);
        Assert.Equal(null, spec.Error, "error");
        Assert.Equal("cocina", spec.ResourceQuery, "recurso");
        Assert.Equal("40", spec.Options!["X"], "x");
        Assert.Equal("corte", spec.Options!["TRANSICION"], "transición");
    }

    [Test("Mostrar personaje: render, posición y encuadre")]
    public static void ShowCharacter()
    {
        var spec = One("[MOSTRAR] Bart | feliz | izquierda | medio cuerpo");
        Assert.Equal(ScriptBlockKind.CharacterShow, spec.Kind);
        Assert.Equal(null, spec.Error, "error");
        Assert.Equal("Bart", spec.CharacterName, "personaje");
        Assert.Equal("feliz", spec.ResourceQuery, "render");
        Assert.Equal("izquierda", spec.Position, "posición");
        Assert.Equal("medio", spec.FramingPreset, "encuadre");
        var defaults = One("[MOSTRAR] Bart");
        Assert.Equal("auto", defaults.Position, "posición por defecto");
        Assert.Equal("Bart", defaults.ResourceQuery, "sin render: busca por el nombre");
    }

    [Test("Diálogo con corchetes y en texto libre («Bart dice: …»)")]
    public static void Dialogue()
    {
        var tagged = One("[DIÁLOGO] Bart: ¡Hola, Fluttershy!");
        Assert.Equal(ScriptBlockKind.Dialogue, tagged.Kind);
        Assert.Equal("Bart", tagged.CharacterName, "personaje");
        Assert.Equal("¡Hola, Fluttershy!", tagged.Text, "texto");
        var free = One("Fluttershy dice: hola");
        Assert.Equal("Fluttershy", free.CharacterName, "personaje en texto libre");
        Assert.Equal("hola", free.Text, "texto libre");
        Assert.True(One("[DIALOGO] sin dos puntos").Error is not null, "diálogo sin «:» debe marcar error");
    }

    [Test("Mayúsculas y tildes no importan en instrucciones ni opciones")]
    public static void AccentInsensitive()
    {
        var spec = One("[música] tema | VOLUMEN=30 | modo audio=tempo | duración=5000");
        Assert.Equal(ScriptBlockKind.Music, spec.Kind);
        Assert.Equal(null, spec.Error, "error");
        Assert.Equal("30", spec.Options!["VOLUMEN"], "volumen");
        Assert.Equal("5000", spec.Options!["DURACION"], "duración");
        Assert.True(DirectorScript.SameDirectorName("Canción", "CANCION"), "comparación sin tildes");
    }

    [Test("Errores: instrucción desconocida, pausa inválida, opción no válida o repetida")]
    public static void Errors()
    {
        Assert.Contains("desconocida", One("[BAILAR] Bart").Error ?? "");
        Assert.Contains("Pausa inválida", One("[PAUSA] mucho").Error ?? "");
        Assert.Equal(1500, One("[PAUSA] 1500ms").PauseMs, "pausa en ms");
        Assert.Contains("inválida", One("[FONDO] cocina | ancho=5000").Error ?? "", "ancho fuera de rango");
        Assert.Contains("inválida", One("[SFX] golpe | capa=sobre").Error ?? "", "opción de vídeo en un SFX");
        Assert.Contains("duplicada", One("[FONDO] cocina | x=1 | x=2").Error ?? "");
        Assert.Contains("Cruce", One("[TRANSICION] entrada | vegas=flash").Error ?? "", "VEGAS solo en cruce");
        Assert.Contains("transicion=plugin", One("[FONDO] cocina | vegas=flash").Error ?? "");
        Assert.Equal(0, DirectorScript.ParseDirectorPrompt("# solo un comentario\n\n").Count, "comentarios y vacías no generan bloques");
    }

    [Test("Transición de cruce con capas y efecto VEGAS")]
    public static void CrossTransition()
    {
        var spec = One("[TRANSICIÓN] cruce | duracion=700 | vegas=flash | capas=personajes");
        Assert.Equal(null, spec.Error, "error");
        var parameters = BlockParameters.Parse(DirectorScript.DirectorParameters(spec));
        Assert.Equal(("cruce", 700L), parameters.Transition, "estilo y duración");
        Assert.Equal(SceneComposer.TargetCharacter, parameters.TransitionTargets, "capas");
        Assert.Equal("flash", parameters.VegasEffect, "efecto");
        Assert.Contains("Transición válida", One("[TRANSICION] giro loco").Error ?? "");
    }

    [Test("Parámetros del bloque: valores escritos y valores por defecto de un solo sitio")]
    public static void Parameters()
    {
        var image = BlockParameters.Parse(DirectorScript.DirectorParameters(
            One("[IMAGEN] logo | ancho=300 | duracion=2000 | transicion=fundido | giro=15")));
        Assert.Equal(300, image.VisualMaxWidth, "ancho escrito");
        Assert.Equal(BlockDefaults.VisualBox(ScriptBlockKind.Image).Height, image.VisualMaxHeight, "alto por defecto");
        Assert.Equal(2000L, image.VisualDuration(ScriptBlockKind.Image), "duración visual");
        Assert.Equal("fundido", image.VisualTransition(ScriptBlockKind.Image), "transición");
        Assert.Equal(15d, image.RotationDegrees, "giro (alias de rotación)");

        var video = BlockParameters.Parse(DirectorScript.DirectorParameters(
            One("[VIDEO] explosion | capa=fondo fijo | croma=si | color=#00ff00 | volumen=65")));
        var (layer, green, key, tolerance) = video.Video;
        Assert.Equal("fondo-fijo", video.VideoLayer, "capa guardada");
        Assert.Equal("fondo", layer, "capa efectiva: fondo fijo");
        Assert.True(green, "croma");
        Assert.Equal("00FF00", key, "color normalizado");
        Assert.Equal(BlockDefaults.KeyTolerance, tolerance, "tolerancia por defecto");
        Assert.Equal(65, video.Volume(ScriptBlockKind.Video), "volumen");

        var music = BlockParameters.Parse(DirectorScript.DirectorParameters(One("[MUSICA] tema")));
        Assert.Equal(BlockDefaults.VolumePercent(ScriptBlockKind.Music), music.Volume(ScriptBlockKind.Music), "volumen de música por defecto");
    }

    [Test("Plugin de VEGAS del catálogo como transición de un recurso")]
    public static void PluginTransition()
    {
        var plugin = VegasTransitionCatalog.All.First(x => x.Presets.Length > 0);
        var spec = One($"[FONDO] cocina | transicion=plugin | vegas={plugin.Id} | preset={plugin.Presets[0]}");
        Assert.Equal(null, spec.Error, "error");
        var parameters = BlockParameters.Parse(DirectorScript.DirectorParameters(spec));
        Assert.Equal("plugin", parameters.TransitionOverride, "modo");
        Assert.Equal(plugin.Id, parameters.VegasPluginId, "ID");
        Assert.True(One("[FONDO] cocina | transicion=plugin | vegas={no-existe}").Error is not null, "plugin inexistente");
        Assert.Contains("Falta vegas", One("[FONDO] cocina | transicion=plugin").Error ?? "", "plugin sin nombre");
    }

    [Test("Alias A1… de la IA: solo se sustituyen en la posición del recurso")]
    public static void AiAliases()
    {
        var id = Guid.NewGuid();
        var aliases = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase) { ["A1"] = id };
        Assert.Equal($"[FONDO] {id:D} | transicion=corte", DirectorScript.ResolveAiAssetAliases("[FONDO] A1 | transicion=corte", aliases));
        Assert.Equal($"[MOSTRAR] Bart | {id:D} | izquierda", DirectorScript.ResolveAiAssetAliases("[MOSTRAR] Bart | a1 | izquierda", aliases));
        Assert.Equal("Bart: dame A1", DirectorScript.ResolveAiAssetAliases("Bart: dame A1", aliases), "el diálogo no se toca");
        Assert.Equal("[FONDO] A7", DirectorScript.ResolveAiAssetAliases("[FONDO] A7", aliases), "alias desconocido intacto");
    }

    [Test("Personaje a partir del nombre del archivo de una toma grabada")]
    public static void CharacterFromFileName()
    {
        var cast = new[] { "Bart", "Fluttershy", "Pinkie Pie", "Bart Simpson", "Al" }
            .Select(name => new CharacterDefinition(Guid.NewGuid(), name, null)).ToArray();
        foreach (var (file, expected) in new[]
                 {
                     ("03_Bart_hola.wav", "Bart"), ("bart01.wav", "Bart"), ("FLUTTERSHY - toma 2.wav", "Fluttershy"),
                     ("pinkie_pie_risa.wav", "Pinkie Pie"), ("bart_simpson_grito.wav", "Bart Simpson"),
                     ("bart_y_fluttershy.wav", null), ("al_final.wav", null), ("toma_07.wav", null),
                     ("Bárt enojado.wav", "Bart"), ("bartolo.wav", null)
                 })
            Assert.Equal(expected, DirectorScript.CharacterFromFileName("C:/voces/" + file, cast)?.Name, file);
    }

    [Test("Música o SFX por tamaño cuando la biblioteca no los clasificó")]
    public static void AudioEstimate()
    {
        AssetRecord Audio(string extension, long size, AssetKind kind = AssetKind.Unknown) =>
            new(Guid.NewGuid(), "x" + extension, kind, "h", "x", DateTimeOffset.UtcNow, Extension: extension, FileSize: size);
        Assert.Equal("musica", AudioDurationEstimate.ClassifyUnlabelled(Audio(".mp3", 7_200_000)), "canción de 5 min");
        Assert.Equal("sfx", AudioDurationEstimate.ClassifyUnlabelled(Audio(".wav", 352_800)), "golpe de 2 s");
        Assert.Equal(null, AudioDurationEstimate.ClassifyUnlabelled(Audio(".mp3", 320_000)), "clip ambiguo de 20 s");
        Assert.True(AudioDurationEstimate.TooLongForEffect(Audio(".mp3", 7_200_000, AssetKind.SoundEffect)), "SFX de 5 min");
        Assert.True(AudioDurationEstimate.TooShortForMusic(Audio(".mp3", 30_000, AssetKind.Music)), "«música» de 2 s");
    }

    [Test("Ocultar varios (1.4.4): «Bart, Lisa» y «todos» salen a la vez; con pausa entre dos [OCULTAR], uno tras otro")]
    public static async Task GroupHide()
    {
        var specs = DirectorScript.ParseDirectorPrompt("""
            [MOSTRAR] Bart | A1 | izquierda
            [MOSTRAR] Lisa | A2 | derecha
            [MOSTRAR] NPC | A7 | centro
            [OCULTAR] Bart, Lisa | pausa=300
            [MOSTRAR] Bart | A3 | izquierda
            [OCULTAR] todos
            """);
        var hides = specs.Where(x => x.Kind == ScriptBlockKind.CharacterHide).ToArray();
        Assert.Sequence(["Bart", "Lisa", "NPC", "Bart"], hides.Select(x => x.CharacterName), "uno por personaje; «todos» = los que siguen en pantalla, por orden de aparición");
        Assert.Equal("A7", hides[2].ResourceQuery, "el NPC se oculta por su render");
        Assert.Sequence([0, 300, 0, 0], hides.Select(x => x.PauseMs), "la pausa va después del último");
        Assert.Sequence(["[OCULTAR] Bart", "[OCULTAR] Lisa | pausa=300", "[OCULTAR] NPC | A7", "[OCULTAR] Bart"],
            hides.Select(x => x.SourceLine), "cada fila con su propia instrucción");
        Assert.True(hides.All(x => x.Error is null), "sin errores");
        Assert.Equal("[MOSTRAR] Lisa | A2 | derecha", specs[1].SourceLine, "las demás filas conservan su línea");

        var start = DirectorScript.ParseDirectorPrompt("[OCULTAR] todos", ["Homero", "Marge"]);
        Assert.Sequence(["Homero", "Marge"], start.Select(x => x.CharacterName), "«todos» incluye a quien ya estaba en la escena");
        Assert.Contains("No hay personajes", DirectorScript.ParseDirectorPrompt("[OCULTAR] todos").Single().Error ?? "", "nadie en pantalla");

        // In the scene: consecutive hides share the instant; a pause between them staggers them.
        Guid bart = Guid.NewGuid(), lisa = Guid.NewGuid();
        SceneScriptBlock Block(int order, ScriptBlockKind kind, Guid? who = null, int pause = 0) =>
            new(Guid.NewGuid(), Guid.Empty, order, kind, who, PauseAfterMs: pause);
        var together = await SceneComposer.PlanAsync([Block(0, ScriptBlockKind.Pause, pause: 500), Block(1, ScriptBlockKind.CharacterHide, bart),
            Block(2, ScriptBlockKind.CharacterHide, lisa, 300)], _ => null);
        Assert.Sequence([500L, 500L], together.Media.Where(x => x.Kind == ScriptBlockKind.CharacterHide).Select(x => x.StartMs), "a la vez");
        var staggered = await SceneComposer.PlanAsync([Block(0, ScriptBlockKind.Pause, pause: 500), Block(1, ScriptBlockKind.CharacterHide, bart, 400),
            Block(2, ScriptBlockKind.CharacterHide, lisa)], _ => null);
        Assert.Sequence([500L, 900L], staggered.Media.Where(x => x.Kind == ScriptBlockKind.CharacterHide).Select(x => x.StartMs), "uno tras otro");

        // Editor «Todos los que están en escena»: characters and NPC renders still on screen before the new block.
        Guid guard = Guid.NewGuid(), extra = Guid.NewGuid();
        SceneScriptBlock Show(int order, Guid? who, Guid asset) =>
            new(Guid.NewGuid(), Guid.Empty, order, ScriptBlockKind.CharacterShow, who, AssetId: asset);
        SceneScriptBlock[] scene =
        [
            Show(0, bart, Guid.NewGuid()), Show(1, lisa, Guid.NewGuid()), Show(2, null, guard), Show(3, null, extra),
            Show(4, null, extra), Block(5, ScriptBlockKind.CharacterHide, lisa),
            new(Guid.NewGuid(), Guid.Empty, 6, ScriptBlockKind.CharacterHide, AssetId: extra), Show(7, bart, Guid.NewGuid())
        ];
        Assert.Sequence(new (Guid?, Guid?)[] { (null, guard), (null, extra), (bart, null) }, SceneComposer.OnScreenBefore(scene, 8),
            "Lisa y un extra ya salieron; Bart cambió de render y cuenta una vez");
        Assert.Sequence(new (Guid?, Guid?)[] { (bart, null), (lisa, null) }, SceneComposer.OnScreenBefore(scene, 2), "antes del bloque #3");
    }
}
