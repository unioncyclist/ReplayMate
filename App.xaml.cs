using System.Threading;
using Microsoft.UI.Xaml;
using ReplayMate.Interop;
using ReplayMate.Services;
using static ReplayMate.Interop.NativeMethods;

namespace ReplayMate;

public partial class App : Application
{
    private static Mutex? _mutex;
    private MainWindow? _window;

    private const string MutexName = "Local\\ReplayMate.SingleInstance";
    private const string WindowTitle = "ReplayMate";

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var autostartRequested = Environment.GetCommandLineArgs()
            .Any(a => a.Equals("--autostart", StringComparison.OrdinalIgnoreCase));

        _mutex = new Mutex(initiallyOwned: true, MutexName, out var isFirst);
        if (!isFirst)
        {
            // 已有实例：把它被隐藏的窗口找回来，然后退出
            WindowLocator.ActivateExistingInstance();
            Environment.Exit(0);
            return;
        }

        Log.Init();
        Log.Info("ReplayMate 启动");

        if (Environment.GetCommandLineArgs().Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            var code = SelfTest.Run();
            Environment.Exit(code);
            return;
        }

        if (Environment.GetCommandLineArgs().Any(a => a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase)))
        {
            AmdReplay.DryRun = true;
            Log.Info("*** dry-run 模式：不会真正修改 AMD 即时重放状态 ***");
        }

        if (Environment.GetCommandLineArgs().Any(a => a.Equals("--verbose", StringComparison.OrdinalIgnoreCase)))
        {
            GameDetector.Verbose = true;
            Log.Info("*** verbose 模式：每轮检测都会记录 ***");
        }

        var state = AppState.Current;
        state.Load();
        Log.Info($"配置载入：白名单 {state.Config.Games.Count} 条，轮询 {state.Config.PollIntervalSeconds}s / 开启 {state.Config.EnableDelaySeconds}s / 关闭 {state.Config.DisableDelaySeconds}s");
        foreach (var g in state.Config.Games)
            Log.Info($"  条目：{g.Name} | 进程名={g.ProcessName} | 路径={g.ExePath} | 启用={g.Enabled}");

        _window = new MainWindow();
        _window.Activate();

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "replaymate.ico");
        state.Tray.ReadyChanged += ok =>
        {
            state.SetTrayAvailable(ok);
            if (ok)
            {
                Log.Info("托盘就绪");
            }
            else
            {
                Log.Warn("托盘不可用，窗口关闭将退化为退出程序");
                if (autostartRequested)
                {
                    // 自启模式下没有托盘会让程序不可见，必须把窗口显示出来
                    _window.DispatcherQueue.TryEnqueue(() => _window.ShowFromTray());
                }
            }
        };
        state.Tray.Start(iconPath);
        state.Tray.OpenRequested += () => _window.DispatcherQueue.TryEnqueue(() => _window.ShowFromTray());
        state.Tray.TogglePauseRequested += () => _window.DispatcherQueue.TryEnqueue(() =>
        {
            AppState.Current.TogglePause();
            _window.RefreshAll();
        });
        state.Tray.ExitRequested += () => _window.DispatcherQueue.TryEnqueue(() => _window.ExitApplication());

        state.SetStatus("空闲");
        state.StartDetection();

        if (autostartRequested)
        {
            Log.Info("以开机自启模式启动，隐藏主窗口");
            _window.HideToTray();
        }

        if (!state.Config.FirstRunDone)
            _ = _window.ShowFirstRunGuideAsync();
    }
}
