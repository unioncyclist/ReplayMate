using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReplayMate.Services;
using Windows.Graphics;
using Windows.UI;

namespace ReplayMate;

public sealed partial class MainWindow : Window
{
    private bool _forceClose;

    public MainWindow()
    {
        InitializeComponent();

        AppWindow.Resize(new SizeInt32(900, 640));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var tb = AppWindow.TitleBar;
        tb.ButtonBackgroundColor = Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Colors.Transparent;
        tb.ButtonForegroundColor = Colors.White;
        tb.ButtonInactiveForegroundColor = Color.FromArgb(255, 120, 125, 135);
        tb.ButtonHoverBackgroundColor = Color.FromArgb(30, 255, 255, 255);

        AppWindow.Closing += (_, e) =>
        {
            if (_forceClose) return;

            // 没有托盘图标时不能隐藏窗口，否则用户无法再把它叫回来
            if (!AppState.Current.TrayAvailable)
            {
                _forceClose = true;
                Log.Info("托盘不可用，关闭窗口即退出程序");
                AppState.Current.Shutdown();
                return;
            }

            e.Cancel = true;
            HideToTray();
        };

        Closed += (_, _) => AppState.Current.Shutdown();

        TrayRetryButton = new Button { Content = "重试" };
        TrayRetryButton.Click += (_, _) =>
        {
            var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "replaymate.ico");
            Log.Info("用户请求重建托盘图标");
            AppState.Current.Tray.Restart(icon);
        };
        TrayWarning.ActionButton = TrayRetryButton;

        AppState.Current.TrayAvailabilityChanged += UpdateTrayState;
        UpdateTrayState();
    }

    private Button TrayRetryButton { get; }

    private void UpdateTrayState()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var ok = AppState.Current.TrayAvailable;
            TrayWarning.IsOpen = !ok;

            if (ok) return;

            TrayWarning.Title = "托盘图标不可用";
            TrayWarning.Message =
                "当前启动方式的权限低于任务栏（错误 5：拒绝访问），托盘无法创建。" +
                "请关闭本程序，然后在文件资源管理器里双击 ReplayMate.exe。" +
                "在此模式下，关闭窗口会直接退出程序。";
        });
    }

    public void HideToTray()
    {
        AppWindow.Hide();
        Log.Info("主窗口已隐藏到托盘");
    }

    public void ShowFromTray()
    {
        AppWindow.Show();
        Activate();
        RefreshAll();
    }

    public void RefreshAll()
    {
        try
        {
            GamesView.Reload();
            SettingsView.Reload();
            LogsView.Reload();
        }
        catch (Exception ex)
        {
            Log.Warn($"刷新界面失败：{ex.Message}");
        }
    }

    public void ExitApplication()
    {
        _forceClose = true;
        Log.Info("用户从托盘退出");
        AppState.Current.Shutdown();
        Close();
        Application.Current.Exit();
    }

    public async Task ShowFirstRunGuideAsync()
    {
        await Task.Delay(800);

        var state = AppState.Current;
        var amdRunning = AmdReplay.IsServiceRunning();
        var regOk = AmdReplay.IsRegistryAvailable();

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = "欢迎使用 ReplayMate",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = "我会在你打开白名单里的游戏时自动开启 AMD 即时重放，退出游戏后延迟关闭。",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8
        });

        panel.Children.Add(new TextBlock
        {
            Text = regOk
                ? $"健康检查：AMD 注册表键正常（缓冲 {AmdReplay.ReadDurationSeconds() ?? 0} 秒）"
                : "健康检查：未找到 AMD 注册表键，可能没有安装 AMD Software。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                regOk ? Color.FromArgb(255, 120, 200, 140) : Color.FromArgb(255, 235, 120, 100))
        });

        panel.Children.Add(new TextBlock
        {
            Text = amdRunning
                ? "AMD Software 正在运行，随时可用。"
                : "AMD Software 当前没有运行。请先打开它，否则 ReplayMate 无法控制即时重放。\n建议把 AMD Software 也设为开机自启。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                amdRunning ? Color.FromArgb(255, 120, 200, 140) : Color.FromArgb(255, 235, 160, 100))
        });

        panel.Children.Add(new TextBlock
        {
            Text = "下一步：在「游戏」页点「扫描 Steam / Epic」，或「添加运行中的游戏」。",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7
        });

        var dialog = new ContentDialog
        {
            Title = "首次设置",
            Content = new ScrollViewer { Content = panel, MaxHeight = 420 },
            PrimaryButtonText = "开始使用",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark
        };

        try { await dialog.ShowAsync(); } catch { }

        state.Config.FirstRunDone = true;
        state.Save();
    }
}
