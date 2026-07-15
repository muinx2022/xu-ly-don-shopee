namespace XuLyDonShopee.Core.Services;

/// <summary>
/// Hàm thuần so khớp text/href của menu &amp; tab khi điều hướng tới "Cài Đặt Vận Chuyển" → tab "Địa Chỉ"
/// trên trang bán hàng Shopee. Tách khỏi Playwright để test được: chỉ nhận vào chuỗi đã lấy từ DOM
/// (InnerText/href) và trả về kết quả so khớp. Text lấy từ DOM có thể kèm xuống dòng/khoảng trắng thừa
/// hoặc rác badge → mọi so khớp đều chuẩn hóa trước qua <see cref="NormalizeUiText"/>.
/// </summary>
public static class ShopeeShippingNav
{
    /// <summary>
    /// Chuẩn hóa text lấy từ UI để so khớp bền: <c>null</c> → rỗng; thay mọi cụm khoảng trắng (kể cả
    /// xuống dòng/tab) bằng một dấu cách, <see cref="string.Trim()"/>, rồi hạ về chữ thường
    /// (<see cref="string.ToLowerInvariant"/>).
    /// </summary>
    public static string NormalizeUiText(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return string.Empty;
        }

        // Split theo mọi ký tự khoảng trắng (space, tab, xuống dòng, non-breaking...) rồi ghép lại bằng
        // một dấu cách → gộp mọi cụm whitespace về 1 space, tự loại khoảng trắng đầu/cuối.
        var collapsed = string.Join(' ', s.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Trim().ToLowerInvariant();
    }

    /// <summary>True nếu <paramref name="href"/> trỏ tới trang Cài đặt vận chuyển
    /// (chứa <c>/portal/all-settings/shipping</c>).</summary>
    public static bool IsShippingSettingHref(string? href)
        => href is not null && href.Contains("/portal/all-settings/shipping", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>True nếu text (đã chuẩn hóa) chính là "cài đặt vận chuyển".</summary>
    public static bool IsShippingSettingText(string? s)
        => NormalizeUiText(s) == "cài đặt vận chuyển";

    /// <summary>True nếu text (đã chuẩn hóa) chính là "quản lý đơn hàng" (mục cha ở menu trái).</summary>
    public static bool IsOrderMenuText(string? s)
        => NormalizeUiText(s) == "quản lý đơn hàng";

    /// <summary>
    /// True nếu text (đã chuẩn hóa) <b>chứa</b> "địa chỉ" — InnerText của tab "Địa Chỉ" có thể kèm rác
    /// badge nên dùng "chứa" thay vì bằng tuyệt đối. Hai tab còn lại ("đơn vị vận chuyển" /
    /// "chứng từ vận chuyển") không chứa chuỗi này nên không bị nhầm.
    /// </summary>
    public static bool IsAddressTabText(string? s)
        => NormalizeUiText(s).Contains("địa chỉ", System.StringComparison.Ordinal);
}
