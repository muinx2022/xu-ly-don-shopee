using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// Test cho hàm lõi thuần <see cref="BrowserLocator.FindFirstExisting"/> (không phụ thuộc máy thật).
/// Không test <see cref="BrowserLocator.FindBraveExecutable"/> vì phụ thuộc hệ thống file cụ thể.
/// </summary>
public class BrowserLocatorTests
{
    [Fact]
    public void FindFirstExisting_CoPhanTuKhop_TraPhanTuDauTienKhop()
    {
        var candidates = new[] { "a", "b", "c" };

        var result = BrowserLocator.FindFirstExisting(candidates, p => p == "b" || p == "c");

        Assert.Equal("b", result);
    }

    [Fact]
    public void FindFirstExisting_KhongPhanTuNaoKhop_TraNull()
    {
        var candidates = new[] { "a", "b", "c" };

        var result = BrowserLocator.FindFirstExisting(candidates, _ => false);

        Assert.Null(result);
    }

    [Fact]
    public void FindFirstExisting_BoQuaNullVaChuoiRong()
    {
        var candidates = new string?[] { null, "", "   ", "match" };

        var result = BrowserLocator.FindFirstExisting(candidates!, p => p == "match");

        Assert.Equal("match", result);
    }

    [Fact]
    public void FindFirstExisting_NhieuPhanTuKhop_UuTienPhanTuTruoc()
    {
        // Cả "first" lẫn "second" đều khớp predicate → phải trả phần tử đầu tiên theo thứ tự.
        var candidates = new[] { "first", "second" };

        var result = BrowserLocator.FindFirstExisting(candidates, _ => true);

        Assert.Equal("first", result);
    }

    [Fact]
    public void FindFirstExisting_DanhSachRong_TraNull()
    {
        var result = BrowserLocator.FindFirstExisting(System.Array.Empty<string>(), _ => true);

        Assert.Null(result);
    }
}
