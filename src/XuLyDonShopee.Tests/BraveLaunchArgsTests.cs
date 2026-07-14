using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// Test cho hàm thuần <see cref="BraveLaunchArgs.BuildBraveArgs"/> — chuỗi tham số launch Brave/Chromium.
/// </summary>
public class BraveLaunchArgsTests
{
    [Fact]
    public void CoCoChongTuDongHoa_UserDataDir_RemoteDebuggingPort()
    {
        var args = BraveLaunchArgs.BuildBraveArgs(@"C:\profiles\1", 9222, null);

        Assert.Contains("--disable-blink-features=AutomationControlled", args);
        Assert.Contains(@"--user-data-dir=C:\profiles\1", args);
        Assert.Contains("--remote-debugging-port=9222", args);
    }

    [Fact]
    public void KhongChua_EnableAutomation_VaKhongChua_Headless()
    {
        var args = BraveLaunchArgs.BuildBraveArgs("/tmp/p", 0, null);

        // Không có bất kỳ tham số nào bật cờ automation hoặc chạy ẩn.
        Assert.DoesNotContain(args, a => a.Contains("--enable-automation"));
        Assert.DoesNotContain(args, a => a.Contains("--headless"));
        Assert.DoesNotContain(args, a => a.Contains("--remote-debugging-pipe"));
    }

    [Fact]
    public void CoProxyHttp_ChuaProxyServerHttp()
    {
        var proxy = new ProxyEntry { Host = "1.2.3.4", Port = 8080, Type = ProxyType.Http };

        var args = BraveLaunchArgs.BuildBraveArgs("/tmp/p", 0, proxy);

        Assert.Contains("--proxy-server=http://1.2.3.4:8080", args);
    }

    [Fact]
    public void CoProxySocks5_ChuaProxyServerSocks5()
    {
        var proxy = new ProxyEntry { Host = "9.9.9.9", Port = 1080, Type = ProxyType.Socks5 };

        var args = BraveLaunchArgs.BuildBraveArgs("/tmp/p", 0, proxy);

        Assert.Contains("--proxy-server=socks5://9.9.9.9:1080", args);
    }

    [Fact]
    public void CoProxyCoUserPass_ProxyServerKhongKemUserPass()
    {
        // User/pass KHÔNG được nhét vào --proxy-server (Chromium không hỗ trợ) — xử lý auth qua CDP.
        var proxy = new ProxyEntry
        {
            Host = "1.2.3.4",
            Port = 8080,
            Username = "u",
            Password = "p",
            Type = ProxyType.Http
        };

        var args = BraveLaunchArgs.BuildBraveArgs("/tmp/p", 0, proxy);

        Assert.Contains("--proxy-server=http://1.2.3.4:8080", args);
        Assert.DoesNotContain(args, a => a.Contains("u:p@"));
        Assert.DoesNotContain(args, a => a.Contains("--proxy-server=http://u:p"));
    }

    [Fact]
    public void ProxyNull_KhongCoProxyServer()
    {
        var args = BraveLaunchArgs.BuildBraveArgs("/tmp/p", 0, null);

        Assert.DoesNotContain(args, a => a.StartsWith("--proxy-server"));
    }
}
