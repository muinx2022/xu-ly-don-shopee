namespace XuLyDonShopee.Core.Services;

/// <summary>
/// Dò đường dẫn file thực thi của trình duyệt <b>Brave</b> đã cài trên máy (đa nền tảng
/// Windows/Linux/macOS). Dùng để ưu tiên mở Brave thay vì tải Chromium đóng gói (~150MB).
/// </summary>
public static class BrowserLocator
{
    /// <summary>
    /// Trả về đường dẫn đầu tiên trong <paramref name="candidates"/> mà
    /// <paramref name="exists"/> trả về <c>true</c>. Bỏ qua các phần tử null/rỗng/toàn khoảng trắng.
    /// Trả về <c>null</c> nếu không phần tử nào tồn tại.
    /// </summary>
    /// <remarks>
    /// Hàm lõi thuần (không trực tiếp đụng hệ thống file — nhận predicate <paramref name="exists"/>)
    /// nên test được độc lập với máy thật.
    /// </remarks>
    internal static string? FindFirstExisting(IEnumerable<string> candidates, Func<string, bool> exists)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            if (exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Tìm đường dẫn tới file thực thi Brave theo HĐH hiện tại. Trả về <c>null</c> nếu không
    /// tìm thấy (ví dụ chưa cài Brave, hoặc HĐH không nằm trong danh sách hỗ trợ).
    /// </summary>
    public static string? FindBraveExecutable()
    {
        return FindFirstExisting(BuildCandidates(), File.Exists);
    }

    /// <summary>Dựng danh sách đường dẫn ứng viên của Brave theo HĐH.</summary>
    private static IEnumerable<string> BuildCandidates()
    {
        if (OperatingSystem.IsWindows())
        {
            const string relative = @"BraveSoftware\Brave-Browser\Application\brave.exe";

            var programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
            var programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");

            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                yield return Path.Combine(programFiles, relative);
            }
            if (!string.IsNullOrWhiteSpace(programFilesX86))
            {
                yield return Path.Combine(programFilesX86, relative);
            }
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                yield return Path.Combine(localAppData, relative);
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            yield return "/usr/bin/brave-browser";
            yield return "/usr/bin/brave-browser-stable";
            yield return "/usr/bin/brave";
            yield return "/opt/brave.com/brave/brave-browser";
            yield return "/snap/bin/brave";
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "/Applications/Brave Browser.app/Contents/MacOS/Brave Browser";
        }
    }
}
