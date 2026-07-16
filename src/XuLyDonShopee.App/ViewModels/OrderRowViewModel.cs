using System.Globalization;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.App.ViewModels;

/// <summary>
/// Một dòng hiển thị (ĐỌC-CHỈ) trên bảng màn "Đơn hàng": bọc <see cref="OrderRow"/> + nhãn tài khoản,
/// tính sẵn các chuỗi hiển thị (sản phẩm "(+n)", tổng tiền định dạng ₫, giờ sync giờ địa phương) để
/// DataGrid bind thẳng và để dựng dòng CSV. Không cần INotifyPropertyChanged: mỗi lần lọc, các dòng
/// được TẠO LẠI thay vì sửa tại chỗ.
/// </summary>
public sealed class OrderRowViewModel
{
    /// <summary>Định dạng số VND: nhóm nghìn bằng dấu chấm (₫1.234.567).</summary>
    private static readonly NumberFormatInfo VndFormat = new() { NumberGroupSeparator = ".", NumberGroupSizes = new[] { 3 } };

    private readonly OrderRow _row;

    public OrderRowViewModel(OrderRow row, string accountLabel)
    {
        _row = row;
        AccountLabel = accountLabel;
    }

    /// <summary>Nhãn tài khoản (email) — do ViewModel map từ account_id.</summary>
    public string AccountLabel { get; }

    public string OrderSn => _row.OrderSn;
    public string Buyer => _row.BuyerUsername ?? string.Empty;

    /// <summary>"Tên SP đầu (+n)" với n = số sản phẩm còn lại khi đơn có &gt;1 sản phẩm.</summary>
    public string Product => BuildProduct(_row.ItemSummary, _row.ItemCount);

    /// <summary>Tổng tiền: ưu tiên số đã parse (₫1.234.567), thiếu thì dùng nguyên văn.</summary>
    public string Total => BuildTotal(_row.TotalPrice, _row.TotalPriceText);

    public string Payment => _row.PaymentMethod ?? string.Empty;
    public string Status => _row.Status ?? string.Empty;

    /// <summary>Cột "Mô tả/Lý do hủy": ưu tiên lý do hủy (nếu có) rồi tới mô tả trạng thái.</summary>
    public string Note => !string.IsNullOrWhiteSpace(_row.CancelReason)
        ? _row.CancelReason!
        : (_row.StatusDescription ?? string.Empty);

    public string Carrier => _row.Carrier ?? string.Empty;
    public string Tracking => _row.TrackingNumber ?? string.Empty;

    /// <summary>Giờ sync theo giờ địa phương, định dạng dd/MM/yyyy HH:mm (rỗng nếu chưa có).</summary>
    public string SyncedAtDisplay => _row.SyncedAt == default
        ? string.Empty
        : _row.SyncedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Chuyển sang dòng xuất CSV (đúng thứ tự cột như bảng).</summary>
    public OrderExportRow ToExportRow() => new(
        AccountLabel, OrderSn, Buyer, Product, Total, Payment, Status, Note, Carrier, Tracking, SyncedAtDisplay);

    /// <summary>Dựng chuỗi cột "Sản phẩm": tên SP đầu, thêm "(+n)" (n = số SP còn lại) khi đơn nhiều SP.</summary>
    public static string BuildProduct(string? summary, int itemCount)
    {
        var name = summary ?? string.Empty;
        if (itemCount > 1)
        {
            return string.IsNullOrEmpty(name) ? $"(+{itemCount - 1})" : $"{name} (+{itemCount - 1})";
        }
        return name;
    }

    /// <summary>Dựng chuỗi cột "Tổng tiền": ₫ + số có phân nhóm nghìn; thiếu số thì dùng nguyên văn.</summary>
    public static string BuildTotal(long? totalPrice, string? totalPriceText)
        => totalPrice is not null
            ? "₫" + totalPrice.Value.ToString("#,##0", VndFormat)
            : (totalPriceText ?? string.Empty);
}
