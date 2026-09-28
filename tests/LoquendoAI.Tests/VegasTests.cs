using System.Text.RegularExpressions;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Composition;

namespace LoquendoAI.Tests;

/// <summary>VEGAS export (audio track modes, regions) and the transition catalog.</summary>
internal static class VegasTests
{
    [Test("Exportación a VEGAS de un episodio: pistas por personaje o una por clip, regiones y tiempos")]
    public static async Task ExportEpisodeAudioModes()
    {
        TestMedia.RequireFfmpeg();
        using var folder = new TempFolder();
        var background = TestMedia.SolidPng(folder.File("media/fondo.png"), 320, 180, (40, 90, 160));
        var music = TestMedia.Wav(folder.File("media/musica.wav"), 6000);
        var voice = TestMedia.Wav(folder.File("media/voz.wav"), 400);
        var voice2 = TestMedia.Wav(folder.File("media/voz2.wav"), 1200);
        var sfx = TestMedia.Wav(folder.File("media/sfx.wav"), 300);
        Guid bart = Guid.NewGuid(), flutter = Guid.NewGuid();
        SceneComposition Scene() => new(3000,
        [
            new(Guid.NewGuid(), ScriptBlockKind.Background, 0, 0, background, AutoTrimBorders: false, VisualMaxWidth: 1280, VisualMaxHeight: 720),
            new(Guid.NewGuid(), ScriptBlockKind.Music, 0, 3000, music, SourceDurationMs: 6000, VolumePercent: 25),
            new(Guid.NewGuid(), ScriptBlockKind.Dialogue, 0, 400, voice, CharacterId: bart, SourceDurationMs: 400),
            new(Guid.NewGuid(), ScriptBlockKind.Dialogue, 400, 1200, voice2, CharacterId: flutter, SourceDurationMs: 1200),
            new(Guid.NewGuid(), ScriptBlockKind.SoundEffect, 500, 300, sfx, SourceDurationMs: 300, VolumePercent: 60),
            new(Guid.NewGuid(), ScriptBlockKind.SoundEffect, 650, 300, sfx, SourceDurationMs: 300, VolumePercent: 60),
            new(Guid.NewGuid(), ScriptBlockKind.Dialogue, 1600, 400, voice, CharacterId: bart, SourceDurationMs: 400),
            new(Guid.NewGuid(), ScriptBlockKind.Narration, 2000, 400, voice, SourceDurationMs: 400)
        ]);
        var names = new Dictionary<Guid, string> { [bart] = "Bart", [flutter] = "Fluttershy" };
        long[] expectedStarts = [0, 0, 400, 500, 650, 1600, 2000, 3000, 3000, 3400, 3500, 3650, 4600, 5000];
        foreach (var audio in new[] { VegasAudioTrackMode.PerCharacter, VegasAudioTrackMode.Legacy })
        {
            var directory = folder.File("out_" + audio);
            await VegasBridge.ExportEpisodeAsync([new("01 · Cocina", Scene()), new("02 · Desastre", Scene())], directory,
                "Episodio 01 - Pizza", 720, mode: VegasExportMode.Normal, audioMode: audio, characterNames: names);
            var script = await File.ReadAllTextAsync(Path.Combine(directory, "Abrir_en_VEGAS_14_o_superior.cs"));
            Assert.True(File.Exists(Path.Combine(directory, "Abrir_en_VEGAS_12_13.cs")), "script para VEGAS 12/13");
            var tracks = Regex.Matches(script, "AudioTrack track = vegas.Project.AddAudioTrack\\(\\); track.Name = \"([^\"]+)\"")
                .Select(m => m.Groups[1].Value).ToArray();
            if (audio == VegasAudioTrackMode.PerCharacter)
                Assert.Sequence(["Música · 25%", "SFX · 60%", "SFX · 60% (2)", "Voz · Bart", "Voz · Fluttershy", "Voz · Narrador"],
                    tracks.Order(StringComparer.Ordinal), "pistas por personaje");
            else Assert.Equal(14, tracks.Length, "una pista por clip");
            var starts = Regex.Matches(script, "AddAudioEvent\\(Timecode.FromMilliseconds\\((\\d+)\\)")
                .Select(m => long.Parse(m.Groups[1].Value)).Order().ToArray();
            Assert.Sequence(expectedStarts, starts, $"inicios de audio ({audio})");
            Assert.Contains("\"01 · Cocina\"", script, "región de la escena 1");
            Assert.Contains("Timecode.FromMilliseconds(3000), Timecode.FromMilliseconds(3000), \"02 · Desastre\"", script, "región de la escena 2");
        }
    }

    [Test("Informe de transiciones de VEGAS: plugins, presets y proveedor")]
    public static void InstallationReport()
    {
        const string report = """
            Transiciones y presets disponibles en esta instalación de VEGAS
            NewBlue MB Zoom | {Svfx:com.NewBlue.MBZoom}
                Big One
                Reset to None
            S_DissolveBlur | {Svfx:com.genarts.sapphire.Transitions.S_DissolveBlur}
                (Predeterminado)
            NewBlue MB Zoom | {Svfx:com.NewBlue.MBZoom}
                Big One
            """;
        var parsed = VegasTransitionCatalog.ParseInstallationReport(report);
        Assert.False(parsed.Any(x => x.Name.StartsWith("Transiciones", StringComparison.Ordinal)), "la cabecera se ignora");
        var zoom = parsed.First(x => x.Name == "NewBlue MB Zoom");
        Assert.Equal("NewBlue", zoom.Vendor, "proveedor NewBlue");
        Assert.True(zoom.Presets.Contains("Big One"), "presets");
        Assert.Equal("Sapphire", parsed.First(x => x.Name == "S_DissolveBlur").Vendor, "proveedor Sapphire");
    }

    [Test("Catálogo: opciones para el Director resolubles, sin repetir, y «negro» sugiere el disolvente a negro")]
    public static void DirectorOptions()
    {
        var options = VegasTransitionCatalog.OptionsForDirector("Bart explota una pizza y todo destella", 12, new Random(7));
        Assert.True(options.Count > 0, "hay opciones");
        Assert.True(options.All(o => VegasTransitionCatalog.TryResolve(o.Id, o.Preset, out _, out var preset) && preset == o.Preset),
            "cada opción resuelve ID + preset");
        Assert.Equal(options.Count, options.Select(o => o.Id).Distinct().Count(), "sin repetidos");
        Assert.True(options.All(o => !o.Preset.Contains('|') && !o.Preset.Contains('=')), "presets seguros para la línea del Director");
        var dark = VegasTransitionCatalog.OptionsForDirector("al final todo se pone negro", 12, new Random(4));
        Assert.True(dark.Any(o => o.Name == "Disolvente" && o.Preset == "Desvanecimiento en negro"),
            "«negro» ofrece Disolvente · Desvanecimiento en negro");
    }
}
