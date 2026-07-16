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
