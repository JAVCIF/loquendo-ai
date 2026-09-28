using LoquendoAI.Core.Models;

namespace LoquendoAI.Infrastructure.Projects;

public static class FolderRuleSuggestions
{
    public static FolderClassification Suggest(string folderNameOrPath)
    {
        // For nested mixed trees the closest folder is the strongest clue.
        // Example: "Personaje/renders/sonidos" should resolve to SFX for audio
        // instead of being swallowed by the older parent hint "renders".
        var segments = folderNameOrPath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var i = segments.Length - 1; i >= 0; i--)
        {
            var suggestion = SuggestSingleName(segments[i]);
            if (suggestion != FolderClassification.Auto)
                return suggestion;
        }

        // Also support callers that pass a plain name or unusual separator text.
        return SuggestSingleName(folderNameOrPath);
    }

    private static FolderClassification SuggestSingleName(string folderName)
    {
        var name = Normalize(folderName);

        if (ContainsAny(name, "fondo", "fondos", "background", "backgrounds", "escenario", "escenarios"))
            return FolderClassification.Backgrounds;
        if (ContainsAny(name, "efectos sonidos", "efectos sonido", "efecto sonido", "sfx", "sound effect", "sound effects", "sonidos"))
            return FolderClassification.SoundEffects;
        if (ContainsAny(name, "musica", "music", "bgm") || ContainsToken(name, "ost"))
            return FolderClassification.Music;
        if (ContainsAny(name, "videos", "video", "clips", "peliculas"))
            return FolderClassification.Videos;
        if (ContainsAny(name, "efectos chingones", "efectos visuales", "visual effects", "vfx", "overlays", "overlay"))
            return FolderClassification.VisualEffects;
        if (ContainsAny(name, "meme", "memes", "relleno", "rellenos", "mlg"))
            return FolderClassification.Memes;
        if (ContainsAny(name, "prop", "props", "objeto", "objetos"))
            return FolderClassification.Props;
        if (ContainsAny(name, "render", "renders", "sprites", "personajes"))
            return FolderClassification.CharacterRenders;

        return FolderClassification.Auto;
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(value.Contains);

    private static bool ContainsToken(string value, string token)
    {
        var tokens = value.Split(
            new[] { ' ', '-', '_', '.', '(', ')', '[', ']', '{', '}' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Any(t => string.Equals(t, token, StringComparison.OrdinalIgnoreCase));
    }
}
