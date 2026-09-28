using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LoquendoAI.App;

/// <summary>
/// Claude (Anthropic Messages API) as a Director / image-analysis provider. Output is constrained with
/// structured outputs (output_config.format = json_schema), so it follows the same typed schema as
/// Ollama and Gemini. Retries 429 / 5xx / 529 (overloaded) like the Gemini client.
/// </summary>
internal static class ClaudeDirectorClient
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(8) };
    // LOQUENDO_AI_CLAUDE_BASE_URL: for a proxy or a local test server; the key is sent there.
    private static readonly string BaseUrl =
        Environment.GetEnvironmentVariable("LOQUENDO_AI_CLAUDE_BASE_URL") is { Length: > 0 } custom
            ? custom.TrimEnd('/') + "/" : "https://api.anthropic.com/v1/";
    private const string ApiVersion = "2023-06-01";
    internal const int MaxAttempts = 4;
    internal static readonly TimeSpan MaxRetryWait = TimeSpan.FromSeconds(60);
    private static readonly double[] BackoffSeconds = [2, 6, 15];

    internal sealed record ClaudeModel(string Id, string DisplayName, bool Vision, bool Effort, bool EffortMax);

    public static async Task<IReadOnlyList<ClaudeModel>> ListModelsAsync(string key, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Configura la API key de Claude.");
        var result = new List<ClaudeModel>();
        string? after = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            for (var page = 0; page < 20; page++)
            {
                using var request = Authorized(HttpMethod.Get,
                    BaseUrl + "models?limit=100" + (after is null ? "" : "&after_id=" + Uri.EscapeDataString(after)), key);
                using var response = await Client.SendAsync(request, timeout.Token);
                var body = await response.Content.ReadAsStringAsync(timeout.Token);
                EnsureSuccess(response, body, key);
                using var json = JsonDocument.Parse(body);
                if (json.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                    foreach (var model in data.EnumerateArray())
                    {
                        var id = model.TryGetProperty("id", out var value) ? value.GetString() : null;
                        if (string.IsNullOrWhiteSpace(id) || !id.StartsWith("claude-", StringComparison.Ordinal)) continue;
                        var capabilities = model.TryGetProperty("capabilities", out var caps) ? caps : default;
                        // Without a capabilities object (older API answers) assume the current defaults.
                        bool Supported(params string[] path)
                        {
                            if (capabilities.ValueKind != JsonValueKind.Object) return path[0] != "effort";
                            var node = capabilities;
                            foreach (var part in path)
                                if (!node.TryGetProperty(part, out node)) return false;
                            return node.TryGetProperty("supported", out var flag) && flag.ValueKind == JsonValueKind.True;
                        }
                        if (capabilities.ValueKind == JsonValueKind.Object && !Supported("structured_outputs")) continue;
                        result.Add(new ClaudeModel(id,
                            model.TryGetProperty("display_name", out var name) ? name.GetString() ?? id : id,
                            Supported("image_input"), Supported("effort"), Supported("effort", "max")));
                    }
                var more = json.RootElement.TryGetProperty("has_more", out var hasMore) && hasMore.ValueKind == JsonValueKind.True;
                after = json.RootElement.TryGetProperty("last_id", out var last) ? last.GetString() : null;
                if (!more || string.IsNullOrEmpty(after)) break;
            }
        }
        catch (HttpRequestException ex) { throw new InvalidOperationException("No se pudo conectar con Claude. Comprueba tu conexión.", ex); }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
        { throw new InvalidOperationException("Claude no respondió al consultar los modelos.", ex); }
        return result.DistinctBy(x => x.Id).ToArray();
    }

    /// <param name="effort">low, medium, high or max; null or "default" leaves the model's default.</param>
    public static async Task<string> ChatAsync(string model, string system, string prompt, object schema,
        string? imageBase64, string? effort, int maxTokens, CancellationToken token,
        IProgress<string>? progress = null, string purpose = "director")
    {
        var key = AiProviderSettings.GetClaudeKey();
        if (key.Length == 0) throw new InvalidOperationException("Configura la API key de Claude en «Configurar IA».");
        if (model.Length > 128 || !model.StartsWith("claude-", StringComparison.Ordinal) ||
            model.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '.' or '_')))
            throw new InvalidOperationException("Selecciona un modelo de Claude del catálogo.");
        var content = new JsonArray();
        if (imageBase64 is not null)
            content.Add(new JsonObject
            {
                ["type"] = "image",
                ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = "image/png", ["data"] = imageBase64 }
            });
        content.Add(new JsonObject { ["type"] = "text", ["text"] = prompt });
        var outputConfig = new JsonObject
        {
            ["format"] = new JsonObject { ["type"] = "json_schema", ["schema"] = SanitizeSchema(schema) }
        };
        if (effort is "low" or "medium" or "high" or "max") outputConfig["effort"] = effort;
        string Payload() => new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = maxTokens,
            ["system"] = system,
            ["messages"] = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = content.DeepClone() } },
            ["output_config"] = outputConfig.DeepClone()
        }.ToJsonString();
        var payload = Payload();
        var simplified = false;

        var clock = Stopwatch.StartNew();
        var attempt = 0;
        var outcome = "error";
        string? stop = null, usage = null, retries = null;
        try
        {
            while (true)
            {
                attempt++;
                using var request = Authorized(HttpMethod.Post, BaseUrl + "messages", key);
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                HttpResponseMessage response;
                string body;
                try
                {
                    response = await Client.SendAsync(request, token);
                    body = await response.Content.ReadAsStringAsync(token);
                }
                catch (HttpRequestException ex)
                {
                    if (attempt >= MaxAttempts)
                        throw new InvalidOperationException("No se pudo conectar con Claude. Comprueba tu conexión.", ex);
                    retries += $" {attempt}:conexión";
                    await WaitAsync(Backoff(attempt), "no se pudo conectar", attempt, progress, token);
                    continue;
                }
                catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
                {
                    outcome = "timeout";
                    throw new InvalidOperationException($"Claude no respondió en {Client.Timeout.TotalMinutes:0} minutos.", ex);
                }
                using (response)
                {
                    var status = (int)response.StatusCode;
                    if (IsTransient(status) && attempt < MaxAttempts && RetryDelay(response, attempt) is { } wait)
                    {
                        retries += $" {attempt}:{status}";
                        await WaitAsync(wait, status == 429 ? "límite de peticiones (429)" :
                            status == 529 ? "Claude está saturado (529)" : $"error temporal ({status})", attempt, progress, token);
                        continue;
                    }
                    // Claude compiles the schema into a grammar with an internal size limit. If a schema is
                    // still too big (a very large catalog or cast), retry once without the long enums:
                    // the app validates those values itself (DirectorAiSchema.CheckRefs).
                    if (status == 400 && !simplified && IsGrammarTooLarge(body))
                    {
                        simplified = true;
                        retries += $" {attempt}:esquema simplificado";
                        ((JsonObject)outputConfig["format"]!)["schema"] = SanitizeSchema(schema, maxEnumValues: 12);
                        payload = Payload();
                        progress?.Report("El esquema era demasiado grande para Claude; reintentando con uno simplificado…");
                        continue;
                    }
                    EnsureSuccess(response, body, key);
                    using var json = JsonDocument.Parse(body);
                    stop = json.RootElement.TryGetProperty("stop_reason", out var reason) ? reason.GetString() : null;
                    usage = Usage(json.RootElement);
                    var text = json.RootElement.TryGetProperty("content", out var blocks) && blocks.ValueKind == JsonValueKind.Array
                        ? string.Concat(blocks.EnumerateArray()
                            .Where(block => block.TryGetProperty("type", out var type) && type.GetString() == "text")
                            .Select(block => block.GetProperty("text").GetString()))
                        : "";
                    if (stop == "refusal")
                    {
                        outcome = "rechazada";
                        throw new InvalidOperationException("Claude declinó esta petición (stop_reason=refusal). Revisa la premisa o usa otro modelo.");
                    }
                    if (stop == "max_tokens")
                    {
                        outcome = "cortada";
                        throw new InvalidOperationException($"Claude llegó al límite de {maxTokens:N0} tokens antes de cerrar el JSON. " +
                            "Baja el esfuerzo, acorta la premisa o genera el episodio por escenas.");
                    }
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        outcome = "vacía";
                        throw new InvalidOperationException("Claude no devolvió el JSON esperado (" + (stop ?? "sin contenido") + ").");
                    }
                    outcome = "ok";
                    return text;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            outcome = "cancelado";
            throw;
        }
        finally
        {
            AiDiagnostics.Note($"claude {model} ({purpose}) resultado={outcome} | {clock.ElapsedMilliseconds / 1000d:0.0}s | " +
                $"intentos={attempt}{(retries is null ? "" : " (reintentos:" + retries + ")")} | esfuerzo={effort ?? "predeterminado"} | " +
                $"max_tokens={maxTokens:N0} | prompt={prompt.Length + system.Length:N0} car" + (usage is null ? "" : " | " + usage) +
                $" | fin={stop ?? "-"}");
        }
    }

    /// <summary>
    /// Structured outputs accept enum, anyOf, minItems/maxItems and additionalProperties, but not numeric
    /// or length ranges: those keywords are removed and written into the description instead, so the model
    /// still sees them (the app clamps the values anyway).
    /// </summary>
    internal static bool IsGrammarTooLarge(string body) =>
        body.Contains("grammar", StringComparison.OrdinalIgnoreCase) ||
        body.Contains("too complex", StringComparison.OrdinalIgnoreCase);

    /// <param name="maxEnumValues">When set, enums longer than this become plain strings (the list moves
    /// to the description only if it is short enough to be useful).</param>
    internal static JsonNode SanitizeSchema(object schema, int? maxEnumValues = null)
    {
        var root = JsonNode.Parse(JsonSerializer.Serialize(schema))!;
        // parentKey "properties": a map of property NAMES, whose keys are not keywords.
        void Walk(JsonNode? node, string? parentKey)
        {
            switch (node)
            {
                case JsonObject obj when parentKey == "properties":
                    foreach (var child in obj.Select(x => x.Value).ToArray()) Walk(child, null);
                    break;
                case JsonObject obj:
                {
                    string? Take(string name)
                    {
                        if (!obj.TryGetPropertyValue(name, out var value) || value is null) return null;
                        obj.Remove(name);
                        return value.ToJsonString();
                    }
                    var min = Take("minimum") ?? Take("exclusiveMinimum");
                    var max = Take("maximum") ?? Take("exclusiveMaximum");
                    var minLength = Take("minLength");
                    var maxLength = Take("maxLength");
                    Take("multipleOf");
                    var notes = new List<string>();
                    if (maxEnumValues is { } limit && obj["enum"] is JsonArray values && values.Count > limit)
                    {
                        obj.Remove("enum");
                        notes.Add(values.Count <= 40
                            ? "Uno de: " + string.Join(", ", values.Select(x => x?.ToString())) + "."
                            : "Usa uno de los valores indicados en el catálogo.");
                    }
                    if (min is not null && max is not null) notes.Add($"Entre {min} y {max}.");
                    else if (min is not null) notes.Add($"Mínimo {min}.");
                    else if (max is not null) notes.Add($"Máximo {max}.");
                    if (minLength is not null || maxLength is not null)
                        notes.Add($"Longitud {minLength ?? "0"}–{maxLength ?? "sin límite"} caracteres.");
                    if (notes.Count > 0)
                        obj["description"] = ((obj["description"]?.GetValue<string>() ?? "") + " " + string.Join(" ", notes)).Trim();
                    foreach (var (name, child) in obj.ToArray()) Walk(child, name);
                    break;
                }
                case JsonArray array:
                    foreach (var child in array.ToArray()) Walk(child, null);
                    break;
            }
        }
        Walk(root, null);
        return root;
    }

    internal static bool IsTransient(int status) => status is 429 or 500 or 502 or 503 or 504 or 529;

    private static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(BackoffSeconds[Math.Clamp(attempt - 1, 0, BackoffSeconds.Length - 1)] * (0.85 + Random.Shared.NextDouble() * 0.3));

    internal static TimeSpan? RetryDelay(HttpResponseMessage response, int attempt)
    {
        TimeSpan? asked = response.Headers.RetryAfter?.Delta ??
            (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        if (asked is null && response.Headers.TryGetValues("retry-after", out var values) &&
            double.TryParse(values.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            asked = TimeSpan.FromSeconds(seconds);
        if (asked is { } value)
            return value > MaxRetryWait ? null : value < TimeSpan.Zero ? TimeSpan.Zero : value + TimeSpan.FromMilliseconds(250);
        return Backoff(attempt);
    }

    private static async Task WaitAsync(TimeSpan wait, string reason, int attempt, IProgress<string>? progress, CancellationToken token)
    {
        progress?.Report($"Claude: {reason}. Reintento {attempt + 1}/{MaxAttempts} en {Math.Ceiling(wait.TotalSeconds):0} s…");
        await Task.Delay(wait, token);
    }

    private static string? Usage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage)) return null;
        string Count(string name) => usage.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number.ToString("N0") : "?";
        return $"tokens entrada={Count("input_tokens")} salida={Count("output_tokens")}";
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string key)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("x-api-key", key);
        request.Headers.TryAddWithoutValidation("anthropic-version", ApiVersion);
        return request;
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body, string key)
    {
        if (response.IsSuccessStatusCode) return;
        string detail;
        try
        {
            using var json = JsonDocument.Parse(body);
            detail = json.RootElement.GetProperty("error").GetProperty("message").GetString() ?? "Error de API";
        }
        catch (Exception) { detail = "No se pudo leer el error de la API"; }
        detail = detail.Replace(key, "[clave oculta]", StringComparison.Ordinal);
        var status = (int)response.StatusCode;
        if (status == 429 || detail.Contains("credit balance", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("spend limit", StringComparison.OrdinalIgnoreCase))
            throw new AiQuotaException($"Claude respondió {status}: " + detail[..Math.Min(detail.Length, 250)] +
                (status == 429 ? " Es el límite de uso de tu clave (peticiones o gasto): espera un rato o revisa los límites de tu organización."
                               : " Tu cuenta se quedó sin crédito o llegó a su límite de gasto: revisa Billing en platform.claude.com."));
        if (status is 401 or 403)
            detail += " Comprueba la API key en la consola de Claude (platform.claude.com).";
        else if (status is 500 or 502 or 503 or 504 or 529)
            detail += " Claude está ocupado o falló temporalmente (el Director ya reintenta varias veces); prueba en unos minutos u otro modelo.";
        throw new InvalidOperationException($"Claude respondió {status}: " + detail[..Math.Min(detail.Length, 350)]);
    }
}
