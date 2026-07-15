using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// Test các hàm thuần so khớp menu/tab của <see cref="ShopeeShippingNav"/> — dùng để điều hướng tới
/// "Cài Đặt Vận Chuyển" → tab "Địa Chỉ". Text lấy từ DOM có thể kèm xuống dòng / khoảng trắng thừa / rác
/// badge nên mọi so khớp phải chuẩn hóa trước.
/// </summary>
public class ShopeeShippingNavTests
{
    // ===== NormalizeUiText: null/rỗng/space thừa/xuống dòng/tab/hoa-thường =====
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("Hello", "hello")]
    [InlineData("  Hello   World  ", "hello world")]
    [InlineData("Cài\nĐặt\tVận  Chuyển", "cài đặt vận chuyển")]
    [InlineData("CÀI ĐẶT VẬN CHUYỂN\n", "cài đặt vận chuyển")]
    public void NormalizeUiText_ChuanHoaDung(string? input, string expected)
    {
        Assert.Equal(expected, ShopeeShippingNav.NormalizeUiText(input));
    }

    // ===== IsShippingSettingHref =====
    [Theory]
    [InlineData("/portal/all-settings/shipping", true)]
    [InlineData("https://banhang.shopee.vn/portal/all-settings/shipping?x=1", true)]
    [InlineData("/portal/all-settings/address", false)]
    [InlineData("/portal/sale/order", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsShippingSettingHref_DungSai(string? href, bool expected)
    {
        Assert.Equal(expected, ShopeeShippingNav.IsShippingSettingHref(href));
    }

    // ===== IsShippingSettingText: khớp cả có space thừa / xuống dòng / hoa-thường =====
    [Theory]
    [InlineData("Cài Đặt Vận Chuyển", true)]
    [InlineData("CÀI đặt  vận chuyển\n", true)]
    [InlineData("  cài đặt vận chuyển  ", true)]
    [InlineData("Cài Đặt", false)]
    [InlineData("Quản Lý Đơn Hàng", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsShippingSettingText_KhopChuanHoa(string? input, bool expected)
    {
        Assert.Equal(expected, ShopeeShippingNav.IsShippingSettingText(input));
    }

    // ===== IsOrderMenuText =====
    [Theory]
    [InlineData("Quản Lý Đơn Hàng", true)]
    [InlineData("  quản lý  đơn hàng \n", true)]
    [InlineData("Cài Đặt Vận Chuyển", false)]
    [InlineData("Quản Lý", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsOrderMenuText_KhopChuanHoa(string? input, bool expected)
    {
        Assert.Equal(expected, ShopeeShippingNav.IsOrderMenuText(input));
    }

    // ===== IsAddressTabText: "chứa" địa chỉ (InnerText tab có thể kèm rác badge) =====
    [Theory]
    [InlineData("Địa Chỉ", true)]
    [InlineData("địa chỉ", true)]
    [InlineData("Địa Chỉ\n", true)]
    [InlineData("Địa Chỉ 99+", true)]        // kèm text badge
    [InlineData("Địa Chỉ\n12", true)]        // badge trên dòng khác
    [InlineData("Đơn vị vận chuyển", false)]
    [InlineData("Chứng từ vận chuyển", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAddressTabText_ChuaDiaChi(string? input, bool expected)
    {
        Assert.Equal(expected, ShopeeShippingNav.IsAddressTabText(input));
    }
}
