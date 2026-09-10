using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiveHourKeeper.Models;
using FiveHourKeeper.Services;

namespace FiveHourKeeper.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ConfigRootHolder _config;
    private readonly JsonConfigStore _store;

    [ObservableProperty] private string _defaultPrompt = "仅回复1";
    [ObservableProperty] private int _safetyOffsetSeconds;
    [ObservableProperty] private AnchorSemantics _anchorSemantics;
    [ObservableProperty] private int _jitterMaxSeconds;
    [ObservableProperty] private int _gracePeriodSeconds;
    [ObservableProperty] private bool _notifyOnSuccess;
    [ObservableProperty] private bool _notifyOnFailure;
    [ObservableProperty] private bool _autoStart;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private string _timeZoneId = "";
    [ObservableProperty] private int _httpTimeoutSeconds;

    public SettingsViewModel(ConfigRootHolder config, JsonConfigStore store)
    {
        _config = config;
        _store = store;
        Hydrate();
    }

    private void Hydrate()
    {
        var g = _config.Current.Global;
        DefaultPrompt = g.DefaultPrompt;
        SafetyOffsetSeconds = g.SafetyOffsetSeconds;
        AnchorSemantics = g.AnchorSemantics;
        JitterMaxSeconds = g.JitterMaxSeconds;
        GracePeriodSeconds = g.GracePeriodSeconds;
        NotifyOnSuccess = g.NotifyOnSuccess;
        NotifyOnFailure = g.NotifyOnFailure;
        AutoStart = AutoStartService.IsEnabled();
        MinimizeToTray = g.MinimizeToTray;
        TimeZoneId = g.TimeZoneId ?? "";
        HttpTimeoutSeconds = g.HttpTimeoutSeconds;
    }

    [RelayCommand]
    private void Save()
    {
        var g = _config.Current.Global;
        g.DefaultPrompt = string.IsNullOrWhiteSpace(DefaultPrompt) ? "仅回复1" : DefaultPrompt;
        g.SafetyOffsetSeconds = SafetyOffsetSeconds;
        g.AnchorSemantics = AnchorSemantics;
        g.JitterMaxSeconds = JitterMaxSeconds;
        g.GracePeriodSeconds = GracePeriodSeconds;
        g.NotifyOnSuccess = NotifyOnSuccess;
        g.NotifyOnFailure = NotifyOnFailure;
        g.MinimizeToTray = MinimizeToTray;
        g.TimeZoneId = string.IsNullOrWhiteSpace(TimeZoneId) ? null : TimeZoneId;
        g.HttpTimeoutSeconds = HttpTimeoutSeconds;
        AutoStartService.SetEnabled(AutoStart);
        _store.Save(_config.Current);
        System.Windows.MessageBox.Show(
            System.Windows.Application.Current?.MainWindow,
            "设置已保存", "提示",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }
}