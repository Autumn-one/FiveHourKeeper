using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiveHourKeeper.Models;
using FiveHourKeeper.Services;

namespace FiveHourKeeper.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IModelRegistry _registry;
    private readonly ConfigRootHolder _config;
    private readonly SchedulerService _scheduler;
    private readonly RunLogStore _log;
    private readonly TrayService _tray;

    private int _recentCount = -1;      // 上次构建 Recent 时的日志条数
    private string _upcomingSignature = ""; // Upcoming 内容签名，避免每秒全量重建
    private int _tick;

    [ObservableProperty]
    private string _nextFireText = "计算中…";

    [ObservableProperty]
    private string _countdownText = "";

    [ObservableProperty]
    private bool _paused;

    public ObservableCollection<UpcomingRow> Upcoming { get; } = new();
    public ObservableCollection<RunLog> Recent { get; } = new();

    public DashboardViewModel(
        IModelRegistry registry,
        ConfigRootHolder config,
        SchedulerService scheduler,
        RunLogStore log,
        TrayService tray)
    {
        _registry = registry;
        _config = config;
        _scheduler = scheduler;
        _log = log;
        _tray = tray;

        Paused = config.Current.Global.SchedulingPaused;

        // 模型增删改（registry 变化）→ 立即刷新，不等 5 秒轮询。
        if (registry is System.ComponentModel.INotifyPropertyChanged inpc)
        {
            inpc.PropertyChanged += (_, _) =>
            {
                _tick = 0; // 重置节拍，强制本轮重建表格
                Refresh();
            };
        }

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Refresh();
        timer.Start();

        Refresh();
    }

    private void Refresh()
    {
        Paused = _config.Current.Global.SchedulingPaused;

        var nowUtc = DateTimeOffset.UtcNow;
        var localOffset = TimeZoneInfo.Local.GetUtcOffset(nowUtc);

        // 倒计时每秒更新；表格每 5 秒或有变化时才重建（消除闪烁与选中丢失）。
        DateTimeOffset? nextAt = null;
        string? nextModel = null;
        var rows = new List<UpcomingRow>();
        foreach (var p in _registry.EnabledProfiles())
        {
            // skipPastSendAt=true：展示视角只显示“尚未过期”的发送时刻，已错过的顺延下一轮。
            var n = ScheduleCalculator.Next(
                p.Schedules, nowUtc,
                _config.Current.Global.AnchorSemantics,
                _config.Current.Global.SafetyOffsetSeconds,
                localOffset,
                skipPastSendAt: true);
            if (n is null) continue;
            rows.Add(new UpcomingRow(p.Name, n.TargetAtUtc.LocalDateTime, n.SendAtUtc.LocalDateTime, n.Schedule.Kind.ToString()));
            if (nextAt is null || n.SendAtUtc < nextAt)
            {
                nextAt = n.SendAtUtc;
                nextModel = p.Name;
            }
        }

        if (nextAt is null)
        {
            NextFireText = "暂无计划任务";
            CountdownText = "";
        }
        else
        {
            var delta = nextAt.Value - nowUtc;
            CountdownText = delta > TimeSpan.Zero
                ? $"{(int)delta.TotalHours:D2}:{delta.Minutes:D2}:{delta.Seconds:D2}"
                : "正在发送…";
            NextFireText = $"{nextModel} · 目标 {nextAt.Value.LocalDateTime:HH:mm:ss}";
        }

        if (_tick++ % 5 == 0)
        {
            var signature = string.Join("|", rows.Select(r => $"{r.ModelName}@{r.SendLocal:MMddHHmmss}"));
            if (signature != _upcomingSignature)
            {
                _upcomingSignature = signature;
                Upcoming.Clear();
                foreach (var r in rows) Upcoming.Add(r);
            }

            var snapshot = _log.Snapshot(50);
            if (snapshot.Count != _recentCount)
            {
                _recentCount = snapshot.Count;
                Recent.Clear();
                foreach (var r in snapshot) Recent.Add(r);
            }
        }
    }

    [RelayCommand]
    private void TogglePause()
    {
        _config.Current.Global.SchedulingPaused = !_config.Current.Global.SchedulingPaused;
        _tray.SetStateIcon(_config.Current.Global.SchedulingPaused ? TrayState.Paused : TrayState.Normal);
        Refresh();
    }
}

public sealed record UpcomingRow(string ModelName, DateTime TargetLocal, DateTime SendLocal, string Kind);