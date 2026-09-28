using System.Globalization;
using LoquendoAI.Core.Abstractions;
using LoquendoAI.Core.Models;
using Microsoft.Data.Sqlite;

namespace LoquendoAI.Infrastructure.Persistence;

public sealed class SqliteProjectRepository : IProjectRepository
{
    private readonly SqliteConnection _connection;

    public string ProjectRoot { get; }
    public ProjectManifest Manifest { get; }

    public SqliteProjectRepository(string projectRoot, ProjectManifest manifest)
    {
        ProjectRoot = Path.GetFullPath(projectRoot);
        Manifest = manifest;
        var dbPath = Path.Combine(ProjectRoot, "project.db");
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            // Private cache (the default). Shared-cache mode is discouraged by SQLite and takes
            // table-level locks that defeat WAL's concurrent readers.
            Cache = SqliteCacheMode.Default
        }.ToString());
    }

    /// <summary>Schema this build writes (single source: ProjectManifest.CurrentSchemaVersion).</summary>
    internal const int LatestSchemaVersion = ProjectManifest.CurrentSchemaVersion;

    /// <summary>Version stored in the database (schema_info), after migrating.</summary>
    public int SchemaVersion { get; private set; }

    /// <summary>True when the FTS5 index exists; otherwise searches use LIKE.</summary>
    public bool FullTextSearchAvailable { get; private set; }

    /// <summary>Why the full-text index could not be created, if it failed (the project still opens).</summary>
    public string? FullTextSearchError { get; private set; }

    private static readonly (int Version, string Sql)[] Migrations =
    [
        (2, DatabaseSchema.V2), (3, DatabaseSchema.V3), (4, DatabaseSchema.V4), (5, DatabaseSchema.V5),
        (6, DatabaseSchema.V6), (7, DatabaseSchema.V7)
    ];

    /// <summary>Opens the project database, creating or migrating it to LatestSchemaVersion.
    /// A database written by a newer build is refused instead of being modified.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var databasePath = Path.Combine(ProjectRoot, "project.db");
        var existingDatabase = File.Exists(databasePath) && new FileInfo(databasePath).Length > 0;
        await _connection.OpenAsync(cancellationToken);
        await ExecuteAsync(DatabaseSchema.Pragmas, cancellationToken);

        if (!await TableExistsAsync("schema_info", cancellationToken))
            await ApplyMigrationAsync(DatabaseSchema.V1, cancellationToken);

        var schemaVersion = await GetSchemaVersionAsync(cancellationToken);
        if (schemaVersion > LatestSchemaVersion)
            throw new NotSupportedException($"La base de datos del proyecto usa schema v{schemaVersion}, pero esta versión soporta hasta v{LatestSchemaVersion}. Actualiza Loquendo AI para abrirlo.");
        if (existingDatabase && schemaVersion < LatestSchemaVersion)
            // One copy per pending version: a migration that keeps failing does not copy on every open.
            await ProjectBackup.CreateAsync(ProjectRoot, $"antes-v{schemaVersion}", TimeSpan.FromHours(20), cancellationToken);

        foreach (var (version, sql) in Migrations)
        {
            if (schemaVersion >= version) continue;
            if (version == 6)
            {
                // The search index is an improvement, not a requirement: without FTS5 the version still
                // advances (so later migrations run) and the index is retried below on every open.
                try { await ApplyMigrationAsync(sql, cancellationToken); }
                catch (SqliteException ex)
                {
                    FullTextSearchError = ex.Message;
                    await ApplyMigrationAsync("UPDATE schema_info SET version = 6;", cancellationToken);
                }
            }
            else if (version == 7 && await TableExistsAsync("asset_search", cancellationToken))
                // Replaces the beta.3 search triggers, which failed on UPSERT (see DatabaseSchema.SearchTriggers).
                await ApplyMigrationAsync(sql + "\n" + DatabaseSchema.SearchTriggers, cancellationToken);
            else await ApplyMigrationAsync(sql, cancellationToken);
            schemaVersion = version;
        }
        SchemaVersion = schemaVersion;

        FullTextSearchAvailable = await TableExistsAsync("asset_search", cancellationToken);
        if (!FullTextSearchAvailable && FullTextSearchError is null)
        {
            try
            {
                await ApplyMigrationAsync(DatabaseSchema.SearchIndex, cancellationToken);
                FullTextSearchAvailable = true;
            }
            catch (SqliteException ex) { FullTextSearchError = ex.Message; }
        }

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
INSERT INTO project_info(id, name, created_utc, updated_utc)
VALUES($id, $name, $created, $updated)
ON CONFLICT(id) DO UPDATE SET
    name = excluded.name,
    updated_utc = excluded.updated_utc;
""";
        cmd.Parameters.AddWithValue("$id", Manifest.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$name", Manifest.Name);
        cmd.Parameters.AddWithValue("$created", Manifest.CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$updated", Manifest.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Second connection to a project that is already open and migrated (background scans, bulk
    /// edits): applies the connection settings only, without the migration and backup checks.
    /// Falls back to InitializeAsync if the database is not at LatestSchemaVersion.
    /// </summary>
    public async Task AttachAsync(CancellationToken cancellationToken = default)
    {
        await _connection.OpenAsync(cancellationToken);
        await ExecuteAsync(DatabaseSchema.Pragmas, cancellationToken);
        var current = await TableExistsAsync("schema_info", cancellationToken) &&
            await GetSchemaVersionAsync(cancellationToken) == LatestSchemaVersion;
        if (!current)
        {
            await _connection.CloseAsync();
            await InitializeAsync(cancellationToken);
            return;
        }
        SchemaVersion = LatestSchemaVersion;
        FullTextSearchAvailable = await TableExistsAsync("asset_search", cancellationToken);
    }

    private async Task ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<bool> TableExistsAsync(string name, CancellationToken cancellationToken)
    {
        await using var probe = _connection.CreateCommand();
        probe.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        probe.Parameters.AddWithValue("$name", name);
        return Convert.ToInt64(await probe.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0;
    }

    /// <summary>Every audio reference in the script (all scenes): generated/imported WAV path,
    /// voice hash and parameters (which may name a kept recording). Used by the cache cleanup.</summary>
    public Task<IReadOnlyList<Projects.ScriptAudioReference>> GetScriptAudioReferencesAsync(CancellationToken cancellationToken = default) => ReadAsync<IReadOnlyList<Projects.ScriptAudioReference>>(async connection =>
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT generated_audio_path, generated_audio_hash, parameters_json FROM scene_script_blocks;";
        var result = new List<Projects.ScriptAudioReference>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new Projects.ScriptAudioReference(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2)));
        return result;
    }, cancellationToken);

    private async Task<int> GetSchemaVersionAsync(CancellationToken cancellationToken)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(version), 1) FROM schema_info;";
        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private async Task ApplyMigrationAsync(string sql, CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var migration = _connection.CreateCommand();
            migration.Transaction = transaction;
            migration.CommandText = sql;
            await migration.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public Task<IReadOnlyList<AssetRecord>> GetAssetsAsync(CancellationToken cancellationToken = default) => ReadAsync<IReadOnlyList<AssetRecord>>(async connection =>
    {
        var result = new List<AssetRecord>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = AssetSelectSql + " ORDER BY display_name;";
        await ReadAssetsAsync(cmd, result, cancellationToken);
        return result;
    }, cancellationToken);

    public Task<IReadOnlyList<AssetFolder>> GetSpriteFoldersForCharacterAsync(string characterName, CancellationToken cancellationToken = default) => ReadAsync<IReadOnlyList<AssetFolder>>(async connection =>
    {
        if (string.IsNullOrWhiteSpace(characterName)) return Array.Empty<AssetFolder>();
        var name = characterName.Trim();
        var escaped = EscapeLike(name);
        var exact = new Dictionary<(Guid, string), AssetFolder>();
        var bySubject = new Dictionary<(Guid, string), AssetFolder>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
SELECT source_id, source_relative_path, subject_name FROM assets
WHERE kind = $kind AND is_missing = 0 AND source_id IS NOT NULL AND source_relative_path IS NOT NULL
  AND (source_relative_path LIKE $root ESCAPE '!' COLLATE NOCASE
       OR source_relative_path LIKE $nested ESCAPE '!' COLLATE NOCASE
       OR subject_name = $name COLLATE NOCASE);
""";
        cmd.Parameters.AddWithValue("$kind", (int)AssetKind.CharacterSprite);
        cmd.Parameters.AddWithValue("$root", escaped + "/%");
        cmd.Parameters.AddWithValue("$nested", "%/" + escaped + "/%");
        cmd.Parameters.AddWithValue("$name", name);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var source = Guid.Parse(reader.GetString(0));
            var path = reader.GetString(1).Replace('\\', '/');
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (!string.Equals(parts[i], name, StringComparison.OrdinalIgnoreCase)) continue;
                var folder = string.Join('/', parts.Take(i + 1));
                exact[(source, folder.ToUpperInvariant())] = new AssetFolder(source, folder);
            }
            if (!reader.IsDBNull(2) && string.Equals(reader.GetString(2), name, StringComparison.OrdinalIgnoreCase))
            {
                var folder = string.Join('/', parts[..^1]);
                bySubject[(source, folder.ToUpperInvariant())] = new AssetFolder(source, folder);
            }
        }
        // Exact directory names take priority over a broad inherited subject rule.
        return (exact.Count > 0 ? exact.Values : bySubject.Values)
            .OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase).Take(300).ToArray();
    }, cancellationToken);

    public Task<IReadOnlyList<Guid>> GetAssetSourceIdsByKindsAsync(IReadOnlyCollection<AssetKind> kinds, CancellationToken cancellationToken = default) => ReadAsync<IReadOnlyList<Guid>>(async connection =>
    {
        if (kinds.Count == 0) return Array.Empty<Guid>();
        await using var cmd = connection.CreateCommand();
        var kindParams = BindAssetKinds(cmd, kinds);
        cmd.CommandText = $"SELECT DISTINCT source_id FROM assets WHERE kind IN ({kindParams}) AND is_missing = 0 AND source_id IS NOT NULL ORDER BY source_id;";
        var result = new List<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(Guid.Parse(reader.GetString(0)));
        return result;
    }, cancellationToken);

    public Task<IReadOnlyList<string>> GetAssetSubfoldersAsync(IReadOnlyCollection<AssetKind> kinds, AssetFolder parent, CancellationToken cancellationToken = default) => ReadAsync<IReadOnlyList<string>>(async connection =>
    {
        if (kinds.Count == 0) return Array.Empty<string>();
        var path = parent.RelativePath.Replace('\\', '/').Trim('/');
        var prefix = path.Length == 0 ? "" : path + "/";
        await using var cmd = connection.CreateCommand();
        var kindParams = BindAssetKinds(cmd, kinds);
        var rest = "substr(source_relative_path, length($prefix) + 1)";
        cmd.CommandText = $"SELECT DISTINCT substr({rest}, 1, instr({rest}, '/') - 1) AS child " +
            $"FROM assets WHERE kind IN ({kindParams}) AND is_missing = 0 AND source_id = $source " +
            $"AND source_relative_path LIKE $path ESCAPE '!' COLLATE NOCASE AND instr({rest}, '/') > 0 " +
            "ORDER BY child COLLATE NOCASE LIMIT 300;";
        cmd.Parameters.AddWithValue("$source", parent.SourceId.ToString("D"));
        cmd.Parameters.AddWithValue("$prefix", prefix);
        cmd.Parameters.AddWithValue("$path", EscapeLike(prefix) + "%");
        var result = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(reader.GetString(0));
        return result;
    }, cancellationToken);

    public Task<IReadOnlyList<AssetRecord>> SearchAssetsInFolderAsync(IReadOnlyCollection<AssetKind> kinds, AssetFolder folder,
        bool includeSubfolders, string search, int limit, CancellationToken cancellationToken = default) => ReadAsync<IReadOnlyList<AssetRecord>>(async connection =>
    {
        if (kinds.Count == 0) return Array.Empty<AssetRecord>();
        var path = folder.RelativePath.Replace('\\', '/').Trim('/');
        var prefix = path.Length == 0 ? "" : path + "/";
        await using var cmd = connection.CreateCommand();
        var kindParams = BindAssetKinds(cmd, kinds);
        cmd.CommandText = AssetSelectSql + $" WHERE kind IN ({kindParams}) AND is_missing = 0 AND source_id = $source " +
            "AND source_relative_path LIKE $path ESCAPE '!' COLLATE NOCASE " +
            "AND ($recursive = 1 OR instr(substr(source_relative_path, length($prefix) + 1), '/') = 0) " +
            "AND display_name LIKE $search ESCAPE '!' COLLATE NOCASE ORDER BY display_name COLLATE NOCASE, id LIMIT $limit;";
        cmd.Parameters.AddWithValue("$source", folder.SourceId.ToString("D"));
        cmd.Parameters.AddWithValue("$prefix", prefix);
        cmd.Parameters.AddWithValue("$path", EscapeLike(prefix) + "%");
        cmd.Parameters.AddWithValue("$recursive", includeSubfolders ? 1 : 0);
        cmd.Parameters.AddWithValue("$search", "%" + EscapeLike(search.Trim()) + "%");
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 151));
        var result = new List<AssetRecord>();
        await ReadAssetsAsync(cmd, result, cancellationToken);
        return result;
    }, cancellationToken);

    // The Director searches the catalog by the filename supplied in the script.
    // display_name intentionally has no extension, and a visual file may belong to
    // another library category (for example, a GIF classified as a render).
    public Task<IReadOnlyList<AssetRecord>> SearchDirectorAssetsAsync(string name, int limit = 300,
        CancellationToken cancellationToken = default) => ReadAsync<IReadOnlyList<AssetRecord>>(async connection =>
    {
        if (string.IsNullOrWhiteSpace(name)) return Array.Empty<AssetRecord>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = AssetSelectSql + " WHERE is_missing = 0 AND (" +
            "display_name LIKE $search ESCAPE '!' COLLATE NOCASE OR " +
            "source_relative_path LIKE $search ESCAPE '!' COLLATE NOCASE) " +
            "ORDER BY CASE WHEN display_name = $name COLLATE NOCASE THEN 0 ELSE 1 END, " +
            "length(display_name), display_name COLLATE NOCASE, id LIMIT $limit;";
        cmd.Parameters.AddWithValue("$name", name.Trim());
        cmd.Parameters.AddWithValue("$search", "%" + EscapeLike(name.Trim()) + "%");
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        var result = new List<AssetRecord>();
        await ReadAssetsAsync(cmd, result, cancellationToken);
        return result;
    }, cancellationToken);

    /// <summary>Everything the AI Director needs from the catalog (off the UI thread, see ReadAsync).</summary>
    public Task<(IReadOnlyList<AssetRecord> Assets, IReadOnlyList<AssetTag> Tags)> GetDirectorCatalogSnapshotAsync(
        CancellationToken cancellationToken = default) => ReadAsync<(IReadOnlyList<AssetRecord> Assets, IReadOnlyList<AssetTag> Tags)>(async connection =>
    {
        var assets = new List<AssetRecord>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = AssetSelectSql + " WHERE is_missing = 0 ORDER BY display_name;";
            await ReadAssetsAsync(cmd, assets, cancellationToken);
        }
        var tags = new List<AssetTag>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT asset_id, key, value, confidence FROM asset_tags WHERE key LIKE 'director.%';";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                tags.Add(new AssetTag(Guid.Parse(reader.GetString(0)), reader.GetString(1),
                    reader.GetString(2), reader.GetDouble(3)));
        }
        return (assets, tags);
    }, cancellationToken);

    /// <summary>Asset IDs matching every word (prefix, accent-insensitive) in name, path or Director
    /// tags, best first. Null when the index is unavailable or the text has no searchable words.</summary>
    public Task<IReadOnlyList<Guid>?> SearchAssetIdsAsync(string text, int limit = 20_000,
        CancellationToken cancellationToken = default) => ReadAsync<IReadOnlyList<Guid>?>(async connection =>
    {
        if (!FullTextSearchAvailable || AssetSearchText.FtsMatch(text) is not { } match) return null;
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = AssetSearchText.RankedHits + "SELECT hit_id FROM hits ORDER BY score LIMIT $limit;";
            cmd.Parameters.AddWithValue("$match", match);
            cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 100_000));
            var result = new List<Guid>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                if (Guid.TryParse(reader.GetString(0), out var id)) result.Add(id);
            return result;
        }
        catch (SqliteException) { return null; }
    }, cancellationToken);

    public Task<IReadOnlyList<AssetPickerItem>> SearchAssetsForPickerAsync(AssetPickerQuery query,
        CancellationToken cancellationToken = default) => ReadAsync<IReadOnlyList<AssetPickerItem>>(async connection =>
    {
        if (query.Extensions.Count == 0) return Array.Empty<AssetPickerItem>();
        var assets = new List<AssetRecord>();
        await using (var cmd = connection.CreateCommand())
        {
            var where = new List<string> { "is_missing = 0" };
            var extensions = query.Extensions.Select(x => x.ToLowerInvariant()).Distinct().ToArray();
            where.Add("lower(extension) IN (" + string.Join(",", extensions.Select((_, i) => "$ext" + i)) + ")");
            for (var i = 0; i < extensions.Length; i++) cmd.Parameters.AddWithValue("$ext" + i, extensions[i]);
            if (query.Kinds is { Count: > 0 })
                where.Add("kind IN (" + BindAssetKinds(cmd, query.Kinds) + ")");
            if (query.SourceId is Guid source)
            {
                where.Add("source_id = $source");
                cmd.Parameters.AddWithValue("$source", source.ToString("D"));
            }
            if (!string.IsNullOrWhiteSpace(query.CharacterName))
            {
                var name = query.CharacterName.Trim();
                where.Add("(subject_name = $character COLLATE NOCASE " +
                    "OR source_relative_path LIKE $characterRoot ESCAPE '!' COLLATE NOCASE " +
                    "OR source_relative_path LIKE $characterNested ESCAPE '!' COLLATE NOCASE " +
                    "OR EXISTS (SELECT 1 FROM asset_tags t WHERE t.asset_id = assets.id AND " +
                    "t.key IN ('director.manual.subject', 'director.auto.subject') AND t.value = $character COLLATE NOCASE))");
                cmd.Parameters.AddWithValue("$character", name);
                cmd.Parameters.AddWithValue("$characterRoot", EscapeLike(name) + "/%");
                cmd.Parameters.AddWithValue("$characterNested", "%/" + EscapeLike(name) + "/%");
            }
            var words = query.Search.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
            var match = FullTextSearchAvailable ? AssetSearchText.FtsMatch(query.Search) : null;
            if (match is not null)
            {
                // Ranked full-text search: name counts most, then path, then Director tags.
                cmd.Parameters.AddWithValue("$match", match);
                cmd.CommandText = AssetSearchText.RankedHits + AssetSelectSql + " JOIN hits ON hits.hit_id = assets.id WHERE " +
                    string.Join(" AND ", where) + " ORDER BY hits.score, display_name COLLATE NOCASE, id LIMIT $limit;";
                cmd.Parameters.AddWithValue("$limit", Math.Clamp(query.Limit, 1, 2000));
                try
                {
                    await ReadAssetsAsync(cmd, assets, cancellationToken);
                    words = [];
                }
                catch (SqliteException)
                {
                    assets.Clear();
                    cmd.Parameters.RemoveAt("$match");
                    cmd.Parameters.RemoveAt("$limit");
                    match = null;
                }
            }
            for (var i = 0; match is null && i < words.Length; i++)
            {
                var name = "$word" + i;
                where.Add($"(display_name LIKE {name} ESCAPE '!' COLLATE NOCASE " +
                    $"OR source_relative_path LIKE {name} ESCAPE '!' COLLATE NOCASE " +
                    $"OR EXISTS (SELECT 1 FROM asset_tags t WHERE t.asset_id = assets.id AND t.key LIKE 'director.%' " +
                    $"AND t.value LIKE {name} ESCAPE '!' COLLATE NOCASE))");
                cmd.Parameters.AddWithValue(name, "%" + EscapeLike(words[i]) + "%");
            }
            if (match is null)
            {
                cmd.CommandText = AssetSelectSql + " WHERE " + string.Join(" AND ", where) +
                    " ORDER BY display_name COLLATE NOCASE, id LIMIT $limit;";
                cmd.Parameters.AddWithValue("$limit", Math.Clamp(query.Limit, 1, 2000));
                await ReadAssetsAsync(cmd, assets, cancellationToken);
            }
        }

        var tags = new Dictionary<Guid, Dictionary<string, string>>();
        foreach (var batch in assets.Select(x => x.Id).Chunk(400))
        {
            await using var cmd = connection.CreateCommand();
            var names = batch.Select((_, i) => "$id" + i).ToArray();
            cmd.CommandText = "SELECT asset_id, key, value FROM asset_tags WHERE key LIKE 'director.%' AND asset_id IN (" +
                string.Join(",", names) + ");";
            for (var i = 0; i < batch.Length; i++) cmd.Parameters.AddWithValue(names[i], batch[i].ToString("D"));
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = Guid.Parse(reader.GetString(0));
                if (!tags.TryGetValue(id, out var values)) tags[id] = values = new Dictionary<string, string>(StringComparer.Ordinal);
                values[reader.GetString(1)] = reader.GetString(2);
            }
        }
        string Tag(Guid id, string field) =>
            tags.TryGetValue(id, out var values)
                ? values.GetValueOrDefault("director.manual." + field) ?? values.GetValueOrDefault("director.auto." + field) ?? ""
                : "";
        return assets.Select(asset => new AssetPickerItem(asset, Tag(asset.Id, "description"), Tag(asset.Id, "mood"),
            Tag(asset.Id, "role"), Tag(asset.Id, "subject"))).ToArray();
    }, cancellationToken);

    public Task<AssetRecord?> GetAssetBySourcePathAsync(Guid sourceId, string sourceRelativePath, CancellationToken cancellationToken = default) => ReadAsync<AssetRecord?>(async connection =>
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = AssetSelectSql + " WHERE source_id = $source AND source_relative_path = $path COLLATE NOCASE AND is_missing = 0 LIMIT 1;";
        cmd.Parameters.AddWithValue("$source", sourceId.ToString("D"));
        cmd.Parameters.AddWithValue("$path", sourceRelativePath.Replace('\\', '/'));
        var result = new List<AssetRecord>(1);
        await ReadAssetsAsync(cmd, result, cancellationToken);
        return result.Count == 0 ? null : result[0];
    }, cancellationToken);

    private static string BindAssetKinds(SqliteCommand cmd, IReadOnlyCollection<AssetKind> kinds)
    {
        var parameters = kinds.Distinct().Select((kind, index) => (Kind: kind, Name: $"$filterKind{index}")).ToArray();
        foreach (var item in parameters) cmd.Parameters.AddWithValue(item.Name, (int)item.Kind);
        return string.Join(",", parameters.Select(x => x.Name));
    }

    /// <summary>
    /// Runs a catalog read on its own read-only connection on the thread pool. Microsoft.Data.Sqlite
    /// executes "async" calls synchronously, so a large read on the shared connection froze the
    /// interface; with WAL these reads also run while a scan or a save is writing.
    /// </summary>
    private Task<T> ReadAsync<T>(Func<SqliteConnection, Task<T>> read, CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            await using var connection = await OpenAssetReadConnectionAsync(cancellationToken).ConfigureAwait(false);
            return await read(connection).ConfigureAwait(false);
        }, cancellationToken);

    private async Task<SqliteConnection> OpenAssetReadConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(ProjectRoot, "project.db"), Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Default
        }.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static string EscapeLike(string value) => value.Replace("!", "!!").Replace("%", "!%").Replace("_", "!_");

    public Task<IReadOnlyList<AssetRecord>> GetAssetsByIdsAsync(IReadOnlyCollection<Guid> assetIds, CancellationToken cancellationToken = default) => ReadAsync<IReadOnlyList<AssetRecord>>(async connection =>
    {
        if (assetIds.Count == 0)
            return Array.Empty<AssetRecord>();

        var result = new List<AssetRecord>();
        foreach (var batch in assetIds.Distinct().Chunk(400))
        {
            await using var cmd = connection.CreateCommand();
            var parameters = batch.Select((id, index) => (Id: id, Name: $"$assetId{index}")).ToArray();
            cmd.CommandText = AssetSelectSql + $" WHERE id IN ({string.Join(",", parameters.Select(x => x.Name))}) ORDER BY display_name COLLATE NOCASE;";
            foreach (var parameter in parameters)
                cmd.Parameters.AddWithValue(parameter.Name, parameter.Id.ToString("D"));
            await ReadAssetsAsync(cmd, result, cancellationToken);
        }
        return result;
    }, cancellationToken);

    public async Task<IReadOnlyList<AssetRecord>> GetAssetsForSourceAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        var result = new List<AssetRecord>();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = AssetSelectSql + " WHERE source_id = $source ORDER BY source_relative_path;";
        cmd.Parameters.AddWithValue("$source", sourceId.ToString("D"));
        await ReadAssetsAsync(cmd, result, cancellationToken);
        return result;
    }

    private const string AssetSelectSql = """
SELECT id, relative_path, kind, sha256, display_name, imported_utc,
       source_id, source_relative_path, file_size, last_write_utc, extension,
       cutout_status, subject_name, collection_name, is_missing
FROM assets
""";

    private static async Task ReadAssetsAsync(
        SqliteCommand cmd,
        ICollection<AssetRecord> result,
        CancellationToken cancellationToken)
    {
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new AssetRecord(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                (AssetKind)reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.IsDBNull(6) ? null : Guid.Parse(reader.GetString(6)),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetInt64(8),
                reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetString(10),
                (CutoutStatus)reader.GetInt32(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.GetInt32(14) != 0));
        }
    }

    public async Task<IReadOnlyCollection<Guid>> GetAssetIdsWithTagAsync(Guid sourceId, string key,
        CancellationToken cancellationToken = default)
    {
        var ids = new HashSet<Guid>();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT t.asset_id FROM asset_tags t JOIN assets a ON a.id = t.asset_id " +
            "WHERE a.source_id = $source AND t.key = $key;";
        cmd.Parameters.AddWithValue("$source", sourceId.ToString("D"));
        cmd.Parameters.AddWithValue("$key", key);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) ids.Add(Guid.Parse(reader.GetString(0)));
        return ids;
    }

    public async Task UpsertAssetAsync(AssetRecord asset, CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
INSERT INTO assets(
    id, relative_path, kind, sha256, display_name, imported_utc,
    source_id, source_relative_path, file_size, last_write_utc, extension,
    cutout_status, subject_name, collection_name, is_missing)
VALUES(
    $id, $path, $kind, $sha, $name, $imported,
    $source, $sourcePath, $size, $lastWrite, $extension,
    $cutout, $subject, $collection, $missing)
ON CONFLICT(id) DO UPDATE SET
    relative_path = excluded.relative_path,
    kind = CASE WHEN EXISTS(SELECT 1 FROM asset_tags
        WHERE asset_id = excluded.id AND key = 'catalog.manual.kind')
        THEN assets.kind ELSE excluded.kind END,
    sha256 = excluded.sha256,
    display_name = excluded.display_name,
    source_id = excluded.source_id,
    source_relative_path = excluded.source_relative_path,
    file_size = excluded.file_size,
    last_write_utc = excluded.last_write_utc,
    extension = excluded.extension,
    cutout_status = excluded.cutout_status,
    subject_name = CASE WHEN EXISTS(SELECT 1 FROM asset_tags
        WHERE asset_id = excluded.id AND key = 'catalog.manual.subject')
        THEN assets.subject_name ELSE excluded.subject_name END,
    collection_name = excluded.collection_name,
    is_missing = excluded.is_missing;
""";
        cmd.Parameters.AddWithValue("$id", asset.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$path", asset.RelativePath);
        cmd.Parameters.AddWithValue("$kind", (int)asset.Kind);
        cmd.Parameters.AddWithValue("$sha", asset.Sha256);
        cmd.Parameters.AddWithValue("$name", asset.DisplayName);
        cmd.Parameters.AddWithValue("$imported", asset.ImportedUtc.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$source", DbValue(asset.SourceId?.ToString("D")));
        cmd.Parameters.AddWithValue("$sourcePath", DbValue(asset.SourceRelativePath));
        cmd.Parameters.AddWithValue("$size", asset.FileSize);
        cmd.Parameters.AddWithValue("$lastWrite", DbValue(asset.LastWriteUtc?.ToString("O", CultureInfo.InvariantCulture)));
        cmd.Parameters.AddWithValue("$extension", asset.Extension);
        cmd.Parameters.AddWithValue("$cutout", (int)asset.CutoutStatus);
        cmd.Parameters.AddWithValue("$subject", DbValue(asset.SubjectName));
        cmd.Parameters.AddWithValue("$collection", DbValue(asset.CollectionName));
        cmd.Parameters.AddWithValue("$missing", asset.IsMissing ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> UpdateAssetClassificationsAsync(IReadOnlyCollection<Guid> assetIds,
        AssetKind? kind, bool setSubject, string? subject, CancellationToken cancellationToken = default)
    {
        if (assetIds.Count == 0 || kind is null && !setSubject) return 0;
        var distinct = assetIds.Distinct().ToArray();
        var changed = 0;
        await using var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
        try
        {
            // SQLite has a finite parameter budget; keep each update inside one transaction.
            foreach (var batch in distinct.Chunk(300))
            {
                await using var cmd = _connection.CreateCommand();
                cmd.Transaction = transaction;
                var names = batch.Select((_, i) => "$id" + i).ToArray();
                cmd.CommandText = "UPDATE assets SET " + string.Join(", ", new[]
                {
                    kind is null ? null : "kind = $kind",
                    setSubject ? "subject_name = $subject" : null
                }.OfType<string>()) + " WHERE id IN (" + string.Join(",", names) + ");";
                if (kind is AssetKind value) cmd.Parameters.AddWithValue("$kind", (int)value);
                if (setSubject) cmd.Parameters.AddWithValue("$subject", DbValue(string.IsNullOrWhiteSpace(subject) ? null : subject.Trim()));
                for (var i = 0; i < batch.Length; i++)
                    cmd.Parameters.AddWithValue(names[i], batch[i].ToString("D"));
                changed += await cmd.ExecuteNonQueryAsync(cancellationToken);
                if (setSubject)
                {
                    await using var clearSubjectTag = _connection.CreateCommand();
                    clearSubjectTag.Transaction = transaction;
                    clearSubjectTag.CommandText = "DELETE FROM asset_tags WHERE key = 'director.manual.subject' " +
                        "AND asset_id IN (" + string.Join(",", names) + ");";
                    for (var i = 0; i < batch.Length; i++)
                        clearSubjectTag.Parameters.AddWithValue(names[i], batch[i].ToString("D"));
                    await clearSubjectTag.ExecuteNonQueryAsync(cancellationToken);
                    if (!string.IsNullOrWhiteSpace(subject))
                    {
                        await using var setSubjectTag = _connection.CreateCommand();
                        setSubjectTag.Transaction = transaction;
                        setSubjectTag.CommandText = "INSERT INTO asset_tags(asset_id, key, value, confidence) " +
                            "SELECT id, 'director.manual.subject', $value, 1 FROM assets WHERE id IN (" + string.Join(",", names) + ");";
                        setSubjectTag.Parameters.AddWithValue("$value", subject!.Trim());
                        for (var i = 0; i < batch.Length; i++)
                            setSubjectTag.Parameters.AddWithValue(names[i], batch[i].ToString("D"));
                        await setSubjectTag.ExecuteNonQueryAsync(cancellationToken);
                    }
                }
                foreach (var key in new[] { kind is null ? null : "catalog.manual.kind",
                             setSubject ? "catalog.manual.subject" : null }.OfType<string>())
                {
                    await using var marker = _connection.CreateCommand();
                    marker.Transaction = transaction;
                    marker.CommandText = "INSERT OR IGNORE INTO asset_tags(asset_id, key, value, confidence) " +
                        "SELECT id, $key, '1', 1 FROM assets WHERE id IN (" + string.Join(",", names) + ");";
                    marker.Parameters.AddWithValue("$key", key);
                    for (var i = 0; i < batch.Length; i++)
                        marker.Parameters.AddWithValue(names[i], batch[i].ToString("D"));
                    await marker.ExecuteNonQueryAsync(cancellationToken);
                }
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        return changed;
    }

    public async Task DeleteAssetAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM assets WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", assetId.ToString("D"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
    public Task<IReadOnlyList<AssetTag>> GetAssetTagsAsync(Guid? assetId = null, CancellationToken cancellationToken = default) => ReadAsync<IReadOnlyList<AssetTag>>(async connection =>
    {
        var result = new List<AssetTag>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = assetId.HasValue
            ? "SELECT asset_id, key, value, confidence FROM asset_tags WHERE asset_id = $id AND key LIKE 'director.%'"
            : "SELECT asset_id, key, value, confidence FROM asset_tags WHERE key LIKE 'director.%'";
        if (assetId is Guid id) cmd.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new AssetTag(Guid.Parse(reader.GetString(0)), reader.GetString(1),
                reader.GetString(2), reader.GetDouble(3)));
        return result;
    }, cancellationToken);

    public async Task ReplaceDirectorAssetTagsAsync(Guid assetId, string prefix, IEnumerable<AssetTag> tags,
        CancellationToken cancellationToken = default)
    {
        if (prefix is not ("director.manual." or "director.auto."))
            throw new ArgumentException("Prefijo de etiquetas inválido.", nameof(prefix));
        var entries = tags.ToArray();
        if (entries.Any(x => x.AssetId != assetId || !x.Key.StartsWith(prefix, StringComparison.Ordinal)))
            throw new ArgumentException("Todas las etiquetas deben pertenecer al recurso y al prefijo indicados.", nameof(tags));
        await using var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var delete = _connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM asset_tags WHERE asset_id = $id AND key LIKE $prefix";
                delete.Parameters.AddWithValue("$id", assetId.ToString("D"));
                delete.Parameters.AddWithValue("$prefix", prefix + "%");
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }
            foreach (var tag in entries)
            {
                await using var insert = _connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO asset_tags(asset_id, key, value, confidence) VALUES($id, $key, $value, $confidence)";
                insert.Parameters.AddWithValue("$id", assetId.ToString("D"));
                insert.Parameters.AddWithValue("$key", tag.Key);
                insert.Parameters.AddWithValue("$value", tag.Value);
                insert.Parameters.AddWithValue("$confidence", tag.Confidence);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlySet<Guid>> GetProtectedAssetIdsAsync(CancellationToken cancellationToken = default)
    {
        var ids = new HashSet<Guid>();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
SELECT asset_id FROM scene_script_blocks WHERE asset_id IS NOT NULL
UNION SELECT background_asset_id FROM scenes WHERE background_asset_id IS NOT NULL
UNION SELECT asset_id FROM asset_tags WHERE key LIKE 'director.manual.%' OR key LIKE 'catalog.manual.%';
""";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            if (Guid.TryParse(reader.GetString(0), out var id)) ids.Add(id);
        return ids;
    }

    public async Task MarkAssetsMissingAsync(Guid sourceId, IReadOnlySet<string> presentRelativePaths, CancellationToken cancellationToken = default)
    {
        var assets = await GetAssetsForSourceAsync(sourceId, cancellationToken);
        await using var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
        foreach (var asset in assets)
        {
            var sourcePath = NormalizeRelativePath(asset.SourceRelativePath ?? string.Empty);
            var missing = !presentRelativePaths.Contains(sourcePath);
            if (missing == asset.IsMissing)
                continue;

            await using var cmd = _connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "UPDATE assets SET is_missing = $missing WHERE id = $id;";
            cmd.Parameters.AddWithValue("$missing", missing ? 1 : 0);
            cmd.Parameters.AddWithValue("$id", asset.Id.ToString("D"));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public Task<IReadOnlyList<AssetSource>> GetAssetSourcesAsync(CancellationToken cancellationToken = default) => ReadAsync<IReadOnlyList<AssetSource>>(async connection =>
    {
        var result = new List<AssetSource>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, root_path, enabled, created_utc, last_scan_utc FROM asset_sources ORDER BY name;";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new AssetSource(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3) != 0,
                DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }
        return result;
    }, cancellationToken);

    public async Task UpsertAssetSourceAsync(AssetSource source, CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
INSERT INTO asset_sources(id, name, root_path, enabled, created_utc, last_scan_utc)
VALUES($id, $name, $root, $enabled, $created, $lastScan)
ON CONFLICT(id) DO UPDATE SET
    name = excluded.name,
    root_path = excluded.root_path,
    enabled = excluded.enabled,
    last_scan_utc = excluded.last_scan_utc;
""";
        cmd.Parameters.AddWithValue("$id", source.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$name", source.Name);
        cmd.Parameters.AddWithValue("$root", Path.GetFullPath(source.RootPath));
        cmd.Parameters.AddWithValue("$enabled", source.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$created", source.CreatedUtc.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$lastScan", DbValue(source.LastScanUtc?.ToString("O", CultureInfo.InvariantCulture)));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAssetSourceAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        await using var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);

        await using (var deleteAssets = _connection.CreateCommand())
        {
            deleteAssets.Transaction = transaction;
            deleteAssets.CommandText = "DELETE FROM assets WHERE source_id = $source;";
            deleteAssets.Parameters.AddWithValue("$source", sourceId.ToString("D"));
            await deleteAssets.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteSource = _connection.CreateCommand())
        {
            deleteSource.Transaction = transaction;
            deleteSource.CommandText = "DELETE FROM asset_sources WHERE id = $source;";
            deleteSource.Parameters.AddWithValue("$source", sourceId.ToString("D"));
            await deleteSource.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FolderRule>> GetFolderRulesAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        var result = new List<FolderRule>();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
SELECT id, source_id, relative_folder, classification, subject_name, collection_name,
       include_subfolders, default_cutout_status
FROM folder_rules
WHERE source_id = $source
ORDER BY length(relative_folder), relative_folder;
""";
        cmd.Parameters.AddWithValue("$source", sourceId.ToString("D"));
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new FolderRule(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                (FolderClassification)reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetInt32(6) != 0,
                (CutoutStatus)reader.GetInt32(7)));
        }
        return result;
    }

    public async Task ReplaceFolderRulesAsync(Guid sourceId, IEnumerable<FolderRule> rules, CancellationToken cancellationToken = default)
    {
        await using var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
        await using (var delete = _connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM folder_rules WHERE source_id = $source;";
            delete.Parameters.AddWithValue("$source", sourceId.ToString("D"));
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var rule in rules)
        {
            await using var cmd = _connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
INSERT INTO folder_rules(
    id, source_id, relative_folder, classification, subject_name, collection_name,
    include_subfolders, default_cutout_status)
VALUES($id, $source, $folder, $classification, $subject, $collection, $inherit, $cutout);
""";
            cmd.Parameters.AddWithValue("$id", rule.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$source", sourceId.ToString("D"));
            cmd.Parameters.AddWithValue("$folder", NormalizeRelativePath(rule.RelativeFolder));
            cmd.Parameters.AddWithValue("$classification", (int)rule.Classification);
            cmd.Parameters.AddWithValue("$subject", DbValue(rule.SubjectName));
            cmd.Parameters.AddWithValue("$collection", DbValue(rule.CollectionName));
            cmd.Parameters.AddWithValue("$inherit", rule.IncludeSubfolders ? 1 : 0);
            cmd.Parameters.AddWithValue("$cutout", (int)rule.DefaultCutoutStatus);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<VoiceProfile>> GetVoiceProfilesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<VoiceProfile>();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
SELECT id, name, provider_key, voice_id, pitch_value, speed_value, volume_value, sample_rate, config_json
FROM voice_profiles
ORDER BY name COLLATE NOCASE;
""";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new VoiceProfile(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetString(8)));
        }
        return result;
    }

    public async Task UpsertVoiceProfileAsync(VoiceProfile profile, CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
INSERT INTO voice_profiles(
    id, name, provider_key, voice_id, rate, pitch, config_json,
    speed_value, pitch_value, volume_value, sample_rate)
VALUES(
    $id, $name, $provider, $voice, $legacyRate, $legacyPitch, $config,
    $speed, $pitch, $volume, $sampleRate)
ON CONFLICT(id) DO UPDATE SET
    name = excluded.name,
    provider_key = excluded.provider_key,
    voice_id = excluded.voice_id,
    rate = excluded.rate,
    pitch = excluded.pitch,
    config_json = excluded.config_json,
    speed_value = excluded.speed_value,
    pitch_value = excluded.pitch_value,
    volume_value = excluded.volume_value,
    sample_rate = excluded.sample_rate;
""";
        cmd.Parameters.AddWithValue("$id", profile.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$name", profile.Name);
        cmd.Parameters.AddWithValue("$provider", profile.ProviderKey);
        cmd.Parameters.AddWithValue("$voice", profile.VoiceId);
        cmd.Parameters.AddWithValue("$legacyRate", profile.Speed ?? 0);
        cmd.Parameters.AddWithValue("$legacyPitch", profile.Pitch ?? 0);
        cmd.Parameters.AddWithValue("$config", profile.ConfigJson);
        cmd.Parameters.AddWithValue("$speed", DbValue(profile.Speed));
        cmd.Parameters.AddWithValue("$pitch", DbValue(profile.Pitch));
        cmd.Parameters.AddWithValue("$volume", Math.Clamp(profile.Volume, 0, 100));
        cmd.Parameters.AddWithValue("$sampleRate", profile.SampleRate);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteVoiceProfileAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM voice_profiles WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", profileId.ToString("D"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CharacterDefinition>> GetCharactersAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<CharacterDefinition>();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, default_voice_profile_id, notes FROM characters ORDER BY name COLLATE NOCASE;";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new CharacterDefinition(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }
        return result;
    }

    public async Task UpsertCharacterAsync(CharacterDefinition character, CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
INSERT INTO characters(id, name, default_voice_profile_id, notes)
VALUES($id, $name, $voice, $notes)
ON CONFLICT(id) DO UPDATE SET
    name = excluded.name,
    default_voice_profile_id = excluded.default_voice_profile_id,
    notes = excluded.notes;
""";
        cmd.Parameters.AddWithValue("$id", character.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$name", character.Name);
        cmd.Parameters.AddWithValue("$voice", DbValue(character.DefaultVoiceProfileId?.ToString("D")));
        cmd.Parameters.AddWithValue("$notes", DbValue(character.Notes));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteCharacterAsync(Guid characterId, CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM characters WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", characterId.ToString("D"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Episode>> GetEpisodesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<Episode>();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT id, number, title, synopsis FROM episodes ORDER BY number, title COLLATE NOCASE;";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new Episode(
                Guid.Parse(reader.GetString(0)),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }
        return result;
    }

    public async Task UpsertEpisodeAsync(Episode episode, CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
INSERT INTO episodes(id, number, title, synopsis)
VALUES($id, $number, $title, $synopsis)
ON CONFLICT(id) DO UPDATE SET
    number = excluded.number,
    title = excluded.title,
    synopsis = excluded.synopsis;
""";
        cmd.Parameters.AddWithValue("$id", episode.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$number", episode.Number);
        cmd.Parameters.AddWithValue("$title", episode.Title);
        cmd.Parameters.AddWithValue("$synopsis", DbValue(episode.Synopsis));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteEpisodeAsync(Guid episodeId, CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM episodes WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", episodeId.ToString("D"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Scene>> GetScenesAsync(Guid episodeId, CancellationToken cancellationToken = default)
    {
        var result = new List<Scene>();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
SELECT id, episode_id, scene_index, title, duration_ms, background_asset_id, direction_notes
FROM scenes
WHERE episode_id = $episode
ORDER BY scene_index;
""";
        cmd.Parameters.AddWithValue("$episode", episodeId.ToString("D"));
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new Scene(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetInt64(4),
                reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5)),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }
        return result;
    }

    public async Task UpsertSceneAsync(Scene scene, CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
INSERT INTO scenes(id, episode_id, scene_index, title, duration_ms, background_asset_id, direction_notes)
VALUES($id, $episode, $index, $title, $duration, $background, $notes)
ON CONFLICT(id) DO UPDATE SET
    episode_id = excluded.episode_id,
    scene_index = excluded.scene_index,
    title = excluded.title,
    duration_ms = excluded.duration_ms,
    background_asset_id = excluded.background_asset_id,
    direction_notes = excluded.direction_notes;
""";
        cmd.Parameters.AddWithValue("$id", scene.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$episode", scene.EpisodeId.ToString("D"));
        cmd.Parameters.AddWithValue("$index", scene.Index);
        cmd.Parameters.AddWithValue("$title", scene.Title);
        cmd.Parameters.AddWithValue("$duration", scene.DurationMs);
        cmd.Parameters.AddWithValue("$background", DbValue(scene.BackgroundAssetId?.ToString("D")));
        cmd.Parameters.AddWithValue("$notes", DbValue(scene.DirectionNotes));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteSceneAsync(Guid sceneId, CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM scenes WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", sceneId.ToString("D"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SceneScriptBlock>> GetSceneScriptBlocksAsync(Guid sceneId, CancellationToken cancellationToken = default)
    {
        var result = new List<SceneScriptBlock>();
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
SELECT id, scene_id, order_index, block_type, character_id, text_value, asset_id, voice_profile_id,
       voice_pitch_override, voice_speed_override, voice_volume_override, pause_after_ms, parameters_json,
       start_offset_ms, generated_audio_path, generated_audio_hash, generated_duration_ms
FROM scene_script_blocks
WHERE scene_id = $scene
ORDER BY order_index;
""";
        cmd.Parameters.AddWithValue("$scene", sceneId.ToString("D"));
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new SceneScriptBlock(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetInt32(2),
                (ScriptBlockKind)reader.GetInt32(3),
                reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4)),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : Guid.Parse(reader.GetString(6)),
                reader.IsDBNull(7) ? null : Guid.Parse(reader.GetString(7)),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetInt32(9),
                reader.IsDBNull(10) ? null : reader.GetInt32(10),
                reader.GetInt32(11),
                reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetInt64(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetInt64(16)));
        }
        return result;
    }

    public async Task UpsertSceneScriptBlockAsync(SceneScriptBlock block, CancellationToken cancellationToken = default)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = SceneBlockUpsertSql;
        AddSceneBlockParameters(cmd, block);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
    public async Task ReplaceSceneScriptBlocksAsync(Guid sceneId, IEnumerable<SceneScriptBlock> blocks, CancellationToken cancellationToken = default)
    {
        var ordered = blocks.OrderBy(x => x.OrderIndex).ToArray();
        await using var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var delete = _connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM scene_script_blocks WHERE scene_id = $scene;";
                delete.Parameters.AddWithValue("$scene", sceneId.ToString("D"));
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var block in ordered)
            {
                await using var cmd = _connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = SceneBlockInsertSql;
                AddSceneBlockParameters(cmd, block with { SceneId = sceneId });
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private const string SceneBlockInsertSql = """
INSERT INTO scene_script_blocks(
    id, scene_id, order_index, block_type, character_id, text_value, asset_id, voice_profile_id,
    voice_pitch_override, voice_speed_override, voice_volume_override, pause_after_ms, parameters_json,
    start_offset_ms, generated_audio_path, generated_audio_hash, generated_duration_ms)
VALUES(
    $id, $scene, $order, $type, $character, $text, $asset, $voice,
    $pitch, $speed, $volume, $pause, $params,
    $startOffset, $audioPath, $audioHash, $audioDuration);
""";

    private const string SceneBlockUpsertSql = """
INSERT INTO scene_script_blocks(
    id, scene_id, order_index, block_type, character_id, text_value, asset_id, voice_profile_id,
    voice_pitch_override, voice_speed_override, voice_volume_override, pause_after_ms, parameters_json,
    start_offset_ms, generated_audio_path, generated_audio_hash, generated_duration_ms)
VALUES(
    $id, $scene, $order, $type, $character, $text, $asset, $voice,
    $pitch, $speed, $volume, $pause, $params,
    $startOffset, $audioPath, $audioHash, $audioDuration)
ON CONFLICT(id) DO UPDATE SET
    scene_id = excluded.scene_id,
    order_index = excluded.order_index,
    block_type = excluded.block_type,
    character_id = excluded.character_id,
    text_value = excluded.text_value,
    asset_id = excluded.asset_id,
    voice_profile_id = excluded.voice_profile_id,
    voice_pitch_override = excluded.voice_pitch_override,
    voice_speed_override = excluded.voice_speed_override,
    voice_volume_override = excluded.voice_volume_override,
    pause_after_ms = excluded.pause_after_ms,
    parameters_json = excluded.parameters_json,
    start_offset_ms = excluded.start_offset_ms,
    generated_audio_path = excluded.generated_audio_path,
    generated_audio_hash = excluded.generated_audio_hash,
    generated_duration_ms = excluded.generated_duration_ms;
""";

    private static void AddSceneBlockParameters(SqliteCommand cmd, SceneScriptBlock block)
    {
        cmd.Parameters.AddWithValue("$id", block.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$scene", block.SceneId.ToString("D"));
        cmd.Parameters.AddWithValue("$order", block.OrderIndex);
        cmd.Parameters.AddWithValue("$type", (int)block.Kind);
        cmd.Parameters.AddWithValue("$character", DbValue(block.CharacterId?.ToString("D")));
        cmd.Parameters.AddWithValue("$text", block.Text ?? string.Empty);
        cmd.Parameters.AddWithValue("$asset", DbValue(block.AssetId?.ToString("D")));
        cmd.Parameters.AddWithValue("$voice", DbValue(block.VoiceProfileId?.ToString("D")));
        cmd.Parameters.AddWithValue("$pitch", DbValue(block.VoicePitchOverride));
        cmd.Parameters.AddWithValue("$speed", DbValue(block.VoiceSpeedOverride));
        cmd.Parameters.AddWithValue("$volume", DbValue(block.VoiceVolumeOverride));
        cmd.Parameters.AddWithValue("$pause", Math.Max(0, block.PauseAfterMs));
        cmd.Parameters.AddWithValue("$params", string.IsNullOrWhiteSpace(block.ParametersJson) ? "{}" : block.ParametersJson);
        cmd.Parameters.AddWithValue("$startOffset", block.StartOffsetMs.HasValue ? block.StartOffsetMs.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("$audioPath", DbValue(block.GeneratedAudioPath));
        cmd.Parameters.AddWithValue("$audioHash", DbValue(block.GeneratedAudioHash));
        cmd.Parameters.AddWithValue("$audioDuration", block.GeneratedDurationMs.HasValue ? block.GeneratedDurationMs.Value : DBNull.Value);
    }

    private static object DbValue(string? value) => value is null ? DBNull.Value : value;
    private static object DbValue(int? value) => value.HasValue ? value.Value : DBNull.Value;

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/').Trim('/');

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}
