using System.Diagnostics;
using System.Text.Json;
using Microsoft.Playwright;
using XuLyDonShopee.Core.Models;

namespace XuLyDonShopee.Core.Services;

/// <summary>
/// Một phiên đăng nhập đang mở (cửa sổ trình duyệt). Giữ tham chiếu tới browser/context
/// và cho phép bắt cookie khi người dùng đã đăng nhập xong. Đóng phiên qua <c>DisposeAsync</c>.
/// </summary>
public interface ILoginSession : IAsyncDisposable
{
    /// <summary>Lấy toàn bộ cookie hiện có của phiên dưới dạng JSON (định dạng <see cref="CookieJson"/>).</summary>
    Task<string> CaptureCookiesJsonAsync();

    /// <summary>Task hoàn tất khi người dùng đóng cửa sổ trình duyệt (tiến trình Brave thoát / CDP ngắt).</summary>
    Task Closed { get; }

    /// <summary>True nếu cửa sổ trình duyệt đã đóng.</summary>
    bool IsClosed { get; }
}

/// <summary>
/// Mở trang Shopee Seller Centre bằng <b>Brave thật</b> (tự khởi chạy tiến trình Brave rồi nối vào
/// qua CDP — <see cref="IBrowserType.ConnectOverCDPAsync"/>), định tuyến qua proxy nếu có, để người
/// dùng tự đăng nhập; sau đó bắt cookie phiên.
/// <para>
/// Vì tự launch Brave như trình duyệt bình thường (KHÔNG để Playwright launch với cờ
/// <c>--enable-automation</c>) nên KHÔNG hiện thanh "controlled by automated test software" và
/// <c>navigator.webdriver</c> giữ <c>false</c>. Thêm một init script "stealth" vá các dấu hiệu bot
/// khác (plugins, languages, window.chrome, WebGL...). <b>Không đảm bảo 100%</b> né được anti-bot của
/// Shopee (CDP/fingerprint/hành vi/IP vẫn có thể bị dò) — đây là best-effort.
/// </para>
/// <para>
/// Ưu tiên mở <b>Brave</b> nếu đã cài trên máy; nếu không có Brave dùng <b>Chromium đóng gói</b> của
/// Playwright (cùng cơ chế CDP).
/// </para>
/// </summary>
public class ShopeeLoginService
{
    /// <summary>URL trang bán hàng (Shopee Seller Centre).</summary>
    public const string SellerUrl = "https://banhang.shopee.vn/";

    /// <summary>
    /// Init script "stealth" tiêm vào mọi trang TRƯỚC khi tài liệu chạy, vá các dấu hiệu tự động hóa
    /// hay bị anti-bot dò. Brave thật (không cờ automation) vốn đã đặt <c>navigator.webdriver=false</c>;
    /// script này là lớp bảo hiểm + vá thêm plugins/languages/window.chrome/WebGL.
    /// </summary>
    private const string StealthJs = @"
(() => {
  try { Object.defineProperty(Object.getPrototypeOf(navigator), 'webdriver', { get: () => false }); } catch (e) {}
  try { Object.defineProperty(navigator, 'webdriver', { get: () => false }); } catch (e) {}
  try { Object.defineProperty(navigator, 'languages', { get: () => ['vi-VN', 'vi', 'en-US', 'en'] }); } catch (e) {}
  try { window.chrome = window.chrome || { runtime: {} }; } catch (e) {}
  try {
    Object.defineProperty(navigator, 'plugins', { get: () => [1, 2, 3, 4, 5] });
    Object.defineProperty(navigator, 'mimeTypes', { get: () => [1, 2] });
  } catch (e) {}
  try {
    const originalQuery = navigator.permissions.query.bind(navigator.permissions);
    navigator.permissions.query = (parameters) =>
      parameters && parameters.name === 'notifications'
        ? Promise.resolve({ state: Notification.permission })
        : originalQuery(parameters);
  } catch (e) {}
  try {
    const getParameter = WebGLRenderingContext.prototype.getParameter;
    WebGLRenderingContext.prototype.getParameter = function (parameter) {
      if (parameter === 37445) return 'Intel Inc.';           // UNMASKED_VENDOR_WEBGL
      if (parameter === 37446) return 'Intel Iris OpenGL Engine'; // UNMASKED_RENDERER_WEBGL
      return getParameter.call(this, parameter);
    };
  } catch (e) {}
})();";

    /// <summary>
    /// Đảm bảo có sẵn trình duyệt để mở. Nếu máy đã có <b>Brave</b> thì trả về ngay (0) mà
    /// <b>không tải</b> Chromium đóng gói. Ngược lại tải Chromium của Playwright (~150MB lần đầu;
    /// idempotent — đã cài thì trả về nhanh). Trả về exit code (0 = thành công); bọc try/catch,
    /// trả code khác 0 khi lỗi để tầng gọi thông báo.
    /// </summary>
    public int EnsureBrowserInstalled()
    {
        // Có Brave → không cần tải Chromium đóng gói (đỡ ~150MB).
        if (BrowserLocator.FindBraveExecutable() != null)
        {
            return 0;
        }

        try
        {
            return Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// Mô tả trình duyệt sẽ được dùng (để log/kiểm tra): <c>"Brave (&lt;path&gt;)"</c> nếu tìm thấy
    /// Brave, ngược lại <c>"Chromium đóng gói của Playwright"</c>.
    /// </summary>
    public static string DescribeBrowser()
    {
        var brave = BrowserLocator.FindBraveExecutable();
        return brave != null
            ? $"Brave ({brave})"
            : "Chromium đóng gói của Playwright";
    }

    /// <summary>
    /// Mở một cửa sổ trình duyệt (Brave nếu có, không thì Chromium đóng gói) tới trang bán hàng bằng
    /// <b>hồ sơ persistent</b> đặt tại <paramref name="userDataDir"/> (mỗi tài khoản một thư mục riêng)
    /// — nhờ đó lần sau mở lại vẫn còn đăng nhập — qua proxy đã chọn, rồi trả về phiên đang mở.
    /// Cơ chế: tự khởi chạy tiến trình Brave với cờ stealth + <c>--user-data-dir</c> +
    /// <c>--remote-debugging-port</c> + <c>--proxy-server</c>, chờ CDP sẵn sàng, nối vào qua
    /// <see cref="IBrowserType.ConnectOverCDPAsync"/>. Ném <see cref="InvalidOperationException"/>
    /// (message tiếng Việt) nếu không mở được.
    /// </summary>
    public async Task<ILoginSession> OpenAsync(string userDataDir, ProxyEntry? proxy, CancellationToken ct = default)
    {
        IPlaywright? playwright = null;
        Process? process = null;
        IBrowser? browser = null;

        try
        {
            playwright = await Playwright.CreateAsync().ConfigureAwait(false);

            // Ưu tiên Brave đã cài; không có → Chromium đóng gói (cùng cơ chế CDP).
            var exePath = BrowserLocator.FindBraveExecutable();
            if (exePath == null)
            {
                EnsureChromiumInstalledForFallback();
                exePath = playwright.Chromium.ExecutablePath;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    throw new InvalidOperationException(
                        "Không tìm thấy Brave và cũng chưa tải được Chromium đóng gói của Playwright.");
                }
            }

            // Đọc cổng CDP thật từ DevToolsActivePort → xóa file cũ để tránh đọc nhầm cổng phiên trước.
            var portFile = Path.Combine(userDataDir, "DevToolsActivePort");
            try { if (File.Exists(portFile)) File.Delete(portFile); } catch { /* bỏ qua */ }

            // Launch Brave/Chromium với cổng 0 (Chromium tự chọn cổng trống, ghi vào DevToolsActivePort).
            var psi = new ProcessStartInfo(exePath) { UseShellExecute = false };
            foreach (var arg in BraveLaunchArgs.BuildBraveArgs(userDataDir, 0, proxy))
            {
                psi.ArgumentList.Add(arg);
            }

            process = Process.Start(psi)
                      ?? throw new InvalidOperationException("Không khởi chạy được tiến trình trình duyệt.");
            process.EnableRaisingEvents = true;

            // Chờ Brave mở cổng CDP (đọc cổng thật) rồi chờ endpoint /json/version sẵn sàng.
            var port = await WaitForDevToolsPortAsync(portFile, process, TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
            await WaitForCdpEndpointAsync(port, TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);

            // Nối vào Brave đang chạy qua CDP.
            browser = await playwright.Chromium
                .ConnectOverCDPAsync($"http://127.0.0.1:{port}").ConfigureAwait(false);

            // Brave chạy --user-data-dir → có sẵn context mặc định = hồ sơ persistent.
            var context = browser.Contexts.Count > 0
                ? browser.Contexts[0]
                : await browser.NewContextAsync().ConfigureAwait(false);

            // Tiêm init script stealth TRƯỚC khi điều hướng.
            await context.AddInitScriptAsync(StealthJs).ConfigureAwait(false);

            var page = context.Pages.Count > 0
                ? context.Pages[0]
                : await context.NewPageAsync().ConfigureAwait(false);

            // Proxy có user:pass → xử lý xác thực qua CDP (không hiện hộp thoại đăng nhập proxy).
            if (!string.IsNullOrEmpty(proxy?.Username))
            {
                await SetupProxyAuthAsync(context, page, proxy!).ConfigureAwait(false);
            }

            try
            {
                await page.GotoAsync(SellerUrl, new PageGotoOptions
                {
                    Timeout = 60000,
                    WaitUntil = WaitUntilState.DOMContentLoaded
                }).ConfigureAwait(false);
            }
            catch
            {
                // Nuốt lỗi timeout/điều hướng — vẫn giữ cửa sổ mở để người dùng tự thao tác.
            }

            return new LoginSession(playwright, browser, context, process);
        }
        catch (Exception ex)
        {
            // Dọn dẹp: ngắt CDP, KILL cả cây tiến trình Brave (tránh Brave mồ côi giữ khóa hồ sơ),
            // giải phóng Playwright.
            if (browser is not null)
            {
                try { await browser.CloseAsync().ConfigureAwait(false); } catch { /* bỏ qua */ }
            }
            if (process is { HasExited: false })
            {
                try { process.Kill(entireProcessTree: true); } catch { /* bỏ qua */ }
            }
            try { process?.Dispose(); } catch { /* bỏ qua */ }
            try { playwright?.Dispose(); } catch { /* bỏ qua */ }

            throw new InvalidOperationException(
                "Không mở được trình duyệt Shopee. Kiểm tra đã cài Brave hoặc Chromium và kết nối mạng/proxy. " +
                "Chi tiết: " + ex.Message, ex);
        }
    }

    /// <summary>
    /// Chờ Brave khởi động xong và ghi cổng CDP vào file <c>DevToolsActivePort</c> (dòng đầu = cổng).
    /// Poll có timeout; nếu tiến trình thoát sớm (thường do hồ sơ đang bị một cửa sổ Brave khác khóa)
    /// thì ném lỗi tiếng Việt.
    /// </summary>
    private static async Task<int> WaitForDevToolsPortAsync(
        string portFile, Process process, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    "Trình duyệt thoát ngay khi khởi động (thường do hồ sơ đang bị một cửa sổ Brave khác khóa). " +
                    "Hãy đóng hết cửa sổ Brave rồi thử lại.");
            }

            try
            {
                if (File.Exists(portFile))
                {
                    var lines = await File.ReadAllLinesAsync(portFile, ct).ConfigureAwait(false);
                    if (lines.Length > 0 && int.TryParse(lines[0].Trim(), out var port) && port > 0)
                    {
                        return port;
                    }
                }
            }
            catch (IOException)
            {
                // File đang được Brave ghi dở — thử lại vòng sau.
            }

            await Task.Delay(150, ct).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            "Quá thời gian chờ trình duyệt mở cổng gỡ lỗi (DevToolsActivePort).");
    }

    /// <summary>
    /// Chờ endpoint CDP HTTP <c>/json/version</c> trả 200 (báo trình duyệt đã sẵn sàng nhận kết nối CDP).
    /// Poll có timeout; hết giờ thì ném lỗi tiếng Việt.
    /// </summary>
    private static async Task WaitForCdpEndpointAsync(int port, TimeSpan timeout, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var url = $"http://127.0.0.1:{port}/json/version";
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var res = await http.GetAsync(url, ct).ConfigureAwait(false);
                if (res.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
                // Chưa sẵn sàng — thử lại vòng sau.
            }

            await Task.Delay(150, ct).ConfigureAwait(false);
        }

        throw new InvalidOperationException("Quá thời gian chờ endpoint CDP sẵn sàng.");
    }

    /// <summary>
    /// Xử lý <b>xác thực proxy</b> (proxy có user:pass) qua CDP để không hiện hộp thoại đăng nhập proxy.
    /// Bật <c>Fetch</c> với <c>handleAuthRequests</c>: nghe <c>Fetch.authRequired</c> → trả credential
    /// khi nguồn là "Proxy"; nghe <c>Fetch.requestPaused</c> → tiếp tục request (để không chặn request
    /// thường). Fire-and-forget các lệnh CDP trong handler (event là đồng bộ).
    /// </summary>
    private static async Task SetupProxyAuthAsync(IBrowserContext context, IPage page, ProxyEntry proxy)
    {
        var cdp = await context.NewCDPSessionAsync(page).ConfigureAwait(false);
        await cdp.SendAsync("Fetch.enable", new Dictionary<string, object>
        {
            ["handleAuthRequests"] = true
        }).ConfigureAwait(false);

        var username = proxy.Username ?? string.Empty;
        var password = proxy.Password ?? string.Empty;

        cdp.Event("Fetch.authRequired").OnEvent += (_, e) =>
        {
            if (e is not { } json)
            {
                return;
            }

            if (!TryGetString(json, "requestId", out var requestId))
            {
                return;
            }

            var isProxy = json.TryGetProperty("authChallenge", out var challenge)
                          && challenge.TryGetProperty("source", out var source)
                          && string.Equals(source.GetString(), "Proxy", StringComparison.OrdinalIgnoreCase);

            var response = isProxy
                ? new Dictionary<string, object>
                {
                    ["response"] = "ProvideCredentials",
                    ["username"] = username,
                    ["password"] = password
                }
                : new Dictionary<string, object> { ["response"] = "Default" };

            _ = SafeSendAsync(cdp, "Fetch.continueWithAuth", new Dictionary<string, object>
            {
                ["requestId"] = requestId,
                ["authChallengeResponse"] = response
            });
        };

        cdp.Event("Fetch.requestPaused").OnEvent += (_, e) =>
        {
            if (e is not { } json)
            {
                return;
            }

            if (!TryGetString(json, "requestId", out var requestId))
            {
                return;
            }

            _ = SafeSendAsync(cdp, "Fetch.continueRequest", new Dictionary<string, object>
            {
                ["requestId"] = requestId
            });
        };
    }

    /// <summary>Đọc một thuộc tính chuỗi từ JSON của sự kiện CDP (an toàn, không ném).</summary>
    private static bool TryGetString(JsonElement json, string name, out string value)
    {
        if (json.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString() ?? string.Empty;
            return value.Length > 0;
        }

        value = string.Empty;
        return false;
    }

    /// <summary>Gửi lệnh CDP nuốt lỗi (session có thể đã ngắt / request đã hủy giữa chừng).</summary>
    private static async Task SafeSendAsync(ICDPSession cdp, string method, Dictionary<string, object> args)
    {
        try { await cdp.SendAsync(method, args).ConfigureAwait(false); }
        catch { /* bỏ qua */ }
    }

    /// <summary>
    /// Tải Chromium đóng gói của Playwright cho nhánh fallback (khi máy không có Brave). Nuốt lỗi —
    /// nếu thực sự thiếu, bước lấy <c>ExecutablePath</c>/launch tiếp theo sẽ ném và được xử lý ở tầng trên.
    /// </summary>
    private static void EnsureChromiumInstalledForFallback()
    {
        try { Microsoft.Playwright.Program.Main(new[] { "install", "chromium" }); }
        catch { /* bỏ qua — bước launch tiếp theo sẽ ném nếu thật sự thiếu */ }
    }

    /// <summary>
    /// Phiên đăng nhập <b>sở hữu tiến trình Brave</b>: <see cref="Closed"/> hoàn tất khi tiến trình
    /// thoát / CDP ngắt / context đóng; <see cref="DisposeAsync"/> ngắt CDP và KILL cả cây tiến trình
    /// Brave để không để lại tiến trình mồ côi giữ khóa hồ sơ.
    /// </summary>
    private sealed class LoginSession : ILoginSession
    {
        private readonly IPlaywright _playwright;
        private readonly IBrowser _browser;
        private readonly IBrowserContext _context;
        private readonly Process _process;

        // Hoàn tất khi cửa sổ đóng (tiến trình Brave thoát / CDP ngắt). RunContinuationsAsynchronously
        // để không chạy tiếp phần chờ ngay trong callback sự kiện của Playwright/Process.
        private readonly TaskCompletionSource _closedTcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public LoginSession(IPlaywright playwright, IBrowser browser, IBrowserContext context, Process process)
        {
            _playwright = playwright;
            _browser = browser;
            _context = context;
            _process = process;

            // Người dùng đóng cửa sổ → tiến trình Brave thoát (tín hiệu chính); kèm CDP ngắt / context đóng.
            _process.EnableRaisingEvents = true;
            _process.Exited += (_, _) => _closedTcs.TrySetResult();
            _browser.Disconnected += (_, _) => _closedTcs.TrySetResult();
            _context.Close += (_, _) => _closedTcs.TrySetResult();

            // Phòng trường hợp tiến trình đã thoát trước khi gắn handler.
            if (_process.HasExited)
            {
                _closedTcs.TrySetResult();
            }
        }

        public Task Closed => _closedTcs.Task;

        public bool IsClosed => _closedTcs.Task.IsCompleted;

        public async Task<string> CaptureCookiesJsonAsync()
        {
            // Không truyền URL = lấy tất cả cookie trong context.
            var raw = await _context.CookiesAsync().ConfigureAwait(false);

            var list = raw
                .Select(c => new StoredCookie(
                    c.Name,
                    c.Value,
                    c.Domain,
                    c.Path,
                    c.Expires,
                    c.HttpOnly,
                    c.Secure,
                    c.SameSite.ToString()))
                .ToList();

            return CookieJson.Serialize(list);
        }

        public async ValueTask DisposeAsync()
        {
            // Ngắt CDP trước (đóng kết nối Playwright ↔ Brave).
            try { await _browser.CloseAsync().ConfigureAwait(false); } catch { /* bỏ qua */ }

            // KILL cả cây tiến trình Brave để không để lại tiến trình mồ côi giữ khóa --user-data-dir
            // (nếu còn, lần mở sau sẽ lỗi khóa hồ sơ).
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch { /* bỏ qua */ }
            try { _process.Dispose(); } catch { /* bỏ qua */ }

            try { _playwright.Dispose(); } catch { /* bỏ qua */ }
        }
    }
}
