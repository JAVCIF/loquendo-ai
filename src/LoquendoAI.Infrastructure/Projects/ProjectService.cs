using System.Text.Json;
using LoquendoAI.Core.Models;
using LoquendoAI.Infrastructure.Persistence;

namespace LoquendoAI.Infrastructure.Projects;

public sealed class ProjectService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<SqliteProjectRepository> CreateAsync(
        string parentDirectory,
        string projectName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

        var safeName = string.Concat(projectName.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var root = Path.Combine(Path.GetFullPath(parentDirectory), safeName);
        Directory.CreateDirectory(root);

        foreach (var directory in ProjectLayout.Directories)
            Directory.CreateDirectory(Path.Combine(root, directory.Replace('/', Path.DirectorySeparatorChar)));

        var manifest = ProjectManifest.Create(projectName);
        await SaveManifestAsync(root, manifest, cancellationToken);

        var repository = new SqliteProjectRepository(root, manifest);
        await repository.InitializeAsync(cancellationToken);
        return repository;
    }

    public async Task<SqliteProjectRepository> OpenAsync(
        string projectRoot,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(projectRoot);
        var manifestPath = Path.Combine(root, ProjectLayout.ManifestFileName);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("No es un proyecto Loquendo AI válido: falta el manifiesto.", manifestPath);

        var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
        var manifest = JsonSerializer.Deserialize<ProjectManifest>(json, JsonOptions)
            ?? throw new InvalidDataException("El manifiesto del proyecto está vacío o dañado.");

        if (manifest.SchemaVersion > ProjectManifest.CurrentSchemaVersion)
            throw new NotSupportedException($"El proyecto usa schema v{manifest.SchemaVersion}, pero esta versión soporta hasta v{ProjectManifest.CurrentSchemaVersion}.");

        var needsManifestUpgrade = manifest.SchemaVersion < ProjectManifest.CurrentSchemaVersion;
        if (needsManifestUpgrade)
        {
            manifest = manifest with
            {
                SchemaVersion = ProjectManifest.CurrentSchemaVersion,
                UpdatedUtc = DateTimeOffset.UtcNow
            };
        }

        var repository = new SqliteProjectRepository(root, manifest);
        try
        {
            await repository.InitializeAsync(cancellationToken);
            if (needsManifestUpgrade)
                await SaveManifestAsync(root, manifest, cancellationToken);
            return repository;
        }
        catch
        {
            await repository.DisposeAsync();
            throw;
        }
    }

    private static Task SaveManifestAsync(string root, ProjectManifest manifest, CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, ProjectLayout.ManifestFileName);
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        return File.WriteAllTextAsync(path, json, cancellationToken);
    }
}
