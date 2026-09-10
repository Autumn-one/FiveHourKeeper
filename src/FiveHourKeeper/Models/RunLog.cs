using System.Text.Json.Serialization;

namespace FiveHourKeeper.Models;

/// <summary>运行日志条目，对应 config 同目录下 runs.jsonl 的一行。</summary>
public sealed class RunLog
{
    public DateTimeOffset At { get; set; }
    public string ModelId { get; set; } = "";
    public string ModelName { get; set; } = "";
    public string? ScheduleId { get; set; }
    public RunLogKind Kind { get; set; } = RunLogKind.Scheduled;
    public RunLogStatus Status { get; set; }
    public string? Message { get; set; }
    public int? Attempt { get; set; }
    public int? HttpStatus { get; set; }
    public int? LatencyMs { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RunLogKind
{
    Scheduled,
    Manual,
    TestConnection,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RunLogStatus
{
    Success,
    Failed,
    Skipped,
    Missed,
    ConflictWarn,
}