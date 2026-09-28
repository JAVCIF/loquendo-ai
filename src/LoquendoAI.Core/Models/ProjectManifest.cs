namespace LoquendoAI.Core.Models;

public sealed record ProjectManifest(
    Guid Id,
    string Name,
    int SchemaVersion,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc)
{
    /// <summary>
    /// The one place the project schema version is defined. The database migrates to it
    /// (schema_info holds the database's version) and project.json mirrors it, so older builds,
    /// which compare project.json with their own version, refuse a newer project instead of
    /// opening it.
    /// </summary>
    public const int CurrentSchemaVersion = 7;

    public static ProjectManifest Create(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var now = DateTimeOffset.UtcNow;
        return new ProjectManifest(Guid.NewGuid(), name.Trim(), CurrentSchemaVersion, now, now);
    }
}
