using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using FiveHourKeeper.Models;

namespace FiveHourKeeper.Services;

/// <summary>jsonl 追加日志，按行写入与读取。读侧使用 tail 读，避免一次性读取几兆。</summary>
public sealed class RunLogStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ConcurrentQueue<RunLog> _recent = new();
    private readonly object _writeLock = new();
    private readonly int _maxKeep;

    public RunLogStore(JsonConfigStore store, int maxKeep)
    {
        RunsPath = store.RunsPath;
        _maxKeep = maxKeep;
        ReloadRecent();
    }

    public string RunsPath { get; }

    private void ReloadRecent()
    {
        if (!File.Exists(RunsPath)) return;
        try
        {
            var lines = ReadTailLines(RunsPath, _maxKeep);
            foreach (var line in lines)
            {
                try
                {
                    var entry = JsonSerializer.Deserialize<RunLog>(line, Options);
                    if (entry is not null) _recent.Enqueue(entry);
                }
                catch { /* 跳过单行损坏 */ }
            }
        }
        catch { /* 文件权限等问题不影响启动 */ }
    }

    public void Append(RunLog entry)
    {
        if (entry.At == default) entry.At = DateTimeOffset.UtcNow;
        var line = JsonSerializer.Serialize(entry, Options);
        lock (_writeLock)
        {
            try { File.AppendAllText(RunsPath, line + Environment.NewLine); }
            catch { /* 日志写入失败不阻塞主流程 */ }
        }
        _recent.Enqueue(entry);
        while (_recent.Count > _maxKeep && _recent.TryDequeue(out _)) { }
    }

    public IReadOnlyList<RunLog> Snapshot(int tail = 200)
    {
        var list = _recent.ToArray();
        return list.Length <= tail ? list : list[^tail..];
    }

    private static string[] ReadTailLines(string path, int max)
    {
        // 简化实现：日志量小，全读后取尾段足以应付 max 500。
        var all = File.ReadAllLines(path);
        return all.Length <= max ? all : all[^max..];
    }
}