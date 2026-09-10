using System.Collections.Concurrent;
using FiveHourKeeper.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FiveHourKeeper.Services;

/// <summary>
/// 后台调度服务：循环监听最近一个发送时刻；到点前 5 秒切换为秒级对齐。
/// 防重发核心：<see cref="_firedTargets"/> 记录每条调度最近一次已处理的 targetAt，
/// 同一个 targetAt 绝不二次触发（无论成功、失败、还是判定 Missed）。
/// </summary>
public sealed class SchedulerService : BackgroundService
{
    private readonly IModelRegistry _registry;
    private readonly IAiClientFactory _clientFactory;
    private readonly RunLogStore _log;
    private readonly INotifier _notifier;
    private readonly IClock _clock;
    private readonly ILogger<SchedulerService> _logger;
    private readonly AppConfig _config;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSuccessByModel = new();

    /// <summary>scheduleId → 已触发过的目标时刻。Daily 的 target 每天不同，天然滚动。</summary>
    private readonly ConcurrentDictionary<string, DateTimeOffset> _firedTargets = new();

    public SchedulerService(
        IModelRegistry registry,
        IAiClientFactory clientFactory,
        RunLogStore log,
        INotifier notifier,
        IClock clock,
        ILogger<SchedulerService> logger,
        ConfigRootHolder configHolder)
    {
        _registry = registry;
        _clientFactory = clientFactory;
        _log = log;
        _notifier = notifier;
        _clock = clock;
        _logger = logger;
        _config = configHolder.Current;
    }

    public DateTimeOffset? LastSuccessAt(string modelId) =>
        _lastSuccessByModel.TryGetValue(modelId, out var v) ? v : null;

    public IReadOnlyDictionary<string, DateTimeOffset?> LastSuccessSnapshot() =>
        _lastSuccessByModel.ToDictionary(kv => kv.Key, kv => (DateTimeOffset?)kv.Value);

    /// <summary>某条调度指定的 targetAt 是否已被处理过（供 UI 与测试观察）。</summary>
    public bool HasFired(string scheduleId, DateTimeOffset targetAtUtc) =>
        _firedTargets.TryGetValue(scheduleId, out var t) && t == targetAtUtc;

    /// <summary>
    /// 立即对单个模型发出一次真实请求（托盘菜单 / 手动触发）。
    /// 注意：这次请求同样会锚定 5 小时窗口，因此会与既有计划相互影响。
    /// </summary>
    public async Task<SendResult> SendNowAsync(
        ModelProfile profile,
        RunLogKind kind = RunLogKind.Manual,
        CancellationToken ct = default)
    {
        var prompt = ScheduleCalculator.EffectivePrompt(_config.Global.DefaultPrompt, profile.PromptOverride);
        var working = CloneWithPlainKey(profile);
        var client = _clientFactory.Create(working);

        var wasActive = ScheduleCalculator.StillInActiveWindow(LastSuccessAt(profile.Id), _clock.UtcNow);
        var result = await client.SendPingAsync(working, prompt, ct);

        if (result.Success) _lastSuccessByModel[profile.Id] = _clock.UtcNow;

        _log.Append(new RunLog
        {
            At = _clock.UtcNow,
            ModelId = profile.Id,
            ModelName = profile.Name,
            Kind = kind,
            Status = result.Success ? RunLogStatus.Success : RunLogStatus.Failed,
            Message = result.Success
                ? (wasActive
                    ? $"{result.ResponseSnippet}（注意：上一窗口仍活跃，本次未产生新锚点）"
                    : result.ResponseSnippet)
                : result.Error,
            HttpStatus = result.HttpStatus,
            LatencyMs = result.LatencyMs,
            Attempt = 1,
        });

        if (result.Success && _config.Global.NotifyOnSuccess)
            _notifier.ShowSuccess(profile.Name, _clock.UtcNow.LocalDateTime);
        else if (!result.Success && _config.Global.NotifyOnFailure)
            _notifier.ShowFailure(profile.Name, result.Error ?? "未知错误");

        return result;
    }

    /// <summary>把加密的 Key 解开成明文副本供 HTTP 客户端使用；原对象保持加密态。</summary>
    private static ModelProfile CloneWithPlainKey(ModelProfile profile) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        Protocol = profile.Protocol,
        BaseUrl = profile.BaseUrl,
        ApiKeyCipherBase64 = DpapiSecretProtector.Unprotect(profile.ApiKeyCipherBase64),
        ModelName = profile.ModelName,
        PromptOverride = profile.PromptOverride,
        Enabled = profile.Enabled,
        MaxTokens = profile.MaxTokens,
        MaxRetries = profile.MaxRetries,
        FetchedModels = profile.FetchedModels,
        ModelsFetchedAt = profile.ModelsFetchedAt,
        Schedules = profile.Schedules,
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SchedulerService started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_config.Global.SchedulingPaused)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                var nowUtc = _clock.UtcNow;
                var localOffset = ResolveLocalOffset();

                // 找全局最近一个“尚未处理过”的发送时刻。
                NextFire? next = null;
                ModelProfile? nextProfile = null;
                foreach (var profile in _registry.EnabledProfiles())
                {
                    if (!profile.Enabled) continue;
                    var n = ScheduleCalculator.Next(
                        profile.Schedules, nowUtc,
                        _config.Global.AnchorSemantics,
                        _config.Global.SafetyOffsetSeconds,
                        localOffset);
                    if (n is null) continue;
                    if (HasFired(n.Schedule.Id, n.TargetAtUtc)) continue; // 本轮目标已处理，跳过
                    if (next is null || n.SendAtUtc < next.SendAtUtc)
                    {
                        next = n;
                        nextProfile = profile;
                    }
                }

                if (next is null || nextProfile is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                var dueIn = next.SendAtUtc - nowUtc;
                if (dueIn > TimeSpan.FromSeconds(5))
                {
                    var sleep = dueIn - TimeSpan.FromSeconds(5);
                    if (sleep > TimeSpan.FromMinutes(1)) sleep = TimeSpan.FromMinutes(1);
                    await Task.Delay(sleep, stoppingToken);
                    continue;
                }

                // 最后 5 秒：轮询对齐到时刻。
                while (_clock.UtcNow < next.SendAtUtc && !stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(50, stoppingToken);
                }
                if (stoppingToken.IsCancellationRequested) break;

                await FireAsync(nextProfile, next, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "scheduler loop error");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task FireAsync(ModelProfile profile, NextFire fire, CancellationToken ct)
    {
        // 双保险：并发路径（如手动触发恰好撞上）也不允许同一 target 二次进入。
        if (!_firedTargets.TryAdd(fire.Schedule.Id, fire.TargetAtUtc))
        {
            return;
        }

        var nowUtc = _clock.UtcNow;
        var sendAt = fire.SendAtUtc;

        if (nowUtc < sendAt)
        {
            // 时钟回拨或对齐误差，稍等。
            await Task.Delay(sendAt - nowUtc, ct);
            nowUtc = _clock.UtcNow;
        }

        var grace = TimeSpan.FromSeconds(_config.Global.GracePeriodSeconds);
        if (nowUtc - sendAt > grace)
        {
            _log.Append(new RunLog
            {
                At = _clock.UtcNow,
                ModelId = profile.Id,
                ModelName = profile.Name,
                ScheduleId = fire.Schedule.Id,
                Kind = RunLogKind.Scheduled,
                Status = RunLogStatus.Missed,
                Message = $"超出宽容期未发送（delay={(nowUtc - sendAt).TotalSeconds:F0}s），本轮跳过",
            });
            return;
        }

        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, Math.Max(1, _config.Global.JitterMaxSeconds) * 1000));
        await Task.Delay(jitter, ct);

        var wasActive = ScheduleCalculator.StillInActiveWindow(LastSuccessAt(profile.Id), _clock.UtcNow);
        if (wasActive)
        {
            _log.Append(new RunLog
            {
                At = _clock.UtcNow,
                ModelId = profile.Id,
                ModelName = profile.Name,
                ScheduleId = fire.Schedule.Id,
                Kind = RunLogKind.Scheduled,
                Status = RunLogStatus.ConflictWarn,
                Message = "上一窗口仍活跃，本次发送不会产生新锚点（间隔不足 5h）。",
            });
        }

        await SendWithRetryAsync(profile, fire, wasActive, ct);
    }

    private async Task SendWithRetryAsync(ModelProfile profile, NextFire fire, bool wasActive, CancellationToken ct)
    {
        var prompt = ScheduleCalculator.EffectivePrompt(_config.Global.DefaultPrompt, profile.PromptOverride);
        var working = CloneWithPlainKey(profile);
        var client = _clientFactory.Create(working);

        var attempts = Math.Max(0, profile.MaxRetries);
        for (var i = 0; i <= attempts; i++)
        {
            SendResult result;
            try
            {
                result = await client.SendPingAsync(working, prompt, ct);
            }
            catch (Exception ex)
            {
                result = new SendResult { Success = false, Error = ex.Message };
            }

            if (result.Success)
            {
                _lastSuccessByModel[profile.Id] = _clock.UtcNow;
                if (fire.Schedule.Kind == ScheduleKind.OneShot)
                    _registry.DisableSchedule(profile, fire.Schedule.Id);

                _log.Append(new RunLog
                {
                    At = _clock.UtcNow,
                    ModelId = profile.Id,
                    ModelName = profile.Name,
                    ScheduleId = fire.Schedule.Id,
                    Kind = RunLogKind.Scheduled,
                    Status = RunLogStatus.Success,
                    Message = wasActive
                        ? $"{result.ResponseSnippet}（上一窗口仍活跃，未产生新锚点）"
                        : result.ResponseSnippet,
                    HttpStatus = result.HttpStatus,
                    LatencyMs = result.LatencyMs,
                    Attempt = i + 1,
                });
                if (_config.Global.NotifyOnSuccess)
                    _notifier.ShowSuccess(profile.Name, fire.TargetAtUtc.LocalDateTime);
                return;
            }

            var final = result.ShouldNotRetry || i == attempts;
            if (final)
            {
                _log.Append(new RunLog
                {
                    At = _clock.UtcNow,
                    ModelId = profile.Id,
                    ModelName = profile.Name,
                    ScheduleId = fire.Schedule.Id,
                    Kind = RunLogKind.Scheduled,
                    Status = RunLogStatus.Failed,
                    Message = (i == attempts && !result.ShouldNotRetry)
                        ? $"{result.Error ?? "重试耗尽"}（已重试 {i + 1} 次）"
                        : result.Error,
                    HttpStatus = result.HttpStatus,
                    LatencyMs = result.LatencyMs,
                    Attempt = i + 1,
                });
                if (_config.Global.NotifyOnFailure)
                    _notifier.ShowFailure(profile.Name, result.Error ?? "未知错误");
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(3, i)), ct); // 1s, 3s, 9s
        }
    }

    private TimeSpan ResolveLocalOffset()
    {
        if (!string.IsNullOrWhiteSpace(_config.Global.TimeZoneId))
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(_config.Global.TimeZoneId);
                return tz.GetUtcOffset(_clock.UtcNow);
            }
            catch { /* fallback */ }
        }
        return TimeZoneInfo.Local.GetUtcOffset(_clock.UtcNow);
    }
}