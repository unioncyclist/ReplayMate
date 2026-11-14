using System.Text.Json.Serialization;

namespace ReplayMate;

public sealed class AppConfig
{
    public int PollIntervalSeconds { get; set; } = 5;
    public int EnableDelaySeconds { get; set; } = 10;
    public int DisableDelaySeconds { get; set; } = 60;
    public bool Autostart { get; set; }
    public bool Paused { get; set; }
    public bool FirstRunDone { get; set; }
    public List<GameEntry> Games { get; set; } = new();
}

public sealed class GameEntry
{
    public string Name { get; set; } = "";

    /// <summary>进程名，不含扩展名比较时忽略大小写。</summary>
    public string? ProcessName { get; set; }

    /// <summary>完整 exe 路径，优先级最高。</summary>
    public string? ExePath { get; set; }

    /// <summary>安装目录前缀，用于 Steam/Epic 导入的游戏。</summary>
    public string? InstallDir { get; set; }

    /// <summary>命令行必须包含的关键词，用于 javaw.exe 这类共用宿主进程。</summary>
    public string? CommandLineKeyword { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>manual / running / steam / epic</summary>
    public string Source { get; set; } = "manual";

    [JsonIgnore]
    public string MatchDescription
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(CommandLineKeyword)) return "命令行含 " + CommandLineKeyword;
            if (!string.IsNullOrWhiteSpace(ExePath)) return "路径";
            if (!string.IsNullOrWhiteSpace(InstallDir)) return "安装目录";
            if (!string.IsNullOrWhiteSpace(ProcessName)) return "进程名 " + ProcessName;
            return "未配置";
        }
    }

    [JsonIgnore]
    public string SourceText => Source switch
    {
        "steam" => "Steam",
        "epic" => "Epic",
        "running" => "运行中进程",
        _ => "手动"
    };
}
