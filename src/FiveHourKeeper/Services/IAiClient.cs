using FiveHourKeeper.Models;

namespace FiveHourKeeper.Services;

public interface IAiClient
{
    AiProtocol Protocol { get; }

    Task<IReadOnlyList<string>> ListModelsAsync(
        ModelProfile profile,
        CancellationToken ct = default);

    Task<SendResult> SendPingAsync(
        ModelProfile profile,
        string prompt,
        CancellationToken ct = default);
}

public sealed class SendResult
{
    public bool Success { get; init; }
    public int? HttpStatus { get; init; }
    public int LatencyMs { get; init; }
    public string? Error { get; init; }
    public string? ResponseSnippet { get; init; }
    public bool ShouldNotRetry { get; init; }
}