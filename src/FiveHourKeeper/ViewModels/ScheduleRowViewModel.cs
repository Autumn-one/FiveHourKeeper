using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using FiveHourKeeper.Models;

namespace FiveHourKeeper.ViewModels;

/// <summary>
/// 编辑弹窗里的“一条重置时间”行。把时间/日期与 <see cref="ResetSchedule"/> 的映射收拢在这里，
/// 避免 XAML 直接绑 TimeOnly/DateTimeOffset 这类缺少默认类型转换器的属性。
/// </summary>
public sealed partial class ScheduleRowViewModel : ObservableObject
{
    public string RowId { get; } = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDaily))]
    private ScheduleKind _kind;

    [ObservableProperty]
    private string _timeText;

    [ObservableProperty]
    private string? _dateText;

    [ObservableProperty]
    private bool _enabled = true;

    public bool IsDaily => Kind == ScheduleKind.Daily;

    public ScheduleRowViewModel()
    {
        _kind = ScheduleKind.Daily;
        _timeText = "09:00";
    }

    public ScheduleRowViewModel(ResetSchedule s)
    {
        _kind = s.Kind;
        _enabled = s.Enabled;
        if (s.DailyTime is { } t)
        {
            _timeText = t.ToString("HH:mm");
            _dateText = DateTime.Today.ToString("yyyy-MM-dd");
        }
        else if (s.OneShotAt is { } one)
        {
            var local = one.ToLocalTime();
            _timeText = local.ToString("HH:mm");
            _dateText = local.ToString("yyyy-MM-dd");
        }
        else
        {
            _timeText = "09:00";
            _dateText = DateTime.Today.ToString("yyyy-MM-dd");
        }
    }

    partial void OnKindChanged(ScheduleKind value)
    {
        if (value == ScheduleKind.OneShot && string.IsNullOrEmpty(DateText))
            DateText = DateTime.Today.ToString("yyyy-MM-dd");
    }

    /// <summary>解析结果；无效时 Message 说明原因。</summary>
    public bool TryToModel(out ResetSchedule schedule, out string error)
    {
        schedule = new ResetSchedule { Id = RowId, Kind = Kind, Enabled = Enabled };
        if (Kind == ScheduleKind.Daily)
        {
            if (!TimeOnly.TryParseExact(TimeText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            {
                error = $"时刻 “{TimeText}” 无效，请使用 HH:mm（如 21:00）";
                return false;
            }
            schedule.DailyTime = t;
            error = "";
            return true;
        }

        if (!TimeOnly.TryParseExact(TimeText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var tt))
        {
            error = $"时刻 “{TimeText}” 无效，请使用 HH:mm";
            return false;
        }
        if (!DateOnly.TryParseExact(DateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            error = $"日期 “{DateText}” 无效，请使用 yyyy-MM-dd";
            return false;
        }
        var local = d.ToDateTime(tt);
        schedule.OneShotAt = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        error = "";
        return true;
    }

    /// <summary>供列表展示的摘要文本。</summary>
    public string Summary => Kind == ScheduleKind.Daily
        ? $"每天 {TimeText}"
        : $"{DateText} {TimeText}（一次性）";
}