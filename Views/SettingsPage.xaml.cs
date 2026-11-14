using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReplayMate.Services;

namespace ReplayMate.Views;

public sealed partial class SettingsPage : UserControl
{
    private bool _loading;

    public SettingsPage()
    {
        InitializeComponent();
        Reload();
    }

    public void Reload()
    {
        _loading = true;
        try
        {
            var state = AppState.Current;
            var cfg = state.Config;

            PollBox.Value = cfg.PollIntervalSeconds;
            EnableDelayBox.Value = cfg.EnableDelaySeconds;
            DisableDelayBox.Value = cfg.DisableDelaySeconds;

            var autostart = AutostartService.IsEnabled();
            AutostartSwitch.IsOn = autostart;
            AutostartHint.Text = autostart
                ? "启动项已存在：登录后自动启动并隐藏到托盘。移动程序目录后请关掉再打开一次以重建快捷方式。"
                : "未设置启动项。打开开关会立即在「启动」文件夹创建快捷方式。";

            PauseButton.Content = cfg.Paused ? "恢复自动化" : "暂停自动化";
            CurrentStatusText.Text = $"当前状态：{state.StatusText}";
        }
        finally
        {
            _loading = false;
        }

        RefreshAmd();
    }

    private void RefreshAmd()
    {
        var regOk = AmdReplay.IsRegistryAvailable();
        var serv = AmdReplay.IsServiceRunning();
        var software = AmdReplay.IsAmdSoftwareRunning();
        var enabled = AmdReplay.ReadEnabled();
        var duration = AmdReplay.ReadDurationSeconds();

        if (!regOk)
        {
            AmdStatusText.Text = "未找到 AMD 注册表键，可能没有安装 AMD Software。";
            return;
        }

        var parts = new List<string>
        {
            serv ? "AMDRSServ 正在运行" : "AMDRSServ 未运行",
            software ? "AMD Software 正在运行" : "AMD Software 未运行",
            enabled is null ? "即时重放状态未知" : (enabled.Value ? "即时重放当前是开启的" : "即时重放当前是关闭的"),
            duration is null ? "缓冲时长未知" : $"缓冲时长 {duration} 秒"
        };

        AmdStatusText.Text = string.Join("　|　", parts);

        if (!serv)
            AmdStatusText.Text += "\n提示：请先打开 AMD Software，否则 ReplayMate 无法控制即时重放。";
    }

    private void RefreshAmd_Click(object sender, RoutedEventArgs e) => RefreshAmd();

    private void PollBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => ApplyNumbers();

    private void EnableDelayBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => ApplyNumbers();

    private void DisableDelayBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => ApplyNumbers();

    private void ApplyNumbers()
    {
        if (_loading) return;

        var state = AppState.Current;
        var cfg = state.Config;

        var poll = ToInt(PollBox.Value, cfg.PollIntervalSeconds, 1, 60);
        var enable = ToInt(EnableDelayBox.Value, cfg.EnableDelaySeconds, 0, 300);
        var disable = ToInt(DisableDelayBox.Value, cfg.DisableDelaySeconds, 0, 600);

        if (poll == cfg.PollIntervalSeconds && enable == cfg.EnableDelaySeconds && disable == cfg.DisableDelaySeconds)
            return;

        cfg.PollIntervalSeconds = poll;
        cfg.EnableDelaySeconds = enable;
        cfg.DisableDelaySeconds = disable;
        state.Save();

        // 让新参数立刻生效
        if (!cfg.Paused)
        {
            state.Detector.Stop();
            state.Detector.Start();
        }

        Log.Info($"参数即时生效：轮询 {poll}s / 开启 {enable}s / 关闭 {disable}s");
    }

    private void AutostartSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        var want = AutostartSwitch.IsOn;
        var ok = want ? AutostartService.Enable() : AutostartService.Disable();

        if (!ok)
        {
            _loading = true;
            AutostartSwitch.IsOn = !want;
            _loading = false;
            AutostartHint.Text = "启动项创建/删除失败，详见日志。常见原因是程序以低权限（沙箱）方式启动。";
            Log.Error(want ? "开机自启启用失败" : "开机自启关闭失败");
            return;
        }

        var state = AppState.Current;
        state.Config.Autostart = want;
        state.Save();

        AutostartHint.Text = want
            ? "启动项已创建：登录后自动启动并隐藏到托盘。"
            : "启动项已删除，登录后不再自动启动。";
        Log.Info(want ? "已启用开机自启" : "已关闭开机自启");
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        AppState.Current.TogglePause();
        Reload();
    }

    private static int ToInt(double value, int fallback, int min, int max)
    {
        if (double.IsNaN(value)) return fallback;
        return Math.Clamp((int)Math.Round(value), min, max);
    }
}
