using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Core.Data;

/// <summary>
/// Lưu/đọc cấu hình dạng key-value trong bảng <c>settings</c>.
/// </summary>
public class SettingsRepository
{
    /// <summary>Key lưu danh sách API key KiotProxy (mỗi dòng một key).</summary>
    public const string KiotProxyApiKeys = "kiotproxy_api_key";

    /// <summary>Key: số tài khoản mỗi lô của bộ "Chạy tự động".</summary>
    public const string AutoRunBatchSize = "autorun_batch_size";

    /// <summary>Key: số phút nghỉ giữa các lô của bộ "Chạy tự động".</summary>
    public const string AutoRunGapMinutes = "autorun_gap_minutes";

    /// <summary>Key: có tự Sync đơn hàng trong mỗi lượt "Chạy tự động" hay không.</summary>
    public const string AutoRunDoSync = "autorun_do_sync";

    /// <summary>Key: có tự Xử lý đơn (arrange + in phiếu) trong mỗi lượt "Chạy tự động" hay không.</summary>
    public const string AutoRunDoProcess = "autorun_do_process";

    /// <summary>Key: thư mục lưu phiếu/hóa đơn người dùng chọn (rỗng/thiếu → mặc định cạnh app.db).</summary>
    private const string InvoiceFolderKey = "invoice_folder";

    /// <summary>Key: chu kỳ theo dõi đơn (phút) giữa các lần tự đọc "Chờ Lấy Hàng" (thiếu/lạ → 30, kẹp [1,1440]).</summary>
    private const string OrderIntervalMinutesKey = "order_interval_minutes";

    private readonly Database _db;

    public SettingsRepository(Database db) => _db = db;

    /// <summary>Đọc danh sách API key KiotProxy đã lưu (đã chuẩn hóa).</summary>
    public List<string> GetKiotProxyKeys() => KiotProxyKeyParser.Parse(Get(KiotProxyApiKeys));

    /// <summary>Lưu danh sách API key KiotProxy (chuẩn hóa rồi ghép mỗi dòng một key).</summary>
    public void SetKiotProxyKeys(IEnumerable<string> keys)
        => Set(KiotProxyApiKeys, KiotProxyKeyParser.Join(keys));

    /// <summary>Đọc cấu hình "Chạy tự động" từ các khóa rời (thiếu/hỏng → mặc định an toàn, đã chuẩn hóa).</summary>
    public AutoRunSettings GetAutoRunSettings() => AutoRunSettings.Parse(
        Get(AutoRunBatchSize),
        Get(AutoRunGapMinutes),
        Get(AutoRunDoSync),
        Get(AutoRunDoProcess));

    /// <summary>Ghi cấu hình "Chạy tự động" ra các khóa rời (chuẩn hóa trước khi ghi).</summary>
    public void SetAutoRunSettings(AutoRunSettings settings)
    {
        var s = AutoRunSettings.Normalize(settings.BatchSize, settings.GapMinutes, settings.DoSync, settings.DoProcess);
        Set(AutoRunBatchSize, AutoRunSettings.IntToStorage(s.BatchSize));
        Set(AutoRunGapMinutes, AutoRunSettings.IntToStorage(s.GapMinutes));
        Set(AutoRunDoSync, AutoRunSettings.BoolToStorage(s.DoSync));
        Set(AutoRunDoProcess, AutoRunSettings.BoolToStorage(s.DoProcess));
    }

    /// <summary>
    /// Thư mục lưu phiếu/hóa đơn THỰC DÙNG: giá trị người dùng đã chọn (đã trim) nếu có, ngược lại mặc định
    /// cạnh app.db (<see cref="Database.DefaultInvoiceDir"/>). NGUỒN DUY NHẤT cho cả 3 nơi — xử lý đơn (lưu
    /// phiếu), link "In phiếu" ở màn Đơn hàng (mở phiếu) và ô hiển thị ở Cài đặt — để không nơi nào lệch chỗ.
    /// </summary>
    public string GetInvoiceFolder()
    {
        var folder = AppGeneralSettings.Parse(Get(InvoiceFolderKey), null).InvoiceFolder; // trim; rỗng nếu chưa đặt
        return string.IsNullOrEmpty(folder) ? _db.DefaultInvoiceDir() : folder;
    }

    /// <summary>Lưu thư mục lưu hóa đơn người dùng chọn (rỗng → xóa cấu hình ⇒ quay về mặc định app).</summary>
    public void SetInvoiceFolder(string? path)
    {
        var folder = AppGeneralSettings.Parse(path, null).InvoiceFolder; // trim
        Set(InvoiceFolderKey, string.IsNullOrEmpty(folder) ? null : folder);
    }

    /// <summary>Chu kỳ theo dõi đơn (phút): config đã kẹp [1,1440]; thiếu/lạ → 30.</summary>
    public int GetOrderIntervalMinutes()
        => AppGeneralSettings.Parse(null, Get(OrderIntervalMinutesKey)).OrderIntervalMinutes;

    /// <summary>Lưu chu kỳ theo dõi đơn (phút) — chuẩn hóa (kẹp [1,1440]) trước khi ghi.</summary>
    public void SetOrderIntervalMinutes(int minutes)
    {
        var norm = AppGeneralSettings.Normalize(null, minutes).OrderIntervalMinutes;
        Set(OrderIntervalMinutesKey, AppGeneralSettings.IntToStorage(norm));
    }

    /// <summary>Lấy giá trị theo key, trả null nếu chưa có.</summary>
    public string? Get(string key)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $key;";
        cmd.Parameters.AddWithValue("$key", key);
        var result = cmd.ExecuteScalar();
        return result is string s ? s : null;
    }

    /// <summary>Ghi (thêm mới hoặc cập nhật) giá trị theo key.</summary>
    public void Set(string key, string? value)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO settings (key, value) VALUES ($key, $value)
                            ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", (object?)value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}
