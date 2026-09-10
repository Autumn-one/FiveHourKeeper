using System.Net.Http;
using FiveHourKeeper.Models;

namespace FiveHourKeeper.Services;

public sealed class AiClientFactory : IAiClientFactory
{
    private readonly IHttpClientFactory _http;
    public AiClientFactory(IHttpClientFactory http) { _http = http; }

    public IAiClient Create(ModelProfile profile) => profile.Protocol switch
    {
        AiProtocol.OpenAiCompatible => new OpenAiClient(_http.CreateClient("ai")),
        AiProtocol.AnthropicCompatible => new AnthropicClient(_http.CreateClient("ai")),
        _ => throw new InvalidOperationException("未知协议"),
    };
}