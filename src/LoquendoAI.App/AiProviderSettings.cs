using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LoquendoAI.App;

/// <summary>A model of one provider. Shown as "gemma4:12b · Ollama"; stored as "ollama|gemma4:12b".</summary>
internal sealed record AiModelChoice(string Provider, string Model)
{
    public string Key => Provider + "|" + Model;

    public string ProviderName => AiProviderSettings.ProviderName(Provider);

    public override string ToString() => Model + " · " + ProviderName;

    public static AiModelChoice? Parse(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var separator = key.IndexOf('|');
        if (separator <= 0 || separator == key.Length - 1) return null;
        var provider = key[..separator];
        return AiProviderSettings.Providers.Contains(provider) ? new AiModelChoice(provider, key[(separator + 1)..]) : null;
    }
}

/// <summary>
/// AI configuration (v2, 1.0.0-beta.3): every provider can be used at the same time. The model lists
/// show the models the user marked as visible; the default Director and image models are chosen in
/// «Configurar IA». Remote model lists (Gemini, Claude) are cached so startup needs no network.
/// </summary>
internal sealed class AiProviderSettings
{
    public AiProviderSettings() { }

    public static readonly string[] Providers = ["ollama", "gemini", "claude", "openai"];

    private static readonly string Folder = AppPaths.DataFolder;
    private static readonly string SettingsPath = Path.Combine(Folder, "ai-provider.json");
    private static readonly string KeyPath = Path.Combine(Folder, "gemini-key.bin");
    private static readonly string ClaudeKeyPath = Path.Combine(Folder, "claude-key.bin");
    private static readonly string OpenAiKeyPath = Path.Combine(Folder, "openai-key.bin");

    /// <summary>0 in files written before 1.0.0-beta.3 (the field did not exist): they are migrated on load.</summary>
    public int Version { get; set; }
    /// <summary>Provider of versions ≤ 1.0.0-beta.2 (one provider at a time); only read to migrate.</summary>
    public string Provider { get; set; } = "ollama";
    /// <summary>"provider|model" (v2). Plain model names are migrated on load.</summary>
    public string DirectorModel { get; set; } = "";
    public string VisionModel { get; set; } = "";
    /// <summary>Gemini 3 thinking level: default, low, medium, high.</summary>
    public string ThinkingLevel { get; set; } = "default";
    /// <summary>Claude effort: default, low, medium, high, max.</summary>
    public string ClaudeEffort { get; set; } = "default";
    /// <summary>ChatGPT reasoning models (GPT-5 and later, o-series): default, low, medium, high.</summary>
    public string OpenAiEffort { get; set; } = "default";
    public Dictionary<string, List<string>> KnownModels { get; set; } = new();
    /// <summary>Explicit choices from «Modelos en las listas»; models without an entry use <see cref="DefaultVisible"/>.</summary>
    public Dictionary<string, bool> ModelVisibility { get; set; } = new();
    /// <summary>Per model key: "vision", "effort", "effort-max" (from the provider's catalog).</summary>
    public Dictionary<string, List<string>> ModelCapabilities { get; set; } = new();

    public bool IsConfigured => DirectorModel.Length > 0;

    public static string ProviderName(string provider) => provider switch
    {
        "gemini" => "Gemini", "claude" => "Claude", "openai" => "ChatGPT", _ => "Ollama"
    };

    public static AiProviderSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath) &&
                JsonSerializer.Deserialize<AiProviderSettings>(File.ReadAllText(SettingsPath)) is { } settings)
            {
                settings.KnownModels ??= new();
                settings.ModelVisibility ??= new();
                settings.ModelCapabilities ??= new();
                if (settings.Version < 2) settings.MigrateFromSingleProvider();
                return settings;
            }
        }
        catch (Exception) { /* Invalid or old settings must not block app startup. */ }
        return new AiProviderSettings();
    }

    /// <summary>Up to 1.0.0-beta.2 the models were plain names of the single active provider.</summary>
    internal void MigrateFromSingleProvider()
    {
        var provider = Providers.Contains(Provider) ? Provider : "ollama";
        string Qualify(string model) => model.Length == 0 || AiModelChoice.Parse(model) is not null ? model : provider + "|" + model;
        DirectorModel = Qualify(DirectorModel ?? "");
        VisionModel = Qualify(VisionModel ?? "");
        foreach (var choice in new[] { DirectorModel, VisionModel }.Select(AiModelChoice.Parse).OfType<AiModelChoice>())
        {
            if (!KnownModels.TryGetValue(choice.Provider, out var list)) KnownModels[choice.Provider] = list = [];
            if (!list.Contains(choice.Model)) list.Add(choice.Model);
            ModelVisibility[choice.Key] = true;
        }
        Version = 2;
    }

    public void Save()
    {
        Version = 2;
        Directory.CreateDirectory(Folder);
        var temp = SettingsPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this));
        File.Move(temp, SettingsPath, true);
    }

    public IReadOnlyList<AiModelChoice> AllModels() => Providers
        .SelectMany(provider => (KnownModels.TryGetValue(provider, out var list) ? list : [])
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(model => new AiModelChoice(provider, model)))
        .ToArray();

    public bool IsVisible(AiModelChoice choice) =>
        ModelVisibility.TryGetValue(choice.Key, out var visible) ? visible : DefaultVisible(choice);

    /// <summary>The models shown in the lists; the defaults are always included.</summary>
    public IReadOnlyList<AiModelChoice> VisibleModels() => AllModels()
        .Where(choice => IsVisible(choice) || choice.Key == DirectorModel || choice.Key == VisionModel).ToArray();

    /// <summary>New local models appear automatically (you installed them); remote catalogues are long,
    /// so only the main current models are shown until you choose.</summary>
    public static bool DefaultVisible(AiModelChoice choice)
    {
        var model = choice.Model.ToLowerInvariant();
        return choice.Provider switch
        {
            "gemini" => (model.Contains("flash") || model.Contains("pro")) &&
                        !new[] { "tts", "image", "audio", "live", "embedding", "computer-use", "robotics", "learnlm", "exp" }
                            .Any(model.Contains) &&
                        !System.Text.RegularExpressions.Regex.IsMatch(model, @"-\d{3}$|preview-\d{2}-\d{2}"),
            "claude" => !model.StartsWith("claude-3", StringComparison.Ordinal),
            "openai" => OpenAiDirectorClient.DefaultVisible(choice.Model),
            _ => true
        };
    }

    public bool Has(AiModelChoice choice, string capability) =>
        ModelCapabilities.TryGetValue(choice.Key, out var list) && list.Contains(capability);

    /// <summary>Replaces the cached list of one provider, keeping the defaults' capabilities.</summary>
    public void SetKnownModels(string provider, IEnumerable<string> models)
    {
        KnownModels[provider] = models.Distinct(StringComparer.Ordinal).ToList();
    }

    public static string GetGeminiKey() => ReadKey(KeyPath, "GEMINI_API_KEY");

    public static bool HasGeminiKey => !string.IsNullOrWhiteSpace(GetGeminiKey());

    public static void SaveGeminiKey(string key) => SaveKey(KeyPath, key, "Gemini");

    public static void DeleteGeminiKey()
    {
        if (File.Exists(KeyPath)) File.Delete(KeyPath);
    }

    public static string GetClaudeKey() => ReadKey(ClaudeKeyPath, "ANTHROPIC_API_KEY");

    public static bool HasClaudeKey => !string.IsNullOrWhiteSpace(GetClaudeKey());

    public static void SaveClaudeKey(string key) => SaveKey(ClaudeKeyPath, key, "Claude");

    public static void DeleteClaudeKey()
    {
        if (File.Exists(ClaudeKeyPath)) File.Delete(ClaudeKeyPath);
    }

    public static string GetOpenAiKey() => ReadKey(OpenAiKeyPath, "OPENAI_API_KEY");

    public static bool HasOpenAiKey => !string.IsNullOrWhiteSpace(GetOpenAiKey());

    public static void SaveOpenAiKey(string key) => SaveKey(OpenAiKeyPath, key, "OpenAI");

    public static void DeleteOpenAiKey()
    {
        if (File.Exists(OpenAiKeyPath)) File.Delete(OpenAiKeyPath);
    }

    private static string ReadKey(string path, string environment)
    {
        if (File.Exists(path))
        {
            try { return Encoding.UTF8.GetString(Unprotect(File.ReadAllBytes(path))); }
            catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
            { /* A key protected by another Windows user cannot be reused. */ }
        }
        return Environment.GetEnvironmentVariable(environment)?.Trim() ?? "";
    }

    private static void SaveKey(string path, string key, string provider)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException($"La clave de {provider} está vacía.");
        Directory.CreateDirectory(Folder);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, Protect(Encoding.UTF8.GetBytes(key.Trim())));
        File.Move(temp, path, true);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Size; public IntPtr Data; }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description,
        IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);

    private static byte[] Protect(byte[] plain) => Transform(plain, true);
    private static byte[] Unprotect(byte[] cipher) => Transform(cipher, false);

    private static byte[] Transform(byte[] bytes, bool encrypt)
    {
        var input = new DataBlob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            DataBlob output;
            var success = encrypt
                ? CryptProtectData(ref input, "Loquendo AI API key", IntPtr.Zero,
                    IntPtr.Zero, IntPtr.Zero, 0, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero,
                    IntPtr.Zero, IntPtr.Zero, 0, out output);
            if (!success) throw new CryptographicException(Marshal.GetLastWin32Error());
            try
            {
                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, result.Length);
                return result;
            }
            finally { LocalFree(output.Data); }
        }
        finally { Marshal.FreeHGlobal(input.Data); }
    }
}
