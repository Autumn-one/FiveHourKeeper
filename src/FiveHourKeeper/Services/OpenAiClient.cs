using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FiveHourKeeper.Models;

namespace FiveHourKeeper.Services;

/// <summary>OpenAI /v1/chat/completions 与 /v1/models。兼容大部分中转站。</summary>
public sealed class OpenAiClient : IAiClient
{
    private readonly HttpClient _http;

    public OpenAiClient(HttpClient http)
    {
        _http = http;
    }

    public AiProtocol Protocol => AiProtocol.OpenAiCompatible;

    public async Task<IReadOnlyList<string>> ListModelsAsync(ModelProfile profile, CancellationToken ct = default)
    {
        var baseUri = AiUrl.NormalizeBase(profile.BaseUrl);
        var uri = AiUrl.EnsureModelsEndpoint(baseUri, AiProtocol.OpenAiCompatible);

        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", profile.ApiKeyCipherBase64);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new AiClientException((int)resp.StatusCode, body.Length > 400 ? body[..400] : body);
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var list = new List<string>();
        foreach (var item in data.EnumerateArray())
        {
            if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                list.Add(id.GetString() ?? "");
        }
        return list.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
    }

    public async Task<SendResult> SendPingAsync(ModelProfile profile, string prompt, CancellationToken ct = default)
    {
        var baseUri = AiUrl.NormalizeBase(profile.BaseUrl);
        var uri = AiUrl.EnsureChatEndpoint(baseUri, AiProtocol.OpenAiCompatible);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, uri);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", profile.ApiKeyCipherBase64);

            var body = BuildBody(profile, prompt);
            req.Content = JsonContent.Create(body);
            using var resp = await _http.SendAsync(req, ct);
            sw.Stop();

            var text = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                return new SendResult
                {
                    Success = false,
                    HttpStatus = (int)resp.StatusCode,
                    LatencyMs = (int)sw.ElapsedMilliseconds,
                    Error = text.Length > 400 ? text[..400] : text,
                    ShouldNotRetry = (int)resp.StatusCode is 401 or 403 or 404 or 422,
                };
            }

            return new SendResult
            {
                Success = true,
                HttpStatus = (int)resp.StatusCode,
                LatencyMs = (int)sw.ElapsedMilliseconds,
                ResponseSnippet = ExtractSnippet(text),
            };
        }
        catch (TaskCanceledException)
        {
            return new SendResult { Success = false, Error = "请求超时", LatencyMs = (int)sw.ElapsedMilliseconds };
        }
        catch (Exception ex)
        {
            return new SendResult { Success = false, Error = ex.Message, LatencyMs = (int)sw.ElapsedMilliseconds };
        }
    }

    private static object BuildBody(ModelProfile profile, string prompt)
    {
        var maxTokens = profile.MaxTokens ?? 64;
        // OpenAI o-series 强制使用 max_completion_tokens；其它模型两者均支持 max_tokens。
        var useCompletionTokenParam = profile.ModelName.StartsWith("o", StringComparison.OrdinalIgnoreCase) ||
                                      profile.ModelName.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase);

        var body = new Dictionary<string, object?>
        {
            ["model"] = profile.ModelName,
            ["messages"] = new[]
            {
                new { role = "user", content = prompt },
            },
            [useCompletionTokenParam ? "max_completion_tokens" : "max_tokens"] = maxTokens,
        };
        return body;
    }

    private static string? ExtractSnippet(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var msg = choices[0].GetProperty("message");
                var content = msg.TryGetProperty("content", out var c) ? c.ToString() : msg.ToString();
                return content.Length > 200 ? content[..200] : content;
            }
        }
        catch { }
        return raw.Length > 200 ? raw[..200] : raw;
    }
}

public sealed class AiClientException : Exception
{
    public int HttpStatus { get; }
    public AiClientException(int status, string message) : base(message) { HttpStatus = status; }
}