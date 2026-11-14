using System.Runtime.InteropServices;
using H.NotifyIcon;
using Microsoft.UI.Xaml.Media.Imaging;
using ReplayMate.Services;
using static ReplayMate.Interop.NativeMethods;

namespace ReplayMate.Interop;

public sealed record TraySnapshot(string StatusText, bool Paused, bool AmdRunning);

internal sealed class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Action _action;
    public RelayCommand(Action action) => _action = action;
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => _action();
}

/// <summary>
/// 托盘封装：图标与左键由 H.NotifyIcon.WinUI 提供；右键用 Win32 原生菜单，
/// 因为 MenuFlyout 依赖 XamlRoot，窗口隐藏时会弹不出来。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const string MenuHostClass = "ReplayMateMenuHost";
    private const int CmdPause = 1001;
    private const int CmdOpen = 1002;
    private const int CmdExit = 1003;

    private TaskbarIcon? _icon;
    private IntPtr _menuHost;
    private WndProcDelegate? _menuWndProc;

    public volatile TraySnapshot Snapshot = new("空闲", false, false);

    public bool IsReady { get; private set; }

    public event Action<bool>? ReadyChanged;
    public event Action? OpenRequested;
    public event Action? TogglePauseRequested;
    public event Action? ExitRequested;

    public void Start(string iconPath)
    {
        try
        {
            _icon = new TaskbarIcon
            {
                ToolTipText = "ReplayMate · 空闲",
                NoLeftClickDelay = true,
                LeftClickCommand = new RelayCommand(() => SafeRaise(OpenRequested)),
                RightClickCommand = new RelayCommand(ShowContextMenu)
            };

            try
            {
                _icon.IconSource = new BitmapImage(new Uri(iconPath));
            }
            catch (Exception ex)
            {
                Log.Warn($"加载托盘图标失败（将使用默认图标）：{ex.Message}");
            }

            _icon.ForceCreate();

            EnsureMenuHost();

            IsReady = _icon.IsCreated;
            if (IsReady) Log.Info("托盘图标已创建");
            else Log.Error("托盘图标创建后状态为未创建（ForceCreate 未生效）");
        }
        catch (Exception ex)
        {
            IsReady = false;
            Log.Error($"托盘创建失败：{ex.Message}");
            Log.Error("如果错误信息包含 拒绝访问(5)，说明进程完整性级别低于任务栏（沙箱/低权限启动）；" +
                      "请在文件资源管理器中双击 ReplayMate.exe。");
        }

        ReadyChanged?.Invoke(IsReady);
    }

    /// <summary>创建一个隐藏窗口作为原生菜单的属主（隐藏主窗口时菜单仍能正常弹出）。</summary>
    private void EnsureMenuHost()
    {
        if (_menuHost != IntPtr.Zero) return;

        _menuWndProc = (h, m, w, l) => DefWindowProc(h, m, w, l);

        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_menuWndProc),
            lpszClassName = MenuHostClass,
            hInstance = GetModuleHandle(null)
        };

        if (RegisterClassEx(ref wc) == 0)
        {
            var err = Marshal.GetLastWin32Error();
            if (err != 1410) // ERROR_CLASS_ALREADY_EXISTS
            {
                Log.Warn($"注册菜单窗口类失败，错误码 {err}");
                return;
            }
        }

        _menuHost = CreateWindowEx(WS_EX_TOOLWINDOW, MenuHostClass, "ReplayMateMenu", unchecked((int)0x80000000),
            0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);

        if (_menuHost == IntPtr.Zero)
            Log.Warn($"创建菜单窗口失败，错误码 {Marshal.GetLastWin32Error()}");
    }

    private void ShowContextMenu()
    {
        try
        {
            EnsureMenuHost();

            var snap = Snapshot;
            var menu = CreatePopupMenu();
            if (menu == IntPtr.Zero) return;

            try
            {
                AppendMenu(menu, MF_STRING | MF_GRAYED, (IntPtr)1, $"状态：{snap.StatusText}");
                AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, null);
                AppendMenu(menu, MF_STRING | (snap.Paused ? MF_CHECKED : 0), (IntPtr)CmdPause,
                    snap.Paused ? "恢复自动化" : "暂停自动化");
                AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, null);
                AppendMenu(menu, MF_STRING, (IntPtr)CmdOpen, "打开设置");
                AppendMenu(menu, MF_STRING, (IntPtr)CmdExit, "退出");

                GetCursorPos(out var pt);
                if (_menuHost != IntPtr.Zero) SetForegroundWindow(_menuHost);

                var cmd = TrackPopupMenu(menu,
                    TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY,
                    pt.X, pt.Y, 0, _menuHost, IntPtr.Zero);

                switch (cmd)
                {
                    case CmdPause: SafeRaise(TogglePauseRequested); break;
                    case CmdOpen: SafeRaise(OpenRequested); break;
                    case CmdExit: SafeRaise(ExitRequested); break;
                }
            }
            finally
            {
                DestroyMenu(menu);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"显示托盘菜单失败：{ex.Message}");
        }
    }

    public void UpdateTip(string text)
    {
        try
        {
            if (_icon is null) return;
            _icon.ToolTipText = text.Length > 120 ? text[..120] : text;
        }
        catch { }
    }

    public void Notify(string title, string message, bool warning = false)
    {
        try
        {
            _icon?.ShowNotification(title, message);
        }
        catch (Exception ex)
        {
            Log.Warn($"气泡通知失败：{ex.Message}");
        }
    }

    public void Restart(string iconPath)
    {
        try { _icon?.Dispose(); } catch { }
        _icon = null;
        IsReady = false;
        Start(iconPath);
    }

    private static void SafeRaise(Action? action)
    {
        try { action?.Invoke(); }
        catch (Exception ex) { Log.Error($"托盘回调异常：{ex.Message}"); }
    }

    public void Dispose()
    {
        try { _icon?.Dispose(); } catch { }
        _icon = null;

        if (_menuHost != IntPtr.Zero)
        {
            try { DestroyWindow(_menuHost); } catch { }
            _menuHost = IntPtr.Zero;
        }
    }
}
