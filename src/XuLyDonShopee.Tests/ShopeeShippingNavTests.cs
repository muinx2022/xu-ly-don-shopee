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

    // ===== ParseLinkReadiness: chuỗi trạng thái từ JS → enum (chuẩn hóa hoa-thường/space/xuống dòng);
    //        null / rỗng / giá trị lạ → Unknown =====
    [Theory]
    [InlineData("ready", LinkReadiness.Ready)]
    [InlineData("Ready ", LinkReadiness.Ready)]
    [InlineData("collapsed", LinkReadiness.Collapsed)]
    [InlineData("COLLAPSED\n", LinkReadiness.Collapsed)]
    [InlineData("covered", LinkReadiness.Covered)]
    [InlineData("  Covered  ", LinkReadiness.Covered)]
    [InlineData("unknown", LinkReadiness.Unknown)]
    [InlineData("gibberish", LinkReadiness.Unknown)]
    [InlineData("", LinkReadiness.Unknown)]
    [InlineData(null, LinkReadiness.Unknown)]
    public void ParseLinkReadiness_ChuoiVeEnum(string? input, LinkReadiness expected)
    {
        Assert.Equal(expected, ShopeeShippingNav.ParseLinkReadiness(input));
    }

    // ===== ProvinceCoreName: tên lõi tỉnh từ Account.PickupAddress (bỏ tiền tố loại đơn vị) =====
    [Theory]
    [InlineData("Hà Nội", "hà nội")]
    [InlineData("TP Hồ Chí Minh", "hồ chí minh")]
    [InlineData("Thanh Hóa", "thanh hóa")]
    [InlineData("Thành phố Hà Nội", "hà nội")]
    [InlineData("Tỉnh Thanh Hóa", "thanh hóa")]
    [InlineData("TP. Hồ Chí Minh", "hồ chí minh")]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void ProvinceCoreName_BoTienTo(string? input, string expected)
    {
        Assert.Equal(expected, ShopeeShippingNav.ProvinceCoreName(input));
    }

    // ===== AddressDetailMatchesProvince: khớp trên DÒNG CUỐI không rỗng của .detail =====
    [Theory]
    // ví dụ người dùng: địa chỉ Thanh Hóa 3 dòng → khớp "Thanh Hóa", KHÔNG khớp "Hà Nội"
    [InlineData("xom thang , dong quang\nPhường Đông Sơn\nTỉnh Thanh Hóa", "Thanh Hóa", true)]
    [InlineData("xom thang , dong quang\nPhường Đông Sơn\nTỉnh Thanh Hóa", "Hà Nội", false)]
    // địa chỉ Hà Nội: dòng cuối "Thành phố Hà Nội" → khớp "Hà Nội"
    [InlineData("Số 76, Ngõ 76 Đường Trung Văn, Phường Đại Mỗ, Thành phố Hà Nội, Việt Nam\nPhường Đại Mỗ\nThành phố Hà Nội", "Hà Nội", true)]
    // "Thành phố Hồ Chí Minh" (dòng cuối) khớp option app "TP Hồ Chí Minh"
    [InlineData("123 Đường ABC\nPhường 1\nThành phố Hồ Chí Minh", "TP Hồ Chí Minh", true)]
    // detail 1 dòng chứa tỉnh → khớp (fallback toàn chuỗi)
    [InlineData("Số 1, Đường X, Tỉnh Thanh Hóa", "Thanh Hóa", true)]
    // BẪY: dòng ĐẦU chứa "Thành phố Hà Nội" nhưng dòng CUỐI "Tỉnh Thanh Hóa" → chỉ khớp Thanh Hóa
    [InlineData("Ngõ 5, gần Thành phố Hà Nội, Việt Nam\nPhường Đông Sơn\nTỉnh Thanh Hóa", "Hà Nội", false)]
    [InlineData("Ngõ 5, gần Thành phố Hà Nội, Việt Nam\nPhường Đông Sơn\nTỉnh Thanh Hóa", "Thanh Hóa", true)]
    // dòng cuối rỗng (xuống dòng thừa) vẫn lấy dòng "Tỉnh Thanh Hóa"
    [InlineData("Phường Đông Sơn\nTỉnh Thanh Hóa\n\n", "Thanh Hóa", true)]
    // null/rỗng detail hoặc province → false
    [InlineData(null, "Thanh Hóa", false)]
    [InlineData("", "Thanh Hóa", false)]
    [InlineData("Tỉnh Thanh Hóa", null, false)]
    [InlineData("Tỉnh Thanh Hóa", "", false)]
    public void AddressDetailMatchesProvince_DungDongCuoi(string? detail, string? province, bool expected)
    {
        Assert.Equal(expected, ShopeeShippingNav.AddressDetailMatchesProvince(detail, province));
    }

    // ===== IsSetPickupCheckboxText: CHỈ khớp "đặt làm địa chỉ lấy hàng" =====
    [Theory]
    [InlineData("Đặt làm địa chỉ lấy hàng", true)]
    [InlineData("  đặt làm  địa chỉ lấy hàng \n", true)]
    [InlineData("Đặt làm địa chỉ mặc đinh", false)]   // chính tả THẬT của Shopee (thiếu dấu) — KHÔNG khớp
    [InlineData("Đặt làm địa chỉ mặc định", false)]
    [InlineData("Đặt làm địa chỉ trả hàng", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSetPickupCheckboxText_ChiKhopLayHang(string? input, bool expected)
    {
        Assert.Equal(expected, ShopeeShippingNav.IsSetPickupCheckboxText(input));
    }

    // ===== IsPickupTagText =====
    [Theory]
    [InlineData("Địa chỉ lấy hàng", true)]
    [InlineData("  địa chỉ  lấy hàng \n", true)]
    [InlineData("Default Address", false)]
    [InlineData("Địa chỉ trả hàng", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsPickupTagText_KhopChuanHoa(string? input, bool expected)
    {
        Assert.Equal(expected, ShopeeShippingNav.IsPickupTagText(input));
    }

    // ===== IsEditButtonText =====
    [Theory]
    [InlineData("Sửa", true)]
    [InlineData("  sửa \n", true)]
    [InlineData("Xóa", false)]
    [InlineData("Lưu", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsEditButtonText_KhopChuanHoa(string? input, bool expected)
    {
        Assert.Equal(expected, ShopeeShippingNav.IsEditButtonText(input));
    }

    // ===== IsCancelButtonText =====
    [Theory]
    [InlineData("Hủy", true)]
    [InlineData("  hủy ", true)]
    [InlineData("Lưu", false)]
    [InlineData("Sửa", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsCancelButtonText_KhopChuanHoa(string? input, bool expected)
    {
        Assert.Equal(expected, ShopeeShippingNav.IsCancelButtonText(input));
    }

    // ===== IsSaveButtonText =====
    [Theory]
    [InlineData("Lưu", true)]
    [InlineData("  lưu \n", true)]
    [InlineData("Hủy", false)]
    [InlineData("Lưu và thoát", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSaveButtonText_KhopChuanHoa(string? input, bool expected)
    {
        Assert.Equal(expected, ShopeeShippingNav.IsSaveButtonText(input));
    }

    // ===== IsEditAddressModalTitle =====
    [Theory]
    [InlineData("Sửa Địa chỉ", true)]
    [InlineData("  sửa  địa chỉ \n", true)]
    [InlineData("Thêm Địa chỉ", false)]
    [InlineData("Địa chỉ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsEditAddressModalTitle_KhopChuanHoa(string? input, bool expected)
    {
        Assert.Equal(expected, ShopeeShippingNav.IsEditAddressModalTitle(input));
    }

    // ===== IsConfirmButtonText: nút "Đồng ý" (primary) của hộp xác nhận đổi địa chỉ lấy hàng =====
    [Theory]
    [InlineData("Đồng ý", true)]
    [InlineData("  đồng ý ", true)]
    [InlineData("ĐỒNG Ý\n", true)]
    [InlineData("đồng", false)]
    [InlineData("Kiểm tra chi tiết", false)]
    [InlineData("Huỷ", false)]
    [InlineData("Lưu", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsConfirmButtonText_KhopChuanHoa(string? input, bool expected)
    {
        Assert.Equal(expected, ShopeeShippingNav.IsConfirmButtonText(input));
    }

    // ===== IsCheckDetailButtonText: nút "Kiểm tra chi tiết" (dấu hiệu nhận đúng hộp xác nhận) =====
    [Theory]
    [InlineData("Kiểm tra chi tiết", true)]
    [InlineData("  kiểm tra  chi tiết \n", true)]
    [InlineData("KIỂM TRA CHI TIẾT", true)]
    [InlineData("kiểm tra", false)]
    [InlineData("Đồng ý", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsCheckDetailButtonText_KhopChuanHoa(string? input, bool expected)
    {
        Assert.Equal(expected, ShopeeShippingNav.IsCheckDetailButtonText(input));
    }
}
