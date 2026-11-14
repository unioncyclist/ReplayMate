using System.Diagnostics;
using System.Runtime.InteropServices;
using ReplayMate.Services;

namespace ReplayMate.Interop;

/// <summary>
/// 单实例唤醒：第二个实例启动时，把已有实例被隐藏的窗口找回来。
/// 直接按进程枚举顶层窗口，不依赖窗口标题。
/// </summary>
public static class WindowLocator
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int SW_RESTORE = 9;
    private const int SW_SHOW = 5;

    public static bool ActivateExistingInstance()
    {
        var self = Environment.ProcessId;
        var other = Process.GetProcessesByName("ReplayMate")
            .Where(p => p.Id != self)
            .OrderBy(p => p.StartTime)
            .FirstOrDefault();

        if (other is null) return false;

        var target = IntPtr.Zero;
        try
        {
            EnumWindows((hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out var pid);
                if (pid != (uint)other.Id) return true;
                if (GetWindowTextLength(hWnd) == 0) return true;
                target = hWnd;
                return false;
            }, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Warn($"枚举窗口失败：{ex.Message}");
        }

        if (target == IntPtr.Zero)
        {
            Log.Warn("找到已有实例，但没有定位到它的窗口");
            return false;
        }

        ShowWindow(target, SW_RESTORE);
        ShowWindow(target, SW_SHOW);
        SetForegroundWindow(target);
        Log.Info("已唤醒已有实例的窗口");
        return true;
    }
}
