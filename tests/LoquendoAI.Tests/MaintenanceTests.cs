using LoquendoAI.Infrastructure.Composition;
using LoquendoAI.Infrastructure.Persistence;
using LoquendoAI.Infrastructure.Projects;

namespace LoquendoAI.Tests;

/// <summary>Media measurements, cache cleanup and backups.</summary>
internal static class MaintenanceTests
{
    [Test("Duración de WAV desde la cabecera (PCM 16/24 bits, float, tamaño de datos 0)")]
    public static void WaveDuration()
    {
        using var folder = new TempFolder();
        Assert.Equal(2500L, MediaProbeCache.WaveDurationMs(TestMedia.Wav(folder.File("pcm16.wav"), 2500)), "PCM 16");
        Assert.Equal(3210L, MediaProbeCache.WaveDurationMs(TestMedia.Wav(folder.File("s24.wav"), 3210, 48000, 2, 24)), "PCM 24 estéreo");
        Assert.Equal(1338L, MediaProbeCache.WaveDurationMs(TestMedia.Wav(folder.File("float.wav"), 1338, 44100, 1, 32, floatSamples: true)), "float");
        var streaming = TestMedia.Wav(folder.File("zero.wav"), 2500);
        using (var stream = new FileStream(streaming, FileMode.Open))
        {
            stream.Position = 40; // data chunk size left at 0 by a recorder still writing
            stream.Write(new byte[4]);
        }
        Assert.Equal(2500L, MediaProbeCache.WaveDurationMs(streaming), "tamaño 0 → hasta el final del archivo");
        File.WriteAllText(folder.File("song.mp3"), "no es wav");
        Assert.Equal(null, MediaProbeCache.WaveDurationMs(folder.File("song.mp3")), "MP3 no usa la cabecera");
    }

    [Test("Limpieza de caché: solo lo planificado dentro de generated; referencias y grabaciones intactas")]
    public static void Cleanup()
    {
        using var folder = new TempFolder();
        var root = folder.Path;
        var now = DateTime.UtcNow;
        string F(string relative, int bytes = 100, double hoursAgo = 0)
        {
            var path = folder.File(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[bytes]);
            File.SetLastWriteTimeUtc(path, now.AddHours(-hoursAgo));
            return path;
        }
        var s1 = new string('1', 32);
        var s2 = new string('2', 32);
        var previewOld = F($"generated/previews/scene_{s1}_aaaa.mp4", 5000, 48);
        var previewMid = F($"generated/previews/scene_{s1}_bbbb.mp4", 5000, 24);
        var previewNew = F($"generated/previews/scene_{s1}_cccc.mp4", 5000, 1);
        var previewOnly = F($"generated/previews/scene_{s2}_dddd.mp4", 5000, 100);
        var previewPlaying = F($"generated/previews/scene_{s2}_eeee.mp4", 5000, 200);
        var used = F("generated/voices/episode_001/scene_001/0001_Bart_abcdef1234.wav");
        var unused = F("generated/voices/episode_001/scene_001/0002_Bart_ffffffffff.wav");
        var keptRecording = F("generated/voices/episode_001/scene_002/0003_Bart_1234512345.wav");
        var imported = F("generated/imported_voices/scene_x/toma.wav", 100, 5000);
        var cacheUsed = F($"generated/voices/_cache/{new string('a', 64)}.wav", 100, 24 * 90);
        var cacheOld = F($"generated/voices/_cache/{new string('b', 64)}.wav", 100, 24 * 90);
        var cacheRecent = F($"generated/voices/_cache/{new string('c', 64)}.wav", 100, 24 * 3);
        var staging = F("generated/voices/_cache/_staging/x.1234.wav", 100, 12);
        var stagingNow = F("generated/voices/_cache/_staging/y.1234.wav", 100, 0.1);
        var vegas = F($"generated/vegas/scene_{s1}_v15/media/clip.mov", 9000, 500);
        var asset = F("assets/fondo.png", 100, 5000);
        ScriptAudioReference[] references =
        [
            new("generated/voices/episode_001/scene_001/0001_Bart_abcdef1234.wav", new string('a', 64), "{}"),
            new("generated/imported_voices/scene_x/otra.wav", "imported-sha256:zz",
                """{"recordedTake":{"path":"generated/voices/episode_001/scene_002/0003_Bart_1234512345.wav"}}"""),
            new(null, null, "{}")
        ];
        var plan = ProjectMaintenance.Plan(root, references, [previewPlaying], now);
        Assert.True(plan.Select(x => x.Path).ToHashSet().SetEquals([previewOld, previewMid, unused, cacheOld, staging]),
            "plan: " + string.Join(", ", plan.Select(x => Path.GetRelativePath(root, x.Path))));
        _ = (previewNew, previewOnly, used, keptRecording, cacheUsed, cacheRecent);
        var hostile = plan.Append(new CleanupItem(imported, 100, "x")).Append(new CleanupItem(vegas, 9000, "x"))
            .Append(new CleanupItem(asset, 100, "x")).Append(new CleanupItem(Path.Combine(root, "generated", "..", "assets", "fondo.png"), 100, "x"));
        var (deleted, bytes, failed) = ProjectMaintenance.Delete(root, hostile);
        Assert.Equal((5, 10300L, 4), (deleted, bytes, failed), "borrados, bytes y rechazados");
        Assert.True(File.Exists(imported) && File.Exists(vegas) && File.Exists(asset) && File.Exists(stagingNow),
            "grabaciones importadas, VEGAS, recursos y temporales en uso intactos");
        Assert.Equal(9000L, ProjectMaintenance.VegasExportBytes(root), "tamaño de exportaciones VEGAS");
    }

    [Test("Copias de seguridad: rotación de 10 por motivo; las de migración no se tocan")]
    public static void BackupRotation()
    {
        using var folder = new TempFolder();
        var backups = ProjectBackup.Folder(folder.Path);
        Directory.CreateDirectory(backups);
        var now = DateTime.UtcNow;
        for (var i = 0; i < 13; i++)
        {
            var file = Path.Combine(backups, $"project_202609{10 + i:00}_120000_auto.db");
            File.WriteAllText(file, "x");
            File.SetLastWriteTimeUtc(file, now.AddDays(-20 + i));
        }
        var migration = Path.Combine(backups, "project_20260901_120000_antes-v4.db");
        File.WriteAllText(migration, "x");
        File.SetLastWriteTimeUtc(migration, now.AddDays(-30));
        ProjectBackup.Prune(backups, ProjectBackup.AutomaticReason, ProjectBackup.KeepPerReason);
        Assert.Equal(10, Directory.GetFiles(backups, "*_auto*.db").Length, "automáticas");
        Assert.True(File.Exists(migration), "la de migración sigue");
        Assert.False(File.Exists(Path.Combine(backups, "project_20260910_120000_auto.db")), "se borra la más antigua");
    }
}
