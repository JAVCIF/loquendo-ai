using System.Text.Json;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;
using LoquendoAI.Infrastructure.Director;

namespace LoquendoAI.Tests;

/// <summary>Typed block parameters: one parser, one set of defaults, unknown keys preserved.</summary>
internal static class BlockParametersTests
{
    [Test("Ida y vuelta: parse → JSON → parse conserva valores y claves desconocidas")]
    public static void RoundTrip()
    {
        const string json = """{"position":"derecha","visualMaxWidth":900,"stt":{"speechStartMs":120,"words":[{"w":"hola"}]},"recordedTake":{"path":"a.wav","hash":"h","durationMs":1200},"futuro":[1,2]}""";
        var parsed = BlockParameters.Parse(json);
        var again = BlockParameters.Parse(parsed.ToJson());
        Assert.Equal("derecha", again.EffectivePosition, "posición");
        Assert.Equal(900, again.VisualMaxWidth, "ancho");
        Assert.Equal(new RecordedTakeReference("a.wav", "h", 1200), again.RecordedTake, "toma grabada");
        Assert.True(again.Extra.ContainsKey("stt") && again.Extra.ContainsKey("futuro"), "claves desconocidas");
        Assert.Equal(parsed.ToJson(), again.ToJson(), "JSON estable");
        Assert.True(ReferenceEquals(BlockParameters.Parse(json), BlockParameters.Parse(json)), "se interpreta una vez por cadena");
    }

    [Test("JSON dañado o vacío: valores por defecto, sin excepción")]
    public static void Broken()
    {
        foreach (var json in new[] { "{roto", "[]", "", "null", "{\"visualMaxWidth\":\"ancho\"}" })
        {
            var parameters = BlockParameters.Parse(json);
            var transform = parameters.Transform(ScriptBlockKind.Image);
            Assert.Equal(BlockDefaults.VisualBox(ScriptBlockKind.Image).Width, transform.MaxWidth, $"ancho por defecto con «{json}»");
        }
    }

    [Test("Valores por defecto por tipo de bloque (definidos en un solo sitio)")]
    public static void Defaults()
    {
        var empty = BlockParameters.Empty;
        Assert.Equal(100, empty.Volume(ScriptBlockKind.SoundEffect), "volumen SFX");
        Assert.Equal(BlockDefaults.VolumePercent(ScriptBlockKind.Music), empty.Volume(ScriptBlockKind.Music), "volumen música");
        Assert.Equal((1280, 720), BlockDefaults.VisualBox(ScriptBlockKind.Background), "caja de fondo");
        Assert.Equal(BlockDefaults.TransitionDurationMs, BlockParameters.Parse("""{"transitionStyle":"cruce"}""").Transition.DurationMs,
            "duración de transición por defecto");
        Assert.True(empty.TrimBorders(ScriptBlockKind.Background), "recorte de bordes en fondos");
        Assert.False(empty.TrimBorders(ScriptBlockKind.Image), "sin recorte en imágenes");
        Assert.True(empty.AutoGreenScreen(ScriptBlockKind.Video), "croma automático si no se eligió");
        Assert.False(BlockParameters.Parse("""{"greenScreenMode":5}""").AutoGreenScreen(ScriptBlockKind.Video),
            "un modo presente pero inválido cuenta como elegido (igual que antes)");
    }

    [Test("Escritores: WithExtra y WithDirectVoice no pierden el resto")]
    public static void Writers()
    {
        var parameters = BlockParameters.Parse("""{"position":"izquierda","stt":{"x":1}}""")
            .WithDirectVoice("tts7", "Jorge")
            .WithExtra("marca", JsonSerializer.SerializeToNode("valor"));
        var again = BlockParameters.Parse(parameters.ToJson());
        Assert.Equal("Jorge", again.DirectVoice("tts7"), "voz directa");
        Assert.Equal("izquierda", again.Position, "posición");
        Assert.True(again.Extra.ContainsKey("stt") && again.Extra.ContainsKey("marca"), "extras");
        Assert.Equal(null, BlockParameters.Parse(parameters.WithDirectVoice("tts7", null).ToJson()).DirectVoice("tts7"), "quitar voz");
    }

    [Test("Editor (1.1.1): lo que guardan los paneles de Cámara, Cine, Gesto y Desenfoque se lee igual que su línea del Director")]
    public static void EffectPanels()
    {
        static DirectorSpec One(string line) => DirectorScript.ParseDirectorPrompt(line).Single();
        var saved = BlockParameters.Parse("""{"otra":1}""");
        // Same writes as MainWindow.TryEffectParameters (the WPF panel is not testable here): built over the saved
        // block, so unknown keys survive.
        var camera = saved with { CameraMode = "punto", CameraZoom = 2, CameraMoveMs = 600, CameraFocus = "cara", CameraOffsetX = 100, CameraOffsetY = -50 };
        Assert.Equal(BlockParameters.Parse(DirectorScript.DirectorParameters(One("[CAMARA] punto | zoom=2 | duracion=600 | x=100 | y=-50"))).Camera(false),
            BlockParameters.Parse(camera.ToJson()).Camera(false), "cámara: punto");
        Assert.True(BlockParameters.Parse(camera.ToJson()).Extra.ContainsKey("otra"), "conserva claves desconocidas");
        var general = saved with { CameraMode = "general", CameraZoom = null, CameraMoveMs = 0, CameraFocus = "cara" };
        Assert.Equal(new CameraSettings("general", 1, 0, "cara", 0, 0), BlockParameters.Parse(general.ToJson()).Camera(true),
            "cámara: plano general aunque el bloque tenga personaje");

        Guid bart = Guid.NewGuid(), lisa = Guid.NewGuid();
        var cinema = BlockParameters.Parse(new BlockParameters
        {
            CinemaMode = "mostrar", CinemaStyle = "abierto", CinemaMoveMs = 600, CinemaLayers = "lista", CinemaCharacters = [bart, lisa]
        }.ToJson()).Cinema();
        var director = BlockParameters.Parse(DirectorScript.DirectorParameters(One("[CINE] mostrar | estilo=fino | duracion=600 | capas=Bart, Lisa"),
            name => name == "Bart" ? bart : name == "Lisa" ? lisa : null)).Cinema();
        Assert.Equal((director.Show, director.Style, director.MoveMs, director.Layers), (cinema.Show, cinema.Style, cinema.MoveMs, cinema.Layers), "cine");
        Assert.Sequence(director.Characters, cinema.Characters, "cine: personajes elegidos");
        var hide = BlockParameters.Parse(new BlockParameters { CinemaMode = "quitar", CinemaStyle = "cerrado", CinemaMoveMs = 0, CinemaLayers = "todos" }.ToJson()).Cinema();
        Assert.Equal((false, 0L, "todos"), (hide.Show, hide.MoveMs, hide.Layers), "cine: quitar de golpe");

        var steps = GestureSettings.ParseSteps("rebote, balanceo+rebote");
        var gesture = BlockParameters.Parse(new BlockParameters
        {
            GestureMode = "habla", GestureSteps = GestureSettings.StepsText(steps), GestureAngle = 7.5, GestureSide = "izquierda",
            GestureStretch = -10, GestureSpeed = 1.5, GesturePivot = "cintura"
        }.ToJson()).Gesture();
        var spoken = BlockParameters.Parse(DirectorScript.DirectorParameters(
            One("[GESTO] habla | rebote, balanceo+rebote | angulo=7.5 | lado=izquierda | estirar=-10 | velocidad=1.5 | eje=cintura"))).Gesture();
        Assert.Equal((spoken.Mode, spoken.Angle, spoken.Side, spoken.StretchPercent, spoken.Speed, spoken.Pivot),
            (gesture.Mode, gesture.Angle, gesture.Side, gesture.StretchPercent, gesture.Speed, gesture.Pivot), "gesto");
        Assert.Equal(GestureSettings.StepsText(spoken.Steps), GestureSettings.StepsText(gesture.Steps), "gesto: movimientos");
        Assert.Equal("rebote, balanceo+rebote", GestureSettings.StepsText(gesture.Steps), "el texto del panel vuelve igual al cargarlo");

        var blur = BlockParameters.Parse(new BlockParameters { BlurTarget = "personaje", BlurAmount = 0.01, BlurMoveMs = 700 }.ToJson());
        Assert.Equal(BlockParameters.Parse(DirectorScript.DirectorParameters(One("[DESENFOQUE] Bart | ligero | duracion=700"))).Blur(true),
            blur.Blur(true), "desenfoque: el personaje elegido");
        Assert.Equal(new BlurSettings("fondo", 0, 0),
            BlockParameters.Parse(new BlockParameters { BlurTarget = "fondo", BlurAmount = 0, BlurMoveMs = 0 }.ToJson()).Blur(true),
            "desenfoque: nítido (0), aunque el bloque tenga personaje");
        Assert.Equal("imagenes", BlockParameters.Parse(new BlockParameters { BlurTarget = "imagenes", BlurAmount = 0.02 }.ToJson()).Blur(true).Target,
            "desenfoque: un objetivo elegido no cambia por tener personaje");
    }
}
