using System.Net;
using FiveHourKeeper.Models;

namespace FiveHourKeeper.Services;

/// <summary>统一两类协议的 Base URL 规范化与端点拼接，避免 /v1/v1 之类重复前缀。</summary>
public static class AiUrl
{
    public static Uri NormalizeBase(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException("Base URL 不能为空");
        var trimmed = raw.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("Base URL 必须是 http(s) 完整地址");
        return uri;
    }

    public static Uri AppendPath(Uri baseUri, string path)
    {
        var left = baseUri.AbsoluteUri.TrimEnd('/');
        var right = path.TrimStart('/');
        return new Uri(left + "/" + right, UriKind.Absolute);
    }

    public static bool LooksLikeOpenAiChatEndpoint(string baseUrl)
    {
        // 部分中转站会直接给到 /chat/completions 或 /v1/chat/completions，允许传入作为 Base URL。
        if (string.IsNullOrEmpty(baseUrl)) return false;
        var u = baseUrl.Trim().TrimEnd('/');
        return u.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) ||
               u.EndsWith("/v1/chat/completions", StringComparison.OrdinalIgnoreCase);
    }

    public static AiProtocol InferProtocolFromUrl(string url) =>
        url.Contains("/anthropic", StringComparison.OrdinalIgnoreCase)
            ? AiProtocol.AnthropicCompatible
            : AiProtocol.OpenAiCompatible;

    public static Uri EnsureModelsEndpoint(Uri baseUri, AiProtocol protocol)
    {
        var path = baseUri.AbsolutePath.TrimEnd('/').ToLowerInvariant();
        // 已经明确指向某个端点的，保留。
        if (path.EndsWith("/models")) return baseUri;
        if (path.EndsWith("/chat/completions") || path.EndsWith("/messages"))
            return new Uri(baseUri.GetLeftPart(UriPartial.Authority), UriKind.Absolute);

        if (protocol == AiProtocol.OpenAiCompatible)
            return AppendPath(baseUri, "models");

        // Anthropic：若已含 /v1，追加 /models；否则追加 /v1/models。
        return path.EndsWith("/v1") ? AppendPath(baseUri, "models") : AppendPath(baseUri, "v1/models");
    }

    public static Uri EnsureChatEndpoint(Uri baseUri, AiProtocol protocol)
    {
        var path = baseUri.AbsolutePath.TrimEnd('/').ToLowerInvariant();
        if (path.EndsWith("/chat/completions") || path.EndsWith("/messages"))
            return baseUri;

        if (protocol == AiProtocol.OpenAiCompatible)
            return AppendPath(baseUri, "chat/completions");

        return path.EndsWith("/v1") ? AppendPath(baseUri, "messages") : AppendPath(baseUri, "v1/messages");
    }

    public static string MaskKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey)) return string.Empty;
        if (apiKey.Length <= 8) return new string('*', apiKey.Length);
        return apiKey[..4] + "…" + apiKey[^4..];
    }
}