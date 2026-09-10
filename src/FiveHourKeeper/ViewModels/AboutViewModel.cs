using CommunityToolkit.Mvvm.ComponentModel;

namespace FiveHourKeeper.ViewModels;

public sealed partial class AboutViewModel : ObservableObject
{
    public string AppName => "五小时窗口管家";

    /// <summary>从程序集元数据读取版本号（与 csproj 的 &lt;Version&gt; 同步）。</summary>
    public string Version => System.Reflection.Assembly.GetExecutingAssembly()
        .GetName().Version?.ToString(3) ?? "1.0.0";

    public string Description => "主动控制 AI 订阅的 5 小时滚动窗口刷新时刻。\n" +
        "调度器在窗口刷新前 5 小时发送一次锚定请求，让每次新窗口都从你期望的时间开始计时。";

    public string QqGroup => "111862811";
    public string TgChannelUrl => "https://t.me/+bnRpqZXuMmE2M2Y9";
    public string YoutubeHint => "我们的 YouTube 大本营（频道名同名：FiveHourKeeper）";
}
