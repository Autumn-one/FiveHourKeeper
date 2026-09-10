using System.Globalization;
using System.Windows;
using System.Windows.Data;
using FiveHourKeeper.Models;
using FiveHourKeeper.Services;

namespace FiveHourKeeper.Views.Converters;

public sealed class MaskKeyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        AiUrl.MaskKey(value as string ?? "");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class LocalTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var fmt = parameter as string ?? "HH:mm:ss";
        return value switch
        {
            DateTimeOffset dto => dto.ToLocalTime().ToString(fmt),
            DateTime dt => dt.ToString(fmt),
            TimeOnly t => t.ToString("HH:mm"),
            _ => value?.ToString() ?? "",
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 1.0 : 0.4;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class NullToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>枚举 → 中文显示。界面不裸奔英文枚举值。</summary>
public sealed class EnumDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        AiProtocol.OpenAiCompatible => "OpenAI 兼容",
        AiProtocol.AnthropicCompatible => "Anthropic 兼容",
        AnchorSemantics.FloorToHour => "整点对齐（Claude Code）",
        AnchorSemantics.Exact => "精确时刻（claude.ai）",
        ScheduleKind.Daily => "每天循环",
        ScheduleKind.OneShot => "一次性",
        RunLogKind.Scheduled => "定时",
        RunLogKind.Manual => "手动",
        RunLogKind.TestConnection => "测试",
        RunLogStatus.Success => "成功",
        RunLogStatus.Failed => "失败",
        RunLogStatus.Skipped => "跳过",
        RunLogStatus.Missed => "错过",
        RunLogStatus.ConflictWarn => "窗口冲突",
        _ => value?.ToString() ?? "",
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>状态文本 → 颜色：成功绿、失败红、进行中主色、默认灰。</summary>
public sealed class StatusToneConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string ?? "";
        if (s.Contains("成功") || s.Contains("已拉取") || s.Contains("已建立")) return "#0F6E56";
        if (s.Contains("失败") || s.Contains("无效") || s.Contains("请先补全")) return "#A32D2D";
        if (s.Length > 0) return "#534AB7";
        return "#5F5E5A";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>启用状态 → 徽标文本/颜色。</summary>
public sealed class EnabledTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "启用" : "停用";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class EnabledToneConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "#0F6E56" : "#888780";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}