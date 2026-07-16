using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.App.ViewModels;

/// <summary>
/// Một dòng hiển thị (ĐỌC-CHỈ) trên bảng màn "Đơn hàng": bọc <see cref="OrderRow"/> + nhãn tài khoản,
/// tính sẵn các chuỗi hiển thị (sản phẩm "(+n)", tổng tiền định dạng ₫, giờ sync giờ địa phương) để
/// DataGrid bind thẳng và để dựng dòng CSV. Không cần INotifyPropertyChanged: mỗi lần lọc, các dòng
/// được TẠO LẠI thay vì sửa tại chỗ. <c>partial</c> để <see cref="OpenSlipCommand"/> được sinh từ
/// <c>[RelayCommand]</c> (không cần kế thừa ObservableObject).
/// </summary>
public sealed partial class OrderRowViewModel
{
    /// <summary>Định dạng số VND: nhóm nghìn bằng dấu chấm (₫1.234.567).</summary>
    private static readonly NumberFormatInfo VndFormat = new() { NumberGroupSeparator = ".", NumberGroupSizes = new[] { 3 } };

    private readonly OrderRow _row;

    /// <summary>Callback báo trạng thái ra màn (OrdersViewModel gán vào StatusMessage). Null → im lặng.</summary>
    private readonly Action<string>? _notify;

    public OrderRowViewModel(OrderRow row, string accountLabel, Action<string>? notify = null)
    {
        _row = row;
        AccountLabel = accountLabel;
        _notify = notify;
    }

    /// <summary>Nhãn tài khoản (email) — do ViewModel map từ account_id.</summary>
    public string AccountLabel { get; }

    public string OrderSn => _row.OrderSn;
    public string Buyer => _row.BuyerUsername ?? string.Empty;

    /// <summary>"Tên SP đầu (+n)" với n = số sản phẩm còn lại khi đơn có &gt;1 sản phẩm.</summary>
    public string Product => BuildProduct(_row.ItemSummary, _row.ItemCount);

    /// <summary>Tổng tiền: ưu tiên số đã parse (₫1.234.567), thiếu thì dùng nguyên văn.</summary>
    public string Total => BuildTotal(_row.TotalPrice, _row.TotalPriceText);

    /// <summary>Cột "Ước tính" = "Số tiền cuối cùng" từ trang chi tiết: ưu tiên số đã parse (₫...), thiếu thì
    /// nguyên văn, rỗng nếu chưa lấy (đơn "Đã hủy" hoặc chưa mở chi tiết).</summary>
    public string Estimate => BuildTotal(_row.FinalAmount, _row.FinalAmountText);

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

    /// <summary>Chuyển sang dòng xuất CSV (đúng thứ tự cột như bảng — "Ước tính" ngay sau "Tổng tiền").</summary>
    public OrderExportRow ToExportRow() => new(
        AccountLabel, OrderSn, Buyer, Product, Total, Estimate, Payment, Status, Note, Carrier, Tracking, SyncedAtDisplay);

    /// <summary>
    /// Đường dẫn file PDF phiếu giao đã tải lúc xử lý đơn: <c>{SlipDownloadDir}\{sanitize(order_sn)}.pdf</c>.
    /// KHỚP TUYỆT ĐỐI cách <c>SaveSlipAsync</c> đặt tên (cùng hằng <see cref="ShopeeShippingNav.SlipDownloadDir"/>
    /// + cùng <see cref="ShopeeShippingNav.SanitizeFileName"/>). Chỉ SUY RA đường dẫn — KHÔNG kiểm tồn tại lúc
    /// render (tránh IO mỗi dòng); chỉ kiểm khi bấm mở trong <see cref="OpenSlip"/>.
    /// </summary>
    public string SlipPath => Path.Combine(
        ShopeeShippingNav.SlipDownloadDir, ShopeeShippingNav.SanitizeFileName(OrderSn) + ".pdf");

    /// <summary>
    /// Link "In phiếu": mở file PDF phiếu đã tải bằng ứng dụng mặc định của Windows (ShellExecute — KHÔNG chạy
    /// binary tự build nên WDAC không chặn). Kiểm <see cref="File.Exists"/> ngay lúc bấm: thiếu file → báo nhẹ
    /// (đơn chưa xử lý / phiếu chưa tải), KHÔNG lỗi. Lỗi mở (máy không có app PDF mặc định...) → nuốt, báo qua
    /// callback, KHÔNG ném.
    /// </summary>
    [RelayCommand]
    private void OpenSlip()
    {
        if (!File.Exists(SlipPath))
        {
            _notify?.Invoke($"Chưa có file phiếu đã tải cho đơn {OrderSn} (đơn chưa xử lý hoặc phiếu chưa tải).");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(SlipPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _notify?.Invoke($"Không mở được file phiếu {SlipPath}: {ex.Message}");
        }
    }

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
