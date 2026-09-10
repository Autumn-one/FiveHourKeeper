using FiveHourKeeper.Models;
using FiveHourKeeper.Services;

namespace FiveHourKeeper.Tests;

public class ScheduleCalculatorTests
{
    private static readonly TimeSpan CstOffset = TimeSpan.FromHours(8);

    [Fact]
    public void CrossDayPushForward_2am_target_sends_9pm_previous_day()
    {
        // 目标 2026-09-10 02:00 CST ⇒ 发送 2026-09-09 21:01 CST（60 秒安全偏移）
        var targetLocal = new DateTimeOffset(2026, 9, 10, 2, 0, 0, CstOffset);
        var sendAt = ScheduleCalculator.ComputeSendAt(targetLocal, AnchorSemantics.FloorToHour, 60);
        var sendLocal = sendAt.ToOffset(CstOffset);

        Assert.Equal(2026, sendLocal.Year);
        Assert.Equal(9, sendLocal.Month);
        Assert.Equal(9, sendLocal.Day);
        Assert.Equal(21, sendLocal.Hour);
        Assert.Equal(1, sendLocal.Minute);
    }

    [Fact]
    public void ComputeSendAt_NoFloor_NonWholeHourTargetPreserved()
    {
        // 回归：曾错误地把发送时刻 floor 到整点（02:30 目标 → 21:00 发送）。
        // 正确行为：02:30 − 5h = 21:30 前一天，精确保留分钟。
        var target = new DateTimeOffset(2026, 9, 10, 2, 30, 0, CstOffset);
        var send = ScheduleCalculator.ComputeSendAt(target, AnchorSemantics.FloorToHour, 0).ToOffset(CstOffset);
        Assert.Equal(9, send.Day);
        Assert.Equal(21, send.Hour);
        Assert.Equal(30, send.Minute);
    }

    [Fact]
    public void Next_Daily_Past_Today_Rolls_To_Tomorrow()
    {
        var nowUtc = new DateTimeOffset(2026, 9, 9, 22, 0, 0, CstOffset).ToUniversalTime();
        var schedules = new List<ResetSchedule>
        {
            new() { Kind = ScheduleKind.Daily, DailyTime = new TimeOnly(9, 0), Enabled = true },
        };

        var next = ScheduleCalculator.Next(schedules, nowUtc, AnchorSemantics.FloorToHour, 60, CstOffset);
        Assert.NotNull(next);
        Assert.Equal(9, next!.TargetAtUtc.ToOffset(CstOffset).Hour);
        Assert.Equal(10, next.TargetAtUtc.ToOffset(CstOffset).Day);
    }

    [Fact]
    public void Next_Daily_Future_Today_Uses_Today()
    {
        var nowUtc = new DateTimeOffset(2026, 9, 9, 6, 0, 0, CstOffset).ToUniversalTime();
        var schedules = new List<ResetSchedule>
        {
            new() { Kind = ScheduleKind.Daily, DailyTime = new TimeOnly(9, 0), Enabled = true },
        };

        var next = ScheduleCalculator.Next(schedules, nowUtc, AnchorSemantics.FloorToHour, 60, CstOffset);
        Assert.NotNull(next);
        Assert.Equal(9, next!.TargetAtUtc.ToOffset(CstOffset).Day);
        Assert.Equal(9, next.TargetAtUtc.ToOffset(CstOffset).Hour);
    }

    [Fact]
    public void DetectConflicts_TwoTargetsWithin5Hours_Reports()
    {
        var t1 = new DateTimeOffset(2026, 9, 10, 9, 0, 0, CstOffset);
        var t2 = new DateTimeOffset(2026, 9, 10, 12, 0, 0, CstOffset);
        var conflicts = ScheduleCalculator.DetectConflicts(new[] { t1, t2 });
        Assert.Single(conflicts);
        Assert.Contains("5 小时", conflicts[0].Message);
    }

    [Fact]
    public void DetectConflicts_SixHoursApart_NoConflict()
    {
        var t1 = new DateTimeOffset(2026, 9, 10, 9, 0, 0, CstOffset);
        var t2 = new DateTimeOffset(2026, 9, 10, 15, 0, 0, CstOffset);
        var conflicts = ScheduleCalculator.DetectConflicts(new[] { t1, t2 });
        Assert.Empty(conflicts);
    }

    [Fact]
    public void DetectConflictsExpanded_DailyNearOneShot_Detected()
    {
        // 场景：每天 09:00 循环 + 明天 11:00 一次性 —— 间隔仅 2h，必须报冲突。
        var now = new DateTimeOffset(2026, 9, 9, 20, 0, 0, CstOffset);
        var schedules = new List<ResetSchedule>
        {
            new() { Kind = ScheduleKind.Daily, DailyTime = new TimeOnly(9, 0), Enabled = true },
            new()
            {
                Kind = ScheduleKind.OneShot,
                OneShotAt = new DateTimeOffset(2026, 9, 10, 11, 0, 0, CstOffset),
                Enabled = true,
            },
        };

        var conflicts = ScheduleCalculator.DetectConflictsExpanded(schedules, now);
        Assert.NotEmpty(conflicts);
    }

    [Fact]
    public void DetectConflictsExpanded_DailyFarFromOneShot_NoConflict()
    {
        // 每天 09:00 + 明天 21:00 —— 间隔 12h，无冲突。
        var now = new DateTimeOffset(2026, 9, 9, 20, 0, 0, CstOffset);
        var schedules = new List<ResetSchedule>
        {
            new() { Kind = ScheduleKind.Daily, DailyTime = new TimeOnly(9, 0), Enabled = true },
            new()
            {
                Kind = ScheduleKind.OneShot,
                OneShotAt = new DateTimeOffset(2026, 9, 10, 21, 0, 0, CstOffset),
                Enabled = true,
            },
        };

        var conflicts = ScheduleCalculator.DetectConflictsExpanded(schedules, now);
        Assert.Empty(conflicts);
    }

    [Fact]
    public void DetectConflictsExpanded_DisabledSchedules_Ignored()
    {
        var now = new DateTimeOffset(2026, 9, 9, 20, 0, 0, CstOffset);
        var schedules = new List<ResetSchedule>
        {
            new() { Kind = ScheduleKind.Daily, DailyTime = new TimeOnly(9, 0), Enabled = true },
            new()
            {
                Kind = ScheduleKind.OneShot,
                OneShotAt = new DateTimeOffset(2026, 9, 10, 11, 0, 0, CstOffset),
                Enabled = false, // 停用的一次性任务不参与冲突判定
            },
        };

        var conflicts = ScheduleCalculator.DetectConflictsExpanded(schedules, now);
        Assert.Empty(conflicts);
    }

    [Fact]
    public void WithinGrace_BeforeSendAt_False()
    {
        var sendAt = DateTimeOffset.UtcNow;
        Assert.False(ScheduleCalculator.WithinGrace(sendAt, sendAt.AddSeconds(-1), 60));
    }

    [Fact]
    public void WithinGrace_InsideGrace_True()
    {
        var sendAt = DateTimeOffset.UtcNow;
        Assert.True(ScheduleCalculator.WithinGrace(sendAt, sendAt.AddSeconds(30), 60));
    }

    [Fact]
    public void WithinGrace_OutsideGrace_False()
    {
        var sendAt = DateTimeOffset.UtcNow;
        Assert.False(ScheduleCalculator.WithinGrace(sendAt, sendAt.AddSeconds(120), 60));
    }

    [Fact]
    public void StillInActiveWindow_LastSuccessWithin5h_True()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(ScheduleCalculator.StillInActiveWindow(now.AddHours(-3), now));
    }

    [Fact]
    public void StillInActiveWindow_LastSuccessMoreThan5h_False()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.False(ScheduleCalculator.StillInActiveWindow(now.AddHours(-6), now));
    }

    [Fact]
    public void EffectivePrompt_NullOverride_FallsBackToGlobal()
    {
        Assert.Equal("ping", ScheduleCalculator.EffectivePrompt("ping", null));
        Assert.Equal("ping", ScheduleCalculator.EffectivePrompt("ping", " "));
        Assert.Equal("custom", ScheduleCalculator.EffectivePrompt("ping", "custom"));
    }

    [Fact]
    public void OneShot_In_Past_Returns_Null()
    {
        var nowUtc = new DateTimeOffset(2026, 9, 10, 8, 0, 0, CstOffset).ToUniversalTime();
        var schedules = new List<ResetSchedule>
        {
            new()
            {
                Kind = ScheduleKind.OneShot,
                OneShotAt = new DateTimeOffset(2026, 9, 10, 7, 0, 0, CstOffset),
                Enabled = true,
            },
        };
        var next = ScheduleCalculator.Next(schedules, nowUtc, AnchorSemantics.FloorToHour, 60, CstOffset);
        Assert.Null(next);
    }

    [Theory]
    [InlineData(AnchorSemantics.FloorToHour)]
    [InlineData(AnchorSemantics.Exact)]
    public void AnchorSemantics_Choice_DoesNotBreakDate(AnchorSemantics mode)
    {
        var target = new DateTimeOffset(2026, 9, 10, 2, 0, 0, CstOffset);
        var send = ScheduleCalculator.ComputeSendAt(target, mode, 0);
        var sl = send.ToOffset(CstOffset);
        Assert.Equal(9, sl.Day);
        Assert.Equal(21, sl.Hour);
    }

    [Fact]
    public void Next_SkipPastSendAt_PastSendRollsToTomorrow()
    {
        // 场景：01:54 CST 把目标改成 05:00 → 发送时刻是昨天 22:00（已过）。
        // 展示视角应顺延到明天 05:00（发送今晚 22:00），而不是卡在已过的发送时刻。
        var nowUtc = new DateTimeOffset(2026, 9, 10, 1, 54, 0, CstOffset).ToUniversalTime();
        var schedules = new List<ResetSchedule>
        {
            new() { Kind = ScheduleKind.Daily, DailyTime = new TimeOnly(5, 0), Enabled = true },
        };

        var display = ScheduleCalculator.Next(
            schedules, nowUtc, AnchorSemantics.FloorToHour, 60, CstOffset, skipPastSendAt: true);
        Assert.NotNull(display);
        var dl = display!.TargetAtUtc.ToOffset(CstOffset);
        Assert.Equal(11, dl.Day); // 明天
        Assert.Equal(5, dl.Hour);

        // 调度视角（默认）保持今天这一轮，供宽容期补发逻辑处理。
        var scheduler = ScheduleCalculator.Next(
            schedules, nowUtc, AnchorSemantics.FloorToHour, 60, CstOffset);
        Assert.NotNull(scheduler);
        Assert.Equal(10, scheduler!.TargetAtUtc.ToOffset(CstOffset).Day);
    }

    [Fact]
    public void Next_UpdatableAfterRegistryUpsert()
    {
        // 复现用户场景：改时间后调度计划应立即变化。
        // 09:00 目标 → 发送 04:01；改成 11:30 → 发送 06:01。
        var profile = new ModelProfile
        {
            Name = "t",
            Schedules = { new ResetSchedule { Kind = ScheduleKind.Daily, DailyTime = new TimeOnly(9, 0), Enabled = true } },
        };
        var registry = new ModelRegistry(new[] { profile });
        var nowUtc = new DateTimeOffset(2026, 9, 10, 1, 54, 0, CstOffset).ToUniversalTime();

        var before = ScheduleCalculator.Next(
            registry.EnabledProfiles().First().Schedules, nowUtc,
            AnchorSemantics.FloorToHour, 60, CstOffset, skipPastSendAt: true);
        Assert.NotNull(before);
        Assert.Equal(4, before!.SendAtUtc.ToOffset(CstOffset).Hour); // 09:00−5h=04:00+60s → 04:01

        // 编辑保存：新对象、同 Id、新时刻（与 App 的 EditDialogLauncher 等价路径）
        var edited = new ModelProfile
        {
            Id = profile.Id,
            Name = "t",
            Schedules = { new ResetSchedule { Kind = ScheduleKind.Daily, DailyTime = new TimeOnly(11, 30), Enabled = true } },
        };
        registry.Upsert(edited);

        var after = ScheduleCalculator.Next(
            registry.EnabledProfiles().First().Schedules, nowUtc,
            AnchorSemantics.FloorToHour, 60, CstOffset, skipPastSendAt: true);
        Assert.NotNull(after);
        var al = after!.SendAtUtc.ToOffset(CstOffset);
        Assert.Equal(6, al.Hour);    // 11:30 − 5h = 06:30 + 60s
        Assert.Equal(31, al.Minute);
        Assert.NotEqual(before.SendAtUtc, after.SendAtUtc);
    }
}