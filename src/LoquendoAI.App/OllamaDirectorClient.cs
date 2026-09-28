using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace LoquendoAI.App;

/// <summary>Per-request limits for a structured Ollama call.</summary>
/// <param name="MaxOutputTokens">num_predict: hard cap on generated tokens so a looping model always ends.</param>
/// <param name="Purpose">Short label written to the diagnostic log (director, recorded, vision…).</param>
internal sealed record OllamaChatOptions(int MaxOutputTokens = 4096, string Purpose = "director");

/// <summary>Raised when Ollama stops producing useful output (no first token, idle stream,
/// whitespace/thinking loop). Deliberately NOT an OperationCanceledException, so the UI
/// never reports a stuck model as "cancelled by the user".</summary>
internal sealed class OllamaStallException(string message) : TimeoutException(message);

// Ollama local only: cloud credentials and image uploads are never involved.
internal static class OllamaDirectorClient
{
    // Streaming responses are guarded by our own watchdog (see ChatAsync), not by
    // HttpClient.Timeout, which would otherwise cut a healthy long generation.
    private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly Uri BaseUri = ResolveBaseUri();
    private static readonly Uri ChatEndpoint = new(BaseUri, "api/chat");
    private static readonly Uri ModelsEndpoint = new(BaseUri, "api/tags");
    private static readonly Uri ShowEndpoint = new(BaseUri, "api/show");
    private static readonly Uri PsEndpoint = new(BaseUri, "api/ps");

    /// <summary>Loading a large model from disk plus evaluating a long prompt can take minutes on
    /// a partially CPU-offloaded model, so the first token gets a generous budget.</summary>
    internal static TimeSpan FirstTokenTimeout { get; set; } = SecondsFromEnvironment("LOQUENDO_AI_OLLAMA_FIRST_TOKEN_SECONDS", 300);
    /// <summary>Once tokens flow, a healthy model never pauses this long between chunks.</summary>
    internal static TimeSpan IdleTimeout { get; set; } = SecondsFromEnvironment("LOQUENDO_AI_OLLAMA_IDLE_SECONDS", 90);

    private static TimeSpan SecondsFromEnvironment(string name, int fallback) =>
        TimeSpan.FromSeconds(int.TryParse(Environment.GetEnvironmentVariable(name), out var seconds) &&
            seconds is >= 10 and <= 3600 ? seconds : fallback);
    /// <summary>Consecutive whitespace characters that mean the JSON grammar is looping.</summary>
    internal const int WhitespaceLoopLimit = 200;
    /// <summary>Thinking chunks allowed before we treat the model as stuck in reasoning.</summary>
    internal const int ThinkingLoopLimit = 6000;

    internal static string Endpoint => BaseUri.ToString();

    private static Uri ResolveBaseUri()
    {
        // Honour OLLAMA_HOST like the Ollama CLI does ("host:port", "http://host:port", "0.0.0.0").
        var raw = Environment.GetEnvironmentVariable("OLLAMA_HOST")?.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return new Uri("http://127.0.0.1:11434/");
        if (!raw.Contains("://", StringComparison.Ordinal)) raw = "http://" + raw;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return new Uri("http://127.0.0.1:11434/");
        var host = uri.Host is "0.0.0.0" or "::" or "[::]" ? "127.0.0.1" : uri.Host;
        var port = uri.IsDefaultPort && !raw.Contains(":" + uri.Port, StringComparison.Ordinal) ? 11434 : uri.Port;
        return new UriBuilder(uri.Scheme, host, port, "/").Uri;
    }

    public static async Task<IReadOnlyList<string>> ListInstalledModelsAsync(CancellationToken cancellationToken = default)
    {
        // This endpoint follows OLLAMA_MODELS automatically; scanning a default disk folder would miss custom locations.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            using var response = await Client.GetAsync(ModelsEndpoint, timeout.Token);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            if (!json.RootElement.TryGetProperty("models", out var models) ||
                models.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Ollama devolvió un catálogo de modelos inválido.");

            return models.EnumerateArray()
                .Select(model => model.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                    ? name.GetString() : null)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"No se pudo consultar Ollama local ({Endpoint}). Abre Ollama y recarga los modelos.", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Ollama no respondió al consultar los modelos instalados. Comprueba que esté abierto y vuelve a recargar.", ex);
        }
    }

    public static readonly object StorySchema = new
    {
        type = "object",
        properties = new
        {
            steps = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new { line = new { type = "string" } },
                    required = new[] { "line" },
                    additionalProperties = false
                }
            }
        },
        required = new[] { "steps" },
        additionalProperties = false
    };

    public static object RecordedSceneSchema(IReadOnlyList<string> blockIds) => new
    {
        type = "object",
        properties = new
        {
            steps = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new { line = new { type = "string" },
                        blockId = new { type = "string", @enum = new[] { "" }.Concat(blockIds).ToArray() } },
                    required = new[] { "line", "blockId" },
                    additionalProperties = false
                }
            }
        },
        required = new[] { "steps" },
        additionalProperties = false
    };

    public static async Task<bool?> ModelSupportsVisionAsync(string model, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var payload = new StringContent(JsonSerializer.Serialize(new { model }), Encoding.UTF8, "application/json");
        try
        {
            using var response = await Client.PostAsync(ShowEndpoint, payload, timeout.Token);
            if (!response.IsSuccessStatusCode) return null; // Older installations may not expose capabilities.
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            if (!json.RootElement.TryGetProperty("capabilities", out var capabilities) ||
                capabilities.ValueKind != JsonValueKind.Array) return null;
            return capabilities.EnumerateArray().Any(item =>
                item.ValueKind == JsonValueKind.String &&
                string.Equals(item.GetString(), "vision", StringComparison.OrdinalIgnoreCase));
        }
        catch (HttpRequestException) { return null; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
    }

    public static readonly object ImageSchema = new
    {
        type = "object",
        properties = new
        {
            description = new { type = "string" },
            role = new { type = "string" },
            mood = new { type = "string" },
            subject = new { type = "string" }
        },
        required = new[] { "description", "role", "mood", "subject" },
        additionalProperties = false
    };

    /// <summary>Rough, deliberately pessimistic token estimate (Spanish text, JSON and GUIDs
    /// tokenize worse than English prose). Only used to choose a context bucket.</summary>
    internal static int EstimateTokens(string? text) => text is null ? 0 : (int)Math.Ceiling(text.Length / 2.7);

    /// <summary>Ollama reloads the model whenever num_ctx changes, so we only use a few fixed
    /// sizes. LOQUENDO_AI_OLLAMA_NUM_CTX forces a value (e.g. on GPUs with little VRAM).</summary>
    internal static int ContextBucket(int neededTokens)
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("LOQUENDO_AI_OLLAMA_NUM_CTX"), out var forced) &&
            forced is >= 2048 and <= 262144)
            return forced;
        foreach (var bucket in new[] { 8192, 16384, 32768 })
            if (neededTokens <= bucket) return bucket;
        return 32768;
    }

    public static async Task<string> ChatAsync(string model, string system, string prompt, object schema,
        string? imageBase64, CancellationToken cancellationToken, IProgress<string>? progress = null,
        OllamaChatOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(model) || model.Length > 128)
            throw new InvalidOperationException("Escribe el nombre de un modelo instalado en Ollama.");
        options ??= imageBase64 is null ? new OllamaChatOptions() : new OllamaChatOptions(800, "vision");
        var maxOutput = Math.Clamp(options.MaxOutputTokens, 128, 16384);
        var schemaJson = JsonSerializer.Serialize(schema);
        var estimatedPrompt = EstimateTokens(system) + EstimateTokens(prompt) + EstimateTokens(schemaJson) +
            (imageBase64 is null ? 0 : 1600);
        var numCtx = ContextBucket(estimatedPrompt + maxOutput + 512);

        var message = new Dictionary<string, object> { ["role"] = "user", ["content"] = prompt };
        if (imageBase64 is not null) message["images"] = new[] { imageBase64 };
        var request = new Dictionary<string, object?>
        {
            ["model"] = model.Trim(),
            // Streaming lets us see progress and detect a stuck model instead of waiting blind.
            ["stream"] = true,
            ["format"] = schema,
            // Reasoning models (qwen3, deepseek-r1…) can think for thousands of tokens before the
            // JSON starts; with the old 4k default context that looked like a hang. The Director
            // validates everything afterwards, so it does not need hidden reasoning.
            ["think"] = false,
            // Keep the model resident between drafts; reloading a large model costs tens of seconds.
            ["keep_alive"] = "15m",
            ["options"] = new Dictionary<string, object>
            {
                ["temperature"] = imageBase64 is null ? 0.2 : 0.0,
                // Without num_ctx Ollama uses 4096 tokens on GPUs under 24 GB and silently
                // drops the start of longer prompts (grammar + catalog).
                ["num_ctx"] = numCtx,
                // Without num_predict a looping model generates forever.
                ["num_predict"] = maxOutput
            },
            ["messages"] = new object[] { new { role = "system", content = system }, message }
        };

        var diagnostic = new AiDiagnostics.Entry("ollama", model.Trim(), options.Purpose)
        {
            PromptChars = system.Length + prompt.Length,
            EstimatedPromptTokens = estimatedPrompt,
            NumCtx = numCtx,
            NumPredict = maxOutput
        };
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var content = new StringBuilder();
        var chunks = 0;
        var thinkingChunks = 0;
        var trailingWhitespace = 0;
        var stage = "esperando la primera respuesta";

        using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        watchdog.CancelAfter(FirstTokenTimeout);
        using var payload = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ChatEndpoint) { Content = payload };
        try
        {
            progress?.Report($"Ollama: cargando «{model}» y leyendo el encargo (≈{estimatedPrompt:N0} tokens, contexto {numCtx:N0})…");
            using var response = await Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, watchdog.Token);
            if (!response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(watchdog.Token);
                if (json.Contains("unknown model architecture", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Ollama no puede cargar «{model}» por una arquitectura que no reconoce. " +
                        "Prueba «ollama run " + model + "» en PowerShell; si falla igual, actualiza Ollama " +
                        "y reinícialo. El modelo figura instalado, pero la app no puede hacerlo funcionar.");
                throw new InvalidOperationException($"Ollama respondió {(int)response.StatusCode}: " +
                    json[..Math.Min(json.Length, 450)]);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(watchdog.Token);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var finished = false;
            while (await reader.ReadLineAsync(watchdog.Token) is string line)
            {
                if (line.Length == 0) continue;
                using var chunk = JsonDocument.Parse(line);
                var root = chunk.RootElement;
                if (root.TryGetProperty("error", out var error))
                    throw new InvalidOperationException("Ollama interrumpió la respuesta: " + error.GetString());

                if (root.TryGetProperty("message", out var messageElement))
                {
                    if (messageElement.TryGetProperty("content", out var piece) &&
                        piece.ValueKind == JsonValueKind.String && piece.GetString() is { Length: > 0 } text)
                    {
                        content.Append(text);
                        chunks++;
                        trailingWhitespace = string.IsNullOrWhiteSpace(text) ? trailingWhitespace + text.Length : 0;
                    }
                    if (messageElement.TryGetProperty("thinking", out var thought) &&
                        thought.ValueKind == JsonValueKind.String && thought.GetString() is { Length: > 0 })
                        thinkingChunks++;
                }

                if (root.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.True)
                {
                    diagnostic.ReadStats(root);
                    finished = true;
                    break;
                }

                // Any chunk proves the runner is alive: restart the idle timer.
                stage = chunks > 0 ? "generando" : "pensando";
                watchdog.CancelAfter(IdleTimeout);
                if (trailingWhitespace >= WhitespaceLoopLimit)
                    throw new OllamaStallException(
                        $"«{model}» entró en un bucle de espacios en blanco dentro del JSON y no iba a terminar. " +
                        "Vuelve a generar; si se repite, prueba otro modelo o acorta la premisa.");
                if (thinkingChunks >= ThinkingLoopLimit && chunks == 0)
                    throw new OllamaStallException(
                        $"«{model}» lleva {thinkingChunks:N0} tokens razonando sin empezar el borrador. " +
                        "Usa un modelo sin razonamiento o vuelve a generar.");
                if (chunks + thinkingChunks > 0 && (chunks + thinkingChunks) % 40 == 0)
                    progress?.Report(chunks > 0
                        ? $"Ollama generando el borrador… {chunks:N0} tokens ({clock.Elapsed.TotalSeconds:0} s)"
                        : $"Ollama razonando… {thinkingChunks:N0} tokens ({clock.Elapsed.TotalSeconds:0} s)");
            }

            if (!finished)
                throw new InvalidOperationException(
                    "Ollama cerró la conexión antes de terminar. Suele pasar si el runner se quedó sin memoria; " +
                    "revisa el registro del servidor de Ollama o prueba un modelo más pequeño.");

            var result = content.ToString();
            if (diagnostic.DoneReason == "length")
                throw new InvalidDataException(
                    $"La respuesta se cortó al llegar al límite de {maxOutput:N0} tokens: el modelo probablemente " +
                    "entró en un bucle o la escena es demasiado larga. Vuelve a generar o divide la escena.");
            if (string.IsNullOrWhiteSpace(result)) throw new InvalidOperationException("Ollama no devolvió contenido.");
            try { using var _ = JsonDocument.Parse(result); }
            catch (JsonException)
            {
                throw new InvalidDataException("Ollama devolvió un JSON incompleto o inválido. Vuelve a generar el borrador.");
            }
            diagnostic.Outcome = "ok";
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our watchdog fired, not the user's Cancel button.
            var seconds = (chunks + thinkingChunks == 0 ? FirstTokenTimeout : IdleTimeout).TotalSeconds;
            var stall = new OllamaStallException(chunks + thinkingChunks == 0
                ? $"Ollama no empezó a responder en {seconds:0} s ({stage}). El modelo puede estar cargándose " +
                  "desde disco o no cabe en la VRAM y corre en CPU. Comprueba «ollama ps» y prueba un modelo más pequeño."
                : $"Ollama dejó de enviar tokens durante {seconds:0} s mientras estaba {stage}. " +
                  "Vuelve a generar; si se repite, reinicia Ollama.");
            diagnostic.Outcome = "stall: " + stall.Message;
            throw stall;
        }
        catch (OperationCanceledException)
        {
            diagnostic.Outcome = "cancelado por el usuario";
            throw;
        }
        catch (HttpRequestException ex)
        {
            diagnostic.Outcome = "http: " + ex.Message;
            throw new InvalidOperationException($"No se pudo conectar a Ollama local ({Endpoint}). Ábrelo e instala el modelo indicado.", ex);
        }
        catch (IOException ex)
        {
            diagnostic.Outcome = "io: " + ex.Message;
            throw new InvalidOperationException("Se perdió la conexión con Ollama durante la respuesta (¿se reinició o se quedó sin memoria?).", ex);
        }
        catch (Exception ex)
        {
            diagnostic.Outcome ??= ex.GetType().Name + ": " + ex.Message;
            throw;
        }
        finally
        {
            diagnostic.ElapsedMs = clock.ElapsedMilliseconds;
            diagnostic.ContentChunks = chunks;
            diagnostic.ThinkingChunks = thinkingChunks;
            diagnostic.OutputPreview = content.ToString();
            diagnostic.Placement = await DescribePlacementAsync(model.Trim());
            AiDiagnostics.Write(diagnostic);
        }
    }

    /// <summary>Reports how much of the loaded model sits in VRAM (from /api/ps). A model that
    /// spills to CPU is the most common reason for a "frozen" Director on a 16 GB GPU.</summary>
    private static async Task<string?> DescribePlacementAsync(string model)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var response = await Client.GetAsync(PsEndpoint, timeout.Token);
            if (!response.IsSuccessStatusCode) return null;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            if (!json.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var entry in models.EnumerateArray())
            {
                var name = entry.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (!string.Equals(name, model, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, model + ":latest", StringComparison.OrdinalIgnoreCase)) continue;
                var size = entry.TryGetProperty("size", out var s) && s.TryGetInt64(out var total) ? total : 0;
                var vram = entry.TryGetProperty("size_vram", out var v) && v.TryGetInt64(out var gpu) ? gpu : 0;
                var context = entry.TryGetProperty("context_length", out var c) && c.TryGetInt32(out var ctx) ? ctx : 0;
                var percent = size > 0 ? vram * 100d / size : 0;
                return $"{size / 1073741824d:0.0} GB, {percent:0}% en GPU" + (context > 0 ? $", contexto cargado {context:N0}" : "");
            }
            return "no aparece en /api/ps";
        }
        catch (Exception) { return null; }
    }
}

/// <summary>One line per AI request in %LOCALAPPDATA%\LoquendoAI\ia-diagnostico.log, so a
/// "the AI froze" report comes with numbers (timings, tokens, context, VRAM placement).</summary>
internal static class AiDiagnostics
{
    private static readonly object Gate = new();

    public static string PathName => AppPaths.PathOf("ia-diagnostico.log");

    internal sealed class Entry(string provider, string model, string purpose)
    {
        public string Provider { get; } = provider;
        public string Model { get; } = model;
        public string Purpose { get; } = purpose;
        public int PromptChars { get; set; }
        public int EstimatedPromptTokens { get; set; }
        public int NumCtx { get; set; }
        public int NumPredict { get; set; }
        public long ElapsedMs { get; set; }
        public int ContentChunks { get; set; }
        public int ThinkingChunks { get; set; }
        public string? DoneReason { get; set; }
        public long? PromptEvalCount { get; set; }
        public long? EvalCount { get; set; }
        public long? LoadMs { get; set; }
        public long? PromptEvalMs { get; set; }
        public long? EvalMs { get; set; }
        public string? Placement { get; set; }
        public string? Outcome { get; set; }
        public string OutputPreview { get; set; } = "";

        public void ReadStats(JsonElement root)
        {
            DoneReason = root.TryGetProperty("done_reason", out var reason) ? reason.GetString() : null;
            PromptEvalCount = Long(root, "prompt_eval_count");
            EvalCount = Long(root, "eval_count");
            LoadMs = Long(root, "load_duration") / 1_000_000;
            PromptEvalMs = Long(root, "prompt_eval_duration") / 1_000_000;
            EvalMs = Long(root, "eval_duration") / 1_000_000;
        }

        private static long? Long(JsonElement root, string name) =>
            root.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : null;
    }

    public static void Write(Entry entry)
    {
        var preview = entry.OutputPreview.Replace('\r', ' ').Replace('\n', ' ');
        if (entry.Outcome != "ok" && preview.Length > 600)
            preview = preview[..300] + " … " + preview[^300..];
        else if (entry.Outcome == "ok")
            preview = ""; // Successful drafts are visible in the grid; keep the log compact.
        var truncationRisk = entry.PromptEvalCount is long evaluated && entry.NumCtx > 0 &&
            evaluated >= entry.NumCtx - entry.NumPredict ? " ¡POSIBLE RECORTE DEL PROMPT!" : "";
        var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {entry.Provider} {entry.Model} ({entry.Purpose}) " +
            $"resultado={entry.Outcome ?? "desconocido"} | {entry.ElapsedMs / 1000d:0.0}s | " +
            $"prompt={entry.PromptChars:N0} car ≈{entry.EstimatedPromptTokens:N0} tok (evaluados {entry.PromptEvalCount?.ToString("N0") ?? "?"}) | " +
            $"num_ctx={entry.NumCtx:N0} num_predict={entry.NumPredict:N0} | salida={entry.EvalCount?.ToString("N0") ?? entry.ContentChunks.ToString("N0")} tok " +
            $"razonamiento={entry.ThinkingChunks:N0} | fin={entry.DoneReason ?? "-"} | carga={entry.LoadMs?.ToString("N0") ?? "?"}ms " +
            $"lectura={entry.PromptEvalMs?.ToString("N0") ?? "?"}ms generación={entry.EvalMs?.ToString("N0") ?? "?"}ms | " +
            $"modelo en memoria: {entry.Placement ?? "?"}{truncationRisk}" +
            (preview.Length > 0 ? $"{Environment.NewLine}    salida parcial: {preview}" : "");
        WriteLine(line);
    }

    public static void Note(string text) =>
        WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {text}");

    private static void WriteLine(string line)
    {
        try
        {
            lock (Gate)
            {
                var path = PathName;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var info = new FileInfo(path);
                if (info.Exists && info.Length > 2_000_000)
                    File.Move(path, path + ".old", true);
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception) { /* Diagnostics must never break an AI request. */ }
    }
}
