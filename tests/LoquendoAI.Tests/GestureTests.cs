using System.Text.Json;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;
using LoquendoAI.Infrastructure.Director;
using static LoquendoAI.Infrastructure.Director.DirectorScript;

namespace LoquendoAI.Tests;

internal static class GestureTests
{
    private static GestureSettings Settings(string steps, string mode = "personaje", string side = "auto", double angle = 5,
        double stretch = 8, double speed = 1, string pivot = "pies") =>
        new(mode, GestureSettings.ParseSteps(steps), angle, side, stretch, speed, pivot);

    [Test("Gesto: balanceo (4 + 5 fotogramas, «Rápido»), rebote (fotogramas 2 y 4), juntos y seguidos")]
    public static void Steps()
    {
        var (sway, end, endMs) = GesturePlan.Step(1000, GesturePose.Rest, ["balanceo"], Settings("balanceo"), 1);
        Assert.Sequence([1000L, 1160L, 1360L], sway.Select(x => x.AtMs), "balanceo: 0, 4 y 9 fotogramas");
        Assert.Sequence([0d, 5d, -2d], sway.Select(x => x.Pose.Degrees), "se inclina y rebota un poco al otro lado");
        Assert.Sequence([true, true, false], sway.Select(x => x.Fast), "«Rápido» mientras se mueve");
        Assert.Equal((-2d, 1360L), (end.Degrees, endMs), "se queda levemente inclinado");

        var (bounce, _, _) = GesturePlan.Step(0, GesturePose.Rest, ["rebote"], Settings("rebote", stretch: 10), 1);
        Assert.Sequence([0L, 80L, 160L], bounce.Select(x => x.AtMs), "rebote: 0, 2 y 4 fotogramas");
        Assert.Sequence([1d, 1.1, 1d], bounce.Select(x => Math.Round(x.Pose.Stretch, 5)), "estira y vuelve");

        var (both, _, _) = GesturePlan.Step(0, GesturePose.Rest, ["balanceo", "rebote"], Settings("ambos"), -1);
        Assert.Sequence([0L, 80L, 160L, 360L], both.Select(x => x.AtMs), "juntos: tiempos de los dos");
        Assert.Equal(-5d, both[2].Pose.Degrees, "a la izquierda");
        Assert.True(both[1].Pose.Degrees < -3 && both[1].Pose.Stretch > 1.07, "a mitad: inclinándose y estirado (curva rápida)");

        var slow = GesturePlan.Step(0, GesturePose.Rest, ["balanceo"], Settings("balanceo", speed: 0.5), 1).Keys;
        Assert.Equal(720L, slow[^1].AtMs, "velocidad 0,5: el doble de lento");
        Assert.Sequence(["balanceo", "rebote"], GestureSettings.ParseSteps("movimiento , estirar").Select(x => x[0]), "sinónimos y secuencia");
    }

    private static SceneComposition Scene(Guid bart, Guid lisa, params GestureCue[] cues) => new(4000,
    [
        new(Guid.NewGuid(), ScriptBlockKind.CharacterShow, 0, 0, "bart.png", "izquierda", bart, VisualMaxWidth: 1280, VisualMaxHeight: 610),
        new(Guid.NewGuid(), ScriptBlockKind.CharacterShow, 0, 0, "lisa.png", "derecha", lisa, VisualMaxWidth: 1280, VisualMaxHeight: 610),
        new(Guid.NewGuid(), ScriptBlockKind.Dialogue, 500, 1000, "a.wav", CharacterId: bart),
        new(Guid.NewGuid(), ScriptBlockKind.Dialogue, 1500, 1000, "b.wav", CharacterId: lisa),
        new(Guid.NewGuid(), ScriptBlockKind.Dialogue, 2500, 1000, "c.wav", CharacterId: bart)
    ], GestureCues: cues);

    [Test("Gesto en escena: hacia el centro y alternando, «habla» por cada línea, interrupción y secuencia")]
    public static void Resolve()
    {
        Guid bart = Guid.NewGuid(), lisa = Guid.NewGuid();
        var scene = Scene(bart, lisa,
            new GestureCue(0, Settings("balanceo"), bart), new GestureCue(0, Settings("balanceo"), lisa),
            new GestureCue(1000, Settings("balanceo"), bart));
        var resolved = GesturePlan.Resolve(scene);
        var bartTrack = resolved.Gestures![scene.Media[0].BlockId];
        var lisaTrack = resolved.Gestures[scene.Media[1].BlockId];
        Assert.Equal(5d, bartTrack.At(160).Degrees, "Bart (izquierda) se inclina hacia el centro: a la derecha");
        Assert.Equal(-5d, lisaTrack.At(160).Degrees, "Lisa (derecha) hacia el centro: a la izquierda");
        Assert.Equal(-5d, bartTrack.At(1160).Degrees, "el siguiente balanceo de Bart va al otro lado");
        Assert.Equal(2d, lisaTrack.At(3000).Degrees, "Lisa se queda levemente inclinada hacia el otro lado");

        var speaking = GesturePlan.Resolve(Scene(bart, lisa, new GestureCue(0, Settings("rebote", "habla"), null),
            new GestureCue(2000, Settings("rebote", "quitar"), null)));
        var bartSpeaking = speaking.Gestures![speaking.Media[0].BlockId];
        Assert.Sequence([500L, 580L, 660L], bartSpeaking.Keys.Select(x => x.AtMs), "Bart: solo su primera línea (la tercera ya es tras «quitar»)");
        Assert.Sequence([1500L, 1580L, 1660L], speaking.Gestures[speaking.Media[1].BlockId].Keys.Select(x => x.AtMs), "Lisa al empezar su línea");

        var interrupted = GesturePlan.Resolve(Scene(bart, lisa, new GestureCue(0, Settings("balanceo"), bart),
            new GestureCue(100, Settings("rebote"), bart))).Gestures!.Values.Single();
        Assert.True(interrupted.At(100).Degrees is > 3 and < 5, "a los 100 ms iba inclinándose");
        Assert.Equal(interrupted.At(100).Degrees, interrupted.At(400).Degrees, "el rebote sigue desde esa inclinación");

        var sequence = GesturePlan.Resolve(Scene(bart, lisa, new GestureCue(0, Settings("rebote, balanceo"), bart))).Gestures!.Values.Single();
        Assert.Sequence([0L, 80L, 160L, 320L, 520L], sequence.Keys.Select(x => x.AtMs), "rebote y luego balanceo");
        Assert.True(ReferenceEquals(GesturePlan.Resolve(resolved).Gestures, resolved.Gestures), "idempotente");
    }

    private static DirectorSpec One(string line) => ParseDirectorPrompt(line).Single();

    [Test("[GESTO]: personaje o quien habla, movimientos, opciones y errores")]
    public static void Language()
    {
        GestureSettings Parsed(string line)
        {
            var spec = One(line);
            Assert.Equal(null, spec.Error, "error de " + line);
            return BlockParameters.Parse(DirectorParameters(spec)).Gesture();
        }
        var full = Parsed("[GESTO] Bart | balanceo+rebote | angulo=8 | estirar=12% | lado=izquierda | velocidad=rapido | eje=cintura");
        Assert.Equal(("personaje", "balanceo+rebote", 8d, 12d, "izquierda", 1.5, "cintura"),
            (full.Mode, GestureSettings.StepsText(full.Steps), full.Angle, full.StretchPercent, full.Side, full.Speed, full.Pivot), "todas las opciones");
        Assert.Equal("Bart", One("[GESTO] Bart | rebote").CharacterName, "personaje");
        var defaults = Parsed("[BALANCEO] Bart");
        Assert.Equal(("balanceo", 5d, 8d, "auto", 1d, "pies"), (GestureSettings.StepsText(defaults.Steps), defaults.Angle,
            defaults.StretchPercent, defaults.Side, defaults.Speed, defaults.Pivot), "valores por defecto");
        Assert.Equal("rebote, balanceo", GestureSettings.StepsText(Parsed("[GESTO] Bart | estirar, movimiento | giro=6,5").Steps), "secuencia y sinónimos");
        Assert.Equal("balanceo+rebote", GestureSettings.StepsText(Parsed("[GESTO] Bart | ambos").Steps), "ambos");
        var speaking = One("[GESTO] habla | rebote");
        Assert.Equal(("habla", ""), (speaking.Text, speaking.CharacterName), "quien habla");
        Assert.Equal("quitar", Parsed("[GESTO] quien habla | quitar").Mode, "deja de gesticular al hablar");
        foreach (var bad in new[] { "[GESTO] Bart | quitar", "[GESTO] Bart | bailar", "[GESTO] Bart | angulo=50",
                     "[GESTO] Bart | estirar=0", "[GESTO] Bart | velocidad=9", "[GESTO] Bart | eje=cabeza", "[GESTO] Bart | zoom=2" })
            Assert.True(One(bad).Error is not null, "debe fallar: " + bad);
        var checks = CameraChecks(ParseDirectorPrompt("[GESTO] Bart | balanceo\n[MOSTRAR] Bart | feliz\n[GESTO] Bart"));
        Assert.True(checks.TryGetValue(0, out var check) && check.IsError && !checks.ContainsKey(2), "antes de [MOSTRAR] es error; después, no");
    }

    [Test("Acción «gesto» de la IA y PlanAsync (el gesto no consume tiempo)")]
    public static async Task AiActionAndPlan()
    {
        string Line(string json)
        {
            using var step = JsonDocument.Parse(json);
            return DirectorAiSchema.ToLine(step.RootElement).Line;
        }
        Assert.Equal("[GESTO] Bart | balanceo+rebote", Line("""{"accion":"gesto","personaje":"Bart","movimiento":"balanceo+rebote"}"""), "personaje");
        Assert.Equal("[GESTO] habla | rebote", Line("""{"accion":"gesto","personaje":"quien habla","movimiento":"rebote"}"""), "quien habla");
        Assert.Equal("[GESTO] habla | quitar", Line("""{"accion":"gesto","personaje":"Bart","movimiento":"quitar"}"""), "quitar");
        Assert.Equal(null, One(Line("""{"accion":"gesto","personaje":"Bart","movimiento":"rebote"}""")).Error, "línea válida");

        var bart = Guid.NewGuid();
        var blocks = new[]
        {
            new SceneScriptBlock(Guid.NewGuid(), Guid.Empty, 0, ScriptBlockKind.Pause, PauseAfterMs: 700),
            new SceneScriptBlock(Guid.NewGuid(), Guid.Empty, 1, ScriptBlockKind.Gesture, bart,
                ParametersJson: DirectorParameters(One("[GESTO] Bart | rebote | estirar=-10"))),
            new SceneScriptBlock(Guid.NewGuid(), Guid.Empty, 2, ScriptBlockKind.Pause, PauseAfterMs: 500)
        };
        var plan = await SceneComposer.PlanAsync(blocks, _ => null);
        var cue = plan.GestureCues!.Single();
        Assert.Equal((700L, bart, -10d), (cue.StartMs, cue.CharacterId!.Value, cue.Settings.StretchPercent), "en su sitio del reloj");
        Assert.Equal(1200L, plan.DurationMs, "no alarga la escena");
    }

    [Test("Gesto partido por un corte de cámara: cada evento sigue la misma curva que la preview")]
    public static void SliceKeepsCurve()
    {
        var bart = Guid.NewGuid();
        var track = GesturePlan.Resolve(Scene(bart, Guid.NewGuid(), new GestureCue(1000, Settings("balanceo+rebote"), bart))).Gestures!.Values.First();
        foreach (var cut in new long[] { 1100, 1230, 1300 })
        {
            var (first, second) = (track.Slice(0, cut)!, track.Slice(cut, 4000)!);
            for (var t = 1000L; t <= 1400; t += 40)
            {
                var piece = t < cut ? first : second;
                var offset = t < cut ? 0 : cut;
                var expected = track.At(t);
                var actual = piece.At(t - offset);
                Assert.True(Math.Abs(expected.Degrees - actual.Degrees) < 0.001 && Math.Abs(expected.Stretch - actual.Stretch) < 0.0001,
                    $"corte en {cut} ms, fotograma {t}: esperado {expected}, VEGAS {actual}");
            }
        }
    }

    private static bool Red(byte r, byte g, byte b) => r > 150 && g < 90 && b < 90;

    /// <summary>Where VEGAS draws source points with a Pan/Crop quad (the quad fills the frame).</summary>
    private static int[] QuadBox((double X, double Y)[] quad, IEnumerable<(double X, double Y)> points)
    {
        var (tl, tr, bl) = (quad[0], quad[1], quad[3]);
        var u = (X: tr.X - tl.X, Y: tr.Y - tl.Y);
        var v = (X: bl.X - tl.X, Y: bl.Y - tl.Y);
        var mapped = points.Select(p =>
        {
            var d = (X: p.X - tl.X, Y: p.Y - tl.Y);
            return ((d.X * u.X + d.Y * u.Y) / (u.X * u.X + u.Y * u.Y) * 1280, (d.X * v.X + d.Y * v.Y) / (v.X * v.X + v.Y * v.Y) * 720);
        }).ToArray();
        return
        [
            (int)Math.Round(mapped.Min(p => p.Item1)), (int)Math.Round(mapped.Min(p => p.Item2)),
            (int)Math.Round(mapped.Max(p => p.Item1)), (int)Math.Round(Math.Min(720, mapped.Max(p => p.Item2)))
        ];
    }

    [Test("Gesto renderizado: preview y VEGAS (Pan/Crop nativo y medio normalizado) coinciden en reposo, inclinado y estirado")]
    public static async Task Rendered()
    {
        TestMedia.RequireFfmpeg();
        using var folder = new TempFolder();
        var red = TestMedia.SolidPng(folder.File("media/bart.png"), 400, 800, (220, 20, 20));
        var bart = Guid.NewGuid();
        // Balanceo + rebote together from 480 ms (frame 12): peak stretch at 560, peak tilt at 640, rest-tilt at 840.
        var scene = new SceneComposition(1600,
            [new(Guid.NewGuid(), ScriptBlockKind.CharacterShow, 0, 0, red, "izquierda", bart, VisualMaxWidth: 1280, VisualMaxHeight: 610)],
            GestureCues: [new GestureCue(480, Settings("balanceo+rebote", side: "derecha", angle: 8, stretch: 12), bart)]);
        var preview = folder.File("out/gesto.mp4");
        await SceneComposer.RenderAsync(scene, preview);
        var times = new long[] { 200, 520, 560, 600, 640, 720, 840 };
        var expected = times.ToDictionary(t => t, t => TestMedia.BoundingBox(TestMedia.Frame(preview, t / 1000d), Red)!);
        Assert.True(expected[640][2] - expected[640][0] > expected[200][2] - expected[200][0] + 40, "inclinado: la caja se ensancha");
        Assert.Near(expected[200][3], expected[640][3], 3, "los pies no se mueven al inclinarse (pivote en los pies)");
        Assert.True(expected[560][1] < expected[200][1] - 20, "estirado: la cabeza sube");
        var problems = new List<string>();
        foreach (var mode in new[] { VegasExportMode.Normal, VegasExportMode.Legacy })
        {
            var export = folder.File($"out/vegas_{mode}");
            await VegasBridge.ExportAsync(scene, export, "gesto", 720, mode: mode);
            var script = await File.ReadAllTextAsync(Path.Combine(export, "Abrir_en_VEGAS_14_o_superior.cs"));
            Assert.Contains("ScaleToFill = true", script, $"{mode}: estirar para llenar");
            Assert.Contains("ev.MaintainAspectRatio = false;", script, $"{mode}: sin mantener la relación de aspecto (si no, VEGAS encoge en vez de aplastar)");
            Assert.True(!script.Contains("VideoKeyframeType.Fast"), $"{mode}: la curva va en claves por fotograma, no en «Rápido» de VEGAS");
            foreach (var frame in new[] { 520, 600, 680, 800 })
                Assert.Contains($"Timecode.FromMilliseconds({frame})", script, $"{mode}: clave en el fotograma de {frame} ms");
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(export, "escena.json")));
            var clip = JsonSerializer.Deserialize<VegasBridge.Clip>(manifest.RootElement.GetProperty("Clips")[0].GetRawText())!;
            (double X, double Y)[] corners;
            if (clip.Native is not null) corners = [(0, 0), (400, 0), (400, 800), (0, 800)];
            else
            {
                var canvas = TestMedia.BoundingBox(TestMedia.Frame(clip.ImportPath!), Red)!;
                corners = [(canvas[0], canvas[1]), (canvas[2], canvas[1]), (canvas[2], canvas[3]), (canvas[0], canvas[3])];
            }
            foreach (var t in times)
            {
                var actual = QuadBox(VegasBridge.GestureQuad(clip, 1280, 720, t - clip.StartMs).Quad, corners);
                var difference = expected[t].Zip(actual, (a, b) => Math.Abs(a - b)).Max();
                if (difference > 3)
                    problems.Add($"{mode} {t} ms: preview [{string.Join(",", expected[t])}] VEGAS [{string.Join(",", actual)}]");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}
