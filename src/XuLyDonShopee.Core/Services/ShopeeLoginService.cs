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

    /// <summary>
    /// <b>Về trang chủ Seller rồi đọc lại số "Chờ Lấy Hàng" ngay:</b> điều hướng tab hiện tại về trang chủ
    /// Seller Centre (<see cref="ShopeeLoginService.SellerUrl"/>) bằng <see cref="IPage.GotoAsync"/> —
    /// tương đương người gõ URL / bấm bookmark (hành vi bình thường, <b>KHÔNG</b> click máy vào element) —
    /// kèm khoảng dừng "đọc trang" ngẫu nhiên trước/sau, rồi đọc số "Chờ Lấy Hàng" từ to-do box qua
    /// <see cref="ReadToShipCountAsync"/> với <c>reload=false</c> (trang vừa load nên không reload lại).
    /// Dùng cho việc kiểm tra đơn THỦ CÔNG (không đợi chu kỳ 30').
    /// <para>
    /// <b>Graceful — không bao giờ ném:</b> chưa đăng nhập, không có trang, không đọc được, hoặc bất kỳ
    /// lỗi nào → trả <c>null</c> (KHÔNG phá phiên). Trả về số đơn (≥ 0) khi đọc được.
    /// </para>
    /// </summary>
    Task<int?> GoHomeAndReadToShipCountAsync(CancellationToken ct = default);

    /// <summary>
    /// <b>Bước đầu xử lý đơn:</b> ở menu trái (nhóm "Quản Lý Đơn Hàng") tìm &amp; bấm link
    /// <b>"Cài Đặt Vận Chuyển"</b>, chờ trang cài đặt vận chuyển mở rồi bấm tab <b>"Địa Chỉ"</b> —
    /// <b>toàn bộ bằng thao tác kiểu người CÓ HIT-TEST</b> (di chuột theo đường cong <see cref="HumanMouse"/>,
    /// click down→trễ→up, có khoảng dừng/chờ ngẫu nhiên kiểu "người đọc trang"; TRƯỚC KHI nhả click kiểm
    /// <c>document.elementFromPoint</c> để KHÔNG click nhầm link khác khi submenu bị cụp / flyout đè). Nếu
    /// submenu đang đóng thì click mục cha "Quản Lý Đơn Hàng" kiểu người để mở ra rồi tìm lại.
    /// <para>
    /// <b>Graceful — không bao giờ ném:</b> mọi lỗi/hủy → trả một giá trị <see cref="ShippingNavResult"/>
    /// (KHÔNG phá phiên). Kết quả phân biệt bước hỏng: <see cref="ShippingNavResult.Ok"/> (tab "Địa Chỉ" đã
    /// active — đã bấm hoặc vốn đang active); <see cref="ShippingNavResult.PageNotOpened"/> (không đưa được
    /// tới trang cài đặt vận chuyển, kể cả sau fallback Goto); <see cref="ShippingNavResult.AddressTabNotFound"/>
    /// (đã mở trang nhưng không thấy / không bấm được tab "Địa Chỉ"); <see cref="ShippingNavResult.Failed"/>
    /// (không có trang/phiên hoặc lỗi bất ngờ).
    /// </para>
    /// </summary>
    Task<ShippingNavResult> OpenShippingAddressSettingsAsync(CancellationToken ct = default);

    /// <summary>
    /// <b>Bước 2 xử lý đơn — đặt "địa chỉ lấy hàng":</b> chạy khi ĐANG ở tab "Địa Chỉ" của Cài đặt vận
    /// chuyển. Duyệt danh sách địa chỉ, tìm địa chỉ có dòng tỉnh/thành khớp
    /// <paramref name="province"/> (<c>Account.PickupAddress</c>). Nếu địa chỉ đó đã là địa chỉ lấy hàng
    /// (có tag "Địa chỉ lấy hàng") → coi như xong. Ngược lại bấm <b>Sửa</b> → tick checkbox "Đặt làm địa
    /// chỉ lấy hàng" → bấm <b>Lưu</b> → chờ modal đóng. <b>Toàn bộ bằng thao tác kiểu người</b> (di chuột
    /// theo đường cong, click down→trễ→up, dừng "đọc trang" ngẫu nhiên giữa các bước); CHỈ click khi phần
    /// tử có bounding box. Modal chứa Google Map load bất đồng bộ → Vue vẽ lại form nên checkbox được
    /// <b>re-query tươi</b> trước mỗi lần dùng (không giữ handle qua re-render), trạng thái tick đọc bằng
    /// JS eval trên phần tử vừa query.
    /// <para>
    /// <b>Graceful — không bao giờ ném:</b> mọi lỗi/không-làm-được → trả một giá trị
    /// <see cref="SetPickupResult"/> (KHÔNG phá phiên, nghiêng về KHÔNG bấm thêm), và <b>mọi nhánh thất bại
    /// đều Hủy modal</b> (không để modal "Sửa Địa chỉ" mở treo). Kết quả phân biệt bước hỏng:
    /// <see cref="SetPickupResult.Ok"/> (địa chỉ lấy hàng đã đúng — sẵn có hoặc đã Lưu thành công);
    /// <see cref="SetPickupResult.AddressNotFound"/> (không thấy địa chỉ khớp tỉnh);
    /// <see cref="SetPickupResult.EditModalNotOpened"/> (bấm Sửa nhưng modal không mở — shop khóa sửa?);
    /// <see cref="SetPickupResult.CheckboxNotFound"/> (modal mở nhưng không thấy ô cần tick);
    /// <see cref="SetPickupResult.CheckboxClickFailed"/> (click không tick được sau vài lần);
    /// <see cref="SetPickupResult.SaveFailed"/> (đã tick nhưng bấm Lưu không được / modal không đóng);
    /// <see cref="SetPickupResult.Failed"/> (không có trang/phiên hoặc lỗi bất ngờ).
    /// </para>
    /// </summary>
    Task<SetPickupResult> SetPickupAddressAsync(string province, CancellationToken ct = default);
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
        /// Di chuột theo <b>đường cong</b> từ (<paramref name="mx"/>,<paramref name="my"/>) tới tâm phần tử
        /// (+jitter nhỏ), tự <c>Mouse.MoveAsync</c> <b>từng điểm</b> (KHÔNG dùng <c>steps</c> lớn để đi
        /// thẳng). <b>Chỉ đưa chuột tới đích — KHÔNG click.</b> Trả về (vị trí chuột cuối, có bounding box
        /// hay không): box null → kéo phần tử vào tầm nhìn, GIỮ nguyên vị trí chuột, <c>HasBox=false</c>.
        /// </summary>
        private static async Task<(double X, double Y, bool HasBox)> HumanMoveToAsync(
            IPage page, IElementHandle el, double mx, double my, Random rng, CancellationToken ct)
        {
            // Handle có thể đã DETACHED (Vue vẽ lại form sau khi map/modal re-render) → BoundingBoxAsync ném.
            // Bọc try: lỗi handle → coi như không có box (HasBox=false), KHÔNG để exception rò lên catch ngoài.
            ElementHandleBoundingBoxResult? box;
            try { box = await el.BoundingBoxAsync().ConfigureAwait(false); }
            catch { box = null; }

            double tx, ty;
            bool hasBox;
            if (box is not null)
            {
                // Tâm ô + jitter nhỏ (không luôn nhấn đúng chính giữa).
                tx = box.X + box.Width / 2.0 + (rng.NextDouble() - 0.5) * Math.Min(box.Width * 0.3, 20);
                ty = box.Y + box.Height / 2.0 + (rng.NextDouble() - 0.5) * Math.Min(box.Height * 0.3, 8);
                hasBox = true;
            }
            else
            {
                // Không lấy được bounding box → kéo phần tử vào tầm nhìn, giữ nguyên vị trí chuột.
                try { await el.ScrollIntoViewIfNeededAsync().ConfigureAwait(false); } catch { /* bỏ qua */ }
                tx = mx;
                ty = my;
                hasBox = false;
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

            return (tx, ty, hasBox);
        }

        /// <summary>
        /// Di chuột theo <b>đường cong</b> tới tâm phần tử rồi click kiểu người (down + trễ + up). Trả về
        /// vị trí chuột cuối (điểm đích). <b>Click MÙ theo tọa độ — KHÔNG hit-test</b>: CHỈ dùng cho luồng
        /// đăng nhập (<see cref="TryHumanLoginAsync"/> — form login đơn giản, không có submenu cụp/flyout
        /// đè). Mọi thao tác NGHIỆP VỤ (menu/modal) dùng <see cref="HumanMoveAndClickVerifiedAsync"/>.
        /// </summary>
        private static async Task<(double X, double Y)> HumanMoveAndClickAsync(
            IPage page, IElementHandle el, double mx, double my, Random rng, CancellationToken ct)
        {
            (double tx, double ty, _) = await HumanMoveToAsync(page, el, mx, my, rng, ct).ConfigureAwait(false);

            // Click kiểu người: nhấn giữ một khoảng ngắn rồi nhả.
            await page.Mouse.DownAsync().ConfigureAwait(false);
            await Task.Delay(rng.Next(40, 121), ct).ConfigureAwait(false);
            await page.Mouse.UpAsync().ConfigureAwait(false);

            return (tx, ty);
        }

        /// <summary>True nếu tại điểm (x,y) của viewport, phần tử nhận sự kiện chính là el / con của el /
        /// tổ tiên của el (elementFromPoint trả node TRÊN CÙNG — bị phần tử khác đè thì trả phần tử đè).</summary>
        private static async Task<bool> IsPointOnElementAsync(IElementHandle el, double x, double y)
        {
            try
            {
                return await el.EvaluateAsync<bool>(
                    "(node, pt) => { const hit = document.elementFromPoint(pt.x, pt.y);" +
                    " return !!hit && (node === hit || node.contains(hit) || hit.contains(node)); }",
                    new { x, y }).ConfigureAwait(false);
            }
            catch { return false; }
        }

        /// <summary>
        /// Primitive click <b>kiểu người CÓ HIT-TEST</b> cho thao tác nghiệp vụ: đưa chuột theo đường cong
        /// tới phần tử (<see cref="HumanMoveToAsync"/>), rồi TRƯỚC KHI nhả click <b>kiểm tra
        /// <c>document.elementFromPoint</c></b> tại điểm click có đúng là phần tử đích (hoặc con/tổ tiên
        /// của nó) — chống <b>click nhầm link khác</b> khi submenu bị cụp hoặc flyout/popover đè lên toạ độ.
        /// Poll hit-test tối đa ~2s với chuột ĐỨNG YÊN tại đích (giống người dừng nhìn rồi mới bấm; popover
        /// hover của item khác tự tắt khi chuột rời item đó). Chỉ <c>Down/trễ/Up</c> khi hit-test PASS. Trả
        /// về (vị trí chuột cuối, đã click hay chưa) — <c>Clicked=false</c> khi không có bounding box hoặc
        /// hit-test fail suốt ~2s (KHÔNG bao giờ click mù vào tọa độ).
        /// </summary>
        private static async Task<(double X, double Y, bool Clicked)> HumanMoveAndClickVerifiedAsync(
            IPage page, IElementHandle el, double mx, double my, Random rng, CancellationToken ct)
        {
            (double tx, double ty, bool hasBox) =
                await HumanMoveToAsync(page, el, mx, my, rng, ct).ConfigureAwait(false);

            // Không có bounding box → thử kéo vào tầm nhìn + move lại MỘT lần; vẫn không có box → KHÔNG click.
            if (!hasBox)
            {
                try { await el.ScrollIntoViewIfNeededAsync().ConfigureAwait(false); } catch { /* bỏ qua */ }
                (tx, ty, hasBox) = await HumanMoveToAsync(page, el, mx, my, rng, ct).ConfigureAwait(false);
                if (!hasBox)
                {
                    return (mx, my, false);
                }
            }

            // Poll hit-test tối đa ~2s: chuột ĐỨNG YÊN tại đích, dừng ngẫu nhiên rồi kiểm — giống người dừng
            // nhìn rồi mới bấm (popover hover của item khác tự tắt vì chuột không còn trên item đó).
            var deadline = DateTime.UtcNow.AddMilliseconds(2000);
            do
            {
                ct.ThrowIfCancellationRequested();
                if (await IsPointOnElementAsync(el, tx, ty).ConfigureAwait(false))
                {
                    // Hit-test PASS → click kiểu người: nhấn giữ một khoảng ngắn rồi nhả.
                    await page.Mouse.DownAsync().ConfigureAwait(false);
                    await Task.Delay(rng.Next(40, 121), ct).ConfigureAwait(false);
                    await page.Mouse.UpAsync().ConfigureAwait(false);
                    return (tx, ty, true);
                }

                await Task.Delay(rng.Next(150, 301), ct).ConfigureAwait(false);
            }
            while (DateTime.UtcNow < deadline);

            // Poll fail suốt ~2s → điểm click đang thuộc phần tử khác (bị che/cụp) → KHÔNG Down/Up.
            return (tx, ty, false);
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

        public async Task<int?> GoHomeAndReadToShipCountAsync(CancellationToken ct = default)
        {
            try
            {
                var page = _context.Pages.Count > 0 ? _context.Pages[0] : null;
                if (page is null)
                {
                    return null;
                }

                // Gate: CHƯA đăng nhập → KHÔNG điều hướng. Goto lúc này phá form đăng nhập/captcha đang dở
                // y hệt reload (xem ghi chú trong ReadToShipCountAsync), và "gõ nửa chừng rồi nhảy trang"
                // là dấu hiệu máy móc lộ liễu. Trả null ngay — tầng App báo "có thể chưa đăng nhập xong".
                if (!ShopeeLoginCookies.IsLoggedIn(await CaptureCookiesJsonAsync().ConfigureAwait(false)))
                {
                    return null;
                }

                // Random nội bộ (app dùng ngẫu nhiên thật, đồng bộ style các thao tác kiểu người).
                var rng = new Random();

                // Dừng "đọc trang" trước khi về trang chủ (giống người gõ URL / bấm bookmark rồi đọc).
                await Task.Delay(rng.Next(800, 2500), ct).ConfigureAwait(false);

                // Về trang chủ Seller: Goto như người gõ URL / bấm bookmark (KHÔNG click máy vào element).
                // Nuốt lỗi điều hướng — vẫn thử đọc số bên dưới.
                try
                {
                    await page.GotoAsync(SellerUrl, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 30000
                    }).ConfigureAwait(false);
                }
                catch
                {
                    // Nuốt lỗi điều hướng (timeout/context ngắt) — vẫn thử đọc to-do box.
                }

                // Dừng "đọc trang" sau khi về trang chủ (để to-do box render).
                await Task.Delay(rng.Next(800, 2500), ct).ConfigureAwait(false);

                // Trang vừa load → KHÔNG reload nữa (ReadToShipCountAsync tự gate IsLoggedIn + poll to-do box).
                return await ReadToShipCountAsync(reload: false, ct).ConfigureAwait(false);
            }
            catch
            {
                // Bất kỳ lỗi nào (context ngắt, hủy...) → null, KHÔNG phá phiên.
                return null;
            }
        }

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

        // ===== Điều hướng "Cài Đặt Vận Chuyển" → tab "Địa Chỉ" (KIỂU NGƯỜI) =====
        // URL trực tiếp trang Cài đặt vận chuyển — CHỈ dùng ở fallback cuối (khi Shopee đổi DOM menu khiến
        // không tìm được link để click kiểu người).
        private const string ShippingSettingsUrl = "https://banhang.shopee.vn/portal/all-settings/shipping";

        public async Task<ShippingNavResult> OpenShippingAddressSettingsAsync(CancellationToken ct = default)
        {
            try
            {
                var page = _context.Pages.Count > 0 ? _context.Pages[0] : null;
                if (page is null)
                {
                    return ShippingNavResult.Failed;
                }

                // Random nội bộ (app dùng ngẫu nhiên thật, đồng bộ style với TryHumanLoginAsync).
                var rng = new Random();

                // Con trỏ bắt đầu ở vị trí NGẪU NHIÊN trong viewport (đọc kích thước thật; null → 1280x720).
                var vp = page.ViewportSize;
                double vw = vp is not null ? vp.Width : 1280;
                double vh = vp is not null ? vp.Height : 720;
                double mx = rng.NextDouble() * vw;
                double my = rng.NextDouble() * vh;

                // Dừng kiểu "người đọc trang" trước khi bắt đầu.
                await Task.Delay(rng.Next(800, 2500), ct).ConfigureAwait(false);

                // Click mục cha "Quản Lý Đơn Hàng" TỐI ĐA 1 lần/lượt: click lần 2 khi nhóm đang mở sẽ toggle
                // cụp lại (cấm). Cờ dùng chung cho cả nhánh đọc-trạng-thái lẫn nhánh không-thấy-link.
                bool parentClicked = false;
                bool clickedLink = false;

                // 1) Tìm link "Cài Đặt Vận Chuyển" (poll, deadline ~10s).
                var link = await FindShippingLinkAsync(page, 10000, rng, ct).ConfigureAwait(false);

                if (link is not null)
                {
                    // 2) TRƯỚC khi di chuột: đọc trạng thái bung/cụp bằng JS hình học (KHÔNG cần hover) rồi
                    //    xử lý theo trạng thái. Poll nhẹ ~5s để trạng thái nhất thời (popover hover của item
                    //    khác) tự tan; mỗi vòng chờ 300–800ms ngẫu nhiên.
                    var readyDeadline = DateTime.UtcNow.AddMilliseconds(5000);
                    bool scrolledForUnknown = false;
                    while (!clickedLink && DateTime.UtcNow < readyDeadline)
                    {
                        ct.ThrowIfCancellationRequested();
                        var readiness = await GetLinkReadinessAsync(link).ConfigureAwait(false);

                        if (readiness == LinkReadiness.Ready)
                        {
                            // Link nhận click tại tâm → click CÓ HIT-TEST (hàng rào cuối ngay trước Down/Up).
                            (mx, my, clickedLink) =
                                await HumanMoveAndClickVerifiedAsync(page, link, mx, my, rng, ct).ConfigureAwait(false);
                            break;
                        }

                        if (readiness == LinkReadiness.Collapsed)
                        {
                            // Nhóm "Quản Lý Đơn Hàng" đang CỤP (đúng yêu cầu người dùng: kiểm tra rồi bung).
                            // Đã bung THẬT 1 lần rồi mà vẫn cụp → thôi (không click lại kẻo toggle cụp nhóm đang mở).
                            if (parentClicked)
                            {
                                break;
                            }

                            var parent = await FindOrderMenuParentAsync(page, ct).ConfigureAwait(false);
                            if (parent is null)
                            {
                                break;
                            }

                            // Click mục cha CÓ HIT-TEST. CHỈ tiêu "ngân sách bung 1 lần" (parentClicked) khi chuột
                            // THẬT SỰ nhả (Clicked==true): hit-test fail thì chuột CHƯA HỀ nhả → không có nguy cơ
                            // toggle → vòng sau readiness vẫn Collapsed sẽ thử bung lại (còn trong deadline).
                            bool parentActuallyClicked;
                            (mx, my, parentActuallyClicked) =
                                await HumanMoveAndClickVerifiedAsync(page, parent, mx, my, rng, ct).ConfigureAwait(false);
                            if (parentActuallyClicked)
                            {
                                parentClicked = true;
                                // Bung THÀNH CÔNG → cấp lại trọn 5s cho phần còn lại (chờ, tìm lại link, đọc
                                // readiness, click link kiểu người) — bảo đảm sau khi bung LUÔN có ≥1 lượt đọc
                                // readiness + thử click link trước khi được phép rơi xuống Goto (Goto là đường
                                // thoát HIẾM, không phải kết cục của một lượt bung menu thành công).
                                readyDeadline = DateTime.UtcNow.AddMilliseconds(5000);
                            }

                            await Task.Delay(rng.Next(500, 1500), ct).ConfigureAwait(false);

                            // Tìm lại link (instance có thể đổi sau khi submenu bung) rồi đọc lại ở vòng sau.
                            link = await FindShippingLinkAsync(page, 10000, rng, ct).ConfigureAwait(false);
                            if (link is null)
                            {
                                break;
                            }
                            continue;
                        }

                        if (readiness == LinkReadiness.Unknown)
                        {
                            // Không rõ → thử kéo vào tầm nhìn MỘT lần rồi đọc lại; vẫn Unknown → hết cách bằng chuột.
                            if (scrolledForUnknown)
                            {
                                break;
                            }
                            scrolledForUnknown = true;
                            try { await link.ScrollIntoViewIfNeededAsync().ConfigureAwait(false); } catch { /* bỏ qua */ }
                            await Task.Delay(rng.Next(300, 801), ct).ConfigureAwait(false);
                            continue;
                        }

                        // Covered (bị popover/flyout trong cùng submenu đè) → chờ rồi đọc lại; KHÔNG click mục cha.
                        await Task.Delay(rng.Next(300, 801), ct).ConfigureAwait(false);
                    }
                }
                else
                {
                    // 4) Không thấy link ngay từ đầu (submenu nhiều khả năng chưa render) → click mục cha
                    //    verified (không cần đọc trạng thái) rồi tìm lại & click. Vẫn giữ giới hạn 1 lần click cha.
                    var parent = await FindOrderMenuParentAsync(page, ct).ConfigureAwait(false);
                    if (parent is not null && !parentClicked)
                    {
                        (mx, my, _) =
                            await HumanMoveAndClickVerifiedAsync(page, parent, mx, my, rng, ct).ConfigureAwait(false);
                        parentClicked = true;
                        await Task.Delay(rng.Next(500, 1500), ct).ConfigureAwait(false);
                        link = await FindShippingLinkAsync(page, 10000, rng, ct).ConfigureAwait(false);
                        if (link is not null)
                        {
                            (mx, my, clickedLink) =
                                await HumanMoveAndClickVerifiedAsync(page, link, mx, my, rng, ct).ConfigureAwait(false);
                        }
                    }
                }

                // 3) Click được link → chờ trang cài đặt vận chuyển mở (CHỈ nhận theo URL).
                bool opened = clickedLink
                    && await WaitShippingPageAsync(page, 20000, ct).ConfigureAwait(false);

                // 4b) Chưa mở được (click không ăn / không thấy link / URL không đổi) → fallback Goto MỘT lần
                //     (đường thoát cuối, kém human hơn — hiếm khi tới nếu hit-test click đã ăn).
                if (!opened)
                {
                    try
                    {
                        await page.GotoAsync(ShippingSettingsUrl, new PageGotoOptions
                        {
                            WaitUntil = WaitUntilState.DOMContentLoaded,
                            Timeout = 30000
                        }).ConfigureAwait(false);
                    }
                    catch { /* nuốt lỗi điều hướng — vẫn thử chờ trang bên dưới */ }

                    opened = await WaitShippingPageAsync(page, 20000, ct).ConfigureAwait(false);
                }

                if (!opened)
                {
                    return ShippingNavResult.PageNotOpened;
                }

                // 5) Dừng "đọc trang" rồi tìm & bấm tab "Địa Chỉ".
                await Task.Delay(rng.Next(800, 2500), ct).ConfigureAwait(false);

                var tab = await FindAddressTabAsync(page, 10000, rng, ct).ConfigureAwait(false);
                if (tab is null)
                {
                    return ShippingNavResult.AddressTabNotFound;
                }

                // Tab đã active → coi như xong (không click lại). Chưa active → click CÓ HIT-TEST.
                if (IsTabActive(await tab.GetAttributeAsync("class").ConfigureAwait(false)))
                {
                    return ShippingNavResult.Ok;
                }

                bool clickedTab;
                (mx, my, clickedTab) =
                    await HumanMoveAndClickVerifiedAsync(page, tab, mx, my, rng, ct).ConfigureAwait(false);
                return clickedTab ? ShippingNavResult.Ok : ShippingNavResult.AddressTabNotFound;
            }
            catch
            {
                // Bất kỳ lỗi nào (selector đổi, context ngắt, hủy...) → Failed, KHÔNG phá phiên.
                return ShippingNavResult.Failed;
            }
        }

        /// <summary>
        /// Dò link "Cài Đặt Vận Chuyển" trong menu trái, poll tới khi hết <paramref name="timeoutMs"/>.
        /// Thử theo thứ tự: (a) <c>a.sidebar-submenu-item-link[href*='/portal/all-settings/shipping']</c>;
        /// (b) <c>a[test-id='order shipping setting']</c>; (c) duyệt mọi <c>a.sidebar-submenu-item-link</c>
        /// khớp <see cref="ShopeeShippingNav.IsShippingSettingText"/>. Chỉ nhận element đang HIỂN THỊ
        /// (<c>BoundingBoxAsync() != null</c>). Không thấy → <c>null</c> (không ném).
        /// </summary>
        private static async Task<IElementHandle?> FindShippingLinkAsync(
            IPage page, int timeoutMs, Random rng, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            do
            {
                ct.ThrowIfCancellationRequested();

                var el = await FirstVisibleByBoxAsync(
                             page, "a.sidebar-submenu-item-link[href*='/portal/all-settings/shipping']", ct)
                         .ConfigureAwait(false)
                         ?? await FirstVisibleByBoxAsync(page, "a[test-id='order shipping setting']", ct)
                             .ConfigureAwait(false);
                if (el is not null)
                {
                    return el;
                }

                // Fallback theo text: duyệt mọi link submenu, khớp "Cài Đặt Vận Chuyển".
                try
                {
                    var links = await page.QuerySelectorAllAsync("a.sidebar-submenu-item-link").ConfigureAwait(false);
                    foreach (var a in links)
                    {
                        var text = await a.InnerTextAsync().ConfigureAwait(false);
                        if (ShopeeShippingNav.IsShippingSettingText(text)
                            && await a.BoundingBoxAsync().ConfigureAwait(false) is not null)
                        {
                            return a;
                        }
                    }
                }
                catch { /* selector chưa render / không hợp lệ — thử vòng sau */ }

                await Task.Delay(rng.Next(300, 501), ct).ConfigureAwait(false);
            }
            while (DateTime.UtcNow < deadline);

            return null;
        }

        /// <summary>
        /// Đọc <b>trạng thái bung/cụp</b> của link trong submenu bằng <b>JS hình học — KHÔNG cần di chuột</b>
        /// (elementFromPoint là hình học thuần, không cần hover). DOM Shopee không có class trạng thái trên
        /// <c>li.sidebar-menu-box</c> nên phải suy từ chiều cao <c>ul.sidebar-submenu</c> + phần tử nhận
        /// click tại tâm link. Nuốt lỗi → <see cref="LinkReadiness.Unknown"/>. Cho kết quả qua
        /// <see cref="ShopeeShippingNav.ParseLinkReadiness"/>.
        /// </summary>
        private static async Task<LinkReadiness> GetLinkReadinessAsync(IElementHandle link)
        {
            string raw;
            try
            {
                raw = await link.EvaluateAsync<string>(
                    "(node) => {" +
                    " const ul = node.closest('ul.sidebar-submenu');" +
                    " const ulRect = ul ? ul.getBoundingClientRect() : null;" +
                    " if (ulRect && ulRect.height < 2) return 'collapsed';" +
                    " const r = node.getBoundingClientRect();" +
                    " if (r.width === 0 || r.height === 0) return 'collapsed';" +
                    " const cx = r.left + r.width / 2, cy = r.top + r.height / 2;" +
                    " const hit = document.elementFromPoint(cx, cy);" +
                    " if (!hit) return 'covered';" +
                    " if (node === hit || node.contains(hit) || hit.contains(node)) return 'ready';" +
                    " return ul && ul.contains(hit) ? 'covered' : 'collapsed';" +
                    "}").ConfigureAwait(false);
            }
            catch { raw = "unknown"; }

            return ShopeeShippingNav.ParseLinkReadiness(raw);
        }

        /// <summary>
        /// Dò mục cha "Quản Lý Đơn Hàng" ở menu trái (để click mở submenu). Thử: (a)
        /// <c>li.ps_menu_order div.sidebar-menu-item</c>; (b) fallback duyệt mọi <c>.sidebar-menu-item</c>
        /// khớp <see cref="ShopeeShippingNav.IsOrderMenuText"/>. Chỉ nhận element đang hiển thị. Một lượt
        /// (không poll) — không thấy → <c>null</c>.
        /// </summary>
        private static async Task<IElementHandle?> FindOrderMenuParentAsync(IPage page, CancellationToken ct)
        {
            var el = await FirstVisibleByBoxAsync(page, "li.ps_menu_order div.sidebar-menu-item", ct)
                .ConfigureAwait(false);
            if (el is not null)
            {
                return el;
            }

            try
            {
                var items = await page.QuerySelectorAllAsync(".sidebar-menu-item").ConfigureAwait(false);
                foreach (var item in items)
                {
                    var text = await item.InnerTextAsync().ConfigureAwait(false);
                    if (ShopeeShippingNav.IsOrderMenuText(text)
                        && await item.BoundingBoxAsync().ConfigureAwait(false) is not null)
                    {
                        return item;
                    }
                }
            }
            catch { /* bỏ qua */ }

            return null;
        }

        /// <summary>
        /// Chờ trang Cài đặt vận chuyển mở: poll tới khi hết <paramref name="timeoutMs"/>, điều kiện DUY NHẤT
        /// là <c>page.Url</c> chứa <c>/portal/all-settings/shipping</c>
        /// (<see cref="ShopeeShippingNav.IsShippingSettingHref"/>). <b>KHÔNG</b> nhận theo
        /// <c>.eds-tabs__nav-tab</c> nữa: trang khác cũng có thanh eds-tabs → dương tính giả (nhận nhầm là đã
        /// mở rồi fail muộn ở bước tìm tab "Địa Chỉ"). <c>page.Url</c> của Playwright phản ánh cả đổi route
        /// SPA qua history API nên đủ tin (KHÔNG dùng WaitForNavigation). Hết giờ → false.
        /// </summary>
        private static async Task<bool> WaitShippingPageAsync(IPage page, int timeoutMs, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            do
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (ShopeeShippingNav.IsShippingSettingHref(page.Url))
                    {
                        return true;
                    }
                }
                catch { /* điều hướng dở — thử vòng sau */ }

                await Task.Delay(250, ct).ConfigureAwait(false);
            }
            while (DateTime.UtcNow < deadline);

            return false;
        }

        /// <summary>
        /// Dò tab "Địa Chỉ" trong thanh <c>.eds-tabs__nav-tab</c>, poll tới khi hết
        /// <paramref name="timeoutMs"/>, khớp <see cref="ShopeeShippingNav.IsAddressTabText"/> (InnerText
        /// có thể kèm rác badge). Không thấy → <c>null</c>.
        /// </summary>
        private static async Task<IElementHandle?> FindAddressTabAsync(
            IPage page, int timeoutMs, Random rng, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            do
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var tabs = await page.QuerySelectorAllAsync(".eds-tabs__nav-tab").ConfigureAwait(false);
                    foreach (var t in tabs)
                    {
                        var text = await t.InnerTextAsync().ConfigureAwait(false);
                        if (ShopeeShippingNav.IsAddressTabText(text))
                        {
                            return t;
                        }
                    }
                }
                catch { /* chưa render — thử vòng sau */ }

                await Task.Delay(rng.Next(300, 501), ct).ConfigureAwait(false);
            }
            while (DateTime.UtcNow < deadline);

            return null;
        }

        /// <summary>True nếu chuỗi class của tab chứa token "active" (tab đang được chọn).</summary>
        private static bool IsTabActive(string? classAttr)
        {
            if (string.IsNullOrWhiteSpace(classAttr))
            {
                return false;
            }

            foreach (var c in classAttr.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(c, "active", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Dò phần tử đầu tiên khớp <paramref name="selector"/> đang HIỂN THỊ
        /// (<c>BoundingBoxAsync() != null</c>). Một lượt, nuốt lỗi selector → <c>null</c>.
        /// </summary>
        private static async Task<IElementHandle?> FirstVisibleByBoxAsync(IPage page, string selector, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var el = await page.QuerySelectorAsync(selector).ConfigureAwait(false);
                if (el is not null && await el.BoundingBoxAsync().ConfigureAwait(false) is not null)
                {
                    return el;
                }
            }
            catch { /* selector không hợp lệ / chưa render */ }

            return null;
        }

        // ===== Bước 2: đặt "địa chỉ lấy hàng" theo tỉnh mặc định (KIỂU NGƯỜI) =====

        public async Task<SetPickupResult> SetPickupAddressAsync(string province, CancellationToken ct = default)
        {
            try
            {
                var page = _context.Pages.Count > 0 ? _context.Pages[0] : null;
                if (page is null)
                {
                    return SetPickupResult.Failed;
                }

                // Random nội bộ + con trỏ bắt đầu ở vị trí ngẫu nhiên (đồng bộ style các thao tác kiểu người).
                var rng = new Random();
                var vp = page.ViewportSize;
                double vw = vp is not null ? vp.Width : 1280;
                double vh = vp is not null ? vp.Height : 720;
                double mx = rng.NextDouble() * vw;
                double my = rng.NextDouble() * vh;

                // Dừng "đọc trang" trước khi bắt đầu.
                await Task.Delay(rng.Next(800, 2500), ct).ConfigureAwait(false);

                // 1) Chờ danh sách địa chỉ & tìm địa chỉ khớp tỉnh (đầu tiên theo thứ tự trang).
                var item = await FindMatchingAddressItemAsync(page, province, 15000, ct).ConfigureAwait(false);
                if (item is null)
                {
                    return SetPickupResult.AddressNotFound;
                }

                // 2) Đã là địa chỉ lấy hàng (có tag) → coi như xong, KHÔNG đụng gì.
                if (await ItemHasPickupTagAsync(item).ConfigureAwait(false))
                {
                    return SetPickupResult.Ok;
                }

                // 3) Tìm & bấm nút "Sửa" của địa chỉ đó (chỉ click khi có bounding box). Không thấy nút /
                //    click không ăn → coi như không mở được modal sửa.
                var editBtn = await FindEditButtonAsync(item).ConfigureAwait(false);
                if (editBtn is null)
                {
                    return SetPickupResult.EditModalNotOpened;
                }

                bool clicked;
                (mx, my, clicked) = await TryHumanClickVisibleAsync(page, editBtn, mx, my, rng, ct).ConfigureAwait(false);
                if (!clicked)
                {
                    return SetPickupResult.EditModalNotOpened;
                }

                // 4) Chờ modal "Sửa Địa chỉ" mở (shop bị khóa sửa → không mở). Dừng "đọc modal".
                var modal = await WaitEditAddressModalAsync(page, 10000, ct).ConfigureAwait(false);
                if (modal is null)
                {
                    return SetPickupResult.EditModalNotOpened;
                }
                await Task.Delay(rng.Next(800, 2500), ct).ConfigureAwait(false);

                // 5) Thao tác checkbox "Đặt làm địa chỉ lấy hàng". Modal chứa Google Map load bất đồng bộ
                //    → Vue vẽ lại form nên checkbox re-query TƯƠI trước mỗi lần dùng, trạng thái tick đọc
                //    bằng DOM sống (KHÔNG giữ handle qua re-render). Bọc try/finally: kết quả KHÁC Ok mà
                //    modal còn mở → LUÔN Hủy (chốt chặn cuối, kể cả khi có exception rơi lên catch ngoài).
                var result = SetPickupResult.Failed;
                try
                {
                    // 5a) Chờ ổn định: thấy label checkbox HAI LẦN LIÊN TIẾP (~400ms) để map/form re-render
                    //     xong mới thao tác; deadline ~8s. Không thấy → không có ô cần tick.
                    if (!await WaitPickupCheckboxStableAsync(modal, 8000, ct).ConfigureAwait(false))
                    {
                        result = SetPickupResult.CheckboxNotFound;
                        return result;
                    }

                    // 5b) Đã tick sẵn (đọc DOM sống) → trạng thái mong muốn đã có → Hủy (không đổi gì), Ok.
                    if (await IsPickupCheckedAsync(modal).ConfigureAwait(false) == true)
                    {
                        (mx, my) = await HumanCancelModalAsync(page, modal, mx, my, rng, ct).ConfigureAwait(false);
                        result = SetPickupResult.Ok;
                        return result;
                    }

                    // 5c) Vòng tick tối đa 3 lần: re-query text span TƯƠI → click kiểu người CÓ HIT-TEST →
                    //     chờ → đọc lại trạng thái bằng DOM sống. true → thoát vòng thành công.
                    bool ticked = false;
                    for (int attempt = 0; attempt < 3 && !ticked; attempt++)
                    {
                        var span = await FindPickupClickTargetAsync(modal).ConfigureAwait(false);
                        if (span is not null)
                        {
                            (mx, my, _) = await TryHumanClickVisibleAsync(page, span, mx, my, rng, ct).ConfigureAwait(false);
                        }
                        await Task.Delay(rng.Next(300, 900), ct).ConfigureAwait(false);
                        if (await IsPickupCheckedAsync(modal).ConfigureAwait(false) == true)
                        {
                            ticked = true;
                        }
                    }

                    if (!ticked)
                    {
                        result = SetPickupResult.CheckboxClickFailed;
                        return result;
                    }

                    // 6) Bấm "Lưu" (GHI lên shop thật). Không tìm / không click được → SaveFailed.
                    var saveBtn = await FindSaveButtonAsync(modal).ConfigureAwait(false);
                    if (saveBtn is null)
                    {
                        result = SetPickupResult.SaveFailed;
                        return result;
                    }

                    (mx, my, clicked) = await TryHumanClickVisibleAsync(page, saveBtn, mx, my, rng, ct).ConfigureAwait(false);
                    if (!clicked)
                    {
                        result = SetPickupResult.SaveFailed;
                        return result;
                    }

                    // 7) Chờ Lưu hoàn tất. Sau khi Lưu, Shopee CÓ THỂ (không chắc) bật thêm hộp xác nhận đổi
                    //    địa chỉ lấy hàng — nếu có thì bấm "Đồng ý" kiểu người rồi chờ modal Sửa đóng.
                    //    Hoàn tất → Ok; hết giờ (lỗi form/shop khóa / chưa chốt được) → SaveFailed.
                    result = await WaitPickupSaveCompletedAsync(page, 15000, mx, my, rng, ct).ConfigureAwait(false)
                        ? SetPickupResult.Ok
                        : SetPickupResult.SaveFailed;
                    return result;
                }
                finally
                {
                    // Chốt chặn: mọi nhánh thất bại (kể cả exception) mà modal còn mở → Hủy, KHÔNG để modal
                    // "Sửa Địa chỉ" mở treo. Re-find modal TƯƠI (handle cũ có thể đã stale). Best-effort.
                    if (result != SetPickupResult.Ok)
                    {
                        try
                        {
                            var openModal = await FindEditAddressModalAsync(page).ConfigureAwait(false);
                            if (openModal is not null)
                            {
                                await HumanCancelModalAsync(page, openModal, mx, my, rng, ct).ConfigureAwait(false);
                            }
                        }
                        catch { /* best-effort — nuốt lỗi (context ngắt / hủy) */ }
                    }
                }
            }
            catch
            {
                // Bất kỳ lỗi nào (selector đổi, context ngắt, hủy...) → Failed, KHÔNG phá phiên. Modal (nếu
                // còn mở) đã được finally trên Hủy trước khi exception rơi tới đây.
                return SetPickupResult.Failed;
            }
        }

        /// <summary>
        /// Dò địa chỉ (<c>.address-list .address-item-container</c>) đầu tiên có ô "Địa chỉ" khớp
        /// <paramref name="province"/>, poll tới khi hết <paramref name="timeoutMs"/> (danh sách có thể
        /// render dần). Không có item khớp → <c>null</c> (không ném).
        /// </summary>
        private static async Task<IElementHandle?> FindMatchingAddressItemAsync(
            IPage page, string province, int timeoutMs, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            do
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var items = await page.QuerySelectorAllAsync(".address-list .address-item-container")
                        .ConfigureAwait(false);
                    foreach (var it in items)
                    {
                        var detail = await ReadAddressDetailAsync(it).ConfigureAwait(false);
                        if (ShopeeShippingNav.AddressDetailMatchesProvince(detail, province))
                        {
                            return it; // địa chỉ khớp ĐẦU TIÊN theo thứ tự trang
                        }
                    }
                }
                catch { /* chưa render / selector không hợp lệ — thử vòng sau */ }

                await Task.Delay(400, ct).ConfigureAwait(false);
            }
            while (DateTime.UtcNow < deadline);

            return null;
        }

        /// <summary>
        /// Đọc InnerText của ô <c>.detail</c> thuộc hàng "Địa chỉ" trong một địa chỉ: duyệt các
        /// <c>div.grid</c>, lấy grid có <c>span.label</c> chuẩn hóa == "địa chỉ" (KHÔNG lấy nhầm
        /// <c>.detail</c> của hàng "Số điện thoại"). Không thấy → <c>null</c>.
        /// </summary>
        private static async Task<string?> ReadAddressDetailAsync(IElementHandle item)
        {
            try
            {
                var grids = await item.QuerySelectorAllAsync("div.grid").ConfigureAwait(false);
                foreach (var grid in grids)
                {
                    var label = await grid.QuerySelectorAsync("span.label").ConfigureAwait(false);
                    if (label is null)
                    {
                        continue;
                    }

                    var labelText = ShopeeShippingNav.NormalizeUiText(
                        await label.InnerTextAsync().ConfigureAwait(false));
                    if (labelText != "địa chỉ")
                    {
                        continue;
                    }

                    var detail = await grid.QuerySelectorAsync(".detail").ConfigureAwait(false);
                    if (detail is not null)
                    {
                        return await detail.InnerTextAsync().ConfigureAwait(false);
                    }
                }
            }
            catch { /* bỏ qua */ }

            return null;
        }

        /// <summary>True nếu địa chỉ có tag "Địa chỉ lấy hàng" (đang là địa chỉ lấy hàng của shop).</summary>
        private static async Task<bool> ItemHasPickupTagAsync(IElementHandle item)
        {
            try
            {
                var tags = await item.QuerySelectorAllAsync(".address-label").ConfigureAwait(false);
                foreach (var tag in tags)
                {
                    if (ShopeeShippingNav.IsPickupTagText(await tag.InnerTextAsync().ConfigureAwait(false)))
                    {
                        return true;
                    }
                }
            }
            catch { /* bỏ qua */ }

            return false;
        }

        /// <summary>
        /// Dò nút "Sửa" trong một địa chỉ: ưu tiên các <c>button</c> trong <c>.operations</c>, fallback mọi
        /// <c>button</c> trong item; khớp <see cref="ShopeeShippingNav.IsEditButtonText"/>. Không thấy → <c>null</c>.
        /// </summary>
        private static async Task<IElementHandle?> FindEditButtonAsync(IElementHandle item)
        {
            try
            {
                var ops = await item.QuerySelectorAsync(".operations").ConfigureAwait(false);
                if (ops is not null)
                {
                    var found = await FindButtonByTextAsync(ops, ShopeeShippingNav.IsEditButtonText).ConfigureAwait(false);
                    if (found is not null)
                    {
                        return found;
                    }
                }

                return await FindButtonByTextAsync(item, ShopeeShippingNav.IsEditButtonText).ConfigureAwait(false);
            }
            catch { /* bỏ qua */ }

            return null;
        }

        /// <summary>
        /// Dò nút "Lưu" trong footer modal: ưu tiên <c>button.eds-button--primary</c> trong
        /// <c>.eds-modal__footer</c>, fallback button có text khớp <see cref="ShopeeShippingNav.IsSaveButtonText"/>.
        /// </summary>
        private static async Task<IElementHandle?> FindSaveButtonAsync(IElementHandle modal)
        {
            try
            {
                var footer = await modal.QuerySelectorAsync(".eds-modal__footer").ConfigureAwait(false);
                var scope = footer ?? modal;

                var primary = await scope.QuerySelectorAsync("button.eds-button--primary").ConfigureAwait(false);
                if (primary is not null)
                {
                    return primary;
                }

                return await FindButtonByTextAsync(scope, ShopeeShippingNav.IsSaveButtonText).ConfigureAwait(false);
            }
            catch { /* bỏ qua */ }

            return null;
        }

        /// <summary>Dò nút "Hủy" trong footer modal (fallback: toàn modal), khớp
        /// <see cref="ShopeeShippingNav.IsCancelButtonText"/>. Không thấy → <c>null</c>.</summary>
        private static async Task<IElementHandle?> FindCancelButtonAsync(IElementHandle modal)
        {
            try
            {
                var footer = await modal.QuerySelectorAsync(".eds-modal__footer").ConfigureAwait(false);
                var scope = footer ?? modal;
                return await FindButtonByTextAsync(scope, ShopeeShippingNav.IsCancelButtonText).ConfigureAwait(false);
            }
            catch { /* bỏ qua */ }

            return null;
        }

        /// <summary>Duyệt mọi <c>button</c> trong <paramref name="scope"/>, trả cái đầu tiên có InnerText
        /// khớp <paramref name="match"/>. Không thấy → <c>null</c>.</summary>
        private static async Task<IElementHandle?> FindButtonByTextAsync(
            IElementHandle scope, Func<string?, bool> match)
        {
            var buttons = await scope.QuerySelectorAllAsync("button").ConfigureAwait(false);
            foreach (var b in buttons)
            {
                if (match(await b.InnerTextAsync().ConfigureAwait(false)))
                {
                    return b;
                }
            }

            return null;
        }

        /// <summary>
        /// Trong modal, dò <b>label</b> (<c>label.eds-checkbox</c>) của checkbox "Đặt làm địa chỉ lấy
        /// hàng": duyệt các label, khớp <c>span.eds-checkbox__label</c> qua
        /// <see cref="ShopeeShippingNav.IsSetPickupCheckboxText"/>. Query <b>TƯƠI mỗi lần gọi</b> — KHÔNG
        /// giữ handle qua re-render form (map load bất đồng bộ khiến Vue vẽ lại). Không thấy / lỗi (label
        /// detached / chưa render) → <c>null</c> (không ném).
        /// </summary>
        private static async Task<IElementHandle?> FindPickupCheckboxLabelAsync(IElementHandle modal)
        {
            try
            {
                var labels = await modal.QuerySelectorAllAsync("label.eds-checkbox").ConfigureAwait(false);
                foreach (var label in labels)
                {
                    var span = await label.QuerySelectorAsync("span.eds-checkbox__label").ConfigureAwait(false);
                    if (span is null)
                    {
                        continue;
                    }

                    if (ShopeeShippingNav.IsSetPickupCheckboxText(await span.InnerTextAsync().ConfigureAwait(false)))
                    {
                        return label;
                    }
                }
            }
            catch { /* modal/label detached hoặc chưa render — coi như chưa thấy */ }

            return null;
        }

        /// <summary>
        /// Đọc "đã tick chưa" của checkbox "Đặt làm địa chỉ lấy hàng" bằng <b>DOM sống</b>: re-query label
        /// TƯƠI (<see cref="FindPickupCheckboxLabelAsync"/>) rồi eval trạng thái <c>checked</c> của
        /// <c>input.eds-checkbox__input</c> bên trong — KHÔNG giữ handle <c>input</c> qua re-render (Vue vẽ
        /// lại input). <c>true</c>/<c>false</c> theo DOM; <c>null</c> khi không đọc được (label detached /
        /// chưa render).
        /// </summary>
        private static async Task<bool?> IsPickupCheckedAsync(IElementHandle modal)
        {
            var label = await FindPickupCheckboxLabelAsync(modal).ConfigureAwait(false);
            if (label is null)
            {
                return null;
            }

            try
            {
                return await label.EvaluateAsync<bool>(
                    "l => l.querySelector('input.eds-checkbox__input')?.checked === true").ConfigureAwait(false);
            }
            catch { return null; }
        }

        /// <summary>
        /// Trong modal, dò <b>mục tiêu click</b> để tick: text span <c>span.eds-checkbox__label</c> của đúng
        /// ô "Đặt làm địa chỉ lấy hàng" (mục tiêu lớn, rõ, hit-test sạch hơn cả thẻ label có <c>input</c>
        /// <c>opacity:0</c> phủ lên). Query <b>TƯƠI mỗi lần gọi</b>. Không thấy / lỗi → <c>null</c>.
        /// </summary>
        private static async Task<IElementHandle?> FindPickupClickTargetAsync(IElementHandle modal)
        {
            var label = await FindPickupCheckboxLabelAsync(modal).ConfigureAwait(false);
            if (label is null)
            {
                return null;
            }

            try
            {
                return await label.QuerySelectorAsync("span.eds-checkbox__label").ConfigureAwait(false);
            }
            catch { return null; }
        }

        /// <summary>
        /// Chờ ô checkbox "Đặt làm địa chỉ lấy hàng" <b>ổn định</b>: modal chứa Google Map load bất đồng bộ
        /// → Vue vẽ lại form; poll <see cref="FindPickupCheckboxLabelAsync"/> tới khi thấy label <b>HAI LẦN
        /// LIÊN TIẾP</b> (cách ~400ms) — để form re-render xong mới thao tác — hoặc hết
        /// <paramref name="timeoutMs"/>. Ổn định → <c>true</c>; hết giờ mà chưa từng thấy 2 lần liên tiếp →
        /// <c>false</c>.
        /// </summary>
        private static async Task<bool> WaitPickupCheckboxStableAsync(
            IElementHandle modal, int timeoutMs, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            bool seenPrev = false;
            do
            {
                ct.ThrowIfCancellationRequested();
                var seen = await FindPickupCheckboxLabelAsync(modal).ConfigureAwait(false) is not null;
                if (seen && seenPrev)
                {
                    return true;
                }
                seenPrev = seen;
                await Task.Delay(400, ct).ConfigureAwait(false);
            }
            while (DateTime.UtcNow < deadline);

            return false;
        }

        /// <summary>
        /// Chờ modal "Sửa Địa chỉ" xuất hiện (<c>.eds-modal__box</c> có <c>.title</c> khớp
        /// <see cref="ShopeeShippingNav.IsEditAddressModalTitle"/>), poll tới hết <paramref name="timeoutMs"/>.
        /// Không hiện → <c>null</c>.
        /// </summary>
        private static async Task<IElementHandle?> WaitEditAddressModalAsync(IPage page, int timeoutMs, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            do
            {
                ct.ThrowIfCancellationRequested();
                var modal = await FindEditAddressModalAsync(page).ConfigureAwait(false);
                if (modal is not null)
                {
                    return modal;
                }

                await Task.Delay(300, ct).ConfigureAwait(false);
            }
            while (DateTime.UtcNow < deadline);

            return null;
        }

        /// <summary>
        /// Chờ modal "Sửa Địa chỉ" <b>biến mất</b> (bấm Lưu xong), poll tới hết <paramref name="timeoutMs"/>.
        /// Đóng → <c>true</c>; hết giờ (vẫn còn) → <c>false</c>.
        /// </summary>
        private static async Task<bool> WaitEditAddressModalClosedAsync(IPage page, int timeoutMs, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            do
            {
                ct.ThrowIfCancellationRequested();
                if (await FindEditAddressModalAsync(page).ConfigureAwait(false) is null)
                {
                    return true;
                }

                await Task.Delay(300, ct).ConfigureAwait(false);
            }
            while (DateTime.UtcNow < deadline);

            return false;
        }

        /// <summary>
        /// Chờ thao tác <b>Lưu địa chỉ lấy hàng</b> hoàn tất, xử lý hộp xác nhận (nếu có). Sau khi bấm Lưu,
        /// Shopee CÓ THỂ (không phải lúc nào cũng) bật thêm hộp xác nhận đổi địa chỉ lấy hàng có nút "Đồng
        /// ý" — khi đó modal "Sửa Địa chỉ" KHÔNG đóng cho tới khi bấm "Đồng ý". Poll tới hết
        /// <paramref name="timeoutMs"/> (mỗi vòng ~300ms):
        /// <list type="number">
        /// <item>Ưu tiên: hộp xác nhận hiện → bấm "Đồng ý" <b>kiểu người (verified)</b>
        /// (<see cref="FindConfirmChangePickupButtonAsync"/> + <see cref="TryHumanClickVisibleAsync"/>),
        /// rồi kiểm lại vòng sau. Chỉ bấm MỘT lần (cờ <c>confirmDone</c>).</item>
        /// <item>Modal "Sửa Địa chỉ" đã đóng: nếu đã bấm "Đồng ý" → xong. Nếu CHƯA bấm mà modal biến mất →
        /// chờ <b>ân hạn ~1.2s</b> xem hộp xác nhận có hiện muộn không (Shopee có thể THAY modal Sửa bằng hộp
        /// xác nhận với khe render) — tránh báo Ok GIẢ; có hộp → quay lại bấm "Đồng ý", không → Lưu thẳng, xong.</item>
        /// </list>
        /// Thứ tự QUAN TRỌNG: bấm "Đồng ý" TRƯỚC khi coi "modal đóng = xong" — tránh trả <c>true</c> sớm khi
        /// hộp xác nhận còn treo/hiện muộn (chưa thực sự chốt đổi địa chỉ). Hết giờ → <c>false</c>. Hủy cắt được
        /// mỗi vòng (<see cref="CancellationToken.ThrowIfCancellationRequested"/> + <c>Task.Delay(ct)</c>).
        /// <paramref name="mx"/>/<paramref name="my"/> chỉ dùng nội bộ (bước cuối, không cần trả ra).
        /// </summary>
        private static async Task<bool> WaitPickupSaveCompletedAsync(
            IPage page, int timeoutMs, double mx, double my, Random rng, CancellationToken ct)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            bool confirmDone = false;
            do
            {
                ct.ThrowIfCancellationRequested();

                // 1) Hộp xác nhận đổi địa chỉ lấy hàng có thể hiện (KHÔNG chắc chắn) → bấm "Đồng ý" kiểu người.
                if (!confirmDone)
                {
                    var confirmBtn = await FindConfirmChangePickupButtonAsync(page).ConfigureAwait(false);
                    if (confirmBtn is not null)
                    {
                        bool ok;
                        (mx, my, ok) = await TryHumanClickVisibleAsync(page, confirmBtn, mx, my, rng, ct).ConfigureAwait(false);
                        if (ok)
                        {
                            confirmDone = true;
                            await Task.Delay(rng.Next(300, 900), ct).ConfigureAwait(false); // "đọc" rồi tiếp
                        }
                        continue; // kiểm lại vòng sau (hộp tan / modal đóng)
                    }
                }

                // 2) Modal "Sửa Địa chỉ" đã đóng.
                if (await FindEditAddressModalAsync(page).ConfigureAwait(false) is null)
                {
                    // Đã bấm "Đồng ý" rồi → chốt xong.
                    if (confirmDone)
                    {
                        return true;
                    }

                    // CHƯA bấm "Đồng ý" mà modal Sửa đã biến mất: hoặc (a) không cần xác nhận (Lưu thẳng),
                    // hoặc (b) Shopee THAY modal Sửa bằng hộp xác nhận nhưng hộp CHƯA KỊP render (khe thời
                    // gian). Ân hạn ngắn ~1.2s để hộp xác nhận (nếu có) kịp hiện — TRÁNH báo Ok GIẢ khi thực
                    // ra còn phải bấm "Đồng ý" (thao tác ghi thật). Thấy hộp → quay lại vòng chính bấm; hết
                    // ân hạn vẫn không có → Lưu thẳng, xong.
                    var grace = DateTime.UtcNow.AddMilliseconds(1200);
                    var lateConfirm = false;
                    do
                    {
                        ct.ThrowIfCancellationRequested();
                        if (await FindConfirmChangePickupButtonAsync(page).ConfigureAwait(false) is not null)
                        {
                            lateConfirm = true;
                            break;
                        }
                        await Task.Delay(300, ct).ConfigureAwait(false);
                    }
                    while (DateTime.UtcNow < grace);

                    if (!lateConfirm)
                    {
                        return true;
                    }
                    continue; // hộp xác nhận hiện muộn → vòng chính (block 1) bấm "Đồng ý"
                }

                await Task.Delay(300, ct).ConfigureAwait(false);
            }
            while (DateTime.UtcNow < deadline);

            return false;
        }

        /// <summary>Tìm modal "Sửa Địa chỉ" hiện có: <c>.eds-modal__box</c> có <c>.title</c> khớp
        /// <see cref="ShopeeShippingNav.IsEditAddressModalTitle"/>. Không có → <c>null</c> (không ném).</summary>
        private static async Task<IElementHandle?> FindEditAddressModalAsync(IPage page)
        {
            try
            {
                var boxes = await page.QuerySelectorAllAsync(".eds-modal__box").ConfigureAwait(false);
                foreach (var box in boxes)
                {
                    var title = await box.QuerySelectorAsync(".title").ConfigureAwait(false);
                    if (title is null)
                    {
                        continue;
                    }

                    if (ShopeeShippingNav.IsEditAddressModalTitle(await title.InnerTextAsync().ConfigureAwait(false)))
                    {
                        return box;
                    }
                }
            }
            catch { /* bỏ qua */ }

            return null;
        }

        /// <summary>
        /// Dò nút <b>"Đồng ý"</b> của <b>hộp xác nhận đổi địa chỉ lấy hàng</b> (modal thứ hai bật lên SAU khi
        /// bấm Lưu — không phải lúc nào cũng hiện). Duyệt mọi <c>.eds-modal__footer</c> đang mở; chỉ nhận
        /// footer nào ĐỒNG THỜI có nút khớp <see cref="ShopeeShippingNav.IsConfirmButtonText"/> ("đồng ý")
        /// LẪN nút khớp <see cref="ShopeeShippingNav.IsCheckDetailButtonText"/> ("kiểm tra chi tiết") — dấu
        /// hiệu riêng để KHÔNG bấm nhầm "Đồng ý" của hộp thoại khác. Nút "Đồng ý" phải đang hiển thị
        /// (<c>BoundingBoxAsync() != null</c>) mới trả về. Không thấy / lỗi (DOM đổi, detached) →
        /// <c>null</c> (không ném). <b>Lưu ý:</b> bấm nút này sẽ TẮT kênh vận chuyển "Trong Ngày".
        /// </summary>
        private static async Task<IElementHandle?> FindConfirmChangePickupButtonAsync(IPage page)
        {
            try
            {
                var footers = await page.QuerySelectorAllAsync(".eds-modal__footer").ConfigureAwait(false);
                foreach (var footer in footers)
                {
                    var confirmBtn = await FindButtonByTextAsync(footer, ShopeeShippingNav.IsConfirmButtonText).ConfigureAwait(false);
                    if (confirmBtn is null)
                    {
                        continue;
                    }

                    // Guard đúng hộp: footer phải có CẢ "Kiểm tra chi tiết" → tránh nhầm hộp thoại khác.
                    var checkDetailBtn = await FindButtonByTextAsync(footer, ShopeeShippingNav.IsCheckDetailButtonText).ConfigureAwait(false);
                    if (checkDetailBtn is null)
                    {
                        continue;
                    }

                    // Chỉ nhận nút "Đồng ý" đang hiển thị.
                    if (await HasBoundingBoxAsync(confirmBtn).ConfigureAwait(false))
                    {
                        return confirmBtn;
                    }
                }
            }
            catch { /* DOM đổi / detached — coi như không thấy */ }

            return null;
        }

        /// <summary>
        /// Bấm nút "Hủy" của modal kiểu người (best-effort) rồi chờ modal đóng — dùng ở các nhánh thoát an
        /// toàn (không ghi gì lên shop). Trả về vị trí chuột mới.
        /// </summary>
        private static async Task<(double X, double Y)> HumanCancelModalAsync(
            IPage page, IElementHandle modal, double mx, double my, Random rng, CancellationToken ct)
        {
            var cancel = await FindCancelButtonAsync(modal).ConfigureAwait(false);
            if (cancel is not null)
            {
                (mx, my, _) = await TryHumanClickVisibleAsync(page, cancel, mx, my, rng, ct).ConfigureAwait(false);
            }

            await WaitEditAddressModalClosedAsync(page, 10000, ct).ConfigureAwait(false);
            return (mx, my);
        }

        /// <summary>
        /// Click <b>kiểu người CÓ HIT-TEST</b> nhưng chỉ khi phần tử đang hiển thị
        /// (<c>BoundingBoxAsync() != null</c>): scroll vào tầm nhìn trước, box vẫn null → KHÔNG click và trả
        /// <c>Clicked=false</c>. Có box → gọi <see cref="HumanMoveAndClickVerifiedAsync"/> (chỉ nhả chuột khi
        /// <c>elementFromPoint</c> tại điểm click đúng là phần tử đích — chống click nhầm link khác khi bị
        /// che/cụp); <c>Clicked</c> lấy từ kết quả verified (hit-test fail → false, KHÔNG click mù). Trả về
        /// vị trí chuột mới + đã click hay chưa.
        /// </summary>
        private static async Task<(double X, double Y, bool Clicked)> TryHumanClickVisibleAsync(
            IPage page, IElementHandle el, double mx, double my, Random rng, CancellationToken ct)
        {
            try { await el.ScrollIntoViewIfNeededAsync().ConfigureAwait(false); } catch { /* bỏ qua */ }

            if (!await HasBoundingBoxAsync(el).ConfigureAwait(false))
            {
                try { await el.ScrollIntoViewIfNeededAsync().ConfigureAwait(false); } catch { /* bỏ qua */ }
                if (!await HasBoundingBoxAsync(el).ConfigureAwait(false))
                {
                    return (mx, my, false);
                }
            }

            bool clicked;
            (mx, my, clicked) = await HumanMoveAndClickVerifiedAsync(page, el, mx, my, rng, ct).ConfigureAwait(false);
            return (mx, my, clicked);
        }

        /// <summary>
        /// Phần tử có <b>bounding box</b> không (đang hiển thị), <b>nuốt lỗi</b> handle DETACHED (Vue vẽ lại
        /// form sau khi map/modal re-render khiến <c>BoundingBoxAsync</c> ném) → <c>false</c> graceful, KHÔNG
        /// để exception rò lên catch ngoài cùng của <see cref="SetPickupAddressAsync"/> (lỗi handle biến
        /// thành "không click được", modal vẫn được Hủy).
        /// </summary>
        private static async Task<bool> HasBoundingBoxAsync(IElementHandle el)
        {
            try { return await el.BoundingBoxAsync().ConfigureAwait(false) is not null; }
            catch { return false; }
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
