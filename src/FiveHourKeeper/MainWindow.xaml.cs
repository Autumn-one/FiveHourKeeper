using System.Windows;
using FiveHourKeeper.Services;
using FiveHourKeeper.ViewModels;

namespace FiveHourKeeper;

public partial class MainWindow : Window, IMainWindowAccess
{
    private MainViewModel? _vm;
    private TrayService? _tray;
    private bool _allowClose;
    private bool _trayTipShown;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
    }

    public void Attach(MainViewModel vm, TrayService tray)
    {
        _vm = vm;
        _tray = tray;
        DataContext = vm;
        vm.RefreshPauseUi();
    }

    public void BringToFront()
    {
        base.Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    public void RequestExit()
    {
        _allowClose = true;
        Application.Current.Shutdown();
    }

    private void OnNav(object sender, RoutedEventArgs e)
    {
        // 按钮文本即页面名（仪表盘/模型/设置/日志）。
        if (sender is System.Windows.Controls.ContentControl { Content: string page })
            _vm?.NavigateToCommand.Execute(page);
    }

    private void OnPause(object sender, RoutedEventArgs e)
    {
        _vm?.TogglePauseCommand.Execute(null);
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose) return;
        if (_vm?.Settings?.MinimizeToTray == true)
        {
            // 关闭即最小化到托盘；从托盘菜单“退出”会先置 _allowClose。
            e.Cancel = true;
            Hide();
            // 首次进托盘给气泡提示，否则用户以为程序退出了。
            if (!_trayTipShown && _tray is not null)
            {
                _trayTipShown = true;
                _tray.ShowInfo("五小时窗口管家",
                    "已最小化到托盘，调度继续在后台运行。\n双击托盘图标可重新打开窗口（没看到图标就点任务栏的 ^ 展开隐藏区）。");
            }
        }
    }
}