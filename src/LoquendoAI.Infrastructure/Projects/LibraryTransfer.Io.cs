using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace LoquendoAI.Infrastructure.Projects;

/// <summary>Reading and writing of <see cref="LibraryTransfer"/> (the merge itself is in LibraryTransfer.cs).</summary>
public static partial class LibraryTransfer
{
    /// <summary>Reads the other project and applies its library to <paramref name="into"/>.</summary>
    public static async Task<LibraryTransferPlan> ImportAsync(string fromProjectRoot, SqliteProjectRepository into,
        LibraryTransferOptions options, CancellationToken cancellationToken = default)
    {
        var from = await ReadProjectAsync(fromProjectRoot, cancellationToken);
        var plan = Plan(from, await ReadAsync(into, cancellationToken), options);
        if (!plan.IsEmpty) await into.ApplyLibraryTransferAsync(plan, cancellationToken);
        return plan;
    }

    /// <summary>The snapshot of an open project.</summary>
    public static async Task<LibrarySnapshot> ReadAsync(SqliteProjectRepository repository, CancellationToken cancellationToken = default)
    {
        var sources = await repository.GetAssetSourcesAsync(cancellationToken);
        var rules = new List<FolderRule>();
        foreach (var source in sources) rules.AddRange(await repository.GetFolderRulesAsync(source.Id, cancellationToken));
        return new LibrarySnapshot(sources, rules, await repository.GetAssetsAsync(cancellationToken),
            await repository.GetAllAssetTagsAsync(cancellationToken), await repository.GetVoiceProfilesAsync(cancellationToken),
            await repository.GetCharactersAsync(cancellationToken));
    }

    /// <summary>Reads another project from a copy of its database (migrated, if it is older), leaving it untouched.</summary>
    public static async Task<LibrarySnapshot> ReadProjectAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        var database = Path.Combine(projectRoot, ProjectLayout.DatabaseFileName);
        if (!File.Exists(database))
            throw new FileNotFoundException("Esa carpeta no es un proyecto de Loquendo AI (no tiene project.db).", database);
        var folder = Path.Combine(Path.GetTempPath(), "LoquendoAI-biblioteca-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                         { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            {
                await connection.OpenAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = "VACUUM INTO $target;";
                command.Parameters.AddWithValue("$target", Path.Combine(folder, ProjectLayout.DatabaseFileName));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            var manifest = new ProjectManifest(Guid.NewGuid(), "copia", ProjectManifest.CurrentSchemaVersion,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            await using var copy = new SqliteProjectRepository(folder, manifest);
            await copy.InitializeAsync(cancellationToken);
            return await ReadAsync(copy, cancellationToken);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(folder, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* A temporary copy left behind. */ }
        }
    }
}
