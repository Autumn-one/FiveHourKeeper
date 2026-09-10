using FiveHourKeeper.Models;

namespace FiveHourKeeper.Services;

/// <summary>
/// 调度计算的纯函数集合。所有时间点一律用 UTC <see cref="DateTimeOffset"/> 表示，
/// 仅在显示与用户输入侧做本地时间 ↔ UTC 转换，避免序列化与跨时区迁移时漂移。
/// </summary>
public static class ScheduleCalculator
{
    /// <summary>
    /// 发送时刻 = 目标时刻 − 5 小时 + 安全偏移。
    /// 无论服务端按整点（Claude Code）还是精确分钟（claude.ai）锚定窗口，
    /// 该公式都是最优：整点语义下锚点 = 发送时刻所在整点，窗口恰好在目标时刻所在整点刷新；
    /// 精确语义下窗口恰好在目标时刻 + 偏移秒数后刷新。
    /// 曾错在“把发送时刻向下取整”——那会让精确语义下的窗口提前至多 59 分钟失效，且对整点语义毫无收益。
    /// </summary>
    public static DateTimeOffset ComputeSendAt(
        DateTimeOffset targetLocal,
        AnchorSemantics anchor,
        int safetyOffsetSeconds)
    {
        _ = anchor; // 语义只影响用户预期（建议整点目标），不影响发送时刻计算
        return targetLocal
            .AddHours(-5)
            .AddSeconds(safetyOffsetSeconds)
            .ToUniversalTime()
            .ToOffset(TimeSpan.Zero);
    }

    /// <summary>给定本地整点（HH:mm）与基准日，计算 target 的 UTC 时间（目标所在时区）。</summary>
    public static DateTimeOffset DailyTargetOnDay(
        DateOnly baseLocalDay,
        TimeOnly dailyTime,
        TimeSpan localOffset)
    {
        var local = baseLocalDay.ToDateTime(dailyTime);
        return new DateTimeOffset(local, localOffset);
    }

    /// <summary>从一组时刻表得到 “下一个 ≥ now” 的目标时刻与发送时刻。</summary>
    public static NextFire? Next(
        IEnumerable<ResetSchedule> schedules,
        DateTimeOffset nowUtc,
        AnchorSemantics anchor,
        int safetyOffsetSeconds,
        TimeSpan localOffset,
        bool skipPastSendAt = false)
    {
        var candidates = new List<NextFire>();
        foreach (var s in schedules)
        {
            if (!s.Enabled) continue;
            NextFire? nf = s.Kind switch
            {
                ScheduleKind.Daily => NextDaily(s, nowUtc, localOffset, anchor, safetyOffsetSeconds, skipPastSendAt),
                ScheduleKind.OneShot => NextOneShot(s, nowUtc, anchor, safetyOffsetSeconds, skipPastSendAt),
                _ => null,
            };
            if (nf is not null) candidates.Add(nf);
        }
        return candidates.Count == 0 ? null : candidates.MinBy(x => x.SendAtUtc);
    }

    private static NextFire? NextDaily(
        ResetSchedule s,
        DateTimeOffset nowUtc,
        TimeSpan localOffset,
        AnchorSemantics anchor,
        int safetyOffsetSeconds,
        bool skipPastSendAt)
    {
        if (s.DailyTime is null) return null;
        var localNow = nowUtc.ToOffset(localOffset);
        var today = DateOnly.FromDateTime(localNow.Date);
        var target = DailyTargetOnDay(today, s.DailyTime.Value, localOffset);
        if (target <= localNow) target = target.AddDays(1);

        var sendAt = ComputeSendAt(target, anchor, safetyOffsetSeconds);

        // 显示用途：今天的发送时刻已过（如 01:00 设了 05:00 目标，发送时刻是昨天 00:01）
        // 则顺延到明天一轮，避免界面卡在“正在发送…”。调度器不走此分支（保留宽容期补发）。
        if (skipPastSendAt && sendAt < nowUtc)
        {
            target = target.AddDays(1);
            sendAt = ComputeSendAt(target, anchor, safetyOffsetSeconds);
        }
        return new NextFire(s, target, sendAt);
    }

    private static NextFire? NextOneShot(
        ResetSchedule s,
        DateTimeOffset nowUtc,
        AnchorSemantics anchor,
        int safetyOffsetSeconds,
        bool skipPastSendAt)
    {
        if (s.OneShotAt is null) return null;
        if (s.OneShotAt.Value <= nowUtc) return null;
        var sendAt = ComputeSendAt(s.OneShotAt.Value, anchor, safetyOffsetSeconds);
        // 显示用途：一次性任务的发送时刻已过则不再展示（已错过或已发）。
        if (skipPastSendAt && sendAt < nowUtc) return null;
        return new NextFire(s, s.OneShotAt.Value, sendAt);
    }

    /// <summary>相邻目标时刻必须 ≥ 5 小时，否则第二个目标不会产生新锚点（窗口仍在第一个窗口内）。</summary>
    public static IReadOnlyList<ScheduleConflict> DetectConflicts(
        IEnumerable<DateTimeOffset> targetsLocal)
    {
        var list = targetsLocal.OrderBy(t => t).ToList();
        var conflicts = new List<ScheduleConflict>();
        for (var i = 1; i < list.Count; i++)
        {
            var gap = list[i] - list[i - 1];
            if (gap < TimeSpan.FromHours(5))
            {
                conflicts.Add(new ScheduleConflict(
                    list[i - 1],
                    list[i],
                    gap,
                    "相邻目标间隔不足 5 小时，后一个目标不会产生新窗口。"));
            }
        }
        return conflicts;
    }

    /// <summary>
    /// 展开式冲突检测：Daily 按今天/明天两轮展开，OneShot 取未来 48h 内的发生时刻，
    /// 再按相邻间隔检查。混合场景（Daily 与 OneShot 并存）下比同日比较更准确。
    /// </summary>
    public static IReadOnlyList<ScheduleConflict> DetectConflictsExpanded(
        IEnumerable<ResetSchedule> schedules,
        DateTimeOffset nowLocal)
    {
        var occurrences = new List<DateTimeOffset>();
        foreach (var s in schedules)
        {
            if (!s.Enabled) continue;
            if (s.Kind == ScheduleKind.Daily && s.DailyTime is { } t)
            {
                var today = nowLocal.Date + t.ToTimeSpan();
                var asLocal = new DateTimeOffset(today, nowLocal.Offset);
                occurrences.Add(asLocal);          // 今天这一轮（可能已过，仅用于相邻性检查）
                occurrences.Add(asLocal.AddDays(1)); // 明天这一轮
            }
            else if (s.Kind == ScheduleKind.OneShot && s.OneShotAt is { } one)
            {
                var local = one.ToLocalTime();
                if (local >= nowLocal && local <= nowLocal.AddDays(2))
                    occurrences.Add(local);
            }
        }
        return DetectConflicts(occurrences);
    }

    /// <summary>宽容期判定：now 落在 [SendAt, SendAt + grace] 内即允许补发。</summary>
    public static bool WithinGrace(
        DateTimeOffset sendAtUtc,
        DateTimeOffset nowUtc,
        int gracePeriodSeconds)
    {
        if (nowUtc < sendAtUtc) return false;
        return (nowUtc - sendAtUtc).TotalSeconds <= gracePeriodSeconds;
    }

    /// <summary>是否仍处于上一个成功请求的 5 小时窗口内（用于判断本次发送是 “仍活跃” 而非新锚点）。</summary>
    public static bool StillInActiveWindow(DateTimeOffset? lastSuccessAtUtc, DateTimeOffset nowUtc)
    {
        if (lastSuccessAtUtc is null) return false;
        return (nowUtc - lastSuccessAtUtc.Value) < TimeSpan.FromHours(5);
    }

    /// <summary>根据 “仅回复1” 默认提示词拼接有效提示词。</summary>
    public static string EffectivePrompt(string globalPrompt, string? modelOverride)
    {
        var prompt = string.IsNullOrWhiteSpace(modelOverride) ? globalPrompt : modelOverride;
        if (string.IsNullOrEmpty(prompt)) return "仅回复1";
        return prompt;
    }
}

public sealed record NextFire(ResetSchedule Schedule, DateTimeOffset TargetAtUtc, DateTimeOffset SendAtUtc);

public sealed record ScheduleConflict(
    DateTimeOffset EarlierTarget,
    DateTimeOffset LaterTarget,
    TimeSpan Gap,
    string Message);