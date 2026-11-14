using ReplayMate.Interop;
using ReplayMate.Services;

namespace ReplayMate;

public sealed class AppState
{
    public static AppState Current { get; } = new();

    public ConfigStore Store { get; } = new();
    public AppConfig Config { get; private set; } = new();
    public GameDetector Detector { get; }
    public TrayIcon Tray { get; } = new();

    public event Action? StatusChanged;
    public event Action? TrayAvailabilityChanged;

    public bool TrayAvailable { get; private set; } = true;

    public void SetTrayAvailable(bool available)
    {
        TrayAvailable = available;
        try { TrayAvailabilityChanged?.Invoke(); } catch { }
    }

    public string StatusText { get; private set; } = "空闲";

    private AppState()
    {
        Detector = new GameDetector(() => Config, SetStatus);
        Detector.Notify += payload =>
        {
            var parts = payload.Split('\u0001');
            var title = parts.Length > 0 ? parts[0] : "ReplayMate";
            var body = parts.Length > 1 ? parts[1] : "";
            Tray.Notify(title, body, warning: true);
        };
    }

    public void Load()
    {
        Config = Store.Load();
        if (Config.Paused)
            SetStatus("已暂停");
    }

    public bool Save()
    {
        var ok = Store.Save(Config);
        return ok;
    }

    public void SetStatus(string text)
    {
        StatusText = text;
        try { StatusChanged?.Invoke(); } catch { }
        Tray.Snapshot = new TraySnapshot(text, Config.Paused, AmdReplay.IsServiceRunning());
        Tray.UpdateTip($"ReplayMate · {text}");
    }

    public void StartDetection()
    {
        if (Config.Paused) return;
        Detector.Start();
    }

    public void TogglePause()
    {
        Config.Paused = !Config.Paused;

        if (Config.Paused)
        {
            Detector.Stop();
            Tray.Snapshot = new TraySnapshot("已暂停", true, AmdReplay.IsServiceRunning());
        }
        else
        {
            Detector.Start();
            SetStatus("空闲");
        }

        Save();
        Log.Info(Config.Paused ? "用户暂停了自动化" : "用户恢复了自动化");
    }

    public void Shutdown()
    {
        try { Detector.Stop(); } catch { }
        try { Tray.Dispose(); } catch { }
    }
}
