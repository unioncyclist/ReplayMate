using System.Text.Encodings.Web;
using System.Text.Json;

namespace ReplayMate.Services;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    public string Path { get; }

    public ConfigStore()
    {
        Path = System.IO.Path.Combine(AppContext.BaseDirectory, "config.json");
    }

    public AppConfig Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var json = File.ReadAllText(Path);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, Options);
                if (cfg is not null)
                {
                    cfg.Games ??= new List<GameEntry>();
                    Sanitize(cfg);
                    return cfg;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"读取配置失败，使用默认值：{ex.Message}");
        }

        return new AppConfig();
    }

    public bool Save(AppConfig config)
    {
        try
        {
            Sanitize(config);
            var json = JsonSerializer.Serialize(config, Options);
            File.WriteAllText(Path, json);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"保存配置失败：{ex.Message}");
            return false;
        }
    }

    private static void Sanitize(AppConfig cfg)
    {
        cfg.PollIntervalSeconds = Math.Clamp(cfg.PollIntervalSeconds, 1, 60);
        cfg.EnableDelaySeconds = Math.Clamp(cfg.EnableDelaySeconds, 0, 300);
        cfg.DisableDelaySeconds = Math.Clamp(cfg.DisableDelaySeconds, 0, 600);
        cfg.Games.RemoveAll(g => string.IsNullOrWhiteSpace(g.Name));
    }
}
