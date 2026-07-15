using XuLyDonShopee.Core.Models;

namespace XuLyDonShopee.Core.Services;

/// <summary>Rải đều API key KiotProxy cho danh sách tài khoản theo vòng tròn (round-robin):
/// tài khoản thứ i nhận keys[i % keys.Count]. Hàm thuần: chỉ GÁN vào object, không tự lưu DB.</summary>
public static class ProxyKeyDistributor
{
    /// <summary>Gán key round-robin cho từng tài khoản (ghi đè ProxyKey cũ).
    /// keys rỗng → không đổi gì, trả 0. Trả về số tài khoản đã gán.</summary>
    public static int Distribute(IReadOnlyList<string> keys, IReadOnlyList<Account> accounts)
    {
        if (keys.Count == 0 || accounts.Count == 0)
        {
            return 0;
        }

        for (var i = 0; i < accounts.Count; i++)
        {
            accounts[i].ProxyKey = keys[i % keys.Count];
        }
        return accounts.Count;
    }
}
