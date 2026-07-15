using Microsoft.Data.Sqlite;
using XuLyDonShopee.Core.Data;
using XuLyDonShopee.Core.Models;

namespace XuLyDonShopee.Tests;

/// <summary>
/// Test migration cột <c>ProxyKey</c> cho bảng <c>accounts</c>: DB CŨ (chưa có cột) phải được thêm cột
/// bằng ALTER TABLE ADD COLUMN mà KHÔNG mất dữ liệu; chạy nhiều lần idempotent.
/// </summary>
public class DatabaseMigrationTests
{
    /// <summary>Dựng schema CŨ (bảng accounts KHÔNG có cột ProxyKey) rồi ghi 1 dòng dữ liệu sẵn.</summary>
    private static void CreateOldSchemaWithRow(string path, string email)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
CREATE TABLE accounts (
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    Email      TEXT NOT NULL,
    Password   TEXT NOT NULL,
    Phone      TEXT,
    Cookie     TEXT,
    Note       TEXT,
    Status     TEXT NOT NULL,
    CreatedAt  TEXT NOT NULL,
    UpdatedAt  TEXT NOT NULL
);";
            cmd.ExecuteNonQuery();
        }

        using (var ins = conn.CreateCommand())
        {
            ins.CommandText = @"INSERT INTO accounts (Email, Password, Phone, Cookie, Note, Status, CreatedAt, UpdatedAt)
                                VALUES ($e, 'matkhau', '0900', 'cookie-cu', 'ghi chu cu', 'HoatDong',
                                        '2020-01-01T00:00:00.0000000', '2020-01-01T00:00:00.0000000');";
            ins.Parameters.AddWithValue("$e", email);
            ins.ExecuteNonQuery();
        }
    }

    /// <summary>Kiểm tra bảng có cột hay không qua PRAGMA table_info (cột name ở chỉ số 1).</summary>
    private static bool HasColumn(string path, string table, string column)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    [Fact]
    public void KhoiTao_DbCu_ThieuProxyKey_DuocThemCot_KhongMatDuLieu()
    {
        using var temp = new TempDatabase();
        CreateOldSchemaWithRow(temp.Path, "old@x.com");

        // Trước migration: schema cũ chưa có cột ProxyKey.
        Assert.False(HasColumn(temp.Path, "accounts", "ProxyKey"));

        // Khởi tạo Database mới trỏ cùng file → Initialize() chạy migration.
        _ = new Database(temp.Path);

        // Sau migration: đã có cột ProxyKey.
        Assert.True(HasColumn(temp.Path, "accounts", "ProxyKey"));

        // Dữ liệu cũ CÒN NGUYÊN; ProxyKey mặc định null.
        var repo = new AccountRepository(new Database(temp.Path));
        var all = repo.GetAll();
        Assert.Single(all);
        var acc = all[0];
        Assert.Equal("old@x.com", acc.Email);
        Assert.Equal("cookie-cu", acc.Cookie);
        Assert.Equal("ghi chu cu", acc.Note);
        Assert.Equal(AccountStatus.HoatDong, acc.Status);
        Assert.Null(acc.ProxyKey);
    }

    [Fact]
    public void KhoiTao_DbCu_SauMigration_GhiDocProxyKeyBinhThuong()
    {
        using var temp = new TempDatabase();
        CreateOldSchemaWithRow(temp.Path, "old@x.com");

        var repo = new AccountRepository(new Database(temp.Path)); // migration chạy tại đây
        var acc = repo.GetAll()[0];

        acc.ProxyKey = "KEY-SAU-MIGRATION";
        repo.Update(acc);

        Assert.Equal("KEY-SAU-MIGRATION", repo.GetById(acc.Id)!.ProxyKey);
    }

    [Fact]
    public void KhoiTao_NhieuLan_Idempotent_KhongNem()
    {
        using var temp = new TempDatabase();

        var ex = Record.Exception(() =>
        {
            _ = new Database(temp.Path); // tạo mới (đã có ProxyKey trong CREATE TABLE)
            _ = new Database(temp.Path); // chạy lại migration lần 2 — không được ném
            _ = new Database(temp.Path); // lần 3
        });

        Assert.Null(ex);
        Assert.True(HasColumn(temp.Path, "accounts", "ProxyKey"));
    }
}
