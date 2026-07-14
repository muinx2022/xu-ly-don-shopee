using XuLyDonShopee.Core.Data;

namespace XuLyDonShopee.Tests;

public class SettingsRepositoryTests
{
    [Fact]
    public void SetVaGetKiotProxyKeys_ChuanHoa_BoTrongVaTrung()
    {
        using var temp = new TempDatabase();
        var repo = new SettingsRepository(temp.Open());

        repo.SetKiotProxyKeys(new[] { " k1 ", "k1", "", "k2" });

        Assert.Equal(new[] { "k1", "k2" }, repo.GetKiotProxyKeys());
    }

    [Fact]
    public void GetKiotProxyKeys_ChuaLuu_TraVeRong()
    {
        using var temp = new TempDatabase();
        var repo = new SettingsRepository(temp.Open());

        Assert.Empty(repo.GetKiotProxyKeys());
    }

    [Fact]
    public void KiotProxyKeys_ConNguyen_SauKhiMoLaiDatabase()
    {
        using var temp = new TempDatabase();

        // Phiên 1: ghi.
        {
            var repo1 = new SettingsRepository(temp.Open());
            repo1.SetKiotProxyKeys(new[] { "k1", "k2" });
        }

        // Phiên 2: mở lại, dữ liệu còn nguyên.
        {
            var repo2 = new SettingsRepository(temp.Open());
            Assert.Equal(new[] { "k1", "k2" }, repo2.GetKiotProxyKeys());
        }
    }
}
