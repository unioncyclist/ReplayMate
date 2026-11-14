namespace ReplayMate.Services;

public static class AutostartService
{
    private const string ShortcutName = "ReplayMate.lnk";

    private static string ShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ShortcutName);

    public static bool IsEnabled()
    {
        try { return File.Exists(ShortcutPath); }
        catch { return false; }
    }

    public static bool Enable()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;
            var dir = AppContext.BaseDirectory;

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return false;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic lnk = shell.CreateShortcut(ShortcutPath);
            lnk.TargetPath = exe;
            lnk.Arguments = "--autostart";
            lnk.WorkingDirectory = dir;
            lnk.IconLocation = exe;
            lnk.Description = "ReplayMate - 游戏时自动开启 AMD 即时重放";
            lnk.Save();

            Log.Info($"已创建启动快捷方式：{ShortcutPath}");
            return File.Exists(ShortcutPath);
        }
        catch (Exception ex)
        {
            Log.Error($"创建开机自启失败：{ex.Message}");
            return false;
        }
    }

    public static bool Disable()
    {
        try
        {
            if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
            Log.Info("已移除开机自启快捷方式");
            return !File.Exists(ShortcutPath);
        }
        catch (Exception ex)
        {
            Log.Error($"移除开机自启失败：{ex.Message}");
            return IsEnabled();
        }
    }
}
