using System.Windows;
using H.NotifyIcon;
using FiveHourKeeper.Models;

namespace FiveHourKeeper.Services;

/// <summary>Tray 与主窗口之间的弱耦合回调，避免 DI 循环依赖。</summary>
public interface IMainWindowAccess
{
    void BringToFront();

    /// <summary>退出程序（从托盘菜单触发，需绕过“关闭即最小化”逻辑）。</summary>
    void RequestExit();
}

/// <summary>
/// 系统托盘：三态图标 + 倒计时 Tooltip + 右键菜单。
/// 同时充当 <see cref="INotifier"/>，避免 Tray ↔ Notifier 的构造循环依赖。
/// </summary>
public sealed class TrayService : INotifier, IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly IModelRegistry _registry;
    private readonly Lazy<SchedulerService> _scheduler;
    private readonly ConfigRootHolder _config;
    private readonly JsonConfigStore _store;
    private readonly IMainWindowAccess _window;

    public TrayService(
        IModelRegistry registry,
        Lazy<SchedulerService> scheduler,
        ConfigRootHolder config,
        JsonConfigStore store,
        IMainWindowAccess window)
    {
        _registry = registry;
        _scheduler = scheduler;
        _config = config;
        _store = store;
        _window = window;

        _icon = new TaskbarIcon
        {
            ToolTipText = "五小时窗口管家",
            NoLeftClickDelay = true,
            IconSource = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/Assets/tray-normal.ico", UriKind.Absolute)),
        };

        var menu = new System.Windows.Controls.ContextMenu();
        menu.Items.Add(MakeItem("显示主窗口", (_, _) => _window.BringToFront()));
        menu.Items.Add(MakeItem("立即发送一次", (_, _) => SendAllNowAsync()));
        menu.Items.Add(MakeItem("暂停 / 恢复调度", (_, _) => TogglePause()));
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(MakeItem("退出", (_, _) => _window.RequestExit()));
        _icon.ContextMenu = menu;

        _icon.TrayMouseDoubleClick += (_, _) => _window.BringToFront();
        _icon.TrayLeftMouseDown += (_, _) => _window.BringToFront();

        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        timer.Tick += (_, _) => RefreshTooltip();
        timer.Start();

        // H.NotifyIcon 的 TaskbarIcon 是 FrameworkElement：只有进入视觉树触发 Loaded 才会自动注册图标。
        // 本服务纯代码创建、永不入树，必须显式 ForceCreate（官方文档："Use it to force create icon
        // if it placed in resources"），否则图标不存在、通知抛 "TrayIcon is not created"。
        // enablesEfficiencyMode:false —— 调度对时效敏感，不允许系统 EcoQoS 节流本进程。
        _icon.ForceCreate(enablesEfficiencyMode: false);
    }

    /// <summary>冒烟/诊断用：托盘图标是否已真实注册（防“关闭到托盘”退化为隐形存活）。</summary>
    public bool IsIconCreated => _icon.IsCreated;

    public void SetStateIcon(TrayState state)
    {
        var name = state switch
        {
            TrayState.Paused => "tray-paused.ico",
            TrayState.Error => "tray-error.ico",
            _ => "tray-normal.ico",
        };
        _icon.IconSource = new System.Windows.Media.Imaging.BitmapImage(
            new Uri($"pack://application:,,,/Assets/{name}", UriKind.Absolute));
    }

    // ---- INotifier ----

    public void ShowSuccess(string modelName, DateTime targetLocal)
    {
        _icon.ShowNotification(
            "窗口已刷新",
            $"{modelName}：锚点已建立，窗口于 {targetLocal:HH:mm:ss} 起新一轮。",
            H.NotifyIcon.Core.NotificationIcon.Info);
    }

    public void ShowFailure(string modelName, string error)
    {
        SetStateIcon(TrayState.Error);
        _icon.ShowNotification(
            "发送失败",
            $"{modelName}：{error}",
            H.NotifyIcon.Core.NotificationIcon.Error);
    }

    public void ShowInfo(string title, string message)
    {
        _icon.ShowNotification(title, message, H.NotifyIcon.Core.NotificationIcon.Info);
    }

    // ---- 托盘菜单行为 ----

    /// <summary>“立即发送一次”：对每个启用模型各发一个真实请求。请求会锚定窗口，故失败时静默降级为状态文本。</summary>
    private async void SendAllNowAsync()
    {
        var enabled = _registry.EnabledProfiles().ToList();
        if (enabled.Count == 0)
        {
            ShowInfo("立即发送", "没有任何已启用的模型");
            return;
        }
        if (!_scheduler.IsValueCreated) _ = _scheduler.Value;

        foreach (var profile in enabled)
        {
            try
            {
                await _scheduler.Value.SendNowAsync(profile, RunLogKind.Manual);
            }
            catch (Exception ex)
            {
                ShowFailure(profile.Name, ex.Message);
            }
        }
    }

    private void TogglePause()
    {
        _config.Current.Global.SchedulingPaused = !_config.Current.Global.SchedulingPaused;
        _store.Save(_config.Current);
        SetStateIcon(_config.Current.Global.SchedulingPaused ? TrayState.Paused : TrayState.Normal);
        ShowInfo("调度", _config.Current.Global.SchedulingPaused ? "已暂停" : "已恢复");
    }

    private void RefreshTooltip()
    {
        try
        {
            var next = ComputeNextText();
            _icon.ToolTipText = next;
        }
        catch { /* ignore */ }
    }

    private string ComputeNextText()
    {
        if (_config.Current.Global.SchedulingPaused) return "调度已暂停";
        var nowUtc = DateTimeOffset.UtcNow;
        DateTimeOffset? nextAt = null;
        string? nextModel = null;
        foreach (var p in _registry.EnabledProfiles())
        {
            var n = ScheduleCalculator.Next(
                p.Schedules, nowUtc,
                _config.Current.Global.AnchorSemantics,
                _config.Current.Global.SafetyOffsetSeconds,
                TimeZoneInfo.Local.GetUtcOffset(nowUtc),
                skipPastSendAt: true);
            if (n is null) continue;
            if (nextAt is null || n.SendAtUtc < nextAt)
            {
                nextAt = n.SendAtUtc;
                nextModel = p.Name;
            }
        }
        return nextAt is null
            ? "五小时窗口管家 · 暂无计划"
            : $"五小时窗口管家 · {nextModel}\n下一次发送：{nextAt.Value.LocalDateTime:HH:mm:ss}";
    }

    private static System.Windows.Controls.MenuItem MakeItem(string header, RoutedEventHandler onClick)
    {
        var item = new System.Windows.Controls.MenuItem { Header = header };
        item.Click += onClick;
        return item;
    }

    public void Dispose()
    {
        _icon.Dispose();
    }
}

public enum TrayState
{
    Normal,
    Paused,
    Error,
}