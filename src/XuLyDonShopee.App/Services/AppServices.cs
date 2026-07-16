using System.IO;
using XuLyDonShopee.Core.Data;

namespace XuLyDonShopee.App.Services;

/// <summary>
/// Gom Database + các repository, khởi tạo một lần và truyền vào ViewModel.
/// (Bước đầu không dùng DI container.)
/// </summary>
public class AppServices
{
    public Database Database { get; }
    public AccountRepository Accounts { get; }
    public ProxyRepository Proxies { get; }
    public SettingsRepository Settings { get; }

    /// <summary>Kho đơn hàng đã sync (bảng <c>orders</c>) — phiên ghi qua đây khi Sync Đơn hàng.</summary>
    public OrdersRepository Orders { get; }

    /// <summary>Nhật ký hoạt động của app (panel UI + ghi file cạnh database). Các phiên nạp log qua đây.</summary>
    public ActivityLog Log { get; }

    /// <summary>Quản lý các phiên mở trang bán hàng song song (mỗi tài khoản một phiên độc lập).
    /// App shutdown gọi <see cref="AccountSessionManager.StopAllAsync"/> để kill hết Brave.</summary>
    public AccountSessionManager Sessions { get; }

    public AppServices(string? dbPath = null)
    {
        Database = new Database(dbPath);
        Accounts = new AccountRepository(Database);
        Proxies = new ProxyRepository(Database);
        Settings = new SettingsRepository(Database);
        Orders = new OrdersRepository(Database);
        // Log đặt TRƯỚC Sessions vì các phiên sẽ nạp log qua Log khi chạy. Thư mục logs cạnh file database.
        var logDir = Path.Combine(Path.GetDirectoryName(Database.Path) ?? ".", "logs");
        Directory.CreateDirectory(logDir);
        Log = new ActivityLog(logDir);
        // Tạo sau các repository vì factory phiên đọc Accounts/Proxies/Settings khi chạy.
        Sessions = new AccountSessionManager(this);
    }
}
