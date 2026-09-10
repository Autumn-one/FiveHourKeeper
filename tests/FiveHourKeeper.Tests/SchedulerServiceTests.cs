using System.IO;
using FiveHourKeeper.Models;
using FiveHourKeeper.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FiveHourKeeper.Tests;

/// <summary>用可拨动的假时钟验证调度器核心行为：到点触发、防重复触发、错过宽容期。</summary>
public class SchedulerServiceTests
{
    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 9, 4, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeClientFactory : IAiClientFactory
    {
        public int SendCount { get; private set; }
        public bool AlwaysFail { get; set; }

        public IAiClient Create(ModelProfile profile) => new FakeClient(this);

        private sealed class FakeClient(FakeClientFactory owner) : IAiClient
        {
            public AiProtocol Protocol => AiProtocol.OpenAiCompatible;

            public Task<IReadOnlyList<string>> ListModelsAsync(ModelProfile profile, CancellationToken ct = default) =>
                Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

            public Task<SendResult> SendPingAsync(ModelProfile profile, string prompt, CancellationToken ct = default)
            {
                owner.SendCount++;
                return Task.FromResult(owner.AlwaysFail
                    ? new SendResult { Success = false, Error = "simulated", HttpStatus = 500 }
                    : new SendResult { Success = true, HttpStatus = 200, ResponseSnippet = "1" });
            }
        }
    }

    private static (SchedulerService Scheduler, FakeClock Clock, FakeClientFactory Factory) Build(
        ModelProfile profile)
    {
        var clock = new FakeClock();
        var factory = new FakeClientFactory();
        var holder = new ConfigRootHolder
        {
            Current = new AppConfig
            {
                Global = new GlobalSettings
                {
                    SafetyOffsetSeconds = 0,
                    JitterMaxSeconds = 0, // 测试中关闭随机抖动
                    GracePeriodSeconds = 600,
                },
                Models = { profile },
            },
        };
        var registry = new ModelRegistry(new[] { profile });
        var store = new JsonConfigStore(Path.Combine(Path.GetTempPath(), "fhk-test-" + Guid.NewGuid().ToString("N")));
        var log = new RunLogStore(store, 100);
        var scheduler = new SchedulerService(
            registry, factory, log, new NoopNotifier(), clock,
            NullLogger<SchedulerService>.Instance, holder);
        return (scheduler, clock, factory);
    }

    [Fact]
    public async Task FireAsync_OncePerTarget_NoGraceRefire()
    {
        var profile = new ModelProfile
        {
            Name = "t",
            BaseUrl = "https://api.example.com",
            ModelName = "m",
            ApiKeyCipherBase64 = "plain-key-for-test",
            Schedules = { new ResetSchedule { Kind = ScheduleKind.Daily, DailyTime = new TimeOnly(12, 10), Enabled = true } },
        };
        var (scheduler, clock, factory) = Build(profile);

        var localOffset = TimeSpan.FromHours(8);
        var next = ScheduleCalculator.Next(
            profile.Schedules, clock.UtcNow, AnchorSemantics.FloorToHour, 0, localOffset);
        Assert.NotNull(next);

        // 时钟对齐到发送时刻：正常触发一次
        clock.UtcNow = next!.SendAtUtc;
        await InvokeFireAsync(scheduler, profile, next);
        Assert.Equal(1, factory.SendCount);

        // 把时钟拨到 grace 期内再触发：必须被 _firedTargets 拦下
        clock.UtcNow = next.SendAtUtc.AddSeconds(30);
        await InvokeFireAsync(scheduler, profile, next);
        Assert.Equal(1, factory.SendCount); // 仍是 1，没有重复发送
    }

    [Fact]
    public async Task FireAsync_BeyondGrace_MissedNotSent()
    {
        var profile = new ModelProfile
        {
            Name = "t",
            BaseUrl = "https://api.example.com",
            ModelName = "m",
            ApiKeyCipherBase64 = "k",
            Schedules = { new ResetSchedule { Kind = ScheduleKind.Daily, DailyTime = new TimeOnly(12, 10), Enabled = true } },
        };
        var (scheduler, clock, factory) = Build(profile);
        var localOffset = TimeSpan.FromHours(8);

        var next = ScheduleCalculator.Next(
            profile.Schedules, clock.UtcNow, AnchorSemantics.FloorToHour, 0, localOffset);
        Assert.NotNull(next);

        // 错过超过 10 分钟
        clock.UtcNow = next!.SendAtUtc.AddSeconds(700);
        await InvokeFireAsync(scheduler, profile, next);

        Assert.Equal(0, factory.SendCount); // Missed 不发送
    }

    [Fact]
    public async Task SendNowAsync_Success_RecordsLastSuccess()
    {
        var profile = new ModelProfile
        {
            Name = "t",
            BaseUrl = "https://api.example.com",
            ModelName = "m",
            ApiKeyCipherBase64 = "k",
        };
        var (scheduler, clock, _) = Build(profile);

        var result = await scheduler.SendNowAsync(profile);
        Assert.True(result.Success);
        Assert.NotNull(scheduler.LastSuccessAt(profile.Id));
    }

    /// <summary>FireAsync 是 private，用反射调用以做单元级验证。</summary>
    private static async Task InvokeFireAsync(SchedulerService scheduler, ModelProfile profile, NextFire next)
    {
        var method = typeof(SchedulerService).GetMethod("FireAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        var task = (Task)method!.Invoke(scheduler, new object[] { profile, next, CancellationToken.None })!;
        await task;
    }
}