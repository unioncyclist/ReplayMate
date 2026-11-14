using System.Text;

namespace ReplayMate.Services;

public static class Log
{
    private static readonly object Gate = new();
    private static readonly List<string> Recent = new(600);
    private static string _dir = "";

    public static event Action<string>? LineAdded;

    public static IReadOnlyList<string> RecentLines
    {
        get { lock (Gate) return Recent.ToArray(); }
    }

    public static void Init()
    {
        _dir = System.IO.Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(_dir);
        Cleanup(TimeSpan.FromHours(48));
    }

    public static void Cleanup(TimeSpan keep)
    {
        try
        {
            var cutoff = DateTime.Now - keep;
            foreach (var f in Directory.EnumerateFiles(_dir, "*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(f) < cutoff)
                    {
                        File.Delete(f);
                        Append("INFO", $"清理过期日志 {System.IO.Path.GetFileName(f)}");
                    }
                }
                catch { /* 文件被占用则跳过 */ }
            }
        }
        catch (Exception ex)
        {
            Append("WARN", $"清理日志失败：{ex.Message}");
        }
    }

    public static void Info(string m) => Append("INFO", m);
    public static void Warn(string m) => Append("WARN", m);
    public static void Error(string m) => Append("ERROR", m);

    private static void Append(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";

        lock (Gate)
        {
            Recent.Add(line);
            if (Recent.Count > 500) Recent.RemoveRange(0, Recent.Count - 500);
        }

        try
        {
            if (string.IsNullOrEmpty(_dir))
            {
                _dir = System.IO.Path.Combine(AppContext.BaseDirectory, "logs");
                Directory.CreateDirectory(_dir);
            }

            var file = System.IO.Path.Combine(_dir, $"{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(file, line + Environment.NewLine, Encoding.UTF8);
        }
        catch { /* 日志失败不能影响主流程 */ }

        try { LineAdded?.Invoke(line); } catch { }
    }
}
