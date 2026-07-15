using System;
using System.IO;
using System.Linq;
using XuLyDonShopee.App.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// Test phần THUẦN của <see cref="ActivityLog"/>: định dạng dòng, ring-buffer cap, ghi file. Truyền
/// <c>uiPost: a =&gt; a()</c> để mutate Entries đồng bộ (không cần Avalonia dispatcher).
/// </summary>
public class ActivityLogTests
{
    /// <summary>Cấp một thư mục tạm cho test, tự dọn khi Dispose.</summary>
    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"xlds_log_{Guid.NewGuid():N}");

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch { /* bỏ qua lỗi dọn */ }
        }
    }

    [Fact]
    public void FormatLine_DungDinhDang_GioNguonNoiDung()
    {
        var entry = new LogEntry(new DateTime(2026, 7, 15, 9, 8, 7), "abc@mail.com", "Đang mở trình duyệt");

        Assert.Equal("09:08:07 [abc@mail.com] Đang mở trình duyệt", ActivityLog.FormatLine(entry));
        // Display là hàm tiện dùng lại FormatLine.
        Assert.Equal(ActivityLog.FormatLine(entry), entry.Display);
    }

    [Fact]
    public void Append_VuotCap_GiuDungSoDongMoiNhat()
    {
        using var dir = new TempDir();
        var log = new ActivityLog(dir.Path, uiPost: a => a(), maxEntries: 3);

        for (int i = 0; i < 5; i++)
        {
            log.Append("tk", $"m{i}");
        }

        // Ring-buffer: chỉ giữ 3 dòng, là 3 dòng MỚI NHẤT (m2, m3, m4); m0, m1 bị loại.
        Assert.Equal(3, log.Entries.Count);
        Assert.Equal(new[] { "m2", "m3", "m4" }, log.Entries.Select(e => e.Message).ToArray());
    }

    [Fact]
    public void Append_GhiFile_TonTaiVaChuaCacDong()
    {
        using var dir = new TempDir();
        var log = new ActivityLog(dir.Path, uiPost: a => a());

        log.Append("tk1", "dong mot");
        log.Append("tk2", "dong hai");

        // File hoatdong-YYYYMMDD.log được tạo trong thư mục và chứa cả hai dòng.
        Assert.True(File.Exists(log.CurrentLogPath));
        var content = File.ReadAllText(log.CurrentLogPath);
        Assert.Contains("[tk1] dong mot", content);
        Assert.Contains("[tk2] dong hai", content);

        // Đúng mẫu tên file hoatdong-*.log.
        var files = Directory.GetFiles(dir.Path, "hoatdong-*.log");
        Assert.Single(files);
    }
}
