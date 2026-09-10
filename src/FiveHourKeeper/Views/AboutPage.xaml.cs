using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace FiveHourKeeper.Views;

public partial class AboutPage : UserControl
{
    public AboutPage()
    {
        InitializeComponent();
    }

    /// <summary>点击 Telegram 频道链接：用系统默认浏览器打开。</summary>
    private void OnTelegramClick(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
