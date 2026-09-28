using System.Text.Json;
using System.Text.Json.Serialization;
using LoquendoAI.Infrastructure.Composition;

namespace LoquendoAI.Infrastructure.Tts;

/// <summary>A TTS voice used directly by a line (without a saved profile): engine + voice name as the engine lists it.</summary>
public sealed record DirectVoiceRef(string Provider, string Voice)
{
    public string Engine => VoiceSelectorSettings.EngineLabel(Provider);

    /// <summary>What the voice selectors show: «TTS7 · Jorge», «SAPI4 · Juan», «SAPI5 · IVONA 2 Enrique».</summary>
    public string Label => $"{Engine} · {Voice}";

    public bool Is(string? provider, string? voice) =>
        string.Equals(Provider, provider, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Voice, voice, StringComparison.OrdinalIgnoreCase);

    /// <summary>The direct voice of a block (1.4.0: any engine of the selector, before only Loquendo TTS7).</summary>
    public static DirectVoiceRef? Of(BlockParameters parameters) =>
        VoiceSelectorSettings.IsDirectProvider(parameters.DirectVoiceProvider) && !string.IsNullOrWhiteSpace(parameters.DirectVoiceId)
            ? new DirectVoiceRef(VoiceSelectorSettings.Canonical(parameters.DirectVoiceProvider!), parameters.DirectVoiceId!)
            : null;
}

/// <summary>A voice the user added to or removed from the selectors.</summary>
public sealed record VoiceSelectorEntry(string Provider, string Voice, bool Show);

/// <summary>
/// Which TTS voices the voice selectors offer (Editor «Voz», Diálogos, recorded takes) — 1.4.0. The installed voices
/// come from the engines (TTS bridge); these settings only filter them. Without a choice of the user: every Loquendo
/// TTS7 voice, the SAPI4 voices Juan and Antonio (through BALCON) and the SAPI5 voices IVONA and Juan. Voice Lab edits the list
/// («Voces en los selectores…»): a voice can be shown or hidden, and a voice the engine did not list (e.g. BALCON not
/// found right now) can be added by name. Saved per PC (the installed voices are the PC's) in selector-voces.json.
/// </summary>
public sealed class VoiceSelectorSettings
{
    public const string Tts7 = "loquendo7-native";
    public const string Sapi4 = "balcon-sapi4";
    public const string Sapi5 = "sapi5-x86";

    /// <summary>The engines a line can use directly, in the order the selectors list them.</summary>
    public static readonly IReadOnlyList<string> Providers = [Tts7, Sapi4, Sapi5];

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>The user's explicit choices; voices not listed here follow <see cref="DefaultShown"/>.</summary>
    public List<VoiceSelectorEntry> Voices { get; set; } = [];

    public static string EngineLabel(string provider) => Canonical(provider) switch
    {
        Tts7 => "TTS7",
        Sapi4 => "SAPI4",
        Sapi5 => "SAPI5",
        _ => provider
    };

    public static string EngineName(string provider) => Canonical(provider) switch
    {
        Tts7 => "Loquendo TTS7",
        Sapi4 => "SAPI4 (BALCON)",
        Sapi5 => "SAPI5",
        _ => provider
    };

    /// <summary>The bridge's aliases («loquendo», «infovox-sapi4», «sapi») written as the selector's keys.</summary>
    public static string Canonical(string provider) => provider.Trim().ToLowerInvariant() switch
    {
        "loquendo7-native" or "loquendo" => Tts7,
        "balcon-sapi4" or "infovox-sapi4" => Sapi4,
        "sapi5-x86" or "sapi" => Sapi5,
        var other => other
    };

    public static bool IsDirectProvider(string? provider) =>
        !string.IsNullOrWhiteSpace(provider) && Providers.Contains(Canonical(provider));

    /// <summary>Without a choice of the user: all of TTS7, Juan and Antonio of SAPI4, and IVONA and Juan of SAPI5 (1.4.1).</summary>
    public static bool DefaultShown(string provider, string voice) => Canonical(provider) switch
    {
        Tts7 => true,
        Sapi4 => Word(voice, "Juan") || Word(voice, "Antonio"),
        Sapi5 => Contains(voice, "IVONA") || Word(voice, "Juan"),
        _ => false
    };

    private static bool Contains(string text, string part) => text.Contains(part, StringComparison.OrdinalIgnoreCase);

    /// <summary>The name as a word of its own («Juan», «ScanSoft Juan_Full_22kHz»), not inside another («Juana»).</summary>
    private static bool Word(string text, string name) => System.Text.RegularExpressions.Regex.IsMatch(text,
        $@"(?<!\p{{L}}){name}(?!\p{{L}})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public bool? Choice(string provider, string voice) =>
        Voices.LastOrDefault(x => Same(x, provider, voice))?.Show;

    public bool IsShown(string provider, string voice) => Choice(provider, voice) ?? DefaultShown(provider, voice);

    /// <summary>Records a choice of the user (it wins over the defaults until «Predeterminadas»).</summary>
    public void Set(string provider, string voice, bool show)
    {
        provider = Canonical(provider);
        voice = voice.Trim();
        if (voice.Length == 0 || !IsDirectProvider(provider)) return;
        Voices.RemoveAll(x => Same(x, provider, voice));
        Voices.Add(new VoiceSelectorEntry(provider, voice, show));
    }

    /// <summary>A voice added by hand: shown, and kept in the list even when its engine does not list it.</summary>
    public void AddManual(string provider, string voice) => Set(provider, voice, true);

    public void ResetToDefaults() => Voices.Clear();

    /// <summary>
    /// The voices the selectors offer: the installed voices (<paramref name="catalog"/>, per engine) that are shown,
    /// plus voices added by hand that the engine did not list. Engine order, then name.
    /// </summary>
    public IReadOnlyList<DirectVoiceRef> Visible(IReadOnlyDictionary<string, IReadOnlyList<string>> catalog)
    {
        var result = new List<DirectVoiceRef>();
        foreach (var provider in Providers)
        {
            var installed = catalog.TryGetValue(provider, out var list) ? list : [];
            var voices = installed.Where(x => !string.IsNullOrWhiteSpace(x) && IsShown(provider, x))
                .Concat(Voices.Where(x => x.Show && Canonical(x.Provider) == provider &&
                    !installed.Contains(x.Voice, StringComparer.OrdinalIgnoreCase)).Select(x => x.Voice))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase);
            result.AddRange(voices.Select(x => new DirectVoiceRef(provider, x)));
        }
        return result;
    }

    /// <summary>Every voice the editor lists for <paramref name="provider"/>: installed ones and those added by hand.</summary>
    public IReadOnlyList<string> Known(string provider, IReadOnlyList<string> installed) =>
        installed.Concat(Voices.Where(x => Canonical(x.Provider) == Canonical(provider)).Select(x => x.Voice))
            .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToArray();

    private static bool Same(VoiceSelectorEntry entry, string provider, string voice) =>
        Canonical(entry.Provider) == Canonical(provider) && string.Equals(entry.Voice.Trim(), voice.Trim(), StringComparison.OrdinalIgnoreCase);

    public static VoiceSelectorSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new VoiceSelectorSettings();
            var loaded = JsonSerializer.Deserialize<VoiceSelectorSettings>(File.ReadAllText(path), Json) ?? new VoiceSelectorSettings();
            loaded.Voices = (loaded.Voices ?? []).Where(x => x is { Provider.Length: > 0, Voice.Length: > 0 } && IsDirectProvider(x.Provider))
                .Select(x => x with { Provider = Canonical(x.Provider), Voice = x.Voice.Trim() }).ToList();
            return loaded;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new VoiceSelectorSettings();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, path, true);
    }
}
