using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace XuLyDonShopee.App.Services;

/// <summary>Trạng thái vòng đời của một phiên mở trang bán hàng.</summary>
public enum SessionState
{
    /// <summary>Chưa chạy / đã dừng / đã kết thúc bình thường (đóng cửa sổ).</summary>
    Stopped,

    /// <summary>Đang chuẩn bị: chọn proxy, tải/khởi chạy trình duyệt.</summary>
    Opening,

    /// <summary>Đã mở trình duyệt; đang tự đăng nhập / bắt cookie / theo dõi đơn.</summary>
    Running,

    /// <summary>Kết thúc do lỗi (xem <see cref="IAccountSession.LastError"/>).</summary>
    Error
}

/// <summary>
/// Một phiên độc lập phục vụ MỘT tài khoản: mở trình duyệt riêng (Brave/profile/CDP port/proxy riêng),
/// tự đăng nhập, bắt cookie, theo dõi đơn — chạy nền song song với các phiên khác.
/// <para>
/// Tách interface nhỏ để <see cref="AccountSessionManager"/> có thể được test bằng stub (không cần
/// Brave/Playwright thật).
/// </para>
/// </summary>
public interface IAccountSession
{
    /// <summary>Id tài khoản mà phiên này phục vụ.</summary>
    long AccountId { get; }

    /// <summary>Trạng thái vòng đời hiện tại.</summary>
    SessionState State { get; }

    /// <summary>Dòng trạng thái hiển thị theo tài khoản (null = ẩn).</summary>
    string? StatusText { get; }

    /// <summary>Số đơn "Chờ Lấy Hàng" đọc gần nhất (null = chưa đọc được / chưa đăng nhập).</summary>
    int? ToShipCount { get; }

    /// <summary>Thông điệp lỗi gần nhất (có khi <see cref="State"/> == <see cref="SessionState.Error"/>).</summary>
    string? LastError { get; }

    /// <summary>Tiến trình Brave/Chromium của phiên (để Plan B đưa cửa sổ ra trước). Null nếu chưa/không có.</summary>
    Process? BraveProcess { get; }

    /// <summary>Phát khi State/StatusText/ToShipCount/LastError đổi — manager/VM nghe để cập nhật UI.</summary>
    event Action? Changed;

    /// <summary>Phát khi vừa lưu cookie vào DB cho tài khoản (id) — VM nghe để làm mới danh sách trên UI thread.</summary>
    event Action<long>? CookieSaved;

    /// <summary>Bắt đầu phiên. Nếu đang chuẩn bị/đang chạy thì bỏ qua (idempotent — không mở trùng).</summary>
    Task StartAsync();

    /// <summary>Dừng phiên: hủy vòng lặp, đóng &amp; kill cây tiến trình Brave, đưa về <see cref="SessionState.Stopped"/>.</summary>
    Task StopAsync();
}
