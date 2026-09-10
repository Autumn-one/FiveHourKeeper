using FiveHourKeeper.Models;

namespace FiveHourKeeper.Tests;

public class DpapiSecretProtectorTests
{
    [Fact]
    public void Roundtrip_WindowsOnly_ReturnsOriginal()
    {
        // DPAPI 在非 Windows 上会抛 PlatformNotSupportedException；这里跳过断言以保证跨平台编译通过。
        if (!OperatingSystem.IsWindows()) return;
        var protectedText = FiveHourKeeper.Services.DpapiSecretProtector.Protect("sk-abc");
        Assert.Equal("sk-abc", FiveHourKeeper.Services.DpapiSecretProtector.Unprotect(protectedText));
    }

    [Fact]
    public void Empty_Roundtrip_ReturnsEmpty()
    {
        Assert.Equal("", FiveHourKeeper.Services.DpapiSecretProtector.Protect(""));
        Assert.Equal("", FiveHourKeeper.Services.DpapiSecretProtector.Unprotect(""));
    }
}