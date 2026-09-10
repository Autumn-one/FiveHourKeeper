using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FiveHourKeeper.Models;

namespace FiveHourKeeper.Services;

/// <summary>Anthropic /v1/messages 与 /v1/models（x-api-key + anthropic-version，分页 has_more）。</summary>
public sealed class AnthropicClient : IAiClient
{
    private const string AnthropicVersion = "2023-06-01";

    private readonly HttpClient _http;

    public AnthropicClient(HttpClient http)
    {
        _http = http;
    }

    public AiProtocol Protocol => AiProtocol.AnthropicCompatible;

    public async Task<IReadOnlyList<string>> ListModelsAsync(ModelProfile profile, CancellationToken ct = default)
    {
        var baseUri = AiUrl.NormalizeBase(profile.BaseUrl);
        var uri = AiUrl.EnsureModelsEndpoint(baseUri, AiProtocol.AnthropicCompatible);

        var list = new List<string>();
        string? cursor = null;
        for (var page = 0; page < 20; page++)
        {
            // after_id 是 query 参数（官方 spec），不是 header。
            var pageUri = string.IsNullOrEmpty(cursor)
                ? uri
                : new Uri(uri.AbsoluteUri + (uri.Query.Contains('?') ? "&" : "?") + "after_id=" + Uri.EscapeDataString(cursor));
            using var req = new HttpRequestMessage(HttpMethod.Get, pageUri);
            req.Headers.Add("x-api-key", profile.ApiKeyCipherBase64);
            req.Headers.Add("anthropic-version", AnthropicVersion);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                throw new AiClientException((int)resp.StatusCode, body.Length > 400 ? body[..400] : body);
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    {
                        var s = id.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                    }
                }
            }

            var hasMore = doc.RootElement.TryGetProperty("has_more", out var hm) && hm.GetBoolean();
            cursor = doc.RootElement.TryGetProperty("last_id", out var lid) ? lid.GetString() : null;
            if (!hasMore || string.IsNullOrEmpty(cursor)) break;
        }

        return list.Distinct().ToList();
    }

    public async Task<SendResult> SendPingAsync(ModelProfile profile, string prompt, CancellationToken ct = default)
    {
        var baseUri = AiUrl.NormalizeBase(profile.BaseUrl);
        var uri = AiUrl.EnsureChatEndpoint(baseUri, AiProtocol.AnthropicCompatible);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, uri);
            req.Headers.Add("x-api-key", profile.ApiKeyCipherBase64);
            req.Headers.Add("anthropic-version", AnthropicVersion);

            var body = new Dictionary<string, object?>
            {
                ["model"] = profile.ModelName,
                ["max_tokens"] = profile.MaxTokens ?? 64,
                ["messages"] = new[]
                {
                    new { role = "user", content = prompt },
                },
            };
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

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

    private static string? ExtractSnippet(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in content.EnumerateArray())
                {
                    if (block.TryGetProperty("text", out var text))
                    {
                        var s = text.GetString() ?? "";
                        return s.Length > 200 ? s[..200] : s;
                    }
                }
            }
        }
        catch { }
        return raw.Length > 200 ? raw[..200] : raw;
    }
}