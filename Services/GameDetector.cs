namespace ReplayMate.Services;

public enum DetectorState
{
    Idle,
    PendingEnable,
    Active,
    PendingDisable
}

/// <summary>
/// 白名单检测状态机：
/// 进入游戏 → 等待 EnableDelay → 写入开 → 退出游戏 → 等待 DisableDelay → 写入关。
/// 只在状态切换时写一次注册表，中途不干预用户手动操作。
/// </summary>
public sealed class GameDetector
{
    private readonly Func<AppConfig> _config;
    private readonly Action<string> _setStatus;

    private CancellationTokenSource? _cts;
    private DetectorState _state = DetectorState.Idle;
    private GameEntry? _activeGame;
    private DateTime _pendingSince = DateTime.MinValue;
    private DateTime _lastNotify = DateTime.MinValue;

    public event Action<string>? Notify;

    public static bool Verbose { get; set; }

    public GameDetector(Func<AppConfig> config, Action<string> setStatus)
    {
        _config = config;
        _setStatus = setStatus;
    }

    public bool IsRunning => _cts is { IsCancellationRequested: false };

    public DetectorState State => _state;
    public string? ActiveGameName => _activeGame?.Name;

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => LoopAsync(_cts.Token));
        Log.Info("检测循环已启动");
    }

    /// <summary>硬暂停：停止监控，不写任何注册表。</summary>
    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        _cts = null;
        _state = DetectorState.Idle;
        _activeGame = null;
        _setStatus("已暂停");
        Log.Info("检测循环已停止（暂停自动化）");
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var cfg = _config();
            try
            {
                Tick(cfg);
            }
            catch (Exception ex)
            {
                Log.Error($"检测循环异常：{ex.Message}");
            }

            var delay = TimeSpan.FromSeconds(Math.Clamp(cfg.PollIntervalSeconds, 1, 60));
            try { await Task.Delay(delay, token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void Tick(AppConfig cfg)
    {
        var procs = ProcessService.Snapshot();

        Dictionary<int, string>? cmdLines = null;
        if (cfg.Games.Any(g => g.Enabled && !string.IsNullOrWhiteSpace(g.CommandLineKeyword)))
            cmdLines = ProcessService.CommandLines();

        var running = FindRunning(cfg, procs, cmdLines);
        var now = DateTime.Now;

        if (Verbose)
        {
            var suspects = procs
                .Where(p => p.Name.Contains("PIN", StringComparison.OrdinalIgnoreCase))
                .Select(p => $"{p.Name}({p.Path ?? "null"})")
                .Take(5);
            Log.Info($"[tick] 进程={procs.Count} 疑似=[{string.Join(", ", suspects)}] 匹配={(running?.Name ?? "无")} 状态={_state}");
        }

        switch (_state)
        {
            case DetectorState.Idle:
                if (running is not null)
                {
                    _state = DetectorState.PendingEnable;
                    _activeGame = running;
                    _pendingSince = now;
                    Log.Info($"检测到游戏「{running.Name}」，{cfg.EnableDelaySeconds} 秒后开启即时重放");
                    _setStatus($"等待开启：{running.Name}");
                }
                break;

            case DetectorState.PendingEnable:
                if (running is null)
                {
                    _state = DetectorState.Idle;
                    _activeGame = null;
                    Log.Info("游戏在延迟期内退出，取消开启");
                    _setStatus("空闲");
                }
                else if ((now - _pendingSince).TotalSeconds >= cfg.EnableDelaySeconds)
                {
                    if (TryWrite(true, cfg))
                    {
                        _state = DetectorState.Active;
                        _activeGame = running;
                        _setStatus($"录制中：{running.Name}");
                    }
                    else
                    {
                        _state = DetectorState.Idle;
                        _activeGame = null;
                        _setStatus("空闲（开启失败）");
                    }
                }
                break;

            case DetectorState.Active:
                if (running is null)
                {
                    _state = DetectorState.PendingDisable;
                    _pendingSince = now;
                    Log.Info($"游戏「{_activeGame?.Name}」已退出，{cfg.DisableDelaySeconds} 秒后关闭即时重放");
                    _setStatus($"等待关闭：{_activeGame?.Name}");
                }
                else if (running.Name != _activeGame?.Name)
                {
                    Log.Info($"切换到另一款游戏「{running.Name}」，保持开启");
                    _activeGame = running;
                    _setStatus($"录制中：{running.Name}");
                }
                break;

            case DetectorState.PendingDisable:
                if (running is not null)
                {
                    _state = DetectorState.Active;
                    _activeGame = running;
                    Log.Info($"延迟期内又检测到「{running.Name}」，取消关闭");
                    _setStatus($"录制中：{running.Name}");
                }
                else if ((now - _pendingSince).TotalSeconds >= cfg.DisableDelaySeconds)
                {
                    TryWrite(false, cfg);
                    _state = DetectorState.Idle;
                    _activeGame = null;
                    _setStatus("空闲");
                }
                break;
        }
    }

    private bool TryWrite(bool enable, AppConfig cfg)
    {
        if (!AmdReplay.DryRun && !AmdReplay.IsServiceRunning())
        {
            Log.Warn("AMDRSServ 未运行，跳过写入（需要先打开 AMD Software）");
            _state = DetectorState.Idle;
            NotifyThrottled("ReplayMate", "AMD Software 没有运行，已跳过本次操作。请先打开 AMD Software。");
            return false;
        }

        var ok = AmdReplay.Write(enable);
        if (!ok)
            NotifyThrottled("ReplayMate", enable ? "开启即时重放失败，详见日志。" : "关闭即时重放失败，详见日志。");

        return ok;
    }

    private void NotifyThrottled(string title, string message)
    {
        if ((DateTime.Now - _lastNotify).TotalMinutes < 5) return;
        _lastNotify = DateTime.Now;
        try { Notify?.Invoke($"{title}\u0001{message}"); } catch { }
    }

    internal static GameEntry? FindRunning(AppConfig cfg, List<ProcInfo> procs, Dictionary<int, string>? cmdLines)
    {
        GameEntry? best = null;
        var bestScore = 0;

        foreach (var g in cfg.Games)
        {
            if (!g.Enabled) continue;

            foreach (var p in procs)
            {
                var score = MatchScore(g, p, cmdLines);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = g;
                    if (score >= 4) return best;
                }
            }
        }

        return best;
    }

    /// <summary>评分：完整路径=4，安装目录=3，命令行关键词=3，进程名兜底=2。</summary>
    internal static int MatchScore(GameEntry g, ProcInfo p, Dictionary<int, string>? cmdLines)
    {
        var score = 0;

        if (!string.IsNullOrWhiteSpace(g.CommandLineKeyword))
        {
            if (cmdLines is null || !cmdLines.TryGetValue(p.Pid, out var cmd)) return 0;
            if (cmd.IndexOf(g.CommandLineKeyword!, StringComparison.OrdinalIgnoreCase) < 0) return 0;
            score = 3;
        }

        if (!string.IsNullOrWhiteSpace(g.ExePath))
        {
            if (p.Path is not null && string.Equals(p.Path, g.ExePath, StringComparison.OrdinalIgnoreCase))
                return 4;
            // 路径不匹配时退回进程名
            if (!string.IsNullOrWhiteSpace(g.ProcessName) &&
                string.Equals(p.Name, g.ProcessName, StringComparison.OrdinalIgnoreCase))
                score = Math.Max(score, 2);
            else if (score < 3)
                return 0;
        }

        if (!string.IsNullOrWhiteSpace(g.InstallDir))
        {
            if (p.Path is not null && p.Path.StartsWith(g.InstallDir!, StringComparison.OrdinalIgnoreCase))
                return Math.Max(score, 3);
        }

        if (!string.IsNullOrWhiteSpace(g.ProcessName) &&
            string.Equals(p.Name, g.ProcessName, StringComparison.OrdinalIgnoreCase))
            score = Math.Max(score, 2);

        return score;
    }
}
