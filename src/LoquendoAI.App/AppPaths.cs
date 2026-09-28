using System.IO;

namespace LoquendoAI.App;

/// <summary>
/// Where the app keeps its own settings (1.3.0): %LOCALAPPDATA%\LoquendoAI when installed, or the «datos» folder next
/// to the .exe in the portable version (marked by a «portable.txt» file next to it). Projects always live where the
/// user created them. API keys are encrypted for the Windows user, so on another PC they must be entered again.
/// </summary>
internal static class AppPaths
{
    public static bool IsPortable { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.txt"));

    public static string DataFolder { get; } = IsPortable
        ? Path.Combine(AppContext.BaseDirectory, "datos")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "LoquendoAI");

    public static string PathOf(string name) => Path.Combine(DataFolder, name);
}
