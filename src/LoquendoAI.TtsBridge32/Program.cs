using System.Text;
using System.Text.Json;
using LoquendoAI.TtsBridge32;

// Keep redirected output/input deterministic across old Windows code pages.
// This matters for voice names such as "Penélope".
Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);

if (args.Length == 0)
{
    PrintHelp();
    return;
}

try
{
    switch (args[0].ToLowerInvariant())
    {
        case "scan":
        case "doctor":
        {
            var engine = GetOption(args, "--engine");
            var json = HasFlag(args, "--json");
            var providers = TtsDoctor.Scan(engine);
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(providers, JsonDefaults.Options));
            }
            else
            {
                Console.WriteLine("Loquendo AI - TTS Doctor (x86)");
                Console.WriteLine($"Proceso: {(Environment.Is64BitProcess ? "x64" : "x86")} | Windows: {Environment.OSVersion}");
                foreach (var p in providers)
                {
                    Console.WriteLine($"{(p.Available ? "[OK]" : "[--]")} {p.Key} - {p.Name}");
                    if (!string.IsNullOrWhiteSpace(p.Version)) Console.WriteLine($"     versión: {p.Version}");
                    if (!string.IsNullOrWhiteSpace(p.Path)) Console.WriteLine($"     ruta: {p.Path}");
                    if (!string.IsNullOrWhiteSpace(p.Detail)) Console.WriteLine($"     {p.Detail}");
                    if (p.Voices is { Count: > 0 }) Console.WriteLine($"     voces: {string.Join(", ", p.Voices)}");
                }
            }
            break;
        }

        case "voices":
        {
            var provider = GetRequiredOption(args, "--provider");
            var engine = GetOption(args, "--engine");
            IReadOnlyList<string> voices = provider.ToLowerInvariant() switch
            {
                "sapi5-x86" or "sapi" => Sapi5Provider.GetVoices(),
                "loquendo7-native" or "loquendo" => GetLoquendoVoices(engine),
                "balcon" => GetBalconVoices(false),
                "balcon-sapi4" or "infovox-sapi4" => GetBalconVoices(true),
                _ => throw new ArgumentException($"Provider desconocido: {provider}")
            };
            foreach (var voice in voices) Console.WriteLine(voice);
            break;
        }

        case "synth":
        {
            var provider = GetRequiredOption(args, "--provider");
            var voice = GetRequiredOption(args, "--voice");
            var output = GetRequiredOption(args, "--out");
            var text = GetOption(args, "--text");
            var textFile = GetOption(args, "--text-file");
            if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(textFile))
                text = File.ReadAllText(textFile);
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Usa --text o --text-file.");

            var request = new SynthRequest(
                provider,
                voice,
                text,
                output,
                ParseNullableIntOption(args, "--rate"),
                ParseNullableIntOption(args, "--volume"),
                ParseIntOption(args, "--sample-rate", 32000),
                ParseIntOption(args, "--channels", 1),
                GetOption(args, "--engine"),
                ParseNullableIntOption(args, "--pitch"));

            switch (provider.ToLowerInvariant())
            {
                case "sapi5-x86":
                case "sapi":
                    Sapi5Provider.Synthesize(request);
                    break;
                case "loquendo7-native":
                case "loquendo":
                    LoquendoNativeIsolation.Synthesize(request);
                    break;
                case "balcon":
                case "balcon-sapi4":
                case "infovox-sapi4":
                    var exe = BalconProvider.Find() ?? throw new FileNotFoundException("No se encontró balcon.exe. Descarga BALCON (https://www.cross-plus-a.com/es/bconsole.htm) y extráelo en tools\\balcon\\ o define LOQUENDO_AI_BALCON.");
                    BalconProvider.Synthesize(exe, request);
                    break;
                default:
                    throw new ArgumentException($"Provider desconocido: {provider}");
            }
            Console.WriteLine(Path.GetFullPath(output));
            break;
        }

        case "qa-suite":
        case "qa":
        {
            var provider = GetOption(args, "--provider") ?? "loquendo7-native";
            var voice = GetOption(args, "--voice") ?? "Jorge";
            var outDir = GetOption(args, "--out-dir") ?? Path.Combine(Environment.CurrentDirectory, "tts-qa");
            var engine = GetOption(args, "--engine");
            Environment.ExitCode = TtsQaRunner.RunQaSuite(provider, voice, outDir, engine) == 0 ? 0 : 1;
            break;
        }

        case "stress":
        {
            var provider = GetOption(args, "--provider") ?? "loquendo7-native";
            var voice = GetOption(args, "--voice") ?? "Jorge";
            var count = ParseIntOption(args, "--count", 100);
            var outDir = GetOption(args, "--out-dir") ?? Path.Combine(Environment.CurrentDirectory, "tts-stress");
            var engine = GetOption(args, "--engine");
            Environment.ExitCode = TtsQaRunner.RunStress(provider, voice, count, outDir, engine) == 0 ? 0 : 1;
            break;
        }

        case "controls-test":
        case "controls":
        {
            var provider = GetOption(args, "--provider") ?? "loquendo7-native";
            var voice = GetOption(args, "--voice") ?? "Jorge";
            var outDir = GetOption(args, "--out-dir") ?? Path.Combine(Environment.CurrentDirectory, "tts-controls");
            var engine = GetOption(args, "--engine");
            Environment.ExitCode = TtsQaRunner.RunControls(provider, voice, outDir, engine) == 0 ? 0 : 1;
            break;
        }

        case "abi-probe":
        {
            var voice = GetOption(args, "--voice") ?? "Jorge";
            var engine = GetOption(args, "--engine");
            Console.WriteLine($"Loquendo TTS7 ABI probe - voice: {voice}");
            foreach (var r in LoquendoNativeAbiProbe.Run(voice, engine))
            {
                Console.WriteLine($"[{(r.Success ? "OK" : "FAIL")}] {r.Abi} - exit {LoquendoNativeAbiProbe.FormatExitCode(r.ExitCode)}");
                if (!string.IsNullOrWhiteSpace(r.Error)) Console.Write(r.Error);
                if (!string.IsNullOrWhiteSpace(r.Output)) Console.Write(r.Output);
            }
            break;
        }

        case "balcon-probe":
        {
            var voice = GetOption(args, "--voice") ?? "Antonio (Spanish) SAPI4 22kHz";
            var exe = BalconProvider.Find() ?? throw new FileNotFoundException("No se encontró balcon.exe.");
            Console.Write(BalconProvider.Probe(exe, voice));
            break;
        }

        case "native-abi-worker":
        {
            var abi = GetRequiredOption(args, "--abi");
            var voice = GetOption(args, "--voice") ?? "Jorge";
            var engine = GetOption(args, "--engine");
            LoquendoNativeAbiProbe.Worker(abi, voice, engine);
            break;
        }

        case "native-synth-worker":
        {
            var requestJson = GetRequiredOption(args, "--request-json");
            var nativeRequest = JsonSerializer.Deserialize<SynthRequest>(File.ReadAllText(requestJson), JsonDefaults.Options)
                ?? throw new InvalidOperationException("Solicitud nativa inválida.");
            using var loq = new LoquendoTts7Provider(nativeRequest.EnginePath);
            loq.Synthesize(nativeRequest);
            Console.WriteLine(Path.GetFullPath(nativeRequest.Output));
            break;
        }

        case "serve":
            await ServeAsync();
            break;

        default:
            PrintHelp();
            Environment.ExitCode = 2;
            break;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    Environment.ExitCode = 1;
}

static IReadOnlyList<string> GetLoquendoVoices(string? engine)
{
    using var loq = new LoquendoTts7Provider(engine);
    return loq.GetVoices();
}

static IReadOnlyList<string> GetBalconVoices(bool sapi4Only = false)
{
    var exe = BalconProvider.Find() ?? throw new FileNotFoundException("No se encontró balcon.exe.");
    return BalconProvider.GetVoices(exe, sapi4Only);
}

static async Task ServeAsync()
{
    string? line;
    while ((line = await Console.In.ReadLineAsync()) is not null)
    {
        BridgeResponse response;
        try
        {
            var req = JsonSerializer.Deserialize<BridgeRequest>(line, JsonDefaults.Options)
                ?? throw new InvalidOperationException("JSON vacío.");
            switch (req.Command.ToLowerInvariant())
            {
                case "scan":
                    response = new BridgeResponse(true, Data: TtsDoctor.Scan(req.EnginePath));
                    break;
                case "voices":
                    if (string.IsNullOrWhiteSpace(req.Provider)) throw new ArgumentException("Falta provider.");
                    var voices = req.Provider.ToLowerInvariant() switch
                    {
                        "sapi5-x86" or "sapi" => Sapi5Provider.GetVoices(),
                        "loquendo7-native" or "loquendo" => GetLoquendoVoices(req.EnginePath),
                        "balcon" => GetBalconVoices(false),
                "balcon-sapi4" or "infovox-sapi4" => GetBalconVoices(true),
                        _ => throw new ArgumentException($"Provider desconocido: {req.Provider}")
                    };
                    response = new BridgeResponse(true, Data: voices);
                    break;
                case "synth":
                    if (string.IsNullOrWhiteSpace(req.Provider) || string.IsNullOrWhiteSpace(req.Voice) || req.Text is null || string.IsNullOrWhiteSpace(req.Output))
                        throw new ArgumentException("synth requiere provider, voice, text y output.");
                    var sr = new SynthRequest(req.Provider, req.Voice, req.Text, req.Output, req.Rate, req.Volume, req.SampleRate, req.Channels, req.EnginePath, req.Pitch);
                    switch (req.Provider.ToLowerInvariant())
                    {
                        case "sapi5-x86": case "sapi": Sapi5Provider.Synthesize(sr); break;
                        case "loquendo7-native": case "loquendo": LoquendoNativeIsolation.Synthesize(sr); break;
                        case "balcon": case "balcon-sapi4": case "infovox-sapi4": BalconProvider.Synthesize(BalconProvider.Find() ?? throw new FileNotFoundException("No se encontró balcon.exe. Descarga BALCON (https://www.cross-plus-a.com/es/bconsole.htm) y extráelo en tools\\balcon\\ o define LOQUENDO_AI_BALCON."), sr); break;
                        default: throw new ArgumentException($"Provider desconocido: {req.Provider}");
                    }
                    response = new BridgeResponse(true, Data: new { output = Path.GetFullPath(req.Output) });
                    break;
                default:
                    throw new ArgumentException($"Comando desconocido: {req.Command}");
            }
        }
        catch (Exception ex)
        {
            response = new BridgeResponse(false, ex.Message);
        }
        Console.WriteLine(JsonSerializer.Serialize(response, JsonDefaults.Options));
        await Console.Out.FlushAsync();
    }
}

static string? GetOption(string[] values, string name)
{
    for (var i = 0; i < values.Length - 1; i++)
        if (string.Equals(values[i], name, StringComparison.OrdinalIgnoreCase)) return values[i + 1];
    return null;
}

static string GetRequiredOption(string[] values, string name)
    => GetOption(values, name) ?? throw new ArgumentException($"Falta {name}.");

static bool HasFlag(string[] values, string name)
    => values.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));

static int ParseIntOption(string[] values, string name, int fallback)
    => int.TryParse(GetOption(values, name), out var n) ? n : fallback;

static int? ParseNullableIntOption(string[] values, string name)
    => int.TryParse(GetOption(values, name), out var n) ? n : null;

static void PrintHelp()
{
    Console.WriteLine("Loquendo AI TTS Bridge/Doctor v0.0.3-beta.4 (x86)");
    Console.WriteLine("  scan [--json] [--engine <Loquendo DataPath|LoqTTS7.dll>]");
    Console.WriteLine("  voices --provider sapi5-x86|loquendo7-native|balcon|balcon-sapi4 [--engine <ruta>]");
    Console.WriteLine("  synth --provider <provider> --voice <voz> --text <texto> --out <archivo.wav> [--rate N] [--pitch N] [--volume N]");
    Console.WriteLine("  synth ... --text-file <archivo.txt>");
    Console.WriteLine("  qa-suite [--provider loquendo7-native|sapi5-x86] [--voice Jorge] [--out-dir <carpeta>]");
    Console.WriteLine("  stress [--provider loquendo7-native|sapi5-x86] [--voice Jorge] [--count 100] [--out-dir <carpeta>]");
    Console.WriteLine("  controls-test [--provider loquendo7-native|sapi5-x86] [--voice Jorge] [--out-dir <carpeta>]");
    Console.WriteLine("  abi-probe [--voice Jorge] [--engine <ruta>]  # compara cdecl vs stdcall en workers aislados");
    Console.WriteLine("  balcon-probe [--voice <voz SAPI4>]  # diagnóstico BALCON / Infovox");
    Console.WriteLine("  serve   # protocolo JSONL stdin/stdout para LoquendoAI.exe");
}
