using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LoquendoAI.Core.Models;

namespace LoquendoAI.Infrastructure.Library;

/// <summary>How «IA: analizar medios» describes a file: images by what they show (vision model), audio by its
/// name first (text model, then optionally by listening to it), GIF and video by name only.</summary>
public enum MediaCategory { None, Image, Gif, Video, Audio }

/// <summary>One file sent to the name analysis: a short reference (A1, A2…), its kind and the words we have.</summary>
public sealed record MediaNameItem(string Ref, MediaCategory Category, AssetKind Kind, string FileName, string Folder);

/// <summary>The description the AI gave for one file. <see cref="Confident"/> is false when the name did not say
/// enough (then its content can be analyzed, if the user wants).</summary>
public sealed record MediaDescription(string Description, string Role, string Mood, string Subject, bool Confident);

/// <summary>
/// The parts of the media analysis that do not need the interface (1.3.0): classifying files, spotting names that
/// say nothing, and the compact batched prompt/schema that describes up to <see cref="NameBatchSize"/> files per
/// request by their names (few tokens, one call for many files).
/// </summary>
public static class MediaAnalysis
{
    public const string Version = "medios-1";
    public const int NameBatchSize = 25;
    /// <summary>Seconds of an audio sent when it is analyzed by its content.</summary>
    public const int AudioClipSeconds = 30;

    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };
    private static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".webm", ".avi", ".mkv", ".wmv", ".m4v", ".flv", ".mpg", ".mpeg" };
    private static readonly HashSet<string> Audios = new(StringComparer.OrdinalIgnoreCase) { ".wav", ".mp3", ".ogg", ".flac", ".m4a", ".aac", ".wma", ".opus", ".mid", ".midi" };

    public static MediaCategory Classify(string extension, AssetKind kind)
    {
        extension = extension.StartsWith('.') ? extension : "." + extension;
        if (extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)) return MediaCategory.Gif;
        if (Images.Contains(extension)) return MediaCategory.Image;
        if (Videos.Contains(extension)) return MediaCategory.Video;
        if (Audios.Contains(extension)) return MediaCategory.Audio;
        return kind switch
        {
            AssetKind.Music or AssetKind.SoundEffect or AssetKind.Audio => MediaCategory.Audio,
            AssetKind.Video => MediaCategory.Video,
            _ => MediaCategory.None
        };
    }

    /// <summary>Only MP3/WAV/OGG/FLAC/M4A… can be listened to (MIDI has no recorded sound).</summary>
    public static bool CanListen(string extension) =>
        Audios.Contains(extension.StartsWith('.') ? extension : "." + extension) &&
        !extension.EndsWith("mid", StringComparison.OrdinalIgnoreCase) && !extension.EndsWith("midi", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A name that says nothing about the content: numbers, hashes, camera/recorder names («track01», «sfx_023»,
    /// «IMG_2041», «a1b2c3d4…», «audio (3)»). These skip the name analysis (it would only spend tokens).
    /// </summary>
    public static bool LooksGeneric(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName).Trim();
        var words = Regex.Replace(name, @"[_\-.()\[\]{}\s]+", " ").Trim();
        if (words.Length == 0) return true;
        if (Regex.IsMatch(words, @"^[0-9a-f]{12,}$", RegexOptions.IgnoreCase)) return true; // hash / GUID
        var letters = Regex.Replace(words, @"[^\p{L} ]", " ");
        var meaningful = letters.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !GenericWords.Contains(w)).ToArray();
        return meaningful.Length == 0;
    }

    private static readonly HashSet<string> GenericWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "track", "pista", "audio", "sound", "sonido", "sfx", "fx", "music", "musica", "música", "song", "cancion", "canción",
        "clip", "video", "vid", "img", "image", "imagen", "dsc", "gif", "file", "archivo", "untitled", "sin", "titulo", "título",
        "new", "nuevo", "copy", "copia", "rec", "recording", "grabacion", "grabación", "voice", "voz", "sample", "take", "export",
        "final", "mix", "edit", "wav", "mp3", "ogg", "mp4", "whatsapp", "vid", "pxl", "screen", "captura", "record", "part", "parte"
    };

    public const string NameSystem = """
        Eres el catalogador de una biblioteca de recursos para videos estilo Loquendo. Recibes archivos (música, efectos
        de sonido, videos y GIF) SOLO por su nombre y carpeta: no puedes oírlos ni verlos. Para cada ref devuelve:
        - description: qué es y cómo se usaría, en español, máx. 160 caracteres. Si reconoces una canción o un meme
          conocido, di cuál es (título, autor/origen) y cómo suena o qué muestra (género, ritmo, ánimo). Para un SFX, cómo
          suena (p. ej. «explosión fuerte y corta»).
        - role: musica | sfx | ambiente | voz | video | meme | gif | transicion | otro.
        - mood: ánimo o energía en 1–3 palabras (épico, tenso, cómico, triste, tranquilo…).
        - subject: obra, personaje, lugar u objeto principal si se deduce; si no, vacío.
        - confianza: alta (el nombre identifica claramente el contenido), media (se deduce lo general), baja (el nombre no
          dice qué suena o qué se ve; entonces no inventes: deja description corta o vacía).
        No inventes datos que el nombre no permita deducir. Responde SOLO con el JSON del esquema.
        """;

    /// <summary>The user message of a name batch: one short line per file.</summary>
    public static string NamePrompt(IEnumerable<MediaNameItem> items)
    {
        var text = new StringBuilder("ARCHIVOS:\n");
        foreach (var item in items)
        {
            var kind = item.Category switch
            {
                MediaCategory.Audio => item.Kind switch
                {
                    AssetKind.Music => "música", AssetKind.SoundEffect => "efecto de sonido", _ => "audio"
                },
                MediaCategory.Gif => "GIF animado",
                MediaCategory.Video => item.Kind == AssetKind.Meme ? "video meme" : "video",
                _ => "archivo"
            };
            text.Append(item.Ref).Append(" | ").Append(kind).Append(" | nombre=").Append(Short(item.FileName, 90));
            if (item.Folder.Length > 0) text.Append(" | carpeta=").Append(Short(item.Folder, 60));
            text.Append('\n');
        }
        return text.ToString();
    }

    private static string Short(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    public static object NameSchema(IEnumerable<string> refs) => new Dictionary<string, object>
    {
        ["type"] = "object",
        ["properties"] = new Dictionary<string, object>
        {
            ["items"] = new Dictionary<string, object>
            {
                ["type"] = "array",
                ["items"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>
                    {
                        ["ref"] = new Dictionary<string, object> { ["type"] = "string", ["enum"] = refs.ToArray() },
                        ["description"] = new Dictionary<string, object> { ["type"] = "string" },
                        ["role"] = new Dictionary<string, object> { ["type"] = "string" },
                        ["mood"] = new Dictionary<string, object> { ["type"] = "string" },
                        ["subject"] = new Dictionary<string, object> { ["type"] = "string" },
                        ["confianza"] = new Dictionary<string, object> { ["type"] = "string", ["enum"] = new[] { "alta", "media", "baja" } }
                    },
                    ["required"] = new[] { "ref", "description", "role", "mood", "subject", "confianza" },
                    ["additionalProperties"] = false
                }
            }
        },
        ["required"] = new[] { "items" },
        ["additionalProperties"] = false
    };

    /// <summary>The answer of a name batch by ref. A missing ref is simply not described (it stays pending).</summary>
    public static IReadOnlyDictionary<string, MediaDescription> ParseNameAnswer(string output)
    {
        var result = new Dictionary<string, MediaDescription>(StringComparer.OrdinalIgnoreCase);
        var start = output.IndexOf('{');
        var end = output.LastIndexOf('}');
        if (start < 0 || end < start) throw new InvalidDataException("La IA no devolvió el JSON esperado.");
        using var json = JsonDocument.Parse(output[start..(end + 1)]);
        if (!json.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("La respuesta de la IA no trae «items».");
        foreach (var item in items.EnumerateArray())
        {
            string Get(string key) => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                ? Regex.Replace(value.GetString() ?? "", @"\s+", " ").Trim() : "";
            var reference = Get("ref");
            if (reference.Length == 0) continue;
            var description = Get("description");
            var confident = Get("confianza").ToLowerInvariant() is "alta" or "media" && description.Length > 0;
            result[reference] = new MediaDescription(Limit(description), Limit(Get("role"), 40), Limit(Get("mood"), 60),
                Limit(Get("subject"), 80), confident);
        }
        return result;
    }

    public const string ListenSystem = """
        Escucha el audio (máx. 30 s) de una biblioteca de recursos para videos estilo Loquendo y descríbelo en español.
        description: qué se oye y para qué sirve en un video (máx. 160 caracteres); si reconoces la canción, di cuál es.
        role: musica | sfx | ambiente | voz | otro. mood: ánimo en 1–3 palabras. subject: obra o fuente del sonido, o vacío.
        Responde SOLO con el JSON del esquema.
        """;

    public static string ListenPrompt(string fileName, AssetKind kind) =>
        $"Archivo: {Short(fileName, 90)} ({(kind == AssetKind.Music ? "música" : kind == AssetKind.SoundEffect ? "efecto de sonido" : "audio")}).";

    private static string Limit(string value, int max = 500) => value.Length <= max ? value : value[..max];
}
