using Microsoft.Win32;

namespace ReplayMate.Services;

/// <summary>
/// AMD 即时重放的实时开关位于 HKCU\SOFTWARE\AMD\DVR\InstantReplayEnabled。
/// AMDRSServ.exe 订阅了该键的变化，写入后约 0.5-3 秒生效（已实测）。
/// </summary>
public static class AmdReplay
{
    private const string KeyPath = @"SOFTWARE\AMD\DVR";
    private const string ValueName = "InstantReplayEnabled";
    private const string DurationName = "InstantReplayDuration";

    public static bool IsRegistryAvailable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    public static bool? ReadEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            if (key is null) return null;
            var v = key.GetValue(ValueName);
            if (v is null) return null;
            return Convert.ToInt32(v) != 0;
        }
        catch (Exception ex)
        {
            Log.Error($"读取即时重放状态失败：{ex.Message}");
            return null;
        }
    }

    public static int? ReadDurationSeconds()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            if (key is null) return null;
            var v = key.GetValue(DurationName);
            if (v is null) return null;
            return Convert.ToInt32(v);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>是否在运行 AMDRSServ（Encoding Service）。</summary>
    public static bool IsServiceRunning()
        => ProcessService.IsProcessRunning("AMDRSServ");

    public static bool IsAmdSoftwareRunning()
        => ProcessService.IsProcessRunning("RadeonSoftware");

    /// <summary>干跑模式：只记录将要写入的值，不真正改注册表。</summary>
    public static bool DryRun { get; set; }

    /// <summary>写入目标状态；只有与当前值不同才写。返回是否成功。</summary>
    public static bool Write(bool enable)
    {
        if (DryRun)
        {
            Log.Info($"[dry-run] 本应写入 InstantReplayEnabled={(enable ? 1 : 0)}（未实际写入）");
            return true;
        }

        var current = ReadEnabled();
        if (current == enable)
        {
            Log.Info($"即时重放已是 {(enable ? "开启" : "关闭")} 状态，跳过写入");
            return true;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            if (key is null)
            {
                Log.Error(@"注册表键 HKCU\SOFTWARE\AMD\DVR 不存在，可能未安装 AMD 软件");
                return false;
            }

            key.SetValue(ValueName, enable ? 1 : 0, RegistryValueKind.DWord);

            // 回读校验
            var check = Convert.ToInt32(key.GetValue(ValueName) ?? -1) != 0;
            if (check != enable)
            {
                Log.Error($"写入即时重放={(enable ? 1 : 0)} 后回读不一致");
                return false;
            }

            Log.Info($"已写入即时重放 = {(enable ? 1 : 0)}（回读一致）");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"写入即时重放失败：{ex.Message}");
            return false;
        }
    }
}
