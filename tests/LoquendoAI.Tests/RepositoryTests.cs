using System.Globalization;
using System.Text.Json;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Persistence;
using LoquendoAI.Infrastructure.Projects;
using Microsoft.Data.Sqlite;

namespace LoquendoAI.Tests;

/// <summary>
/// The project database with real SQLite: creation, migrations from older projects (including
/// the cleanup of schema v7), refusing newer projects, catalog reads and the library scan.
/// </summary>
internal static class RepositoryTests
{
    private static readonly JsonSerializerOptions ManifestJson = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static SqliteConnection Open(string root)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = Path.Combine(root, "project.db"), Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static object? Scalar(string root, string sql)
    {
        using var connection = Open(root);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static long Count(string root, string sql) => Convert.ToInt64(Scalar(root, sql), CultureInfo.InvariantCulture);

    private static bool TableExists(string root, string table) =>
        Count(root, $"SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = '{table}'") > 0;

    private static bool ColumnExists(string root, string table, string column) =>
        Count(root, $"SELECT count(*) FROM pragma_table_info('{table}') WHERE name = '{column}'") > 0;

    /// <summary>A project as an older build left it: the migrations up to <paramref name="version"/>
    /// (as they shipped) and project.loquendo.json with <paramref name="manifestVersion"/>.</summary>
    private static ProjectManifest OldProject(string root, int version, int manifestVersion, Action<SqliteConnection, ProjectManifest> seed)
    {
        Directory.CreateDirectory(root);
        var manifest = ProjectManifest.Create("Proyecto viejo") with { SchemaVersion = manifestVersion };
        File.WriteAllText(Path.Combine(root, ProjectLayout.ManifestFileName), JsonSerializer.Serialize(manifest, ManifestJson));
        using var connection = Open(root);
        Execute(connection, DatabaseSchema.Pragmas + DatabaseSchema.V1);
        foreach (var (step, sql) in new[] { (2, DatabaseSchema.V2), (3, DatabaseSchema.V3), (4, DatabaseSchema.V4), (5, DatabaseSchema.V5), (6, DatabaseSchema.V6) })
            if (step <= version) Execute(connection, sql);
        Execute(connection, "INSERT INTO project_info(id, name, schema_version, created_utc, updated_utc) VALUES($id, $name, $v, $t, $t);",
            ("$id", manifest.Id.ToString("D")), ("$name", manifest.Name), ("$v", version), ("$t", DateTimeOffset.UtcNow.ToString("O")));
        seed(connection, manifest);
        return manifest;
    }

    [Test("Proyecto nuevo: esquema v7, sin tablas sin uso y con búsqueda de texto completo")]
    public static async Task NewProject()
    {
        using var folder = new TempFolder();
        string root;
        await using (var repository = await new ProjectService().CreateAsync(folder.Path, "Prueba"))
        {
            root = repository.ProjectRoot;
            Assert.Equal(ProjectManifest.CurrentSchemaVersion, repository.SchemaVersion, "versión de la base");
            Assert.True(repository.FullTextSearchAvailable, "índice FTS5");
        }
        Assert.Equal(7, ProjectManifest.CurrentSchemaVersion, "versión actual");
        foreach (var table in new[] { "scene_characters", "dialogue_lines", "effects", "jobs" })
            Assert.False(TableExists(root, table), $"tabla sin uso {table}");
        Assert.False(ColumnExists(root, "project_info", "schema_version"), "la versión ya no se copia en project_info");
        Assert.Equal(7L, Count(root, "SELECT max(version) FROM schema_info"), "schema_info");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ProjectLayout.ManifestFileName)));
        Assert.Equal(7, manifest.RootElement.GetProperty("schemaVersion").GetInt32(), "project.loquendo.json");
    }

    [Test("Migración desde un proyecto de beta.3 (v6): datos intactos, limpieza v7 y copia previa")]
    public static async Task MigrateFromBeta3()
    {
        using var folder = new TempFolder();
        var root = folder.File("viejo");
        Guid sourceId = Guid.NewGuid(), assetId = Guid.NewGuid(), episodeId = Guid.NewGuid(), sceneId = Guid.NewGuid(), blockId = Guid.NewGuid();
        OldProject(root, version: 6, manifestVersion: 5, (connection, _) =>
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            Execute(connection, "INSERT INTO asset_sources(id, name, root_path, enabled, created_utc) VALUES($id, 'Fondos', 'C:/fondos', 1, $t);",
                ("$id", sourceId.ToString("D")), ("$t", now));
            Execute(connection, """
                INSERT INTO assets(id, relative_path, kind, sha256, display_name, imported_utc, source_id, source_relative_path, extension)
                VALUES($id, 'C:/fondos/Canción de cuna.png', 2, 'h', 'Canción de cuna', $t, $source, 'Canción de cuna.png', '.png');
                """, ("$id", assetId.ToString("D")), ("$t", now), ("$source", sourceId.ToString("D")));
            Execute(connection, "INSERT INTO episodes(id, number, title) VALUES($id, 1, 'Piloto');", ("$id", episodeId.ToString("D")));
            Execute(connection, "INSERT INTO scenes(id, episode_id, scene_index, title, duration_ms) VALUES($id, $e, 0, 'Cocina', 0);",
                ("$id", sceneId.ToString("D")), ("$e", episodeId.ToString("D")));
            Execute(connection, """
                INSERT INTO scene_script_blocks(id, scene_id, order_index, block_type, asset_id, parameters_json)
                VALUES($id, $s, 0, 2, $a, '{"position":"centro"}');
                """, ("$id", blockId.ToString("D")), ("$s", sceneId.ToString("D")), ("$a", assetId.ToString("D")));
        });

        await using (var repository = await new ProjectService().OpenAsync(root))
        {
            Assert.Equal(7, repository.SchemaVersion, "migrada a v7");
            var assets = await repository.GetAssetsAsync();
            Assert.Equal(assetId, assets.Single().Id, "recurso conservado");
            var blocks = await repository.GetSceneScriptBlocksAsync(sceneId);
            Assert.Equal(blockId, blocks.Single().Id, "bloque conservado");
            Assert.Equal("""{"position":"centro"}""", blocks[0].ParametersJson, "parámetros conservados");
            Assert.Sequence([assetId], (await repository.SearchAssetIdsAsync("cancion cun"))!, "búsqueda sin tildes y por prefijo");
            // beta.3's triggers failed here (UNIQUE asset_fts_map.asset_id): renaming through the UPSERT.
            await repository.UpsertAssetAsync(assets[0] with { DisplayName = "Nana nocturna", SourceRelativePath = "Nana nocturna.png" });
            Assert.Sequence([assetId], (await repository.SearchAssetIdsAsync("nocturna"))!, "índice al día tras renombrar");
            Assert.Equal(0, (await repository.SearchAssetIdsAsync("cancion"))!.Count, "el nombre viejo sale del índice");
        }
        foreach (var table in new[] { "scene_characters", "dialogue_lines", "effects", "jobs" })
            Assert.False(TableExists(root, table), $"tabla {table} eliminada");
        Assert.False(ColumnExists(root, "project_info", "schema_version"), "columna schema_version eliminada");
        Assert.Equal("Proyecto viejo", (string)Scalar(root, "SELECT name FROM project_info")!, "project_info conservado");
        var backup = Directory.GetFiles(ProjectBackup.Folder(root), "*_antes-v6.db").SingleOrDefault();
        Assert.True(backup is not null, "copia antes de migrar");
        Assert.Equal(6L, Convert.ToInt64(ScalarFile(backup!, "SELECT max(version) FROM schema_info"), CultureInfo.InvariantCulture),
            "la copia es la base v6 original");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ProjectLayout.ManifestFileName)));
        Assert.Equal(7, manifest.RootElement.GetProperty("schemaVersion").GetInt32(), "manifiesto actualizado");
    }

    private static object? ScalarFile(string database, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    [Test("Migración desde v1: los diálogos antiguos pasan a bloques antes de borrar su tabla")]
    public static async Task MigrateFromV1()
    {
        using var folder = new TempFolder();
        var root = folder.File("v1");
        Guid episodeId = Guid.NewGuid(), sceneId = Guid.NewGuid(), lineId = Guid.NewGuid();
        OldProject(root, version: 1, manifestVersion: 1, (connection, _) =>
        {
            Execute(connection, "INSERT INTO episodes(id, number, title) VALUES($id, 1, 'Piloto');", ("$id", episodeId.ToString("D")));
            Execute(connection, "INSERT INTO scenes(id, episode_id, scene_index, title, duration_ms) VALUES($id, $e, 0, 'Cocina', 0);",
                ("$id", sceneId.ToString("D")), ("$e", episodeId.ToString("D")));
            Execute(connection, "INSERT INTO dialogue_lines(id, scene_id, order_index, text) VALUES($id, $s, 0, 'Hola desde v1');",
                ("$id", lineId.ToString("D")), ("$s", sceneId.ToString("D")));
        });
        await using (var repository = await new ProjectService().OpenAsync(root))
        {
            var block = (await repository.GetSceneScriptBlocksAsync(sceneId)).Single();
            Assert.Equal((lineId, "Hola desde v1", ScriptBlockKind.Dialogue), (block.Id, block.Text, block.Kind), "diálogo convertido");
            Assert.Equal(7, repository.SchemaVersion, "versión");
        }
        Assert.False(TableExists(root, "dialogue_lines"), "tabla antigua eliminada");
    }

    [Test("Un proyecto de una versión más nueva se rechaza sin modificarlo")]
    public static async Task RefuseNewer()
    {
        using var folder = new TempFolder();
        string root;
        await using (var repository = await new ProjectService().CreateAsync(folder.Path, "Futuro")) root = repository.ProjectRoot;
        using (var connection = Open(root)) Execute(connection, "UPDATE schema_info SET version = 9;");
        var error = await Assert.ThrowsAsync<NotSupportedException>(() => new ProjectService().OpenAsync(root), "base v9");
        Assert.Contains("v9", error.Message);
        Assert.Equal(9L, Count(root, "SELECT max(version) FROM schema_info"), "la base no se tocó");

        var manifestPath = Path.Combine(root, ProjectLayout.ManifestFileName);
        var manifest = JsonSerializer.Deserialize<ProjectManifest>(File.ReadAllText(manifestPath), ManifestJson)!;
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest with { SchemaVersion = 12 }, ManifestJson));
        await Assert.ThrowsAsync<NotSupportedException>(() => new ProjectService().OpenAsync(root), "manifiesto v12");
    }

    [Test("Lecturas del catálogo en conexiones de solo lectura: no esperan a una escritura en curso")]
    public static async Task ReadsDuringWrite()
    {
        using var folder = new TempFolder();
        await using var repository = await new ProjectService().CreateAsync(folder.Path, "Concurrencia");
        var committed = new AssetRecord(Guid.NewGuid(), "a.png", AssetKind.Background, "h1", "Guardado", DateTimeOffset.UtcNow, Extension: ".png");
        await repository.UpsertAssetAsync(committed);
        using var writer = Open(repository.ProjectRoot);
        Execute(writer, "PRAGMA busy_timeout = 100; BEGIN IMMEDIATE;");
        Execute(writer, "INSERT INTO assets(id, relative_path, kind, sha256, display_name, imported_utc) VALUES($id, 'b.png', 2, 'h2', 'Pendiente', $t);",
            ("$id", Guid.NewGuid().ToString("D")), ("$t", DateTimeOffset.UtcNow.ToString("O")));
        var read = repository.GetAssetsAsync();
        var finished = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.True(finished == read, "la lectura terminó mientras otra conexión escribía");
        Assert.Sequence(["Guardado"], (await read).Select(x => x.DisplayName), "solo lo confirmado");
        Execute(writer, "ROLLBACK;");

        await using var second = new SqliteProjectRepository(repository.ProjectRoot, repository.Manifest);
        await second.AttachAsync();
        Assert.Equal(7, second.SchemaVersion, "segunda conexión sin migrar");
        Assert.Equal(1, (await second.GetAssetsByIdsAsync([committed.Id])).Count, "ve los datos");
    }

    [Test("Escaneo con SQLite real: importa, reescanea estable, detecta movidos, respeta usados y busca")]
    public static async Task Scan()
    {
        using var folder = new TempFolder();
        var library = folder.File("biblioteca");
        TestMedia.SolidPng(Path.Combine(library, "Bart", "feliz.png"), 8, 8, (255, 0, 0));
        TestMedia.SolidPng(Path.Combine(library, "Bart", "triste.png"), 9, 9, (0, 0, 255));
        TestMedia.SolidPng(Path.Combine(library, "fondos", "Cocina de noche.png"), 10, 10, (0, 120, 0));
        TestMedia.SolidPng(Path.Combine(library, "basura", "usado.png"), 11, 11, (1, 2, 3));
        TestMedia.SolidPng(Path.Combine(library, "basura", "libre.png"), 12, 12, (3, 2, 1));
        TestMedia.Wav(Path.Combine(library, "sonidos", "golpe.wav"), 300);

        await using var repository = await new ProjectService().CreateAsync(folder.Path, "Escaneo");
        var source = new AssetSource(Guid.NewGuid(), "Biblioteca", library, true, DateTimeOffset.UtcNow);
        await repository.UpsertAssetSourceAsync(source);
        var rules = new List<FolderRule>
        {
            new(Guid.NewGuid(), source.Id, "Bart", FolderClassification.CharacterRenders, "Bart", null),
            new(Guid.NewGuid(), source.Id, "fondos", FolderClassification.Backgrounds, null, null),
            new(Guid.NewGuid(), source.Id, "basura", FolderClassification.Props, null, null),
            new(Guid.NewGuid(), source.Id, "sonidos", FolderClassification.SoundEffects, null, null)
        };
        await repository.ReplaceFolderRulesAsync(source.Id, rules);
        var service = new AssetLibraryService();

        var first = await service.ScanSourceAsync(repository, source, rules);
        Assert.Equal(6, first.Imported, "importados");
        var assets = await repository.GetAssetsAsync();
        Assert.Equal(AssetKind.CharacterSprite, assets.Single(x => x.DisplayName == "feliz").Kind, "render por regla de carpeta");
        Assert.Equal("Bart", assets.Single(x => x.DisplayName == "feliz").SubjectName, "personaje de la carpeta");
        Assert.Equal(AssetKind.SoundEffect, assets.Single(x => x.DisplayName == "golpe").Kind, "sonido");

        var second = await service.ScanSourceAsync(repository, source, rules);
        Assert.Equal((0, 0), (second.Imported, second.Moved), "reescaneo sin cambios");

        // A block uses «usado»; then the folder is ignored and «feliz» is moved and renamed.
        var used = assets.Single(x => x.DisplayName == "usado");
        var libre = assets.Single(x => x.DisplayName == "libre");
        var happy = assets.Single(x => x.DisplayName == "feliz");
        var episode = new Episode(Guid.NewGuid(), 1, "Piloto");
        var scene = new Scene(Guid.NewGuid(), episode.Id, 0, "Cocina", 0);
        await repository.UpsertEpisodeAsync(episode);
        await repository.UpsertSceneAsync(scene);
        await repository.ReplaceSceneScriptBlocksAsync(scene.Id, [new SceneScriptBlock(Guid.NewGuid(), scene.Id, 0, ScriptBlockKind.Image, AssetId: used.Id)]);
        Directory.CreateDirectory(Path.Combine(library, "Bart", "poses"));
        File.Move(Path.Combine(library, "Bart", "feliz.png"), Path.Combine(library, "Bart", "poses", "feliz_v2.png"));
        rules[2] = rules[2] with { Classification = FolderClassification.Ignore };
        await repository.ReplaceFolderRulesAsync(source.Id, rules);
        await service.ScanSourceAsync(repository, source, rules);

        var after = (await repository.GetAssetsAsync()).ToDictionary(x => x.Id);
        Assert.Equal("Bart/poses/feliz_v2.png", after[happy.Id].SourceRelativePath, "movido conserva su ID");
        Assert.True(after.TryGetValue(used.Id, out var keptUsed) && keptUsed.IsMissing, "ignorado pero usado: se conserva oculto");
        Assert.False(after.ContainsKey(libre.Id), "ignorado sin uso: se quita del catálogo");

        var hits = await repository.SearchAssetIdsAsync("coci noche");
        Assert.Sequence([assets.Single(x => x.DisplayName == "Cocina de noche").Id], hits!, "búsqueda por palabras");
        var picker = await repository.SearchAssetsForPickerAsync(new AssetPickerQuery([".png"], null, "feliz"));
        Assert.Equal(happy.Id, picker.Single().Asset.Id, "selector de recursos");
        var wav = await repository.SearchAssetsForPickerAsync(new AssetPickerQuery([".wav"], null, ""));
        Assert.Equal("golpe", wav.Single().Asset.DisplayName, "filtro por extensión");

        await repository.DeleteAssetSourceAsync(source.Id);
        Assert.Equal(0, (await repository.GetAssetsAsync()).Count(x => x.SourceId == source.Id), "fuente quitada");
        Assert.Equal(0, (await repository.SearchAssetIdsAsync("cocina"))!.Count, "y fuera del índice de búsqueda");
    }
}
