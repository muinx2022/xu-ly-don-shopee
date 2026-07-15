using System.Linq;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.Core.Models;

namespace XuLyDonShopee.Tests;

/// <summary>
/// Test cho ProxiesViewModel.SaveKeys: ngoài lưu settings còn rải đều key KiotProxy cho các tài khoản
/// theo round-robin. ViewModel chạy được bằng xunit thuần (ObservableObject + repository trên DB tạm).
/// </summary>
public class ProxiesViewModelTests
{
    // ===== Lưu N key, M tài khoản → rải round-robin theo Id, lưu settings, thông báo đúng =====
    [Fact]
    public void SaveKeys_RaiRoundRobin_ChoTatCaTaiKhoan()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);

        // 10 tài khoản; một vài tài khoản có sẵn ProxyKey cũ để kiểm ghi đè.
        for (var i = 1; i <= 10; i++)
        {
            services.Accounts.Insert(new Account
            {
                Email = $"acc{i:D2}@mail.com",
                Password = "p",
                ProxyKey = i % 3 == 0 ? "cu-cua-toi" : null
            });
        }

        var vm = new ProxiesViewModel(services);
        vm.Keys = "k1\nk2\nk3\nk4";
        vm.SaveKeysCommand.Execute(null);

        // Đọc lại DB theo Id tăng dần → ProxyKey đúng round-robin keys[i % 4].
        var saved = services.Accounts.GetAll();
        Assert.Equal(10, saved.Count);
        Assert.Equal(
            new[] { "k1", "k2", "k3", "k4", "k1", "k2", "k3", "k4", "k1", "k2" },
            saved.Select(a => a.ProxyKey));

        // Thông báo nêu số tài khoản đã rải.
        Assert.Contains("rải cho 10 tài khoản", vm.SavedKeysMessage);

        // Settings lưu đúng 4 key.
        Assert.Equal(new[] { "k1", "k2", "k3", "k4" }, services.Settings.GetKiotProxyKeys());
    }

    // ===== Ô trống bấm Lưu → không đụng ProxyKey tài khoản; thông báo là câu "chưa có key" cũ =====
    [Fact]
    public void SaveKeys_ORong_KhongDoiProxyKey_ThongBaoCu()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);

        services.Accounts.Insert(new Account { Email = "a@mail.com", Password = "p", ProxyKey = "giu-nguyen-a" });
        services.Accounts.Insert(new Account { Email = "b@mail.com", Password = "p", ProxyKey = null });

        var vm = new ProxiesViewModel(services);
        vm.Keys = "";
        vm.SaveKeysCommand.Execute(null);

        var saved = services.Accounts.GetAll();
        Assert.Equal("giu-nguyen-a", saved[0].ProxyKey);
        Assert.Null(saved[1].ProxyKey);

        Assert.Equal("Đã lưu (chưa có key — sẽ dùng IP máy).", vm.SavedKeysMessage);
        Assert.Empty(services.Settings.GetKiotProxyKeys());
    }

    // ===== Có key nhưng chưa có tài khoản nào → thông báo "chưa có tài khoản nào để rải" =====
    [Fact]
    public void SaveKeys_CoKeyKhongCoTaiKhoan_ThongBaoChuaCoTaiKhoan()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);

        var vm = new ProxiesViewModel(services);
        vm.Keys = "k1\nk2";
        vm.SaveKeysCommand.Execute(null);

        Assert.Contains("chưa có tài khoản nào để rải", vm.SavedKeysMessage);
        Assert.Equal(new[] { "k1", "k2" }, services.Settings.GetKiotProxyKeys());
    }
}
