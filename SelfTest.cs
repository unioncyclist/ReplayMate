using System.Text;
using ReplayMate.Services;

namespace ReplayMate;

/// <summary>离线自检：用合成数据验证白名单匹配规则，不依赖真实进程表。</summary>
public static class SelfTest
{
    private static readonly StringBuilder Sb = new();
    private static int _failed;

    public static int Run()
    {
        Sb.Clear();
        _failed = 0;

        var procs = new List<ProcInfo>
        {
            new(100, "PING", @"C:\WINDOWS\System32\PING.EXE", null),
            new(200, "javaw", @"C:\Program Files\Java\jdk-17\bin\javaw.exe", null),
            new(300, "chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", null),
            new(400, "SomeGame", null, null),
            new(500, "game", @"D:\SteamLibrary\steamapps\common\EldenRing\eldenring.exe", null)
        };

        var cmds = new Dictionary<int, string>
        {
            [200] = @"javaw.exe -Djava.library.path=natives -cp .;libraries net.minecraft.client.main.Main --version 1.20.1",
            [300] = "chrome.exe --type=renderer"
        };

        // 1. 完整路径匹配（大小写不敏感）
        Case("路径精确匹配（大小写敏感差异）",
            Match(new GameEntry { Name = "PING", ProcessName = "PING", ExePath = @"C:\Windows\System32\PING.EXE" }, procs, cmds),
            "PING");

        // 2. 只有路径，进程名不同也能命中
        Case("仅路径匹配",
            Match(new GameEntry { Name = "ER", ExePath = @"D:\SteamLibrary\steamapps\common\EldenRing\eldenring.exe" }, procs, cmds),
            "ER");

        // 3. 安装目录前缀匹配
        Case("安装目录前缀匹配",
            Match(new GameEntry { Name = "SteamER", InstallDir = @"D:\SteamLibrary\steamapps\common\EldenRing" }, procs, cmds),
            "SteamER");

        // 4. 只有进程名，路径为空时兜底
        Case("进程名兜底（路径不可读）",
            Match(new GameEntry { Name = "SomeGame", ProcessName = "SomeGame" }, procs, cmds),
            "SomeGame");

        // 5. 命令行关键词（Minecraft 场景）
        Case("命令行关键词匹配 Minecraft",
            Match(new GameEntry { Name = "MC", ProcessName = "javaw", CommandLineKeyword = "net.minecraft.client.main.Main" }, procs, cmds),
            "MC");

        // 6. 命令行关键词不应误中普通 Java 程序
        Case("命令行关键词不误伤其它 Java",
            Match(new GameEntry { Name = "MC", ProcessName = "javaw", CommandLineKeyword = "net.minecraft.client.main.Main" },
                new List<ProcInfo> { new(201, "javaw", @"C:\Java\bin\javaw.exe", null) },
                new Dictionary<int, string> { [201] = "javaw -jar my-tool.jar" }),
            null);

        // 7. 路径不同且名字不同 → 不命中
        Case("路径/名字都不匹配则不命中",
            Match(new GameEntry { Name = "X", ProcessName = "notepad", ExePath = @"C:\Windows\notepad.exe" }, procs, cmds),
            null);

        // 8. 已停用的条目不应命中
        Case("停用条目被忽略",
            Match(new GameEntry { Name = "PING", ProcessName = "PING", Enabled = false }, procs, cmds),
            null);

        // 9. 多条目取最高分（路径优先于名字）
        var multi = new AppConfig
        {
            Games =
            {
                new GameEntry { Name = "ByName", ProcessName = "PING" },
                new GameEntry { Name = "ByPath", ExePath = @"C:\WINDOWS\System32\PING.EXE" }
            }
        };
        Case("多条目时路径优先", GameDetector.FindRunning(multi, procs, cmds)?.Name, "ByPath");

        var text = Sb.ToString();
        var summary = _failed == 0 ? "ALL PASS" : $"{_failed} FAILED";
        var result = $"=== ReplayMate 自检 {summary} ===\r\n{text}";

        var path = Path.Combine(AppContext.BaseDirectory, "selftest-result.txt");
        try { File.WriteAllText(path, result, Encoding.UTF8); } catch { }
        Log.Info(result.Replace("\r\n", " | "));

        return _failed == 0 ? 0 : 1;
    }

    private static string? Match(GameEntry entry, List<ProcInfo> procs, Dictionary<int, string> cmds)
    {
        var cfg = new AppConfig();
        cfg.Games.Add(entry);
        return GameDetector.FindRunning(cfg, procs, cmds)?.Name;
    }

    private static void Case(string name, string? actual, string? expected)
    {
        var ok = actual == expected;
        if (!ok) _failed++;
        Sb.AppendLine($"[{(ok ? "PASS" : "FAIL")}] {name}：期望={expected ?? "null"} 实际={actual ?? "null"}");
    }
}
