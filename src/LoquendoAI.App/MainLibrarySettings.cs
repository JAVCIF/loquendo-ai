using System.IO;
using System.Text.Json;

namespace LoquendoAI.App;

/// <summary>
/// «Biblioteca principal» (1.4.8), kept by the program, not by a project (biblioteca-principal.json next to ui.json):
/// the project whose library the others follow, which projects follow it (by project id, so a moved project keeps
/// following) and the main database each one last received, to skip a sync when nothing changed.
/// </summary>
internal sealed class MainLibrarySettings
{
    private static readonly string SettingsPath = AppPaths.PathOf("biblioteca-principal.json");

    /// <summary>Folder of the main project; null: none.</summary>
    public string? Project { get; set; }
    public Guid? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public List<Guid> Followers { get; set; } = [];
    /// <summary>Per follower: the write time (UTC ticks) of the main database it last received.</summary>
    public Dictionary<Guid, long> Received { get; set; } = [];

    public bool Follows(Guid projectId) => Followers.Contains(projectId) && ProjectId != projectId;

    public static MainLibrarySettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<MainLibrarySettings>(File.ReadAllText(SettingsPath)) ?? new MainLibrarySettings();
        }
        catch (Exception) { /* Damaged file: no main library. */ }
        return new MainLibrarySettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { ErrorLog.Record(ex); }
    }

    /// <summary>When the main project's database last changed (with its WAL file); 0 if it is not there.</summary>
    public static long DatabaseStamp(string projectRoot)
    {
        long Stamp(string file) => File.Exists(file) ? File.GetLastWriteTimeUtc(file).Ticks : 0;
        var database = Path.Combine(projectRoot, "project.db");
        return Math.Max(Stamp(database), Stamp(database + "-wal"));
    }
}
