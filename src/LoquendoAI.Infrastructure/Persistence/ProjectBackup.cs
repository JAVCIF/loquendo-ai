using System.Globalization;
using Microsoft.Data.Sqlite;

namespace LoquendoAI.Infrastructure.Persistence;

/// <summary>
/// Copies of project.db in &lt;proyecto&gt;\backups. Made with SQLite's VACUUM INTO: a consistent
/// snapshot that includes changes still in the WAL file, taken from a separate read-only
/// connection so the open project keeps working. One automatic copy per day when the project is
/// opened, and one before every database migration.
/// To restore: close Loquendo AI, rename project.db (and project.db-wal / -shm if present) and copy
/// the backup as project.db.
/// </summary>
public static class ProjectBackup
{
    public const string AutomaticReason = "auto";
    public const int KeepPerReason = 10;

    public static string Folder(string projectRoot) => Path.Combine(projectRoot, "backups");

    /// <summary>Creates a copy unless one for the same reason is newer than <paramref name="skipIfNewerThan"/>.
    /// Returns the new file, or null when nothing was copied.</summary>
    public static async Task<string?> CreateAsync(string projectRoot, string reason, TimeSpan? skipIfNewerThan = null,
        CancellationToken cancellationToken = default)
    {
        var database = Path.Combine(projectRoot, "project.db");
        if (!File.Exists(database) || new FileInfo(database).Length == 0) return null;
        var safeReason = new string(reason.Select(c => char.IsAsciiLetterOrDigit(c) || c == '-' ? c : '-').ToArray());
        var folder = Folder(projectRoot);
        Directory.CreateDirectory(folder);
        if (skipIfNewerThan is { } age && Existing(folder, safeReason).FirstOrDefault() is { } latest &&
            DateTime.UtcNow - File.GetLastWriteTimeUtc(latest) < age)
            return null;

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var target = Path.Combine(folder, $"project_{stamp}_{safeReason}.db");
        for (var n = 2; File.Exists(target); n++)
            target = Path.Combine(folder, $"project_{stamp}_{safeReason}_{n}.db");
        var temporary = target + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = database,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString()))
            {
                await connection.OpenAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = "VACUUM INTO $target;";
                command.Parameters.AddWithValue("$target", temporary);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            File.Move(temporary, target);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        Prune(folder, safeReason, KeepPerReason);
        return target;
    }

    /// <summary>Newest first.</summary>
    internal static IEnumerable<string> Existing(string folder, string reason) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "project_*_" + reason + "*.db")
                .Where(path => BelongsTo(Path.GetFileNameWithoutExtension(path), reason))
                .OrderByDescending(File.GetLastWriteTimeUtc).ThenByDescending(x => x, StringComparer.Ordinal)
            : [];

    // "project_20260925_231500_auto" or "project_20260925_231500_auto_2"; "auto" must not match "antes-v4".
    private static bool BelongsTo(string name, string reason)
    {
        var parts = name.Split('_');
        return parts.Length >= 4 && parts[0] == "project" && parts[3] == reason;
    }

    internal static void Prune(string folder, string reason, int keep)
    {
        foreach (var old in Existing(folder, reason).Skip(keep))
            try { File.Delete(old); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
