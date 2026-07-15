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

    /// <summary>
    /// Tiến trình Brave/Chromium mà phiên đang sở hữu. Dùng ở tầng App để (Plan B) đưa cửa sổ ra trước
    /// (focus) và để kill dự phòng khi dừng phiên. Null nếu phiên không giữ tiến trình.
    /// </summary>
    Process? BraveProcess { get; }

    /// <summary>
    /// Số cửa sổ/tab (Pages) đang mở của phiên. Dùng làm tín hiệu "người dùng đã đóng hết cửa sổ"
    /// đáng tin hơn "tiến trình Brave chết" (Brave có thể còn chạy nền). Trả 0 nếu context đã ngắt.
    /// </summary>
    int OpenPageCount { get; }

    /// <summary>
    /// <b>Tự đăng nhập kiểu người</b>: nếu trang đang hiển thị form đăng nhập Shopee thì dò ô user &amp;
    /// password, di chuột theo <b>đường cong</b> (<see cref="HumanMouse"/>) tới từng ô rồi click, gõ
    /// <b>từng ký tự có delay</b> (<see cref="HumanTyping"/>), cuối cùng bấm nút đăng nhập. KHÔNG xử lý
    /// captcha/OTP (để người dùng tự làm).
    /// <para>
    /// <b>Graceful — không bao giờ ném:</b> nếu đã đăng nhập sẵn, hoặc không tìm thấy ô đăng nhập, hoặc
    /// bất kỳ lỗi nào xảy ra → trả <c>false</c> (bỏ qua, để người dùng tự thao tác tay). Trả <c>true</c>
    /// khi đã điền được user &amp; password.
    /// </para>
    /// </summary>
    Task<bool> TryHumanLoginAsync(string user, string password, CancellationToken ct = default);

    /// <summary>
    /// Đọc số đơn <b>"Chờ Lấy Hàng"</b> trong to-do box của trang bán hàng (Seller Centre).
    /// <para>
    /// <b>Gate:</b> chỉ đọc khi đã đăng nhập (to-do box chỉ có sau đăng nhập) — chưa đăng nhập → trả
    /// <c>null</c> và KHÔNG reload (tránh phá thao tác đăng nhập/captcha của người dùng). Nếu
    /// <paramref name="reload"/> = <c>true</c> thì reload lại trang trước khi đọc (lấy số mới nhất).
    /// </para>
    /// <para>
    /// <b>Graceful — không bao giờ ném:</b> chưa đăng nhập, không tìm thấy ô (Shopee đổi selector), hoặc
    /// bất kỳ lỗi nào → trả <c>null</c>. Trả về số đơn (≥ 0) khi đọc được.
    /// </para>
    /// </summary>
    Task<int?> ReadToShipCountAsync(bool reload, CancellationToken ct = default);
}

/// <summary>
/// Mở trang Shopee Seller Centre bằng <b>Brave thật</b> (tự khởi chạy tiến trình Brave rồi nối vào
/// qua CDP — <see cref="IBrowserType.ConnectOverCDPAsync"/>), định tuyến qua proxy nếu có, để người
/// dùng tự đăng nhập; sau đó bắt cookie phiên.
/// <para>
/// Vì tự launch Brave như trình duyệt bình thường (KHÔNG để Playwright launch với cờ
/// <c>--enable-automation</c>) nên KHÔNG hiện thanh "controlled by automated test software" và
/// <c>navigator.webdriver</c> giữ <c>false</c> — <b>do chính Brave thật</b>, không do vá JS.
/// CHỦ ĐÍCH <b>không tiêm init script vá fingerprint</b> (plugins/WebGL/webdriver/window.chrome...) vì
/// các vá đó lại <b>tự tạo dấu hiệu lộ bot</b> (own-property <c>navigator.webdriver</c>, hàm mất
/// <c>"[native code]"</c>, plugin giả không phải <c>Plugin</c>). Dựa vào Brave thật vốn đã sạch
/// (webdriver=false, plugins/window.chrome/WebGL thật) + hành vi kiểu người (Plan 2). Locale VN đặt qua
/// cờ <c>--lang=vi-VN</c>. <b>Không đảm bảo 100%</b> né được anti-bot của Shopee (CDP/fingerprint/hành
/// vi/IP vẫn có thể bị dò) — đây là best-effort.
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

            // CHỦ ĐÍCH KHÔNG tiêm init script vá fingerprint: Brave thật đã sạch (webdriver=false,
            // plugins/window.chrome/WebGL thật), vá lại chỉ tự tạo dấu hiệu lộ bot. Locale VN đặt qua
            // cờ --lang=vi-VN trong BraveLaunchArgs.

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

        public Process? BraveProcess => _process;

        public int OpenPageCount
        {
            get
            {
                // Context đã ngắt (browser chết) → coi như không còn cửa sổ.
                try { return _context.Pages.Count; }
                catch { return 0; }
            }
        }

        // Selector ô đăng nhập Shopee (thử theo thứ tự; selector Shopee CÓ THỂ ĐỔI → luôn có fallback,
        // không thấy gì thì bỏ qua để người dùng tự nhập tay).
        private static readonly string[] UserSelectors =
        {
            "input[name='loginKey']",       // ô user chính của Shopee
            "input[type='text']",           // fallback: ô text đầu tiên
            "input[type='email']",
            "input[type='tel']",
        };

        private static readonly string[] PasswordSelectors =
        {
            "input[name='password']",       // ô mật khẩu chính
            "input[type='password']",       // fallback theo type
        };

        private static readonly string[] SubmitSelectors =
        {
            "button[type='submit']",        // nút submit chính
            "button:has-text('Đăng nhập')", // fallback: nút chứa chữ "Đăng nhập"
            "button:has-text('ĐĂNG NHẬP')",
        };

        public async Task<bool> TryHumanLoginAsync(string user, string password, CancellationToken ct = default)
        {
            try
            {
                // 1) Đã đăng nhập sẵn (profile bền) → bỏ qua, không tự điền.
                if (ShopeeLoginCookies.IsLoggedIn(await CaptureCookiesJsonAsync().ConfigureAwait(false)))
                {
                    return false;
                }

                var page = _context.Pages.Count > 0 ? _context.Pages[0] : null;
                if (page is null)
                {
                    return false;
                }

                // 2) Dò ô đăng nhập (timeout ngắn). Không thấy → return false (người dùng tự nhập tay).
                var userInput = await FindFirstVisibleAsync(page, UserSelectors, 5000, ct).ConfigureAwait(false);
                if (userInput is null)
                {
                    return false;
                }

                var passInput = await FindFirstVisibleAsync(page, PasswordSelectors, 3000, ct).ConfigureAwait(false);
                if (passInput is null)
                {
                    return false;
                }

                // Random tạo nội bộ — app dùng ngẫu nhiên thật (không cần seed).
                var rng = new Random();

                // Con trỏ chuột bắt đầu ở giữa viewport (đọc kích thước thật; null → mặc định 640x360).
                var vp = page.ViewportSize;
                double mx = vp is not null ? vp.Width / 2.0 : 640;
                double my = vp is not null ? vp.Height / 2.0 : 360;

                // 3) Điền user rồi password: di chuột cong + click + gõ từng ký tự có delay.
                (mx, my) = await HumanFillAsync(page, userInput, user, mx, my, rng, ct).ConfigureAwait(false);
                (mx, my) = await HumanFillAsync(page, passInput, password, mx, my, rng, ct).ConfigureAwait(false);

                // 4) Bấm nút đăng nhập (nếu tìm thấy). KHÔNG xử lý captcha/OTP.
                var submit = await FindFirstVisibleAsync(page, SubmitSelectors, 3000, ct).ConfigureAwait(false);
                if (submit is not null)
                {
                    await HumanMoveAndClickAsync(page, submit, mx, my, rng, ct).ConfigureAwait(false);
                }

                return true;
            }
            catch
            {
                // Bất kỳ lỗi nào → bỏ qua, để người dùng tự thao tác (KHÔNG phá luồng).
                return false;
            }
        }

        /// <summary>
        /// Dò phần tử đầu tiên <b>đang hiển thị</b> khớp một trong <paramref name="selectors"/> (thử lần
        /// lượt), poll tới khi hết <paramref name="timeoutMs"/>. Trả <c>null</c> nếu không thấy. Nuốt lỗi
        /// từng selector (selector có thể không hợp lệ trên trang hiện tại).
        /// </summary>
        private static async Task<IElementHandle?> FindFirstVisibleAsync(
            IPage page, string[] selectors, int timeoutMs, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            do
            {
                ct.ThrowIfCancellationRequested();
                foreach (var sel in selectors)
                {
                    try
                    {
                        var el = await page.QuerySelectorAsync(sel).ConfigureAwait(false);
                        if (el is not null && await el.IsVisibleAsync().ConfigureAwait(false))
                        {
                            return el;
                        }
                    }
                    catch
                    {
                        // Selector không dùng được trên trang này — thử selector kế.
                    }
                }
                await Task.Delay(200, ct).ConfigureAwait(false);
            }
            while (DateTime.UtcNow < deadline);

            return null;
        }

        /// <summary>
        /// Điền một ô kiểu người: di chuột cong tới ô + click, rồi gõ <b>từng ký tự</b> với delay ngẫu
        /// nhiên (<see cref="HumanTyping.NextCharDelayMs"/>). Trả về vị trí chuột mới (tâm ô).
        /// </summary>
        private static async Task<(double X, double Y)> HumanFillAsync(
            IPage page, IElementHandle el, string text, double mx, double my, Random rng, CancellationToken ct)
        {
            (mx, my) = await HumanMoveAndClickAsync(page, el, mx, my, rng, ct).ConfigureAwait(false);

            foreach (var ch in text)
            {
                ct.ThrowIfCancellationRequested();
                // Gõ TỪNG ký tự (KHÔNG fill/dán) + delay kiểu người.
                await page.Keyboard.TypeAsync(ch.ToString()).ConfigureAwait(false);
                await Task.Delay(HumanTyping.NextCharDelayMs(rng), ct).ConfigureAwait(false);
            }

            return (mx, my);
        }

        /// <summary>
        /// Di chuột theo <b>đường cong</b> từ (<paramref name="mx"/>,<paramref name="my"/>) tới tâm ô
        /// (+jitter nhỏ), tự <c>Mouse.MoveAsync</c> <b>từng điểm</b> (KHÔNG dùng <c>steps</c> lớn để đi
        /// thẳng), rồi click kiểu người (down + trễ + up). Trả về vị trí chuột cuối (điểm đích).
        /// </summary>
        private static async Task<(double X, double Y)> HumanMoveAndClickAsync(
            IPage page, IElementHandle el, double mx, double my, Random rng, CancellationToken ct)
        {
            var box = await el.BoundingBoxAsync().ConfigureAwait(false);

            double tx, ty;
            if (box is not null)
            {
                // Tâm ô + jitter nhỏ (không luôn nhấn đúng chính giữa).
                tx = box.X + box.Width / 2.0 + (rng.NextDouble() - 0.5) * Math.Min(box.Width * 0.3, 20);
                ty = box.Y + box.Height / 2.0 + (rng.NextDouble() - 0.5) * Math.Min(box.Height * 0.3, 8);
            }
            else
            {
                // Không lấy được bounding box → kéo phần tử vào tầm nhìn, giữ nguyên vị trí chuột.
                try { await el.ScrollIntoViewIfNeededAsync().ConfigureAwait(false); } catch { /* bỏ qua */ }
                tx = mx;
                ty = my;
            }

            // Số điểm theo khoảng cách (đường dài → nhiều điểm), giới hạn [12, 60] cho mượt.
            var dist = Math.Sqrt((tx - mx) * (tx - mx) + (ty - my) * (ty - my));
            var steps = Math.Clamp((int)(dist / 8) + 10, 12, 60);

            foreach (var (px, py) in HumanMouse.GeneratePath(mx, my, tx, ty, steps, rng))
            {
                ct.ThrowIfCancellationRequested();
                // Đi TỪNG điểm (steps mặc định = 1) để đường thật sự cong theo path đã sinh.
                await page.Mouse.MoveAsync((float)px, (float)py).ConfigureAwait(false);
                await Task.Delay(rng.Next(5, 26), ct).ConfigureAwait(false); // 5–25ms giữa các điểm
            }

            // Click kiểu người: nhấn giữ một khoảng ngắn rồi nhả.
            await page.Mouse.DownAsync().ConfigureAwait(false);
            await Task.Delay(rng.Next(40, 121), ct).ConfigureAwait(false);
            await page.Mouse.UpAsync().ConfigureAwait(false);

            return (tx, ty);
        }

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

        // Selector to-do box của Seller Centre (thử theo thứ tự; Shopee CÓ THỂ ĐỔI → luôn có fallback,
        // không thấy thì trả null, KHÔNG ném, KHÔNG phá phiên).
        //   - Chính: duyệt các .to-do-box-item tìm cái có .item-desc == "Chờ Lấy Hàng" → đọc .item-title.
        //   - Fallback theo href: a[href*='type=toship'][href*='to_process'] .item-title.
        private const string ToShipItemSelector = ".to-do-box-item";
        private const string ItemDescSelector = ".item-desc";
        private const string ItemTitleSelector = ".item-title";
        private const string ToShipHrefTitleSelector =
            "a[href*='type=toship'][href*='to_process'] .item-title";
        private const string ToShipDescText = "Chờ Lấy Hàng";

        public async Task<int?> ReadToShipCountAsync(bool reload, CancellationToken ct = default)
        {
            try
            {
                // 1) Gate: chưa đăng nhập → to-do box chưa có → null (KHÔNG reload để không phá đăng nhập/captcha).
                if (!ShopeeLoginCookies.IsLoggedIn(await CaptureCookiesJsonAsync().ConfigureAwait(false)))
                {
                    return null;
                }

                var page = _context.Pages.Count > 0 ? _context.Pages[0] : null;
                if (page is null)
                {
                    return null;
                }

                // 2) Reload nếu cần (nuốt lỗi điều hướng — vẫn thử đọc DOM hiện có).
                if (reload)
                {
                    try
                    {
                        await page.ReloadAsync(new PageReloadOptions
                        {
                            WaitUntil = WaitUntilState.DOMContentLoaded,
                            Timeout = 30000
                        }).ConfigureAwait(false);
                    }
                    catch { /* bỏ qua lỗi reload/điều hướng */ }
                }

                // 3) Tìm ô "Chờ Lấy Hàng" (poll timeout ngắn), có fallback.
                var titleText = await FindToShipTitleAsync(page, ct).ConfigureAwait(false);

                // 4) Parse số (thuần, test được). Không thấy / không parse được → null.
                return ShopeeDashboard.ParseToShipCount(titleText);
            }
            catch
            {
                // Bất kỳ lỗi nào (selector đổi, context ngắt...) → null, KHÔNG phá phiên.
                return null;
            }
        }

        /// <summary>
        /// Dò text ô <c>item-title</c> của mục "Chờ Lấy Hàng" trong to-do box, poll tới khi hết
        /// <c>~8s</c>. Thử lần lượt: (1) duyệt các <c>.to-do-box-item</c> tìm cái có <c>.item-desc</c>
        /// khớp "Chờ Lấy Hàng"; (2) fallback theo href. Không thấy → <c>null</c> (không ném).
        /// </summary>
        private static async Task<string?> FindToShipTitleAsync(IPage page, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow.AddSeconds(8);
            do
            {
                ct.ThrowIfCancellationRequested();

                // Cách 1: duyệt .to-do-box-item, khớp .item-desc == "Chờ Lấy Hàng" → đọc .item-title.
                try
                {
                    var items = await page.QuerySelectorAllAsync(ToShipItemSelector).ConfigureAwait(false);
                    foreach (var item in items)
                    {
                        var desc = await item.QuerySelectorAsync(ItemDescSelector).ConfigureAwait(false);
                        if (desc is null)
                        {
                            continue;
                        }

                        var descText = await desc.InnerTextAsync().ConfigureAwait(false);
                        if (!IsToShipDesc(descText))
                        {
                            continue;
                        }

                        var title = await item.QuerySelectorAsync(ItemTitleSelector).ConfigureAwait(false);
                        if (title is not null)
                        {
                            return await title.InnerTextAsync().ConfigureAwait(false);
                        }
                    }
                }
                catch { /* selector chưa render / không hợp lệ — thử fallback */ }

                // Cách 2 (fallback theo href).
                try
                {
                    var title = await page.QuerySelectorAsync(ToShipHrefTitleSelector).ConfigureAwait(false);
                    if (title is not null)
                    {
                        return await title.InnerTextAsync().ConfigureAwait(false);
                    }
                }
                catch { /* bỏ qua */ }

                await Task.Delay(400, ct).ConfigureAwait(false);
            }
            while (DateTime.UtcNow < deadline);

            return null;
        }

        /// <summary>So khớp text <c>.item-desc</c> với "Chờ Lấy Hàng" (chuẩn hóa khoảng trắng, không phân biệt hoa/thường).</summary>
        private static bool IsToShipDesc(string? descText)
        {
            if (string.IsNullOrWhiteSpace(descText))
            {
                return false;
            }

            var normalized = string.Join(' ',
                descText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return string.Equals(normalized, ToShipDescText, StringComparison.OrdinalIgnoreCase);
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
