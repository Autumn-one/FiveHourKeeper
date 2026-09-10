using System.Security.Cryptography;
using System.Text;

namespace FiveHourKeeper.Services;

/// <summary>使用 DPAPI（CurrentUser）保护 API Key。仅当前 Windows 用户可解密密文。</summary>
public static class DpapiSecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FiveHourKeeper.v1");

    public static string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public static string Unprotect(string cipherBase64)
    {
        if (string.IsNullOrEmpty(cipherBase64)) return string.Empty;
        try
        {
            var protectedBytes = Convert.FromBase64String(cipherBase64);
            var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentNullException or ArgumentOutOfRangeException)
        {
            // 跨用户、DPAPI 损坏或非 Base64 密文：回退为空，让用户重新填写。
            return string.Empty;
        }
    }
}