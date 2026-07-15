using System.Collections.Generic;
using System.Linq;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

public class ProxyKeyDistributorTests
{
    private static List<Account> MakeAccounts(int count)
        => Enumerable.Range(1, count)
            .Select(i => new Account { Email = $"acc{i}@mail.com", Password = "p" })
            .ToList();

    // ===== Ví dụ người dùng: 10 tài khoản, 4 key → round-robin =====
    [Fact]
    public void Distribute_10TaiKhoan4Key_RaiVongTron()
    {
        var keys = new List<string> { "k1", "k2", "k3", "k4" };
        var accounts = MakeAccounts(10);

        var n = ProxyKeyDistributor.Distribute(keys, accounts);

        Assert.Equal(10, n);
        Assert.Equal(
            new[] { "k1", "k2", "k3", "k4", "k1", "k2", "k3", "k4", "k1", "k2" },
            accounts.Select(a => a.ProxyKey));
    }

    // ===== 1 key, nhiều tài khoản → tất cả nhận key đó =====
    [Fact]
    public void Distribute_MotKey_TatCaNhanCungKey()
    {
        var keys = new List<string> { "only" };
        var accounts = MakeAccounts(5);

        var n = ProxyKeyDistributor.Distribute(keys, accounts);

        Assert.Equal(5, n);
        Assert.All(accounts, a => Assert.Equal("only", a.ProxyKey));
    }

    // ===== Nhiều key hơn tài khoản (5 key, 3 acc) → acc nhận key1–3, key4–5 không dùng =====
    [Fact]
    public void Distribute_NhieuKeyHonTaiKhoan_ChiDungKeyDau()
    {
        var keys = new List<string> { "k1", "k2", "k3", "k4", "k5" };
        var accounts = MakeAccounts(3);

        var n = ProxyKeyDistributor.Distribute(keys, accounts);

        Assert.Equal(3, n);
        Assert.Equal(new[] { "k1", "k2", "k3" }, accounts.Select(a => a.ProxyKey));
    }

    // ===== keys rỗng → trả 0, ProxyKey giữ nguyên (kể cả đang có giá trị) =====
    [Fact]
    public void Distribute_KeysRong_KhongDoiGi()
    {
        var keys = new List<string>();
        var accounts = MakeAccounts(3);
        accounts[0].ProxyKey = "cu0";
        accounts[1].ProxyKey = "cu1";
        // accounts[2].ProxyKey = null

        var n = ProxyKeyDistributor.Distribute(keys, accounts);

        Assert.Equal(0, n);
        Assert.Equal("cu0", accounts[0].ProxyKey);
        Assert.Equal("cu1", accounts[1].ProxyKey);
        Assert.Null(accounts[2].ProxyKey);
    }

    // ===== accounts rỗng → trả 0, không ném =====
    [Fact]
    public void Distribute_AccountsRong_TraVe0_KhongNem()
    {
        var keys = new List<string> { "k1", "k2" };
        var accounts = new List<Account>();

        var n = ProxyKeyDistributor.Distribute(keys, accounts);

        Assert.Equal(0, n);
    }
}
