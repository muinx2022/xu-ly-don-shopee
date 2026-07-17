using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.Core.Models;

namespace XuLyDonShopee.App.ViewModels;

/// <summary>
/// Màn "Cài đặt" (HOẠT ĐỘNG THẬT): hai mục lưu xuống DB qua <see cref="SettingsRepository"/>:
/// <list type="bullet">
/// <item><b>Thư mục lưu hóa đơn</b> — chọn qua folder picker; là NGUỒN DUY NHẤT cho nơi xử lý đơn LƯU phiếu
/// và link "In phiếu" ở màn Đơn hàng (mặc định <c>{thư mục app.db}\Phieu-giao-hang</c>).</item>
/// <item><b>Chu kỳ theo dõi đơn (phút)</b> — số phút giữa các lần tự đọc "Chờ Lấy Hàng"; áp cho các phiên
/// mở SAU khi lưu.</item>
/// </list>
/// Quản lý API key KiotProxy đã chuyển sang màn Proxy (<see cref="ProxiesViewModel"/>).
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        Reload();
    }

    /// <summary>Đường dẫn thư mục lưu hóa đơn đang dùng (config đã chọn hoặc mặc định cạnh app.db).</summary>
    [ObservableProperty]
    private string _invoiceFolder = string.Empty;

    /// <summary>Chu kỳ theo dõi đơn (phút) — số phút giữa các lần tự đọc số "Chờ Lấy Hàng".</summary>
    [ObservableProperty]
    private int _orderIntervalMinutes = AppGeneralSettings.DefaultOrderIntervalMinutes;

    /// <summary>Thông báo sau khi lưu (null = ẩn).</summary>
    [ObservableProperty]
    private string? _savedMessage;

    /// <summary>MainViewModel gọi khi chuyển sang màn này: nạp lại cấu hình từ DB (thư mục hóa đơn + chu kỳ).</summary>
    public void Reload()
    {
        InvoiceFolder = _services.Settings.GetInvoiceFolder();
        OrderIntervalMinutes = _services.Settings.GetOrderIntervalMinutes();
        SavedMessage = null;
    }

    /// <summary>
    /// Nút "Chọn…": mở folder picker (thư mục hiện tại làm điểm mở đầu). Chọn xong → LƯU config + cập nhật
    /// hiển thị theo giá trị đã lưu. Hủy → không đổi gì. KHÔNG đọc lại field mutable sau await (chỉ dùng biến
    /// cục bộ + đọc lại từ repo).
    /// </summary>
    [RelayCommand]
    private async Task ChooseInvoiceFolderAsync()
    {
        var picked = await DialogService.PickInvoiceFolderAsync(InvoiceFolder);
        if (string.IsNullOrWhiteSpace(picked))
        {
            return; // người dùng bấm Hủy
        }

        _services.Settings.SetInvoiceFolder(picked);
        InvoiceFolder = _services.Settings.GetInvoiceFolder(); // phản ánh giá trị đã lưu (đã chuẩn hóa)
        SavedMessage = "Đã lưu thư mục lưu hóa đơn.";
    }

    /// <summary>Nút "Lưu": chuẩn hóa (kẹp [1,1440]) + ghi chu kỳ theo dõi đơn xuống DB, phản ánh lại lên form.</summary>
    [RelayCommand]
    private void SaveInterval()
    {
        _services.Settings.SetOrderIntervalMinutes(OrderIntervalMinutes);
        OrderIntervalMinutes = _services.Settings.GetOrderIntervalMinutes(); // phản ánh bản đã kẹp
        SavedMessage = "Đã lưu chu kỳ theo dõi đơn (áp cho các phiên mở sau khi lưu).";
    }
}
