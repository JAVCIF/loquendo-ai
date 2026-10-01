using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace LoquendoAI.App;

public sealed record SttWord(string Word, double Start, double End, double Probability);

/// <summary>One transcribed take. Words/SpeechStart/SpeechEnd (seconds) come with word timestamps.</summary>
public sealed record LocalTranscriptionResult(int Index, string? Text, string? Language, string? Device, string? Error,
    string? Compute = null, IReadOnlyList<SttWord>? Words = null, double? SpeechStart = null, double? SpeechEnd = null)
{
    /// <summary>Words the model was unsure about (probability below 0.5): names, mumbles, noise.</summary>
    public IReadOnlyList<string> DoubtfulWords => Words?.Where(x => x.Probability < 0.5).Select(x => x.Word.Trim(' ', ',', '.', '¿', '?', '¡', '!'))
        .Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
}

/// <summary>The local transcription is not installed (no Python found, or faster-whisper missing).</summary>
public sealed class SttNotInstalledException(string message) : Exception(message);

/// <summary>«cuda» was chosen but the NVIDIA libraries (cuBLAS/cuDNN) are not installed: «Instalar soporte GPU».</summary>
public sealed class SttGpuMissingException(string message) : Exception(message);

public static class LocalTranscriptionClient
{
    /// <summary>The Python the app uses, in the same order as scripts\stt-setup.ps1: the embedded one that the
    /// published version brings (worker\python, 1.4.0) and the development venv (worker\.venv). Null if none.</summary>
    public static string? FindPython()
    {
        if (TryApplicationRoot() is not { } root) return null;
        var candidates = OperatingSystem.IsWindows()
            ? new[] { Path.Combine(root, "worker", "python", "python.exe"), Path.Combine(root, "worker", ".venv", "Scripts", "python.exe") }
            : new[] { Path.Combine(root, "worker", ".venv", "bin", "python") };
        return candidates.FirstOrDefault(File.Exists);
    }

    public static bool IsInstalled => FindPython() is not null;

    /// <summary>Size of «Instalar soporte GPU» (worker\stt\requirements-gpu.txt), for the questions.</summary>
    public const string GpuSupportSize = "≈1.3 GB de descarga, ≈1.8 GB en disco";

    /// <summary>Where «Instalar soporte GPU» puts cuBLAS/cuDNN (1.4.6): the app's data folder («datos\soporte-gpu» in
    /// the portable version), not worker\python, so a new version of the app does not download them again.</summary>
    public static string GpuSupportFolder => AppPaths.PathOf("soporte-gpu");

    /// <summary>The NVIDIA libraries of «Instalar soporte GPU» are there. A CUDA toolkit installed on the PC also works
    /// for transcribe.py, but is not counted here.</summary>
    public static bool GpuSupportInstalled =>
        OperatingSystem.IsWindows() &&
        File.Exists(Path.Combine(GpuSupportFolder, "nvidia", "cublas", "bin", "cublas64_12.dll")) &&
        File.Exists(Path.Combine(GpuSupportFolder, "nvidia", "cudnn", "bin", "cudnn_ops64_9.dll"));

    /// <summary>
    /// Installs the local transcription with scripts\stt-setup.ps1 (1.4.0): the system Python in a venv, or the
    /// official embedded Python when the PC has none. Each line the script prints goes to <paramref name="progress"/>.
    /// <paramref name="gpu"/> also installs the NVIDIA support (stt-setup.ps1 -Gpu).
    /// </summary>
    public static async Task InstallAsync(Action<string> progress, CancellationToken cancellationToken, bool gpu = false)
    {
        var root = FindApplicationRoot();
        var script = Path.Combine(root, "scripts", "stt-setup.ps1");
        if (!File.Exists(script)) throw new FileNotFoundException("No se encontró scripts\\stt-setup.ps1 junto al programa.", script);
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = root
        };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command",
                     "[Console]::OutputEncoding = [Text.Encoding]::UTF8; & '" + script.Replace("'", "''") + "'" +
                     (gpu ? " -Gpu -GpuCarpeta '" + GpuSupportFolder.Replace("'", "''") + "'" : "") + "; exit $LASTEXITCODE" })
            start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var errors = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) progress(e.Data.Trim()); };
        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            lock (errors) errors.AppendLine(e.Data);
        };
        if (!process.Start()) throw new InvalidOperationException("No se pudo iniciar PowerShell para instalar la transcripción local.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try { await process.WaitForExitAsync(cancellationToken); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            throw;
        }
        process.WaitForExit(); // flush the redirected output
        if (process.ExitCode != 0 || !IsInstalled || (gpu && !GpuSupportInstalled))
        {
            string text;
            lock (errors) text = errors.ToString().Trim();
            throw new InvalidOperationException((gpu ? "No se pudo instalar el soporte GPU" : "No se pudo instalar la transcripción local") +
                (text.Length > 0 ? ": " + (text.Length > 1200 ? text[^1200..] : text) : " (revisa la conexión a internet)."));
        }
    }

    /// <param name="hint">Names and words to expect (Whisper initial prompt), e.g. "Personajes: Bart, Fluttershy."</param>
    public static async Task TranscribeAsync(IReadOnlyList<string> wavPaths, string model, string device,
        string language, Action<LocalTranscriptionResult> onResult, CancellationToken cancellationToken,
        string? hint = null, bool wordTimestamps = true)
    {
        var root = FindApplicationRoot();
        var script = Path.Combine(root, "worker", "stt", "transcribe.py");
        var python = FindPython() ?? throw new SttNotInstalledException("La transcripción local no está instalada.");
        if (wavPaths.Count == 0) return;

        var start = new ProcessStartInfo(python)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = root
        };
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        // Portable version: the Whisper models stay next to the .exe («datos\\modelos-stt») instead of the user profile.
        if (AppPaths.IsPortable) start.Environment["HF_HOME"] = AppPaths.PathOf("modelos-stt");
        start.ArgumentList.Add(script);
        start.ArgumentList.Add("--model"); start.ArgumentList.Add(model);
        start.ArgumentList.Add("--device"); start.ArgumentList.Add(device);
        start.ArgumentList.Add("--language"); start.ArgumentList.Add(language);
        // LOQUENDO_AI_STT_COMPUTE: float16, int8_float16 or int8 instead of the automatic choice.
        if (Environment.GetEnvironmentVariable("LOQUENDO_AI_STT_COMPUTE") is "float16" or "int8_float16" or "int8")
        {
            start.ArgumentList.Add("--compute");
            start.ArgumentList.Add(Environment.GetEnvironmentVariable("LOQUENDO_AI_STT_COMPUTE")!);
        }
        if (!string.IsNullOrWhiteSpace(hint)) { start.ArgumentList.Add("--hint"); start.ArgumentList.Add(hint); }
        if (wordTimestamps) start.ArgumentList.Add("--words");
        start.ArgumentList.Add("--gpu-dir"); start.ArgumentList.Add(GpuSupportFolder);
        foreach (var path in wavPaths)
        {
            start.ArgumentList.Add("--file");
            start.ArgumentList.Add(path);
        }

        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("No se pudo iniciar el transcriptor local.");
        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        var errors = process.StandardError.ReadToEndAsync();
        var received = new HashSet<int>();
        try
        {
            while (await process.StandardOutput.ReadLineAsync(cancellationToken) is string line)
            {
                var item = JsonSerializer.Deserialize<LocalTranscriptionResult>(line,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new InvalidDataException("El transcriptor devolvió una respuesta vacía.");
                if (item.Index < 0 || item.Index >= wavPaths.Count || !received.Add(item.Index))
                    throw new InvalidDataException("El transcriptor devolvió un índice de audio inválido.");
                onResult(item);
            }
            await process.WaitForExitAsync(cancellationToken);
            var diagnostics = await errors;
            // transcribe.py exits with 2 when faster-whisper cannot be imported (half-installed environment).
            if (process.ExitCode == 2 && received.Count == 0)
                throw new SttNotInstalledException("Falta faster-whisper en el Python de la transcripción local.");
            // 3: «cuda» without cuBLAS/cuDNN (checked before loading the model, so nothing was transcribed).
            if (process.ExitCode == 3 && received.Count == 0)
                throw new SttGpuMissingException("Falta el soporte GPU (cuBLAS/cuDNN de NVIDIA) de la transcripción local.");
            if (process.ExitCode != 0)
                throw new InvalidOperationException("El transcriptor local falló: " +
                    (diagnostics.Length > 1200 ? diagnostics[^1200..] : diagnostics));
            if (received.Count != wavPaths.Count)
                throw new InvalidDataException("El transcriptor terminó sin responder para todos los audios.");
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }

    private static string FindApplicationRoot() => TryApplicationRoot()
        ?? throw new FileNotFoundException("No se encontró worker\\stt\\transcribe.py. Ejecuta la app desde el proyecto completo.");

    private static string? TryApplicationRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; folder is not null && i < 10; i++, folder = folder.Parent)
        {
            var script = Path.Combine(folder.FullName, "worker", "stt", "transcribe.py");
            if (File.Exists(script)) return folder.FullName;
        }
        return null;
    }
}
