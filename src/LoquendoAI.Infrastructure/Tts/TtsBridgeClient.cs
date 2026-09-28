using System.Diagnostics;
using System.Text;

namespace LoquendoAI.Infrastructure.Tts;

public sealed record TtsPreviewRequest(
    string ProviderKey,
    string VoiceId,
    string Text,
    string OutputPath,
    int? Pitch = null,
    int? Speed = null,
    int Volume = 100,
    int SampleRate = 32000);

public sealed class TtsBridgeClient
{
    public string? BridgePath => FindBridgeExecutable();

    public async Task<IReadOnlyList<string>> GetVoicesAsync(string providerKey, CancellationToken cancellationToken = default)
    {
        var exe = RequireBridge();
        var result = await RunAsync(exe,
            ["voices", "--provider", providerKey],
            cancellationToken).ConfigureAwait(false);

        return result.StdOut
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("[LTTS7]", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<string> SynthesizeAsync(TtsPreviewRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProviderKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.VoiceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Text);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputPath);

        var exe = RequireBridge();
        var output = Path.GetFullPath(request.OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        var textFile = Path.Combine(Path.GetTempPath(), $"loquendoai-preview-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(textFile, request.Text, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        try
        {
            var args = new List<string>
            {
                "synth",
                "--provider", request.ProviderKey,
                "--voice", request.VoiceId,
                "--text-file", textFile,
                "--out", output,
                "--volume", Math.Clamp(request.Volume, 0, 100).ToString(),
                "--sample-rate", request.SampleRate.ToString()
            };
            if (request.Speed is int speed)
            {
                args.Add("--rate");
                args.Add(speed.ToString());
            }
            if (request.Pitch is int pitch)
            {
                args.Add("--pitch");
                args.Add(pitch.ToString());
            }

            await RunAsync(exe, args, cancellationToken, SynthesisTimeout(request.Text)).ConfigureAwait(false);
            if (!File.Exists(output) || new FileInfo(output).Length <= 44)
                throw new IOException("El TTS bridge terminó sin dejar un WAV válido.");
            return output;
        }
        finally
        {
            try { File.Delete(textFile); } catch { }
        }
    }

    /// <summary>
    /// A hung engine (a modal dialog in a SAPI voice, a stuck Loquendo session) used to block
    /// voice generation forever. Default: 60 s plus 1 s per 12 characters, at most 10 minutes;
    /// LOQUENDO_AI_TTS_TIMEOUT_SECONDS sets a fixed limit (0 disables it).
    /// </summary>
    public static TimeSpan? SynthesisTimeout(string text)
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("LOQUENDO_AI_TTS_TIMEOUT_SECONDS"), out var fixedSeconds))
            return fixedSeconds <= 0 ? null : TimeSpan.FromSeconds(Math.Min(fixedSeconds, 3600));
        return TimeSpan.FromSeconds(Math.Min(600, 60 + (text?.Length ?? 0) / 12));
    }

    private string RequireBridge()
        => FindBridgeExecutable() ?? throw new FileNotFoundException(
            "No se encontró LoquendoAI.TtsBridge32.exe. Ejecuta scripts\\tts-publish.cmd una vez o define LOQUENDO_AI_TTS_BRIDGE con la ruta del bridge.");

    private static string? FindBridgeExecutable()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("LOQUENDO_AI_TTS_BRIDGE");
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && File.Exists(fromEnvironment))
            return Path.GetFullPath(fromEnvironment);

        var direct = Path.Combine(AppContext.BaseDirectory, "LoquendoAI.TtsBridge32.exe");
        if (File.Exists(direct))
            return direct;

        // Published version (scripts\publicar.ps1): the x86 bridge in its own folder next to the x64 app.
        var published = Path.Combine(AppContext.BaseDirectory, "tts-bridge", "LoquendoAI.TtsBridge32.exe");
        if (File.Exists(published))
            return published;

        var cursor = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && cursor is not null; i++, cursor = cursor.Parent)
        {
            var candidate = Path.Combine(cursor.FullName, "artifacts", "ttsbridge-win-x86", "LoquendoAI.TtsBridge32.exe");
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static async Task<ProcessResult> RunAsync(string exe, IReadOnlyList<string> args, CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory
            }
        };
        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout is { } maximum) limit.CancelAfter(maximum);
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(limit.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(limit.Token);
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Kill the whole tree: the bridge runs Loquendo in a native-synth-worker child.
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            if (!cancellationToken.IsCancellationRequested && timeout is { } elapsed)
                throw new TimeoutException($"El TTS no respondió en {elapsed.TotalSeconds:0} s y se detuvo. " +
                    "Revisa la voz en Voice Lab o ajusta LOQUENDO_AI_TTS_TIMEOUT_SECONDS.");
            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
                ? $"TTS bridge terminó con código {process.ExitCode}."
                : detail.Trim());
        }
        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}
