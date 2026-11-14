using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ReplayMate.Services;

public sealed record LibraryGame(string Name, string InstallDir, string Source);

public static class GameLibraries
{
    /// <summary>扫描 Steam 全部库 + Epic 已安装清单。</summary>
    public static List<LibraryGame> ScanAll(out string report)
    {
        var games = new List<LibraryGame>();
        var lines = new List<string>();

        try
        {
            var steam = ScanSteam();
            games.AddRange(steam);
            lines.Add($"Steam：{steam.Count} 个");
        }
        catch (Exception ex)
        {
            lines.Add($"Steam 扫描失败：{ex.Message}");
            Log.Warn($"Steam 扫描失败：{ex.Message}");
        }

        try
        {
            var epic = ScanEpic();
            games.AddRange(epic);
            lines.Add($"Epic：{epic.Count} 个");
        }
        catch (Exception ex)
        {
            lines.Add($"Epic 扫描失败：{ex.Message}");
            Log.Warn($"Epic 扫描失败：{ex.Message}");
        }

        report = string.Join("；", lines);
        Log.Info("游戏库扫描完成：" + report);
        return games.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<LibraryGame> ScanSteam()
    {
        var result = new List<LibraryGame>();

        string? steamPath = null;
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            steamPath = k?.GetValue("SteamPath") as string;
        }
        catch { }

        if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath))
            return result;

        var libraries = new List<string> { steamPath.Replace('/', '\\') };

        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
            {
                var p = m.Groups[1].Value.Replace("\\\\", "\\");
                if (Directory.Exists(p) && !libraries.Contains(p, StringComparer.OrdinalIgnoreCase))
                    libraries.Add(p);
            }
        }

        foreach (var lib in libraries)
        {
            var appsDir = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(appsDir)) continue;

            foreach (var acf in Directory.EnumerateFiles(appsDir, "appmanifest_*.acf"))
            {
                try
                {
                    var text = File.ReadAllText(acf);
                    var name = Regex.Match(text, "\"name\"\\s+\"([^\"]*)\"").Groups[1].Value;
                    var installDir = Regex.Match(text, "\"installdir\"\\s+\"([^\"]*)\"").Groups[1].Value;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installDir)) continue;

                    // 过滤运行库/工具类条目
                    if (Regex.IsMatch(name, "Redistributable|Proton|Steamworks|Soundtrack|SDK|Dedicated Server",
                            RegexOptions.IgnoreCase)) continue;

                    var full = Path.Combine(appsDir, "common", installDir);
                    if (!Directory.Exists(full)) continue;

                    result.Add(new LibraryGame(name, full, "steam"));
                }
                catch { }
            }
        }

        return result;
    }

    public static List<LibraryGame> ScanEpic()
    {
        var result = new List<LibraryGame>();
        var manifest = @"C:\ProgramData\Epic\UnrealEngineLauncher\LauncherInstalled.dat";
        if (!File.Exists(manifest)) return result;

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifest));
        if (!doc.RootElement.TryGetProperty("InstallationList", out var list)) return result;

        foreach (var item in list.EnumerateArray())
        {
            try
            {
                var loc = item.TryGetProperty("InstallLocation", out var l) ? l.GetString() : null;
                if (string.IsNullOrWhiteSpace(loc) || !Directory.Exists(loc)) continue;

                var name = Path.GetFileName(loc.TrimEnd('\\', '/'));
                if (string.IsNullOrWhiteSpace(name)) continue;

                result.Add(new LibraryGame(name, loc, "epic"));
            }
            catch { }
        }

        return result;
    }
}
