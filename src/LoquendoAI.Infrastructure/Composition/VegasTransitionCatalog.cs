using System.Text;
using System.Text.Json;

namespace LoquendoAI.Infrastructure.Composition;

public sealed record VegasTransitionChoice(string Name, string Id, string[] Presets)
{
    public string DisplayName => Name + " [" + Vendor + "]";

    public string Vendor =>
        Id.Contains("NewBlue", StringComparison.OrdinalIgnoreCase) || Name.StartsWith("NewBlue", StringComparison.OrdinalIgnoreCase)
            ? "NewBlue" : Id.Contains("sapphire", StringComparison.OrdinalIgnoreCase) || Id.Contains("genarts", StringComparison.OrdinalIgnoreCase)
                ? "Sapphire" : Id.Contains("boris", StringComparison.OrdinalIgnoreCase) || Name.StartsWith("BCC", StringComparison.OrdinalIgnoreCase)
                    ? "Boris" : Id.Contains("redgiant", StringComparison.OrdinalIgnoreCase)
                        ? "Red Giant" : Id.Contains("vegascreativesoftware", StringComparison.OrdinalIgnoreCase) ||
                                        Id.Contains("magix", StringComparison.OrdinalIgnoreCase)
                            ? "VEGAS" : "Plugin";
}

/// <summary>Transitions and presets from VEGAS's Transitions collection. The built-in list ships
/// with the app; a user catalog exported from the real installation (scripts/vegas/Listar_transiciones_*.cs
/// → transiciones_vegas.txt) extends it. The host installation is checked again when the VEGAS
/// import script runs.</summary>
public static class VegasTransitionCatalog
{
    private static readonly object Gate = new();
    private static IReadOnlyList<VegasTransitionChoice>? _entries;

    /// <summary>%LOCALAPPDATA%\LoquendoAI\vegas_transiciones.json: the imported installation catalog.</summary>
    public static string UserCatalogPath => AppDataFolder.PathOf("vegas_transiciones.json");

    /// <summary>Optional favourites, one per line: "Name or ID | preset" (preset optional).
    /// The AI Director offers these first.</summary>
    public static string FavoritesPath => AppDataFolder.PathOf("vegas_transiciones_favoritas.txt");

    public static IReadOnlyList<VegasTransitionChoice> All
    {
        get
        {
            lock (Gate) return _entries ??= Load();
        }
    }

    public static int UserCatalogCount
    {
        get
        {
            try { return File.Exists(UserCatalogPath) ? ReadJson(File.ReadAllText(UserCatalogPath)).Count : 0; }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return 0; }
        }
    }

    public static void Reload()
    {
        lock (Gate) _entries = null;
    }

    private static IReadOnlyList<VegasTransitionChoice> Load()
    {
        using var stream = typeof(VegasTransitionCatalog).Assembly.GetManifestResourceStream(
            "LoquendoAI.Infrastructure.Composition.VegasTransitionCatalog.json")
            ?? throw new InvalidOperationException("No se incluyó el catálogo de transiciones de VEGAS.");
        var builtIn = JsonSerializer.Deserialize<VegasTransitionChoice[]>(stream)
            ?? throw new InvalidOperationException("El catálogo de transiciones de VEGAS está vacío.");
        IReadOnlyList<VegasTransitionChoice> user = [];
        try
        {
            if (File.Exists(UserCatalogPath)) user = ReadJson(File.ReadAllText(UserCatalogPath));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { /* Built-in list still works. */ }
        return Merge(user, builtIn);
    }

    /// <summary>The installation catalog wins for an ID present in both lists.</summary>
    internal static IReadOnlyList<VegasTransitionChoice> Merge(IEnumerable<VegasTransitionChoice> preferred,
        IEnumerable<VegasTransitionChoice> fallback) =>
        preferred.Concat(fallback)
            .Where(x => !string.IsNullOrWhiteSpace(x.Id) && !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => x.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

    private static IReadOnlyList<VegasTransitionChoice> ReadJson(string json) =>
        JsonSerializer.Deserialize<VegasTransitionChoice[]>(json) ?? [];

    /// <summary>Parses the report written by the VEGAS listing script:
    /// "Name | {UniqueID}" lines followed by indented preset names.</summary>
    public static IReadOnlyList<VegasTransitionChoice> ParseInstallationReport(string text)
    {
        var result = new List<(string Name, string Id, List<string> Presets)>();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.Trim().Length == 0) continue;
            var indented = raw.StartsWith(' ') || raw.StartsWith('\t');
            if (!indented)
            {
                var separator = raw.LastIndexOf(" | ", StringComparison.Ordinal);
                if (separator <= 0) continue; // Header or unknown line.
                var name = raw[..separator].Trim();
                var id = raw[(separator + 3)..].Trim();
                if (name.Length > 0 && id.Length > 0) result.Add((name, id, []));
                continue;
            }
            var preset = raw.Trim();
            if (result.Count == 0 || preset.StartsWith("Presets no disponibles", StringComparison.OrdinalIgnoreCase)) continue;
            if (!result[^1].Presets.Contains(preset, StringComparer.Ordinal)) result[^1].Presets.Add(preset);
        }
        return result.Select(x => new VegasTransitionChoice(x.Name, x.Id,
            x.Presets.Count == 0 ? ["(Predeterminado)"] : x.Presets.ToArray())).ToArray();
    }

    /// <summary>Imports transiciones_vegas.txt into the user catalog and reloads it.</summary>
    public static int ImportInstallationReport(string reportPath)
    {
        var entries = Merge(ParseInstallationReport(File.ReadAllText(reportPath, Encoding.UTF8)), []);
        if (entries.Count == 0)
            throw new InvalidDataException("El archivo no contiene transiciones con el formato «Nombre | ID» del script de VEGAS.");
        Directory.CreateDirectory(Path.GetDirectoryName(UserCatalogPath)!);
        var temp = UserCatalogPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(entries), new UTF8Encoding(false));
        File.Move(temp, UserCatalogPath, true);
        Reload();
        return entries.Count;
    }

    public static VegasTransitionChoice? Find(string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector)) return null;
        var all = All;
        var candidates = all.Where(x => string.Equals(x.Id, selector.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length == 0)
            candidates = all.Where(x => string.Equals(x.Name, selector.Trim(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.DisplayName, selector.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    public static bool TryResolve(string? selector, string? preset, out VegasTransitionChoice? plugin, out string selectedPreset)
    {
        plugin = Find(selector);
        selectedPreset = "";
        if (plugin is null) return false;
        var name = string.IsNullOrWhiteSpace(preset) ? "(Predeterminado)" : preset.Trim();
        selectedPreset = plugin.Presets.FirstOrDefault(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)) ?? "";
        if (selectedPreset.Length == 0 && string.IsNullOrWhiteSpace(preset))
            selectedPreset = plugin.Presets.FirstOrDefault() ?? "";
        return selectedPreset.Length > 0;
    }

    public sealed record PluginOption(string Id, string Name, string Preset, string Vendor);

    // Spanish words in a premise → words used in transition names.
    private static readonly (string Spanish, string[] English)[] Keywords =
    [
        ("zoom", ["Zoom"]), ("acerc", ["Zoom"]), ("pagina", ["Page"]), ("libro", ["Page", "Book"]),
        ("explo", ["Explode", "Blow", "Blast"]), ("gir", ["Spin", "Twist", "Rotate", "Flip"]), ("vuelta", ["Flip", "Spin"]),
        ("destello", ["Flash", "Glow", "Light"]), ("flash", ["Flash"]), ("luz", ["Light", "Glow", "Flash"]),
        ("desenfo", ["Blur"]), ("borros", ["Blur"]), ("tembl", ["Shake"]), ("sacud", ["Shake"]),
        ("glitch", ["Glitch", "Digital"]), ("error", ["Glitch"]), ("cubo", ["Cube"]), ("caja", ["Box", "Cube"]),
        ("barrido", ["Wipe", "Slide", "Push"]), ("desliz", ["Slide", "Push"]), ("empuj", ["Push"]),
        ("disolv", ["Dissolve"]), ("fund", ["Dissolve", "Fade"]), ("fuego", ["Burn", "Fire"]), ("quem", ["Burn"]),
        ("agua", ["Water", "Ripple", "Wave"]), ("onda", ["Ripple", "Wave"]), ("confeti", ["Confetti"]),
        ("cristal", ["Glass", "Shatter"]), ("romp", ["Shatter", "Break"]), ("puerta", ["Door"]), ("estrella", ["Star"]),
        ("corazon", ["Heart"]), ("pixel", ["Pixel", "Mosaic"]), ("retro", ["Film", "Old", "VHS"]), ("pelicula", ["Film"])
    ];

    private const string DissolveId = "{Svfx:com.vegascreativesoftware:dissolve}";

    private static readonly (string Spanish, string Preset)[] PresetHints =
    [
        ("negro", "Desvanecimiento en negro"), ("oscur", "Desvanecimiento en negro"), ("apag", "Desvanecimiento en negro"),
        ("blanco", "Desvanecimiento en blanco"), ("desmay", "Desvanecimiento en blanco")
    ];

    // General-purpose transitions that exist in most VEGAS installs; used when nothing else matches.
    private static readonly string[] Curated =
    [
        "Cross Dissolve", "Dissolve", "Push", "Slide", "Zoom", "Iris", "Barn Door", "Clock Wipe", "Flash",
        "NewBlue MB Zoom", "NewBlue 3D Page Turn", "NewBlue 3D Cube Tumble", "S_DissolveBlur", "S_DissolveFlashbulbs",
        "S_WipeStar", "S_ShakeDissolve"
    ];

    /// <summary>
    /// A short list of (transition, preset) pairs the AI may use: favourites first, then
    /// transitions whose names match words of the premise, then well-known ones, then random
    /// extras for variety. Presets containing "|" or line breaks are skipped (they would break
    /// the instruction syntax).
    /// </summary>
    public static IReadOnlyList<PluginOption> OptionsForDirector(string premise, int max = 16, Random? random = null)
    {
        random ??= new Random();
        var all = All.Where(x => !x.Name.Contains('|') && x.Presets.Length > 0).ToArray();
        var result = new List<PluginOption>();
        string Normalize(string value)
        {
            var decomposed = value.Normalize(NormalizationForm.FormD);
            var text = new StringBuilder();
            foreach (var c in decomposed)
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    text.Append(char.ToLowerInvariant(c));
            return text.ToString();
        }
        static bool SafePreset(string preset) => !preset.Contains('|') && !preset.Contains('\n') && !preset.Contains('\r') && !preset.Contains('=');
        void Add(VegasTransitionChoice choice, string? preferredPreset)
        {
            if (result.Count >= max || result.Any(x => string.Equals(x.Id, choice.Id, StringComparison.OrdinalIgnoreCase))) return;
            var presets = choice.Presets.Where(SafePreset).ToArray();
            if (presets.Length == 0) return;
            var preset = presets.FirstOrDefault(x => string.Equals(x, preferredPreset, StringComparison.OrdinalIgnoreCase))
                ?? (presets.Length == 1 ? presets[0] : presets.Where(x => x != "(Predeterminado)" && !x.StartsWith("Reset", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(_ => random.Next()).FirstOrDefault() ?? presets[0]);
            result.Add(new PluginOption(choice.Id, choice.Name, preset, choice.Vendor));
        }

        try
        {
            if (File.Exists(FavoritesPath))
                foreach (var line in File.ReadAllLines(FavoritesPath))
                {
                    var parts = line.Split('|', 2, StringSplitOptions.TrimEntries);
                    if (parts[0].Length == 0 || parts[0].StartsWith('#')) continue;
                    if (Find(parts[0]) is { } favorite) Add(favorite, parts.Length > 1 ? parts[1] : null);
                }
        }
        catch (IOException) { /* Favourites are optional. */ }

        var normalized = Normalize(premise);
        // Fades to a colour are presets of VEGAS's own Dissolve, not transitions with their own name.
        foreach (var (spanish, preset) in PresetHints)
            if (normalized.Contains(spanish, StringComparison.Ordinal) && Find(DissolveId) is { } dissolve &&
                dissolve.Presets.Contains(preset, StringComparer.OrdinalIgnoreCase))
                Add(dissolve, preset);

        var wanted = Keywords.Where(k => normalized.Contains(k.Spanish, StringComparison.Ordinal))
            .SelectMany(k => k.English).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        // Premise matches take at most half of the list so general-purpose transitions remain.
        var keywordLimit = result.Count + Math.Max(1, max / 2);
        foreach (var word in wanted)
            foreach (var match in all.Where(x => x.Name.Contains(word, StringComparison.OrdinalIgnoreCase))
                         .OrderBy(_ => random.Next()).Take(2))
                if (result.Count < keywordLimit) Add(match, null);

        foreach (var name in Curated)
            foreach (var match in all.Where(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) ||
                                                 string.Equals(x.Name, "VEGAS " + name, StringComparison.OrdinalIgnoreCase)).Take(1))
                Add(match, null);

        foreach (var extra in all.OrderBy(_ => random.Next()))
        {
            if (result.Count >= max) break;
            Add(extra, null);
        }
        return result;
    }
}
