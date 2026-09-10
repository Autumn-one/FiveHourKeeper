using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiveHourKeeper.Services;

namespace FiveHourKeeper.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ConfigRootHolder _config;
    private readonly JsonConfigStore _store;
    private readonly TrayService _tray;

    [ObservableProperty]
    private object? _currentPage;

    [ObservableProperty]
    private string _currentPageName = "仪表盘";

    [ObservableProperty]
    private string _pauseButtonText = "暂停";

    [ObservableProperty]
    private string _statusText = "运行中";

    public DashboardViewModel Dashboard { get; }
    public ModelListViewModel ModelList { get; }
    public SettingsViewModel Settings { get; }
    public LogsViewModel Logs { get; }
    public AboutViewModel About { get; }

    public MainViewModel(
        ConfigRootHolder config,
        JsonConfigStore store,
        TrayService tray,
        DashboardViewModel dashboard,
        ModelListViewModel modelList,
        SettingsViewModel settings,
        LogsViewModel logs,
        AboutViewModel about)
    {
        _config = config;
        _store = store;
        _tray = tray;

        Dashboard = dashboard;
        ModelList = modelList;
        Settings = settings;
        Logs = logs;
        About = about;

        CurrentPage = Dashboard;
        RefreshPauseUi();
    }

    public void RefreshPauseUi()
    {
        var paused = _config.Current.Global.SchedulingPaused;
        PauseButtonText = paused ? "恢复" : "暂停";
        StatusText = paused ? "已暂停" : "运行中";
    }

    [RelayCommand]
    private void NavigateTo(string page)
    {
        CurrentPageName = page;
        CurrentPage = page switch
        {
            "模型" => ModelList,
            "设置" => Settings,
            "日志" => Logs,
            "关于" => About,
            _ => Dashboard,
        };
    }

    [RelayCommand]
    private void TogglePause()
    {
        _config.Current.Global.SchedulingPaused = !_config.Current.Global.SchedulingPaused;
        _store.Save(_config.Current);
        _tray.SetStateIcon(_config.Current.Global.SchedulingPaused ? TrayState.Paused : TrayState.Normal);
        RefreshPauseUi();
    }
}