namespace LoquendoAI.Infrastructure;

/// <summary>Folder for the app's own files (caches, VEGAS catalog): %LOCALAPPDATA%\LoquendoAI, or the portable
/// «datos» folder the app sets at startup (1.3.0).</summary>
public static class AppDataFolder
{
    public static string Folder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "LoquendoAI");

    public static string PathOf(string name) => Path.Combine(Folder, name);
}
