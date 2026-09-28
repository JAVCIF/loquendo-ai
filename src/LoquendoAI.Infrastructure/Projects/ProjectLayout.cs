namespace LoquendoAI.Infrastructure.Projects;

public static class ProjectLayout
{
    public static readonly string[] Directories =
    [
        "assets",
        "generated/voices",
        "generated/previews",
        "generated/images",
        "cache/thumbnails",
        "cache/proxies",
        "cache/tts-preview",
        "exports",
        "logs"
    ];

    public const string ManifestFileName = "project.loquendo.json";
    public const string DatabaseFileName = "project.db";
}
