using System.Diagnostics;
using System.Text;

namespace LoquendoAI.TtsBridge32;

internal static class BalconProvider
{
    internal static string? Find()
        => ProgramLocator.BalconCandidates().FirstOrDefault(File.Exists);

    internal static IReadOnlyList<string> GetVoices(string exe, bool sapi4Only = false)
    {
        var result = Run(exe, "-l", null, TimeSpan.FromSeconds(20));
        if (result.ExitCode != 0)
            throw new InvalidOperationException(BuildFailure("balcon -l", result));

        var lines = result.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (!sapi4Only)
            return lines.Where(x => !IsSectionHeader(x)).ToArray();

        var sapi4 = new List<string>();
        var section = VoiceSection.Unknown;
        var sawSections = false;
        foreach (var line in lines)
        {
            if (TryParseSection(line, out var nextSection))
            {
                section = nextSection;
                sawSections = true;
                continue;
            }

            if (section == VoiceSection.Sapi4)
                sapi4.Add(line);
        }

        // Older BALCON builds may omit the section headers. In that case keep
        // only obvious SAPI4 labels; if none exist, return the raw list so the
        // user can still select a voice and run the probe.
        if (!sawSections)
        {
            sapi4.AddRange(lines.Where(x => x.Contains("SAPI4", StringComparison.OrdinalIgnoreCase)
                                          || x.Contains("SAPI 4", StringComparison.OrdinalIgnoreCase)));
            if (sapi4.Count == 0)
                sapi4.AddRange(lines.Where(x => !IsSectionHeader(x)));
        }

        return sapi4.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static void Synthesize(string exe, SynthRequest request)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(request.Output))!);

        var sapi4 = request.Provider.Equals("balcon-sapi4", StringComparison.OrdinalIgnoreCase)
            || request.Provider.Equals("infovox-sapi4", StringComparison.OrdinalIgnoreCase);

        // SAPI4 is old enough that feeding BALCON a UTF-16LE text file is more
        // reliable than depending on the active ANSI code page.
        var tempText = Path.Combine(Path.GetTempPath(), $"loquendoai-balcon-{Guid.NewGuid():N}.txt");
        File.WriteAllText(tempText, request.Text, sapi4 ? Encoding.Unicode : new UTF8Encoding(false));
        try
        {
            var fullVoice = request.Voice.Trim();
            var preferredVoice = sapi4 ? SimplifySapi4VoiceName(fullVoice) : fullVoice;
            var output = Path.GetFullPath(request.Output);

            // For SAPI4, 50 is our UI neutral point. Do not send neutral -p/-s:
            // several legacy engines reject an otherwise harmless prosody call.
            var applyRate = request.Rate is int rate && (!sapi4 || rate != 50);
            var applyPitch = request.Pitch is int pitch && (!sapi4 || pitch != 50);

            var attempts = new List<(string Voice, bool Controls, string Encoding)>();
            attempts.Add((preferredVoice, true, sapi4 ? "unicode" : "utf8"));
            if (!preferredVoice.Equals(fullVoice, StringComparison.OrdinalIgnoreCase))
                attempts.Add((fullVoice, true, sapi4 ? "unicode" : "utf8"));

            // If prosody itself is the problem, a plain attempt tells us so and
            // lets the default Voice Lab profile work instead of failing on -p 50.
            if (sapi4 && (applyRate || applyPitch))
            {
                attempts.Add((preferredVoice, false, "unicode"));
                if (!preferredVoice.Equals(fullVoice, StringComparison.OrdinalIgnoreCase))
                    attempts.Add((fullVoice, false, "unicode"));
            }

            var failures = new List<string>();
            foreach (var attempt in attempts.Distinct())
            {
                try { if (File.Exists(output)) File.Delete(output); } catch { }

                var args = new StringBuilder();
                args.Append("-n ").Append(Quote(attempt.Voice)).Append(' ');
                args.Append("-f ").Append(Quote(tempText)).Append(' ');
                args.Append("-enc ").Append(attempt.Encoding).Append(' ');
                args.Append("-w ").Append(Quote(output)).Append(' ');

                if (attempt.Controls)
                {
                    if (sapi4)
                    {
                        if (applyRate)
                            args.Append("-s ").Append(Math.Clamp(request.Rate!.Value, 0, 100)).Append(' ');
                        if (applyPitch)
                            args.Append("-p ").Append(Math.Clamp(request.Pitch!.Value, 0, 100)).Append(' ');
                    }
                    else
                    {
                        if (request.Rate is int sapiRate)
                            args.Append("-s ").Append(Math.Clamp(sapiRate, -10, 10)).Append(' ');
                        if (request.Pitch is int sapiPitch)
                            args.Append("-p ").Append(Math.Clamp(sapiPitch, -10, 10)).Append(' ');
                        if (request.Volume is int volume)
                            args.Append("-v ").Append(Math.Clamp(volume, 0, 100)).Append(' ');
                    }
                }

                var command = args.ToString().TrimEnd();
                var result = Run(exe, command, null, TimeSpan.FromMinutes(2));
                if (result.ExitCode == 0 && File.Exists(output) && new FileInfo(output).Length > 44)
                {
                    if (sapi4 && !attempt.Controls && (applyRate || applyPitch))
                    {
                        throw new InvalidOperationException(
                            "La voz SAPI4 sí sintetiza, pero BALCON falla al aplicar pitch/velocidad. " +
                            "Déjala temporalmente en pitch 50 y velocidad Auto; el motor base está funcionando.");
                    }
                    return;
                }

                failures.Add($"voz='{attempt.Voice}', controles={(attempt.Controls ? "sí" : "no")}: {BuildFailure("balcon", result)}");
            }

            throw new InvalidOperationException(
                "BALCON no pudo sintetizar esta voz. " + string.Join(" | ", failures));
        }
        finally
        {
            try { File.Delete(tempText); } catch { }
        }
    }

    internal static string Probe(string exe, string voice)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"BALCON: {exe}");
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(exe);
            sb.AppendLine($"Version: {vi.FileVersion ?? vi.ProductVersion ?? "desconocida"}");
        }
        catch { }

        var dir = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory;
        foreach (var dep in new[] { "libsamplerate.dll", "chsdet.dll", "SoundTouch.dll" })
            sb.AppendLine($"Companion {dep}: {(File.Exists(Path.Combine(dir, dep)) ? "OK" : "no encontrado")}");

        var raw = Run(exe, "-l", null, TimeSpan.FromSeconds(20));
        sb.AppendLine($"-l exit: {raw.ExitCode}");
        if (!string.IsNullOrWhiteSpace(raw.StandardOutput))
        {
            sb.AppendLine("--- voces ---");
            sb.AppendLine(raw.StandardOutput.TrimEnd());
        }
        if (!string.IsNullOrWhiteSpace(raw.StandardError))
        {
            sb.AppendLine("--- stderr -l ---");
            sb.AppendLine(raw.StandardError.TrimEnd());
        }

        var simplified = SimplifySapi4VoiceName(voice);
        foreach (var candidate in new[] { simplified, voice }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var info = Run(exe, $"-n {Quote(candidate)} -m", null, TimeSpan.FromSeconds(20));
            sb.AppendLine($"-m voice='{candidate}' exit: {info.ExitCode}");
            if (!string.IsNullOrWhiteSpace(info.StandardOutput)) sb.AppendLine(info.StandardOutput.TrimEnd());
            if (!string.IsNullOrWhiteSpace(info.StandardError)) sb.AppendLine(info.StandardError.TrimEnd());
        }

        var tempOut = Path.Combine(Path.GetTempPath(), $"loquendoai-balcon-probe-{Guid.NewGuid():N}.wav");
        var tempText = Path.Combine(Path.GetTempPath(), $"loquendoai-balcon-probe-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(tempText, "Hola. Prueba de Infovox.", Encoding.Unicode);
            var cmd = $"-n {Quote(simplified)} -f {Quote(tempText)} -enc unicode -w {Quote(tempOut)}";
            var synth = Run(exe, cmd, null, TimeSpan.FromSeconds(45));
            sb.AppendLine($"plain synth voice='{simplified}' exit: {synth.ExitCode}");
            if (!string.IsNullOrWhiteSpace(synth.StandardOutput)) sb.AppendLine(synth.StandardOutput.TrimEnd());
            if (!string.IsNullOrWhiteSpace(synth.StandardError)) sb.AppendLine(synth.StandardError.TrimEnd());
            sb.AppendLine($"WAV: {(File.Exists(tempOut) ? new FileInfo(tempOut).Length + " bytes" : "no creado")}");
        }
        finally
        {
            try { File.Delete(tempOut); } catch { }
            try { File.Delete(tempText); } catch { }
        }

        return sb.ToString();
    }

    internal static string GetCompanionSummary(string exe)
    {
        var dir = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory;
        var missing = new[] { "libsamplerate.dll", "chsdet.dll", "SoundTouch.dll" }
            .Where(x => !File.Exists(Path.Combine(dir, x)))
            .ToArray();
        return missing.Length == 0
            ? "paquete auxiliar completo"
            : "faltan auxiliares del ZIP oficial: " + string.Join(", ", missing);
    }

    private static string SimplifySapi4VoiceName(string voice)
    {
        var idx = voice.IndexOf(" (", StringComparison.Ordinal);
        if (idx > 0)
            return voice[..idx].Trim();

        foreach (var marker in new[] { " SAPI4", " SAPI 4" })
        {
            idx = voice.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
                return voice[..idx].Trim();
        }
        return voice.Trim();
    }

    private static bool IsSectionHeader(string line)
        => TryParseSection(line, out _);

    private static bool TryParseSection(string line, out VoiceSection section)
    {
        var normalized = line.Trim().TrimEnd(':').Replace("  ", " ");
        if (normalized.Equals("SAPI 4", StringComparison.OrdinalIgnoreCase))
        {
            section = VoiceSection.Sapi4;
            return true;
        }
        if (normalized.Equals("SAPI 5", StringComparison.OrdinalIgnoreCase))
        {
            section = VoiceSection.Sapi5;
            return true;
        }
        if (normalized.Contains("Speech Platform", StringComparison.OrdinalIgnoreCase))
        {
            section = VoiceSection.SpeechPlatform;
            return true;
        }
        section = VoiceSection.Unknown;
        return false;
    }

    private static string BuildFailure(string operation, (int ExitCode, string StandardOutput, string StandardError) result)
    {
        var details = new List<string> { $"{operation} terminó con {result.ExitCode}" };
        if (!string.IsNullOrWhiteSpace(result.StandardError))
            details.Add("stderr: " + result.StandardError.Trim());
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            details.Add("stdout: " + result.StandardOutput.Trim());
        return string.Join("; ", details);
    }

    private static (int ExitCode, string StandardOutput, string StandardError) Run(string exe, string arguments, string? stdin, TimeSpan timeout)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin is not null,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };
        process.Start();
        if (stdin is not null)
        {
            process.StandardInput.Write(stdin);
            process.StandardInput.Close();
        }
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { process.Kill(true); } catch { }
            throw new TimeoutException($"{Path.GetFileName(exe)} excedió {timeout.TotalSeconds:0}s.");
        }
        Task.WaitAll(stdoutTask, stderrTask);
        return (process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private enum VoiceSection
    {
        Unknown,
        Sapi4,
        Sapi5,
        SpeechPlatform
    }
}
