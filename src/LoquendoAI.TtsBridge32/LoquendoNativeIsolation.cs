using System.Diagnostics;
using System.Text.Json;

namespace LoquendoAI.TtsBridge32;

internal static class LoquendoNativeIsolation
{
    internal static void Synthesize(SynthRequest request, bool verbose = true)
    {
        var exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("No se pudo determinar la ruta del TTS bridge.");
        var temp = Path.Combine(Path.GetTempPath(), $"loquendoai-ltts7-{Guid.NewGuid():N}.json");
        File.WriteAllText(temp, JsonSerializer.Serialize(request, JsonDefaults.Options));
        try
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            p.StartInfo.ArgumentList.Add("native-synth-worker");
            p.StartInfo.ArgumentList.Add("--request-json");
            p.StartInfo.ArgumentList.Add(temp);
            p.Start();

            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();
            p.WaitForExit();
            Task.WaitAll(stdoutTask, stderrTask);
            var stdout = stdoutTask.Result;
            var stderr = stderrTask.Result;

            if (verbose && !string.IsNullOrWhiteSpace(stderr))
                Console.Error.Write(stderr);
            // native-synth-worker only writes the output path to stdout. The parent
            // command already prints it, so suppress it here to avoid duplicate lines.

            var output = Path.GetFullPath(request.Output);
            var validWave = File.Exists(output) && new FileInfo(output).Length > 44;
            if (p.ExitCode != 0)
            {
                // If LTTS7 crashes only while Windows tears the legacy module down, the
                // requested WAV can already be complete. Accept it, but make the anomaly visible.
                if (validWave && stderr.Contains("WAV verified", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine($"[LTTS7] AVISO: el worker terminó con código {FormatExitCode(p.ExitCode)} después de verificar el WAV; se conserva el audio.");
                    return;
                }

                var last = stderr.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                throw new InvalidOperationException(
                    $"El worker nativo de Loquendo terminó con {FormatExitCode(p.ExitCode)}" +
                    (last is null ? "." : $". Última traza: {last}"));
            }
            if (!validWave)
                throw new IOException("El worker nativo terminó, pero no dejó un WAV válido.");
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    private static string FormatExitCode(int code)
        => $"exit code {code} (0x{unchecked((uint)code):X8})";
}
