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

    /// <summary>Parse chuỗi trạng thái từ JS ("ready"/"collapsed"/"covered", không phân biệt hoa thường,
    /// kèm khoảng trắng thừa) về <see cref="LinkReadiness"/>; null/rỗng/lạ → Unknown.</summary>
    public static LinkReadiness ParseLinkReadiness(string? s) => NormalizeUiText(s) switch
    {
        "ready" => LinkReadiness.Ready,
        "collapsed" => LinkReadiness.Collapsed,
        "covered" => LinkReadiness.Covered,
        _ => LinkReadiness.Unknown,
    };

    // ===== Bước 2: so khớp địa chỉ lấy hàng theo tỉnh + text nút/checkbox/modal trong tab "Địa Chỉ" =====

    /// <summary>
    /// Rút <b>tên lõi tỉnh/thành</b> từ giá trị <c>Account.PickupAddress</c> của app (chuẩn hóa rồi bỏ
    /// tiền tố loại đơn vị hành chính nếu có): "Hà Nội" → <c>"hà nội"</c>; "TP Hồ Chí Minh" →
    /// <c>"hồ chí minh"</c>; "Thanh Hóa" → <c>"thanh hóa"</c>; "Thành phố Hà Nội" → <c>"hà nội"</c>;
    /// "Tỉnh Thanh Hóa" → <c>"thanh hóa"</c>. Null/rỗng → chuỗi rỗng.
    /// </summary>
    public static string ProvinceCoreName(string? appProvince)
    {
        var norm = NormalizeUiText(appProvince);
        if (norm.Length == 0)
        {
            return string.Empty;
        }

        // Bỏ tiền tố loại đơn vị hành chính để còn lại tên lõi. "tp." (dính) đứng trước "tp " (có dấu cách).
        string[] prefixes = { "thành phố ", "tỉnh ", "tp.", "tp " };
        foreach (var prefix in prefixes)
        {
            if (norm.StartsWith(prefix, System.StringComparison.Ordinal))
            {
                norm = norm.Substring(prefix.Length).Trim();
                break;
            }
        }

        return norm;
    }

    /// <summary>
    /// True nếu ô "Địa chỉ" (<paramref name="detailText"/>, InnerText của <c>.detail</c>, có thể nhiều
    /// dòng) thuộc tỉnh <paramref name="appProvince"/>. So khớp trên <b>dòng cuối không rỗng</b> (dòng
    /// tỉnh/thành trên Shopee, vd "Tỉnh Thanh Hóa"): chuẩn hóa rồi kiểm tra <b>chứa</b>
    /// <see cref="ProvinceCoreName"/>. Detail chỉ có 1 dòng / không tách được → so trên toàn chuỗi
    /// (fallback). appProvince rỗng hoặc detail rỗng → false. Chỉ xét dòng cuối nên KHÔNG khớp nhầm khi
    /// dòng đầu chứa tên một tỉnh khác.
    /// </summary>
    public static bool AddressDetailMatchesProvince(string? detailText, string? appProvince)
    {
        var core = ProvinceCoreName(appProvince);
        if (core.Length == 0 || string.IsNullOrWhiteSpace(detailText))
        {
            return false;
        }

        // Dòng cuối KHÔNG rỗng (tách theo '\n'; NormalizeUiText sẽ cắt '\r'/khoảng trắng thừa). Detail 1
        // dòng → dòng cuối chính là toàn chuỗi (fallback tự nhiên).
        string? lastNonEmpty = null;
        foreach (var line in detailText.Split('\n'))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                lastNonEmpty = line;
            }
        }

        var target = NormalizeUiText(lastNonEmpty ?? detailText);
        return target.Contains(core, System.StringComparison.Ordinal);
    }

    /// <summary>True nếu text (đã chuẩn hóa) chính là "địa chỉ lấy hàng" — nhãn tag đánh dấu địa chỉ
    /// đang là địa chỉ lấy hàng của shop.</summary>
    public static bool IsPickupTagText(string? s)
        => NormalizeUiText(s) == "địa chỉ lấy hàng";

    /// <summary>True nếu text (đã chuẩn hóa) chính là "sửa" (nút Sửa của một địa chỉ).</summary>
    public static bool IsEditButtonText(string? s)
        => NormalizeUiText(s) == "sửa";

    /// <summary>True nếu text (đã chuẩn hóa) chính là "hủy" (nút Hủy trong modal).</summary>
    public static bool IsCancelButtonText(string? s)
        => NormalizeUiText(s) == "hủy";

    /// <summary>True nếu text (đã chuẩn hóa) chính là "lưu" (nút Lưu trong footer modal).</summary>
    public static bool IsSaveButtonText(string? s)
        => NormalizeUiText(s) == "lưu";

    /// <summary>
    /// True nếu nhãn checkbox (đã chuẩn hóa) chính là "đặt làm địa chỉ lấy hàng" — cái cần tick. KHÔNG
    /// khớp "đặt làm địa chỉ mặc đinh" (chính tả thật của Shopee, thiếu dấu) hay "đặt làm địa chỉ trả hàng".
    /// </summary>
    public static bool IsSetPickupCheckboxText(string? s)
        => NormalizeUiText(s) == "đặt làm địa chỉ lấy hàng";

    /// <summary>True nếu tiêu đề modal (đã chuẩn hóa) chính là "sửa địa chỉ".</summary>
    public static bool IsEditAddressModalTitle(string? s)
        => NormalizeUiText(s) == "sửa địa chỉ";
}

/// <summary>Trạng thái sẵn sàng nhận click của link trong submenu (đọc từ DOM bằng JS hình học).</summary>
public enum LinkReadiness
{
    /// <summary>Không đọc được / giá trị lạ — coi như không rõ, xử lý thận trọng.</summary>
    Unknown,
    /// <summary>Link nhận click tại tâm của nó — click được ngay.</summary>
    Ready,
    /// <summary>Submenu đang CỤP (chiều cao ~0 / tâm link thuộc về phần tử ngoài submenu) — cần bung mục cha.</summary>
    Collapsed,
    /// <summary>Link đang bị phần tử khác TRONG cùng submenu đè (popover hover...) — chờ rồi thử lại, KHÔNG click mục cha.</summary>
    Covered,
}
