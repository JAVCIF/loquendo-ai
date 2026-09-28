using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace LoquendoAI.App;

internal static class GeminiDirectorClient
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(8) };
    // LOQUENDO_AI_GEMINI_BASE_URL: for a proxy or a local test server; the key is sent there.
    private static readonly string BaseUrl =
        Environment.GetEnvironmentVariable("LOQUENDO_AI_GEMINI_BASE_URL") is { Length: > 0 } custom
            ? custom.TrimEnd('/') + "/" : "https://generativelanguage.googleapis.com/v1beta/";

    public static async Task<IReadOnlyList<string>> ListModelsAsync(string key, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Configura la API key de Gemini.");
        var results = new List<string>();
        string? page = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            do
            {
                var url = BaseUrl + "models?pageSize=100" +
                    (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page));
                using var request = Authorized(HttpMethod.Get, url, key);
                using var response = await Client.SendAsync(request, timeout.Token);
                var body = await response.Content.ReadAsStringAsync(timeout.Token);
                EnsureSuccess(response, body, key);
                using var json = JsonDocument.Parse(body);
                if (json.RootElement.TryGetProperty("models", out var models))
                    foreach (var entry in models.EnumerateArray())
                    {
                        if (!entry.TryGetProperty("name", out var name) ||
                            !entry.TryGetProperty("supportedGenerationMethods", out var methods) ||
                            !methods.EnumerateArray().Any(method => method.GetString() == "generateContent")) continue;
                        var id = name.GetString();
                        if (id is not null && id.StartsWith("models/gemini-", StringComparison.Ordinal))
                            results.Add(id["models/".Length..]);
                    }
                page = json.RootElement.TryGetProperty("nextPageToken", out var next)
                    ? next.GetString() : null;
            } while (!string.IsNullOrEmpty(page));
        }
        catch (HttpRequestException ex) { throw new InvalidOperationException("No se pudo conectar con Gemini. Comprueba tu conexión.", ex); }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
        { throw new InvalidOperationException("Gemini no respondió al consultar los modelos.", ex); }
        return results.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Attempts for 429 / 5xx / dropped connections (the first call included).</summary>
    internal const int MaxAttempts = 4;
    /// <summary>A server asking to wait longer than this (daily quota exhausted) is not retried.</summary>
    internal static readonly TimeSpan MaxRetryWait = TimeSpan.FromSeconds(60);
    private static readonly double[] BackoffSeconds = [2, 6, 15];

    public static async Task<string> ChatAsync(string model, string system, string prompt,
        object schema, string? imageBase64, string thinking, CancellationToken token,
        IProgress<string>? progress = null, string purpose = "director", string mediaMime = "image/png")
    {
        var key = AiProviderSettings.GetGeminiKey();
        if (key.Length == 0) throw new InvalidOperationException("Configura la API key de Gemini en «Configurar IA».");
        if (model.Length > 128 || !model.StartsWith("gemini-", StringComparison.Ordinal) ||
            model.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '.' or '_')))
            throw new InvalidOperationException("Selecciona un modelo Gemini del catálogo.");
        var parts = new List<object> { new { text = prompt } };
        if (imageBase64 is not null)
            // An image (PNG) or, for «IA: analizar medios», an audio clip (audio/mp3): Gemini understands both.
            parts.Add(new { inlineData = new { mimeType = mediaMime, data = imageBase64 } });
        var config = new Dictionary<string, object>
        {
            ["temperature"] = imageBase64 is null ? 0.2 : 0.0,
            // generateContent expects these fields directly under generationConfig.
            // responseFormat.text.mimeType belongs to a different response format API
            // and rejects "application/json" with a 400 for both text and vision calls.
            ["responseMimeType"] = "application/json",
            ["responseJsonSchema"] = schema
        };
        if (model.StartsWith("gemini-3", StringComparison.Ordinal) &&
            thinking is "low" or "medium" or "high")
            config["thinkingConfig"] = new { thinkingLevel = thinking };
        var payload = JsonSerializer.Serialize(new
        {
            systemInstruction = new { parts = new[] { new { text = system } } },
            contents = new[] { new { role = "user", parts } },
            generationConfig = config
        });

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var attempt = 0;
        var outcome = "error";
        string? finish = null, usage = null, retries = null;
        try
        {
            while (true)
            {
                attempt++;
                using var request = Authorized(HttpMethod.Post, BaseUrl + "models/" + model + ":generateContent", key);
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
                        throw new InvalidOperationException("No se pudo conectar con Gemini. Comprueba tu conexión.", ex);
                    retries += $" {attempt}:conexión";
                    await WaitBeforeRetryAsync(Backoff(attempt), "no se pudo conectar", attempt, progress, token);
                    continue;
                }
                catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
                {
                    outcome = "timeout";
                    throw new InvalidOperationException($"Gemini no respondió en {Client.Timeout.TotalMinutes:0} minutos.", ex);
                }
                using (response)
                {
                    var status = (int)response.StatusCode;
                    if (IsTransient(status) && attempt < MaxAttempts && RetryDelay(response, body, attempt) is { } wait)
                    {
                        retries += $" {attempt}:{status}";
                        await WaitBeforeRetryAsync(wait, status == 429 ? "límite de peticiones (429)" : $"servicio ocupado ({status})",
                            attempt, progress, token);
                        continue;
                    }
                    EnsureSuccess(response, body, key);
                    using var json = JsonDocument.Parse(body);
                    usage = Usage(json.RootElement);
                    finish = json.RootElement.TryGetProperty("candidates", out var list) && list.ValueKind == JsonValueKind.Array &&
                        list.GetArrayLength() > 0 && list[0].TryGetProperty("finishReason", out var reasonValue) ? reasonValue.GetString() : null;
                    if (json.RootElement.TryGetProperty("candidates", out var candidates))
                        foreach (var candidate in candidates.EnumerateArray())
                            if (candidate.TryGetProperty("content", out var content) &&
                                content.TryGetProperty("parts", out var output))
                            {
                                var text = string.Concat(output.EnumerateArray()
                                    .Where(part => part.TryGetProperty("text", out _))
                                    .Select(part => part.GetProperty("text").GetString()));
                                if (string.IsNullOrWhiteSpace(text)) continue;
                                if (finish == "MAX_TOKENS" && !IsJson(text))
                                {
                                    outcome = "cortada";
                                    throw new InvalidOperationException("Gemini cortó la respuesta por longitud (MAX_TOKENS) antes de cerrar el JSON. " +
                                        "Acorta la premisa o genera el episodio por escenas.");
                                }
                                outcome = "ok";
                                return text;
                            }
                    var reason = json.RootElement.TryGetProperty("promptFeedback", out var feedback) &&
                        feedback.TryGetProperty("blockReason", out var blocked) ? blocked.GetString() : finish ?? "sin contenido";
                    outcome = "vacía";
                    throw new InvalidOperationException("Gemini no devolvió el JSON esperado (" + reason + ").");
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
            AiDiagnostics.Note($"gemini {model} ({purpose}) resultado={outcome} | {clock.ElapsedMilliseconds / 1000d:0.0}s | " +
                $"intentos={attempt}{(retries is null ? "" : " (reintentos:" + retries + ")")} | prompt={prompt.Length + system.Length:N0} car" +
                (usage is null ? "" : " | " + usage) + $" | fin={finish ?? "-"}");
        }
    }

    internal static bool IsTransient(int status) => status is 429 or 500 or 502 or 503 or 504;

    private static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(BackoffSeconds[Math.Clamp(attempt - 1, 0, BackoffSeconds.Length - 1)] * (0.85 + Random.Shared.NextDouble() * 0.3));

    /// <summary>How long the server asks to wait (Retry-After header or RetryInfo in the error body),
    /// else exponential backoff. Null when the wait is too long to be worth retrying (quota exhausted).</summary>
    internal static TimeSpan? RetryDelay(HttpResponseMessage response, string body, int attempt)
    {
        TimeSpan? asked = response.Headers.RetryAfter?.Delta ??
            (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("details", out var details) &&
                details.ValueKind == JsonValueKind.Array)
                foreach (var detail in details.EnumerateArray())
                    if (detail.TryGetProperty("@type", out var type) && type.GetString()?.EndsWith("RetryInfo", StringComparison.Ordinal) == true &&
                        detail.TryGetProperty("retryDelay", out var delay) && delay.GetString() is { } text && text.EndsWith('s') &&
                        double.TryParse(text[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds))
                        asked = TimeSpan.FromSeconds(seconds);
        }
        catch (JsonException) { }
        if (asked is { } value)
            return value > MaxRetryWait ? null : value < TimeSpan.Zero ? TimeSpan.Zero : value + TimeSpan.FromMilliseconds(250);
        return Backoff(attempt);
    }

    private static async Task WaitBeforeRetryAsync(TimeSpan wait, string reason, int attempt, IProgress<string>? progress, CancellationToken token)
    {
        progress?.Report($"Gemini: {reason}. Reintento {attempt + 1}/{MaxAttempts} en {Math.Ceiling(wait.TotalSeconds):0} s…");
        await Task.Delay(wait, token);
    }

    private static bool IsJson(string text)
    {
        try { using var _ = JsonDocument.Parse(text); return true; }
        catch (JsonException) { return false; }
    }

    private static string? Usage(JsonElement root)
    {
        if (!root.TryGetProperty("usageMetadata", out var usage)) return null;
        string Count(string name) => usage.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number.ToString("N0") : "?";
        return $"tokens prompt={Count("promptTokenCount")} salida={Count("candidatesTokenCount")} razonamiento={Count("thoughtsTokenCount")}";
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string key)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("x-goog-api-key", key);
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
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            detail += " Comprueba en Google AI Studio que tu clave tenga acceso a Gemini y sea de tipo autorización.";
        else if ((int)response.StatusCode == 429)
            // Retried already (or the wait asked was too long: daily quota): a batch stops here and keeps what it finished.
            throw new AiQuotaException("Gemini respondió 429: " + detail[..Math.Min(detail.Length, 250)] +
                " Es el límite de uso de tu clave (por minuto o por día): espera un rato, usa un modelo con más cuota (Flash) o revisa tu plan en Google AI Studio.");
        else if ((int)response.StatusCode is 500 or 502 or 503 or 504)
            detail += " Gemini está ocupado o falló temporalmente (el Director ya reintenta varias veces); prueba en unos minutos u otro modelo.";
        throw new InvalidOperationException($"Gemini respondió {(int)response.StatusCode}: " +
            detail[..Math.Min(detail.Length, 350)]);
    }
}
