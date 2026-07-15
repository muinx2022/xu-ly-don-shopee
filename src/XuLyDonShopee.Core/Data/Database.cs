using Microsoft.Data.Sqlite;

namespace XuLyDonShopee.Core.Data;

/// <summary>
/// Quản lý kết nối và khởi tạo cơ sở dữ liệu SQLite cục bộ.
/// </summary>
public class Database
{
    /// <summary>Đường dẫn file .db đang dùng.</summary>
    public string Path { get; }

    private readonly string _connectionString;

    /// <summary>
    /// Khởi tạo database. Nếu <paramref name="dbPath"/> null thì dùng đường dẫn mặc định
    /// (%APPDATA%\XuLyDonShopee\app.db trên Windows, ~/.config/XuLyDonShopee/app.db trên Linux).
    /// Tự tạo thư mục và các bảng nếu chưa có.
    /// </summary>
    public Database(string? dbPath = null)
    {
        Path = dbPath ?? DefaultPath();

        var dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path
        }.ToString();

        Initialize();
    }

    /// <summary>Đường dẫn file DB mặc định trong thư mục dữ liệu ứng dụng của người dùng.</summary>
    public static string DefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return System.IO.Path.Combine(appData, "XuLyDonShopee", "app.db");
    }

    /// <summary>Mở một kết nối mới (đã Open). Caller chịu trách nhiệm Dispose.</summary>
    public SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private void Initialize()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS accounts (
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    Email      TEXT NOT NULL,
    Password   TEXT NOT NULL,
    Phone      TEXT,
    Cookie     TEXT,
    Note       TEXT,
    ProxyKey   TEXT,
    Status     TEXT NOT NULL,
    CreatedAt  TEXT NOT NULL,
    UpdatedAt  TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS proxies (
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    Host       TEXT NOT NULL,
    Port       INTEGER NOT NULL,
    Username   TEXT,
    Password   TEXT,
    Type       TEXT NOT NULL,
    Status     TEXT NOT NULL,
    CreatedAt  TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS settings (
    key   TEXT PRIMARY KEY,
    value TEXT
);";
        cmd.ExecuteNonQuery();

        // Migration cho DB CŨ đã tồn tại: CREATE TABLE IF NOT EXISTS ở trên KHÔNG sửa bảng cũ, nên
        // thêm cột ProxyKey bằng ALTER TABLE ADD COLUMN (không phá dữ liệu người dùng đang có).
        EnsureColumn(conn, "accounts", "ProxyKey", "TEXT");
    }

    /// <summary>
    /// Đảm bảo bảng <paramref name="table"/> có cột <paramref name="column"/>. Nếu chưa có (DB cũ) thì
    /// <c>ALTER TABLE ... ADD COLUMN</c> — chỉ THÊM cột, KHÔNG đụng dữ liệu sẵn có. Idempotent: chạy
    /// nhiều lần không lỗi (đã có cột thì bỏ qua). Tên bảng/cột/kiểu là hằng nội bộ nên nội suy an toàn.
    /// </summary>
    private static void EnsureColumn(SqliteConnection conn, string table, string column, string columnType)
    {
        // Đọc danh sách cột hiện có; cột "name" nằm ở chỉ số 1 của PRAGMA table_info.
        var exists = false;
        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = $"PRAGMA table_info({table});";
            using var reader = pragma.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists)
        {
            return;
        }

        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {columnType};";
        alter.ExecuteNonQuery();
    }
}
