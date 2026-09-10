using System.IO;
using System.Windows;
using FiveHourKeeper.Services;
using FiveHourKeeper.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FiveHourKeeper;

public partial class App : Application
{
    private IHost? _host;
    private SchedulerService? _scheduler;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 全局兜底：任何未处理异常都落盘，避免“闪退无痕”。
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteCrash("AppDomain", args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrash("Dispatcher", args.Exception);
            args.Handled = true; // UI 线程异常不直接终止进程
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteCrash("Task", args.Exception);
            args.SetObserved();
        };

        WritePhase("handlers-registered");
        base.OnStartup(e);
        WritePhase("base-onstartup");

        var mutex = new System.Threading.Mutex(true, "FiveHourKeeper.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            WritePhase("single-instance: already running, exiting");
            Shutdown();
            return;
        }
        WritePhase("mutex-acquired");

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddHttpClient("ai", (sp, client) =>
                {
                    var holder = sp.GetRequiredService<ConfigRootHolder>();
                    client.Timeout = TimeSpan.FromSeconds(holder.Current.Global.HttpTimeoutSeconds);
                });
                services.AddSingleton<JsonConfigStore>();
                services.AddSingleton<ConfigRootHolder>(sp =>
                {
                    var store = sp.GetRequiredService<JsonConfigStore>();
                    return new ConfigRootHolder { Current = store.Load() };
                });
                services.AddSingleton<IModelRegistry>(sp =>
                {
                    var holder = sp.GetRequiredService<ConfigRootHolder>();
                    return new ModelRegistry(holder.Current.Models);
                });
                services.AddSingleton<RunLogStore>(sp =>
                {
                    var holder = sp.GetRequiredService<ConfigRootHolder>();
                    return new RunLogStore(sp.GetRequiredService<JsonConfigStore>(), holder.Current.Global.MaxLogEntries);
                });
                services.AddSingleton<IClock, SystemClock>();
                services.AddSingleton<IAiClientFactory, AiClientFactory>();
                services.AddSingleton<SchedulerService>();
                services.AddSingleton<Lazy<SchedulerService>>(sp =>
                    new Lazy<SchedulerService>(sp.GetRequiredService<SchedulerService>));
                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<ModelListViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<LogsViewModel>();
                services.AddSingleton<AboutViewModel>();
                services.AddSingleton<MainWindow>();
                services.AddSingleton<IMainWindowAccess>(sp => sp.GetRequiredService<MainWindow>());
                services.AddSingleton<TrayService>();
                services.AddSingleton<INotifier>(sp => sp.GetRequiredService<TrayService>());
                services.AddSingleton<MainViewModel>();
            })
            .Build();
        WritePhase("host-built");

        var vm = _host.Services.GetRequiredService<MainViewModel>();
        WritePhase("vm-resolved");
        var window = _host.Services.GetRequiredService<MainWindow>();
        WritePhase("window-resolved");
        var scheduler = _host.Services.GetRequiredService<SchedulerService>();
        _scheduler = scheduler;
        var tray = _host.Services.GetRequiredService<TrayService>();
        WritePhase("tray-resolved");

        // 模型编辑弹窗由 View 层驱动：ViewModel 只声明需求（EditDialogLauncher）。
        var services = _host.Services;
        var registry = services.GetRequiredService<IModelRegistry>();
        var clientFactory = services.GetRequiredService<IAiClientFactory>();
        var configHolder = services.GetRequiredService<ConfigRootHolder>();
        vm.ModelList.EditDialogLauncher = source =>
        {
            var editVm = new ViewModels.ModelEditViewModel(source, registry, clientFactory, configHolder);
            var dlg = new Views.ModelEditDialog(editVm) { Owner = window };
            if (dlg.ShowDialog() != true || !editVm.IsValid()) return false;
            var draft = editVm.BuildDraft();
            registry.Upsert(draft);
            vm.ModelList.Persist();
            return true;
        };

        window.Attach(vm, tray);
        MainWindow = window;
        WritePhase("window-attached");

        // --smoke：不显示 UI，仅实例化全部视图与弹窗，验证 XAML/资源/绑定不炸。
        if (e.Args.Contains("--smoke"))
        {
            var ok = RunSmoke(registry, clientFactory, configHolder);
            // 托盘回归：图标必须真实注册过（2026-09-10 曾因未 ForceCreate 导致全程隐形存活）。
            if (!tray.IsIconCreated)
            {
                ok = false;
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fh-smoke.txt"),
                    "SMOKE_FAIL\nTray icon was not created.");
            }
            WritePhase($"smoke={ok}");
            Shutdown();
            return;
        }

        if (e.Args.Contains("--tray"))
        {
            // 启动时最小化
            window.WindowState = WindowState.Minimized;
            window.Hide();
            WritePhase("window-hidden-tray");
        }
        else
        {
            window.Show();
            WritePhase("window-shown");
        }

        _ = Task.Run(() => scheduler.StartAsync(default));
        WritePhase("scheduler-started");
    }

    private static bool RunSmoke(
        Services.IModelRegistry registry,
        Services.IAiClientFactory clientFactory,
        Services.ConfigRootHolder configHolder)
    {
        try
        {
            _ = new Views.DashboardPage();
            _ = new Views.ModelListPage();
            _ = new Views.SettingsPage();
            _ = new Views.LogsPage();
            _ = new Views.AboutPage();

            // 主壳窗口同样要实例化：导航栏的 StaticResource 只在运行期解析，
            // 资源缺失时编译不报错、启动即 XamlParseException（已两次踩坑：
            // BadgeStyle 缺失致模型页空白、NavAbout 缺失致启动无窗口）。
            _ = new MainWindow();
            _ = new Views.AboutPage();

            var editVm = new ViewModels.ModelEditViewModel(null, registry, clientFactory, configHolder)
            {
                Name = "smoke",
                BaseUrl = "https://api.example.com/v1",
                ApiKey = "sk-smoke",
                ModelName = "gpt-smoke",
            };
            var dlg = new Views.ModelEditDialog(editVm);
            if (!editVm.IsValid()) throw new InvalidOperationException("validation failed");
            if (editVm.CollectScheduleErrors() is not null) throw new InvalidOperationException("schedule invalid");

            var draft = editVm.BuildDraft();
            if (draft.Schedules.Count != 1) throw new InvalidOperationException("default schedule row missing");
            if (draft.Schedules[0].DailyTime is null) throw new InvalidOperationException("daily time not parsed");
            if (string.IsNullOrEmpty(draft.ApiKeyCipherBase64)) throw new InvalidOperationException("api key not protected");
            if (draft.ApiKeyCipherBase64 == "sk-smoke") throw new InvalidOperationException("api key stored in plaintext");
            if (Services.DpapiSecretProtector.Unprotect(draft.ApiKeyCipherBase64) != "sk-smoke")
                throw new InvalidOperationException("dpapi roundtrip failed");

            System.IO.File.WriteAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fh-smoke.txt"),
                "SMOKE_OK");
            return true;
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fh-smoke.txt"),
                "SMOKE_FAIL\n" + ex);
            return false;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _scheduler?.StopAsync(CancellationToken.None).GetAwaiter().GetResult(); }
        catch { /* ignore */ }
        _host?.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        _host?.Dispose();
        base.OnExit(e);
    }

    /// <summary>同一异常指纹 60 秒内只记录一次，避免布局类异常每帧重试刷爆磁盘（曾写满 166MB）。</summary>
    private static readonly System.Collections.Generic.HashSet<string> _recentCrashKeys = new();
    private static DateTimeOffset _crashWindowStart = DateTimeOffset.MinValue;

    private static void WriteCrash(string origin, Exception? ex)
    {
        try
        {
            if (ex is null) return;

            // 指纹 = 异常类型 + 消息首行；60 秒窗口去重。
            var key = $"{ex.GetType().FullName}|{(ex.Message.Split('\n')[0])}";
            var now = DateTimeOffset.Now;
            if ((now - _crashWindowStart).TotalSeconds > 60)
            {
                _recentCrashKeys.Clear();
                _crashWindowStart = now;
            }
            if (!_recentCrashKeys.Add(key)) return;

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FiveHourKeeper");
            Directory.CreateDirectory(dir);
            var log = $"[{now:yyyy-MM-dd HH:mm:ss}] origin={origin}\n{ex}\n\n";
            File.AppendAllText(Path.Combine(dir, "crash.log"), log);

            // 硬保护：日志超过 5MB 轮转，只留尾部 1MB。
            var crashPath = Path.Combine(dir, "crash.log");
            var fi = new FileInfo(crashPath);
            if (fi.Exists && fi.Length > 5 * 1024 * 1024)
            {
                using var fs = new FileStream(crashPath, FileMode.Open, FileAccess.ReadWrite);
                fs.Seek(-1024 * 1024, SeekOrigin.End);
                var tail = new byte[fs.Length - fs.Position];
                _ = fs.Read(tail, 0, tail.Length);
                fs.SetLength(0);
                fs.Write(tail);
            }
        }
        catch { /* 崩溃日志本身失败则放弃 */ }
    }

    /// <summary>诊断用：把启动进度写到 %TEMP%\fh-phase.log，用于定位“启动即消失”的进程。</summary>
    private static void WritePhase(string step)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "fh-phase.log"),
                $"[{DateTimeOffset.Now:HH:mm:ss.fff}] {step}\n");
        }
        catch { /* ignore */ }
    }
}