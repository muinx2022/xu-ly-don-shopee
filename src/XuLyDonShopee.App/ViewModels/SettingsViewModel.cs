using CommunityToolkit.Mvvm.ComponentModel;
using XuLyDonShopee.App.Services;

namespace XuLyDonShopee.App.ViewModels;

/// <summary>
/// Màn hình cài đặt (bản "thuần vỏ"): dựng đúng giao diện redesign nhưng KHÔNG có logic backend,
/// KHÔNG lưu xuống DB. Các toggle chỉ giữ state trong bộ nhớ (reset khi khởi tạo lại). Slider chu kỳ
/// quét, thư mục hóa đơn và bộ chọn theme chỉ hiển thị tĩnh.
/// Quản lý API key KiotProxy đã chuyển sang màn Proxy (<see cref="ProxiesViewModel"/>).
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    // Giữ chữ ký constructor để ViewLocator / MainViewModel gọi được; không dùng services (thuần vỏ).
    public SettingsViewModel(AppServices services)
    {
    }

    // ===== Toggle "Xử lý đơn hàng" — state trong bộ nhớ =====
    [ObservableProperty]
    private bool _autoPrint = true;

    [ObservableProperty]
    private bool _notifyNewOrder = true;

    [ObservableProperty]
    private bool _autoConfirm;

    // ===== Nhãn hiển thị tĩnh (không có logic) =====
    public string ScanIntervalText => "30 giây";
    public string InvoiceFolderText => @"D:\Shopee\HoaDon\2026";
    public string ThemeChoice => "light";

    /// <summary>No-op để <see cref="MainViewModel"/> gọi khi chuyển sang màn Cài đặt (không nạp gì).</summary>
    public void Reload()
    {
    }
}
