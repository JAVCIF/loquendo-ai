using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace LoquendoAI.App;

/// <summary>
/// The provider ran out of credit or quota (no credits, daily limit, spending cap), or kept refusing for rate limits.
/// A batch stops here and keeps what it already finished; the message says what to do (1.3.0).
/// </summary>
internal sealed class AiQuotaException(string message) : InvalidOperationException(message);

/// <summary>
/// ChatGPT (OpenAI Chat Completions API) as a Director / image-analysis provider (1.3.0). Output is constrained with
/// structured outputs (response_format json_schema, strict), like Claude and Gemini. Reasoning models (GPT-5 and later,
/// o-series) take «reasoning_effort» and no temperature. Retries 429 rate limits and 5xx; «insufficient_quota» (no
/// credit) is reported at once as <see cref="AiQuotaException"/>.
/// </summary>
internal static class OpenAiDirectorClient
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(8) };
    // LOQUENDO_AI_OPENAI_BASE_URL: for a proxy, Azure-compatible gateway or a local test server; the key is sent there.
    private static readonly string BaseUrl =
        Environment.GetEnvironmentVariable("LOQUENDO_AI_OPENAI_BASE_URL") is { Length: > 0 } custom
            ? custom.TrimEnd('/') + "/" : "https://api.openai.com/v1/";
    internal const int MaxAttempts = 4;
    private static readonly double[] BackoffSeconds = [2, 6, 15];

    internal sealed record OpenAiModel(string Id, bool Vision, bool Reasoning);

    /// <summary>Chat models only (no audio, realtime, TTS, transcription, image, embedding or moderation models).</summary>
    public static async Task<IReadOnlyList<OpenAiModel>> ListModelsAsync(string key, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Configura la API key de OpenAI.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            using var request = Authorized(HttpMethod.Get, BaseUrl + "models", key);
            using var response = await Client.SendAsync(request, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            EnsureSuccess(response, body, key);
            using var json = JsonDocument.Parse(body);
            var result = new List<OpenAiModel>();
            if (json.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                foreach (var model in data.EnumerateArray())
                {
                    var id = model.TryGetProperty("id", out var value) ? value.GetString() ?? "" : "";
                    if (!IsChatModel(id)) continue;
                    result.Add(new OpenAiModel(id, SupportsImages(id), IsReasoning(id)));
                }
            return result.DistinctBy(x => x.Id).OrderBy(x => x.Id, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (HttpRequestException ex) { throw new InvalidOperationException("No se pudo conectar con OpenAI. Comprueba tu conexión.", ex); }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
        { throw new InvalidOperationException("OpenAI no respondió al consultar los modelos.", ex); }
    }

    internal static bool IsChatModel(string id) =>
        Regex.IsMatch(id, @"^(gpt-|o\d|chatgpt-)", RegexOptions.IgnoreCase) &&
        !Regex.IsMatch(id, @"audio|realtime|tts|transcribe|whisper|image|dall-e|embedding|moderation|search|instruct|codex|computer-use|deep-research|-oss|-pro\b",
            RegexOptions.IgnoreCase) &&
        // Without structured outputs (json_schema) or only in the Responses API.
        !Regex.IsMatch(id, @"^(gpt-3|gpt-4(-turbo|-\d{4}|-0\d{3}|-32k|$)|chatgpt-4o-latest|o1-mini|o1-preview)", RegexOptions.IgnoreCase);

    internal static bool IsReasoning(string id) => Regex.IsMatch(id, @"^(o\d|gpt-[5-9])", RegexOptions.IgnoreCase) &&
        !id.Contains("chat-latest", StringComparison.OrdinalIgnoreCase);

    internal static bool SupportsImages(string id) => Regex.IsMatch(id, @"^(gpt-4o|gpt-4\.1|gpt-4\.5|gpt-[5-9]|o[3-9]|o1(?!-mini)|chatgpt-4o)",
        RegexOptions.IgnoreCase) && !Regex.IsMatch(id, @"^o3-mini", RegexOptions.IgnoreCase); // o3-mini reads text only

    /// <summary>The main current models, not dated snapshots or special variants.</summary>
    internal static bool DefaultVisible(string id) =>
        Regex.IsMatch(id, @"^(gpt-[5-9](\.\d+)?(-mini|-nano)?|gpt-4\.1(-mini)?|gpt-4o(-mini)?|o[3-9](-mini)?)$", RegexOptions.IgnoreCase);

    /// <param name="effort">Reasoning models: low, medium or high; null or "default" leaves the model's default.</param>
    public static async Task<string> ChatAsync(string model, string system, string prompt, object schema,
        string? imageBase64, string? effort, int maxTokens, CancellationToken token,
        IProgress<string>? progress = null, string purpose = "director")
    {
        var key = AiProviderSettings.GetOpenAiKey();
        if (key.Length == 0) throw new InvalidOperationException("Configura la API key de OpenAI (ChatGPT) en «Configurar IA».");
        if (model.Length > 128 || model.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '.' or '_' or ':')))
            throw new InvalidOperationException("Selecciona un modelo de ChatGPT del catálogo.");
        var reasoning = IsReasoning(model);
        var content = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = prompt } };
        if (imageBase64 is not null)
            content.Add(new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JsonObject { ["url"] = "data:image/png;base64," + imageBase64, ["detail"] = "low" }
            });
        var strict = true;
        var sendEffort = reasoning && effort is "low" or "medium" or "high";
        var sendTemperature = !reasoning;
        string Payload()
        {
            var body = new JsonObject
            {
                ["model"] = model,
                ["messages"] = new JsonArray
                {
                    new JsonObject { ["role"] = reasoning ? "developer" : "system", ["content"] = system },
                    new JsonObject { ["role"] = "user", ["content"] = content.DeepClone() }
                },
                ["max_completion_tokens"] = maxTokens,
                ["response_format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new JsonObject
                    {
                        ["name"] = "respuesta", ["strict"] = strict,
                        ["schema"] = ClaudeDirectorClient.SanitizeSchema(schema)
                    }
                }
            };
            if (sendEffort) body["reasoning_effort"] = effort;
            if (sendTemperature) body["temperature"] = imageBase64 is null ? 0.2 : 0.0;
            return body.ToJsonString();
        }

        var clock = Stopwatch.StartNew();
        var attempt = 0;
        var outcome = "error";
        string? finish = null, usage = null, retries = null;
        try
        {
            while (true)
            {
                attempt++;
                using var request = Authorized(HttpMethod.Post, BaseUrl + "chat/completions", key);
                request.Content = new StringContent(Payload(), Encoding.UTF8, "application/json");
                HttpResponseMessage response;
                string body;
                try
                {
                    response = await Client.SendAsync(request, token);
                    body = await response.Content.ReadAsStringAsync(token);
                }
                catch (HttpRequestException ex)
                {
                    if (attempt >= MaxAttempts) throw new InvalidOperationException("No se pudo conectar con OpenAI. Comprueba tu conexión.", ex);
                    retries += $" {attempt}:conexión";
                    await WaitAsync(Backoff(attempt), "no se pudo conectar", attempt, progress, token);
                    continue;
                }
                catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
                {
                    outcome = "timeout";
                    throw new InvalidOperationException($"OpenAI no respondió en {Client.Timeout.TotalMinutes:0} minutos.", ex);
                }
                using (response)
                {
                    var status = (int)response.StatusCode;
                    var code = ErrorCode(body);
                    if (status == 429 && code == "insufficient_quota")
                    {
                        outcome = "sin cuota";
                        throw new AiQuotaException("OpenAI: tu cuenta no tiene crédito o superó su límite de gasto (insufficient_quota). " +
                            "Añade saldo o sube el límite en platform.openai.com → Billing, o usa otro proveedor.");
                    }
                    if (status is 429 or 500 or 502 or 503 or 504 && attempt < MaxAttempts && RetryDelay(response, attempt) is { } wait)
                    {
                        retries += $" {attempt}:{status}";
                        await WaitAsync(wait, status == 429 ? "límite de peticiones (429)" : $"error temporal ({status})", attempt, progress, token);
                        continue;
                    }
                    // Not every model takes every parameter (temperature in reasoning models, reasoning_effort in chat
                    // models, strict schemas with some keywords): drop the one the API names and try again once.
                    if (status == 400 && attempt < MaxAttempts)
                    {
                        var problem = ErrorMessage(body);
                        if (sendTemperature && problem.Contains("temperature", StringComparison.OrdinalIgnoreCase)) { sendTemperature = false; retries += $" {attempt}:sin temperature"; continue; }
                        if (sendEffort && problem.Contains("reasoning_effort", StringComparison.OrdinalIgnoreCase)) { sendEffort = false; retries += $" {attempt}:sin reasoning_effort"; continue; }
                        if (strict && (problem.Contains("schema", StringComparison.OrdinalIgnoreCase) || problem.Contains("strict", StringComparison.OrdinalIgnoreCase)))
                        {
                            strict = false;
                            retries += $" {attempt}:esquema no estricto";
                            progress?.Report("OpenAI no aceptó el esquema estricto; reintentando con uno flexible…");
                            continue;
                        }
                    }
                    EnsureSuccess(response, body, key);
                    using var json = JsonDocument.Parse(body);
                    usage = Usage(json.RootElement);
                    var choice = json.RootElement.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array &&
                        choices.GetArrayLength() > 0 ? choices[0] : default;
                    finish = choice.ValueKind == JsonValueKind.Object && choice.TryGetProperty("finish_reason", out var reason) ? reason.GetString() : null;
                    var message = choice.ValueKind == JsonValueKind.Object && choice.TryGetProperty("message", out var m) ? m : default;
                    if (message.ValueKind == JsonValueKind.Object && message.TryGetProperty("refusal", out var refusal) &&
                        refusal.ValueKind == JsonValueKind.String && refusal.GetString() is { Length: > 0 } refused)
                    {
                        outcome = "rechazada";
                        throw new InvalidOperationException("ChatGPT declinó esta petición: " + refused[..Math.Min(200, refused.Length)]);
                    }
                    var text = message.ValueKind == JsonValueKind.Object && message.TryGetProperty("content", out var value) &&
                        value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
                    if (finish == "length")
                    {
                        outcome = "cortada";
                        throw new InvalidOperationException($"ChatGPT llegó al límite de {maxTokens:N0} tokens antes de cerrar el JSON. " +
                            "Baja el esfuerzo, acorta la premisa o genera por partes.");
                    }
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        outcome = "vacía";
                        throw new InvalidOperationException("ChatGPT no devolvió el JSON esperado (" + (finish ?? "sin contenido") + ").");
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
            AiDiagnostics.Note($"openai {model} ({purpose}) resultado={outcome} | {clock.ElapsedMilliseconds / 1000d:0.0}s | " +
                $"intentos={attempt}{(retries is null ? "" : " (reintentos:" + retries + ")")} | esfuerzo={effort ?? "predeterminado"} | " +
                $"max_tokens={maxTokens:N0} | prompt={prompt.Length + system.Length:N0} car" + (usage is null ? "" : " | " + usage) +
                $" | fin={finish ?? "-"}");
        }
    }

    private static string ErrorCode(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("code", out var code) &&
                code.ValueKind == JsonValueKind.String ? code.GetString() ?? "" : "";
        }
        catch (JsonException) { return ""; }
    }

    private static string ErrorMessage(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.GetProperty("error").GetProperty("message").GetString() ?? "";
        }
        catch (Exception) { return ""; }
    }

    private static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(BackoffSeconds[Math.Clamp(attempt - 1, 0, BackoffSeconds.Length - 1)] * (0.85 + Random.Shared.NextDouble() * 0.3));

    private static TimeSpan? RetryDelay(HttpResponseMessage response, int attempt)
    {
        TimeSpan? asked = response.Headers.RetryAfter?.Delta;
        if (asked is null && response.Headers.TryGetValues("retry-after", out var values) &&
            double.TryParse(values.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            asked = TimeSpan.FromSeconds(seconds);
        if (asked is { } value)
            return value > ClaudeDirectorClient.MaxRetryWait ? null : value < TimeSpan.Zero ? TimeSpan.Zero : value + TimeSpan.FromMilliseconds(250);
        return Backoff(attempt);
    }

    private static async Task WaitAsync(TimeSpan wait, string reason, int attempt, IProgress<string>? progress, CancellationToken token)
    {
        progress?.Report($"ChatGPT: {reason}. Reintento {attempt + 1}/{MaxAttempts} en {Math.Ceiling(wait.TotalSeconds):0} s…");
        await Task.Delay(wait, token);
    }

    private static string? Usage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage)) return null;
        string Count(string name) => usage.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number.ToString("N0") : "?";
        return $"tokens entrada={Count("prompt_tokens")} salida={Count("completion_tokens")}";
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string key)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
        return request;
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body, string key)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = ErrorMessage(body);
        if (detail.Length == 0) detail = "No se pudo leer el error de la API";
        detail = detail.Replace(key, "[clave oculta]", StringComparison.Ordinal);
        var status = (int)response.StatusCode;
        if (status == 429)
            throw new AiQuotaException("OpenAI respondió 429: " + detail[..Math.Min(detail.Length, 250)] +
                " Es el límite de uso de tu clave (peticiones por minuto o gasto): espera un rato o revisa los límites en platform.openai.com.");
        if (status is 401 or 403) detail += " Comprueba la API key en platform.openai.com → API keys.";
        else if (status is 500 or 502 or 503 or 504) detail += " OpenAI está ocupado o falló temporalmente; prueba en unos minutos u otro modelo.";
        throw new InvalidOperationException($"OpenAI respondió {status}: " + detail[..Math.Min(detail.Length, 350)]);
    }
}
