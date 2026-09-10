using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using FiveHourKeeper.Models;

namespace FiveHourKeeper.Services;

/// <summary>config.json 的原子化读写与版本迁移。每次写入生成 .bak，写入失败时回滚。</summary>
public sealed class JsonConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ConfigPath { get; }
    public string RunsPath { get; }

    public JsonConfigStore(string? overrideDir = null)
    {
        var dir = overrideDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FiveHourKeeper");
        Directory.CreateDirectory(dir);
        ConfigPath = Path.Combine(dir, "config.json");
        RunsPath = Path.Combine(dir, "runs.jsonl");
    }

    public AppConfig Load()
    {
        if (!File.Exists(ConfigPath)) return new AppConfig();
        try
        {
            var json = File.ReadAllText(ConfigPath);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json, Options) ?? new AppConfig();
            Migrate(cfg);
            return cfg;
        }
        catch (Exception)
        {
            // 配置损坏：尝试回滚 .bak，再不行就交给用户手动处理。
            var bak = ConfigPath + ".bak";
            if (File.Exists(bak))
            {
                try { return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(bak), Options) ?? new AppConfig(); }
                catch { /* ignore */ }
            }
            return new AppConfig();
        }
    }

    public void Save(AppConfig cfg)
    {
        cfg.Version = 1;
        var json = JsonSerializer.Serialize(cfg, Options);
        var tmp = ConfigPath + ".tmp";
        var bak = ConfigPath + ".bak";

        File.WriteAllText(tmp, json);
        if (File.Exists(ConfigPath))
        {
            try { File.Replace(tmp, ConfigPath, bak, ignoreMetadataErrors: true); }
            catch (FileNotFoundException)
            {
                File.Move(tmp, ConfigPath);
            }
        }
        else
        {
            File.Move(tmp, ConfigPath);
        }
    }

    private static void Migrate(AppConfig cfg)
    {
        // 单一版本，保留扩展点以备将来。
        if (cfg.Global.DefaultPrompt is null) cfg.Global.DefaultPrompt = "仅回复1";
        foreach (var m in cfg.Models)
        {
            if (string.IsNullOrWhiteSpace(m.Id)) m.Id = Guid.NewGuid().ToString("N");
            if (m.FetchedModels == null) m.FetchedModels = new List<string>();
            if (m.Schedules == null) m.Schedules = new List<ResetSchedule>();
        }
    }
}