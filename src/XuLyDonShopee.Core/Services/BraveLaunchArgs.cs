using XuLyDonShopee.Core.Models;

namespace XuLyDonShopee.Core.Services;

/// <summary>
/// Dựng danh sách tham số dòng lệnh để tự khởi chạy <b>Brave thật</b> (hoặc Chromium đóng gói) rồi
/// nối vào bằng CDP. Hàm thuần (không IO/không trạng thái) nên test được độc lập.
/// <para>
/// Chủ đích KHÔNG thêm <c>--enable-automation</c>, <c>--headless</c>, <c>--remote-debugging-pipe</c>:
/// khi tự launch Brave như trình duyệt người dùng bình thường thì KHÔNG hiện thanh
/// "Brave is being controlled by automated test software" và <c>navigator.webdriver</c> giữ <c>false</c>
/// — đỡ bị Shopee nhận diện là bot.
/// </para>
/// <para>
/// GIỮ <c>--disable-blink-features=AutomationControlled</c> (và <c>AutomationControlled</c> trong
/// <c>--disable-features</c>): KHÔNG được bỏ. Vì app luôn nối vào qua <c>--remote-debugging-port</c>
/// (CDP), chính kết nối CDP khiến Blink bật cờ AutomationControlled → <c>navigator.webdriver</c> thành
/// <c>true</c>. Cờ này ép webdriver về <c>false</c> (đã smoke xác nhận: bỏ cờ → webdriver=true). Đánh đổi:
/// cờ gây thanh vàng "unsupported command-line flag", nhưng chống nhận diện bot quan trọng hơn.
/// </para>
/// </summary>
public static class BraveLaunchArgs
{
    /// <summary>
    /// Trả về danh sách tham số dòng lệnh cho Brave/Chromium:
    /// cổng gỡ lỗi CDP, thư mục hồ sơ riêng, các cờ chống nhận diện tự động hóa, và proxy (nếu có).
    /// </summary>
    /// <param name="userDataDir">Thư mục hồ sơ persistent riêng cho tài khoản.</param>
    /// <param name="remoteDebuggingPort">Cổng CDP; truyền <c>0</c> để Chromium tự chọn cổng trống
    /// (đọc lại cổng thật từ file <c>DevToolsActivePort</c>).</param>
    /// <param name="proxy">Proxy đã chọn; <c>null</c> = đi IP máy. User/pass KHÔNG nhét vào chuỗi
    /// <c>--proxy-server</c> (Chromium không hỗ trợ) — xác thực xử lý qua CDP ở tầng trên.</param>
    public static IReadOnlyList<string> BuildBraveArgs(string userDataDir, int remoteDebuggingPort, ProxyEntry? proxy)
    {
        var args = new List<string>
        {
            $"--remote-debugging-port={remoteDebuggingPort}",
            $"--user-data-dir={userDataDir}",
            // Chống nhận diện tự động hóa: tắt cờ AutomationControlled của Blink. BẮT BUỘC giữ — vì nối
            // CDP (--remote-debugging-port) làm Blink bật cờ này khiến navigator.webdriver=true; cờ dưới
            // ép webdriver về false (smoke đã xác nhận). Đánh đổi: có thanh "unsupported command-line flag".
            "--disable-blink-features=AutomationControlled",
            "--no-first-run",
            "--no-default-browser-check",
            "--disable-features=Translate,AutomationControlled",
            // Mở cửa sổ Brave ở kích thước THƯỜNG (không --start-maximized) theo yêu cầu người dùng.
            // Locale tiếng Việt đặt bằng cờ trình duyệt (KHÔNG hook navigator.languages bằng JS —
            // hook JS tự tạo dấu hiệu lộ bot).
            "--lang=vi-VN",
            // In IM LẶNG ra máy in mặc định của Windows: khi trang phiếu giao gọi window.print(), Brave
            // KHÔNG hiện hộp thoại "Print" mà in thẳng máy in mặc định (dùng để tự in phiếu giao hàng).
            // Không có máy in mặc định thì lệnh in không ra giấy nhưng KHÔNG phá luồng.
            "--kiosk-printing",
            // KHÔNG chặn popup: nút "In phiếu giao" mở tab phiếu bằng window.open — nếu bị chặn popup thì
            // tab phiếu không mở ra (không bắt được để tải/in). Cho phép popup để tab phiếu luôn mở.
            "--disable-popup-blocking",
        };

        if (proxy is not null)
        {
            // Proxy đặt qua --proxy-server (http:// hoặc socks5:// theo Type). KHÔNG kèm user:pass.
            args.Add($"--proxy-server={ProxyHealthChecker.ToProxyAddress(proxy)}");
        }

        return args;
    }
}
