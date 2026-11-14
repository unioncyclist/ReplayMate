using System.Diagnostics;

namespace ReplayMate.Services;

public sealed record ProcInfo(int Pid, string Name, string? Path, string? Title);

public static class ProcessService
{
    public static bool IsProcessRunning(string name)
    {
        try
        {
            var procs = Process.GetProcessesByName(name);
            var any = procs.Length > 0;
            foreach (var p in procs) p.Dispose();
            return any;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>轻量快照：只取进程名与可执行路径（路径取不到时为 null）。</summary>
    public static List<ProcInfo> Snapshot()
    {
        var list = new List<ProcInfo>(256);
        Process[] all;
        try { all = Process.GetProcesses(); }
        catch { return list; }

        foreach (var p in all)
        {
            try
            {
                string? path = null;
                try { path = p.MainModule?.FileName; } catch { }
                list.Add(new ProcInfo(p.Id, p.ProcessName, path, null));
            }
            catch { }
            finally { p.Dispose(); }
        }

        return list;
    }

    /// <summary>带窗口标题的进程，用于“从运行中的进程添加”。</summary>
    public static List<ProcInfo> WithWindows()
    {
        var list = new List<ProcInfo>();
        Process[] all;
        try { all = Process.GetProcesses(); }
        catch { return list; }

        foreach (var p in all)
        {
            try
            {
                var title = p.MainWindowTitle;
                if (string.IsNullOrWhiteSpace(title)) continue;

                string? path = null;
                try { path = p.MainModule?.FileName; } catch { }

                // 读取占位窗口的进程跳过
                if (string.IsNullOrEmpty(path)) continue;

                list.Add(new ProcInfo(p.Id, p.ProcessName, path, title));
            }
            catch { }
            finally { p.Dispose(); }
        }

        return list.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>通过 WMI 批量取命令行，仅在需要命令行匹配时调用。</summary>
    public static Dictionary<int, string> CommandLines()
    {
        var result = new Dictionary<int, string>();
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT ProcessId, CommandLine FROM Win32_Process");
            foreach (var obj in searcher.Get())
            {
                try
                {
                    var pid = Convert.ToInt32(obj["ProcessId"]);
                    var cmd = obj["CommandLine"] as string;
                    if (!string.IsNullOrEmpty(cmd)) result[pid] = cmd;
                }
                catch { }
                finally { obj.Dispose(); }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"读取命令行失败：{ex.Message}");
        }
        return result;
    }
}
