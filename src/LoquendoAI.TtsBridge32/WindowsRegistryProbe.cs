using Microsoft.Win32;

namespace LoquendoAI.TtsBridge32;

internal static class WindowsRegistryProbe
{
    internal static string? FindLoquendoDataPath()
    {
        const string subKey = @"SOFTWARE\Loquendo\LTTS7\Engine";
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Default })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.OpenSubKey(subKey);
                    if (key?.GetValue("DataPath") is string path && !string.IsNullOrWhiteSpace(path))
                        return Environment.ExpandEnvironmentVariables(path.Trim());
                }
                catch
                {
                    // Probe only. A missing/inaccessible registry view is not fatal.
                }
            }
        }
        return null;
    }

    internal static string? FindInfovoxInstallPath()
    {
        var candidates = new[]
        {
            @"SOFTWARE\Acapela Group\Infovox Desktop\HW2L\Infovox Desktop Pro Install",
            @"SOFTWARE\Acapela Group\Infovox Desktop\HW2L"
        };

        foreach (var subKey in candidates)
        {
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                foreach (var view in new[] { RegistryView.Registry32, RegistryView.Default })
                {
                    try
                    {
                        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                        using var key = baseKey.OpenSubKey(subKey);
                        if (key is null) continue;

                        foreach (var name in new[] { "Path", "InstallPath", "InstallDir", "RootDir", "DataPath" })
                        {
                            if (key.GetValue(name) is string path && Directory.Exists(path))
                                return Environment.ExpandEnvironmentVariables(path.Trim());
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var conventional = Path.Combine(programFilesX86, "Acapela Group", "Infovox Desktop 2.2");
        return Directory.Exists(conventional) ? conventional : null;
    }

    internal static IReadOnlyList<string> EnumerateSapi5RegistryVoices()
    {
        var result = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Default })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var voices = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Speech\Voices\Tokens");
                if (voices is null) continue;
                foreach (var tokenName in voices.GetSubKeyNames())
                {
                    using var token = voices.OpenSubKey(tokenName);
                    var display = token?.GetValue(null) as string;
                    result.Add(string.IsNullOrWhiteSpace(display) ? tokenName : display);
                }
            }
            catch
            {
            }
        }
        return result.ToArray();
    }
}
