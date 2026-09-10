using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FiveHourKeeper.Models;

/// <summary>持久化根配置。修改前请同步 <see cref="JsonConfigStore"/> 的迁移表。</summary>
public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public GlobalSettings Global { get; set; } = new();
    public List<ModelProfile> Models { get; set; } = new();
}

public sealed class GlobalSettings
{
    /// <summary>所有模型共用的默认提示词，首行固定为 “仅回复1”。</summary>
    public string DefaultPrompt { get; set; } = "仅回复1";

    /// <summary>在 “目标时刻 − 5 小时” 之后再额外推迟的秒数，避免时钟漂移把锚点甩到上一整点。</summary>
    public int SafetyOffsetSeconds { get; set; } = 60;

    /// <summary>窗口起点向下取整到整点（Claude Code 行为）还是精确到分钟（claude.ai 行为）。</summary>
    public AnchorSemantics AnchorSemantics { get; set; } = AnchorSemantics.FloorToHour;

    /// <summary>错峰抖动上限（秒），多模型同时刻发送时随机偏移以避开服务端并发限制。</summary>
    public int JitterMaxSeconds { get; set; } = 15;

    /// <summary>发送时刻之后的宽容期，超期视为 missed 而不补发。</summary>
    public int GracePeriodSeconds { get; set; } = 600;

    /// <summary>下次发送时刻是否展示通知气泡。</summary>
    public bool NotifyOnSuccess { get; set; } = true;

    /// <summary>发送失败（401 之外）是否展示通知气泡。</summary>
    public bool NotifyOnFailure { get; set; } = true;

    /// <summary>开机自启（HKCU\Run）。</summary>
    public bool AutoStart { get; set; }

    /// <summary>关闭窗口时最小化到托盘而不是退出。</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>调度全局暂停开关（不删除时刻列表）。</summary>
    public bool SchedulingPaused { get; set; }

    /// <summary>时区 ID，空表示跟随系统。移动模型存储用 UTC 时间戳。</summary>
    public string? TimeZoneId { get; set; }

    /// <summary>网络超时（秒）。</summary>
    public int HttpTimeoutSeconds { get; set; } = 30;

    /// <summary>最多保留的运行日志条数。</summary>
    public int MaxLogEntries { get; set; } = 500;
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AnchorSemantics
{
    /// <summary>将首次发送时刻向下取整到整点。Claude Code 的实际行为。</summary>
    FloorToHour,
    /// <summary>保持分钟精度。claude.ai 的实际行为。</summary>
    Exact,
}

public partial class ModelProfile : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public AiProtocol Protocol { get; set; } = AiProtocol.OpenAiCompatible;
    public string BaseUrl { get; set; } = "";
    public string ApiKeyCipherBase64 { get; set; } = "";
    public string ModelName { get; set; } = "";

    /// <summary>覆盖全局默认提示词，留空则使用全局。</summary>
    public string? PromptOverride { get; set; }

    [ObservableProperty]
    private bool _enabled = true;

    public int? MaxTokens { get; set; }

    /// <summary>上次成功拉取的模型列表。</summary>
    public List<string> FetchedModels { get; set; } = new();
    public DateTimeOffset? ModelsFetchedAt { get; set; }

    /// <summary>每个重置时刻相对于本模型配置的独立列表。</summary>
    public List<ResetSchedule> Schedules { get; set; } = new();

    /// <summary>发送失败时的最大退避重试次数。</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>调度摘要（模型列表展示用），如 “09:00、21:00”。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ScheduleSummary
    {
        get
        {
            var parts = Schedules
                .Where(s => s.Enabled)
                .Select(s => s.Kind == ScheduleKind.Daily
                    ? s.DailyTime?.ToString("HH:mm") ?? "-"
                    : s.OneShotAt?.ToLocalTime().ToString("MM-dd HH:mm") ?? "-")
                .ToList();
            return parts.Count == 0 ? "无" : string.Join("、", parts);
        }
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AiProtocol
{
    OpenAiCompatible,
    AnthropicCompatible,
}

/// <summary>单条重置时刻配置。</summary>
public partial class ResetSchedule : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [ObservableProperty] private ScheduleKind _kind = ScheduleKind.Daily;

    /// <summary>每日循环的本地时刻，如 “02:00”。</summary>
    public TimeOnly? DailyTime { get; set; }

    /// <summary>一次性任务的本地时间，发送后自动停用。</summary>
    public DateTimeOffset? OneShotAt { get; set; }

    [ObservableProperty] private bool _enabled = true;
    public string? Note { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScheduleKind
{
    Daily,
    OneShot,
}