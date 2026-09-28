using System.Diagnostics;

namespace LoquendoAI.TtsBridge32;

internal static class ProgramLocator
{
    internal static ProviderInfo InspectExecutable(string key, string displayName, IEnumerable<string> candidates)
    {
        foreach (var candidate in candidates.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var expanded = Environment.ExpandEnvironmentVariables(candidate);
            if (!File.Exists(expanded)) continue;
            try
            {
                var info = FileVersionInfo.GetVersionInfo(expanded);
                var version = info.FileVersion ?? info.ProductVersion;
                return new ProviderInfo(key, displayName, true, "Aplicación detectada", expanded, version);
            }
            catch
            {
                return new ProviderInfo(key, displayName, true, "Aplicación detectada", expanded);
            }
        }

        return new ProviderInfo(key, displayName, false, "No detectado automáticamente");
    }

    internal static IEnumerable<string> BalabolkaCandidates()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var pf64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Path.Combine(pf, "Balabolka", "balabolka.exe");
        yield return Path.Combine(pf64, "Balabolka", "balabolka.exe");
        yield return Path.Combine(AppContext.BaseDirectory, "balabolka.exe");
    }

    internal static IEnumerable<string> BalconCandidates()
    {
        var fromEnv = Environment.GetEnvironmentVariable("LOQUENDO_AI_BALCON");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            yield return fromEnv;

        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var pf64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Path.Combine(pf, "Balabolka", "balcon.exe");
        yield return Path.Combine(pf64, "Balabolka", "balcon.exe");
        yield return Path.Combine(AppContext.BaseDirectory, "balcon.exe");

        // Portable/project layout: tools\balcon\balcon.exe, walking up from the published bridge.
        var cursor = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && cursor is not null; i++, cursor = cursor.Parent)
            yield return Path.Combine(cursor.FullName, "tools", "balcon", "balcon.exe");
    }

    internal static IEnumerable<string> TextAloudCandidates()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var pf64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        foreach (var root in new[] { pf, pf64 })
        {
            yield return Path.Combine(root, "TextAloud", "TextAloud.exe");
            yield return Path.Combine(root, "NextUp-TextAloud", "TextAloudMP3.exe");
            yield return Path.Combine(root, "NextUp.com", "TextAloud", "TextAloud.exe");
            yield return Path.Combine(root, "NextUp.com", "TextAloud 4", "TextAloud.exe");
        }
    }

    internal static IEnumerable<string> SpeechPadCandidates(string? infovoxPath)
    {
        if (!string.IsNullOrWhiteSpace(infovoxPath))
            yield return Path.Combine(infovoxPath, "SpeechPad.exe");
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        yield return Path.Combine(pf, "Acapela Group", "Infovox Desktop 2.2", "SpeechPad.exe");
    }
}
