using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.App.Services;

/// <summary>
/// Một phiên mở trang bán hàng CHẠY NỀN ĐỘC LẬP cho một tài khoản (mỗi tài khoản một Brave/profile/CDP
/// port/proxy/theo-dõi-đơn riêng) — nhờ đó mở được nhiều shop song song. Kế thừa
/// <see cref="ObservableObject"/> để trạng thái quan sát được.
/// <para>
/// Toàn bộ luồng <b>bê nguyên</b> từ <c>AccountsViewModel.OpenSellerAsync</c> cũ (chọn proxy → chuẩn bị
/// trình duyệt → mở → tự đăng nhập kiểu người → vòng poll bắt cookie + theo dõi đơn 30' → bắt-cookie-chốt),
/// CHỈ khác: <b>bỏ mọi hộp thoại modal</b> (15 phiên = 15 modal) → thay bằng trạng thái/log per-account; và
/// việc cập nhật danh sách UI được <b>marshal về UI thread</b> ở ViewModel qua sự kiện (session chỉ ghi DB
/// trên thread nền — SQLite an toàn — rồi phát <see cref="CookieSaved"/>).
/// </para>
/// </summary>
public partial class AccountSession : ObservableObject, IAccountSession
{
    private readonly long _accountId;
    private readonly AppServices _services;
    private readonly ShopeeLoginService _loginService;
    private readonly IProxyHealthChecker _healthChecker;

    // Round-robin BỀN cho proxy thủ công được CHIA SẺ giữa các phiên (do manager giữ chỉ số) → nhiều tài
    // khoản trải đều trên danh sách proxy thay vì cùng nhận proxy đầu tiên.
    private readonly Func<IReadOnlyList<ProxyEntry>, ProxyEntry?> _nextManualProxy;

    private readonly object _lifecycleLock = new();
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private volatile ILoginSession? _session;

    // Bật trong lúc đang XỬ LÝ ĐƠN (điều hướng kiểu người). Khi bật, vòng RunAsync KHÔNG chạy nhịp đọc đơn
    // (ReadToShipCountAsync có reload trang → sẽ phá thao tác điều hướng đang chạy giữa chừng).
    private volatile bool _navigating;

    public AccountSession(
        long accountId,
        AppServices services,
        ShopeeLoginService loginService,
        IProxyHealthChecker healthChecker,
        Func<IReadOnlyList<ProxyEntry>, ProxyEntry?> nextManualProxy)
    {
        _accountId = accountId;
        _services = services;
        _loginService = loginService;
        _healthChecker = healthChecker;
        _nextManualProxy = nextManualProxy;
    }

    public long AccountId => _accountId;

    [ObservableProperty]
    private SessionState _state = SessionState.Stopped;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private int? _toShipCount;

    [ObservableProperty]
    private string? _lastError;

    // Bất kỳ thay đổi quan sát được nào → phát Changed để manager/VM cập nhật UI. Event Changed CỐ Ý bắn
    // từ thread nền: manager dùng ConcurrentDictionary (thread-safe) và VM tự marshal về UI thread khi đụng
    // ObservableCollection. Riêng PropertyChanged (cho binding trực tiếp) được marshal ở OnPropertyChanged.
    partial void OnStateChanged(SessionState value) => Changed?.Invoke();
    partial void OnStatusTextChanged(string? value) => Changed?.Invoke();
    partial void OnToShipCountChanged(int? value) => Changed?.Invoke();
    partial void OnLastErrorChanged(string? value) => Changed?.Invoke();

    /// <summary>
    /// Marshal thông báo <b>PropertyChanged</b> về UI thread. Phiên chạy nền (RunAsync) set
    /// State/StatusText/ToShipCount trên thread nền; nếu UI (Plan B) bind TRỰC TIẾP vào phiên thì Avalonia
    /// cập nhật binding phải trên UI thread — nếu bắn từ nền sẽ ném "Call from invalid thread". Chạy ngay
    /// nếu đã ở UI thread; ngược lại <c>Dispatcher.UIThread.Post</c>.
    /// </summary>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        var ui = Avalonia.Threading.Dispatcher.UIThread;
        if (ui.CheckAccess())
        {
            base.OnPropertyChanged(e);
        }
        else
        {
            ui.Post(() => base.OnPropertyChanged(e));
        }
    }

    public Process? BraveProcess => _session?.BraveProcess;

    public event Action? Changed;
    public event Action<long>? CookieSaved;

    public Task StartAsync()
    {
        lock (_lifecycleLock)
        {
            // Idempotent: đang chuẩn bị / đang chạy → bỏ qua (không mở trùng cùng một tài khoản).
            if (State is SessionState.Opening or SessionState.Running)
            {
                return Task.CompletedTask;
            }

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            LastError = null;
            State = SessionState.Opening;
            _runTask = Task.Run(() => RunAsync(ct));
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? run;
        lock (_lifecycleLock)
        {
            cts = _cts;
            run = _runTask;
        }

        // Phản hồi cho người dùng; GIỮ State=Running để nút "Mở" còn khóa tới khi Brave chết thật (Lỗi 2).
        if (State is SessionState.Opening or SessionState.Running)
        {
            StatusText = "Đang dừng...";
        }

        try { cts?.Cancel(); } catch { /* bỏ qua */ }

        if (run is not null)
        {
            // Chờ vòng lặp thoát & dispose (kill Brave) trong ~8s.
            try { await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(8))).ConfigureAwait(false); }
            catch { /* bỏ qua */ }
        }

        // Phòng hờ: nếu vì lý do gì Brave còn sống thì kill cả cây tiến trình (không để mồ côi giữ khóa hồ sơ).
        try
        {
            var p = _session?.BraveProcess;
            if (p is { HasExited: false })
            {
                p.Kill(entireProcessTree: true);
            }
        }
        catch { /* bỏ qua */ }

        State = SessionState.Stopped;
    }

    /// <summary>
    /// Bước đầu xử lý đơn: trong phiên đang chạy, điều hướng KIỂU NGƯỜI tới "Cài Đặt Vận Chuyển" → tab
    /// "Địa Chỉ". Bật cờ <see cref="_navigating"/> để vòng <see cref="RunAsync"/> KHÔNG reload đọc đơn giữa
    /// chừng (phá thao tác). Graceful: phiên chưa chạy / bị hủy / lỗi → false, KHÔNG ném.
    /// </summary>
    public async Task<bool> ProcessOrdersAsync()
    {
        // Chụp phiên + token dưới lock (nuốt ObjectDisposedException nếu _cts đã dispose).
        var s = _session;
        CancellationToken tok;
        try
        {
            lock (_lifecycleLock)
            {
                tok = _cts?.Token ?? default;
            }
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        // _navigating: đang có lượt điều hướng chạy dở (bấm lặp) → bỏ qua, không chạy 2 luồng chuột chồng nhau.
        if (s is null || State != SessionState.Running || _navigating)
        {
            return false;
        }

        _navigating = true;
        StatusText = "Đang mở Cài đặt vận chuyển → Địa Chỉ (kiểu người)...";
        try
        {
            var ok = await s.OpenShippingAddressSettingsAsync(tok).ConfigureAwait(false);
            StatusText = ok
                ? "Đã mở tab Địa Chỉ (Cài đặt vận chuyển)."
                : "Không mở được Cài đặt vận chuyển — thao tác tay trong cửa sổ Brave.";
            return ok;
        }
        catch (OperationCanceledException)
        {
            // Bị dừng chủ động trong lúc điều hướng — không phải lỗi.
            return false;
        }
        finally
        {
            _navigating = false;
        }
    }

    /// <summary>
    /// Luồng chạy nền của phiên. Bê nguyên logic từ <c>OpenSellerAsync</c>; thay modal bằng trạng thái;
    /// tôn trọng <paramref name="ct"/> để dừng nhanh khi người dùng bấm Dừng / thoát app.
    /// </summary>
    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            // Đọc tài khoản theo Id (KHÔNG đọc form) — dùng cho cả chọn proxy lẫn tự đăng nhập.
            var acc = _services.Accounts.GetById(_accountId);

            // Hồ sơ persistent riêng cho tài khoản này → mở lại vẫn còn đăng nhập.
            var baseDir = Path.GetDirectoryName(_services.Database.Path) ?? ".";
            var userDataDir = BrowserProfilePaths.ForAccount(baseDir, _accountId);
            Directory.CreateDirectory(userDataDir);

            // 1) Chọn proxy theo thứ tự ưu tiên (GIỮ NGUYÊN):
            //    (1) API key KiotProxy RIÊNG của tài khoản → sticky riêng mỗi tài khoản,
            //    (2) danh sách proxy thủ công → round-robin BỀN (chia sẻ giữa các phiên),
            //    (3) danh sách API key KiotProxy CHUNG trong Cài đặt,
            //    (4) IP máy (null).
            SetStatus(SessionState.Opening, "Đang kiểm tra proxy...");
            var manual = _services.Proxies.GetAll();
            ProxyEntry? proxy;
            if (!string.IsNullOrWhiteSpace(acc?.ProxyKey))
            {
                IKiotProxyClient kiot = new KiotProxyClient(new[] { acc!.ProxyKey! });
                proxy = await ProxySelector.SelectKiotProxyAsync(kiot, _healthChecker, ct).ConfigureAwait(false);
            }
            else if (manual.Count > 0)
            {
                proxy = _nextManualProxy(manual); // proxy thủ công: round-robin BỀN, KHÔNG kiểm
            }
            else
            {
                var kiotKeys = _services.Settings.GetKiotProxyKeys();
                IKiotProxyClient? kiot = kiotKeys.Count == 0 ? null : new KiotProxyClient(kiotKeys);
                proxy = await ProxySelector.SelectKiotProxyAsync(kiot, _healthChecker, ct).ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();

            // 2) Đảm bảo trình duyệt đã cài (tải lần đầu ~150MB) — chạy nền.
            SetStatus(SessionState.Opening, "Đang chuẩn bị trình duyệt...");
            var installCode = await Task.Run(() => _loginService.EnsureBrowserInstalled(), ct).ConfigureAwait(false);
            if (installCode != 0)
            {
                SetError("Không cài được trình duyệt. Kiểm tra mạng rồi thử lại.");
                return;
            }

            // 3) Mở cửa sổ trình duyệt (profile persistent) tới trang bán hàng.
            ILoginSession session;
            try
            {
                session = await _loginService.OpenAsync(userDataDir, proxy, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SetError(ex.Message);
                return;
            }

            _session = session; // expose BraveProcess cho Plan B (focus cửa sổ)

            await using (session)
            {
                // 4) Tự đăng nhập KIỂU NGƯỜI bằng user/password của tài khoản này. Graceful: đã đăng nhập
                //    sẵn / không thấy ô → bỏ qua.
                if (acc is not null && !string.IsNullOrEmpty(acc.Password))
                {
                    SetStatus(SessionState.Running, "Đang tự đăng nhập (kiểu người)...");
                    try { await session.TryHumanLoginAsync(acc.Email, acc.Password, ct).ConfigureAwait(false); }
                    catch { /* không phá luồng — người dùng tự nhập tay nếu cần */ }
                }

                // 5) Tự bắt & lưu cookie trong lúc cửa sổ mở; kết thúc khi người dùng đóng hết cửa sổ.
                SetStatus(SessionState.Running,
                    "Đã mở trình duyệt. Đăng nhập xong app sẽ tự theo dõi đơn mỗi 30'; đóng cửa sổ để dừng.");
                ToShipCount = null;

                string? lastSaved = null;
                const int PollMs = 1000;
                const int OrderIntervalMin = 30;
                const int OrderRetrySec = 30;
                var nextOrderCheck = DateTime.UtcNow;
                bool firstOrderCheck = true;
                // Chốt chặn: cap an toàn dài (12h). Tín hiệu kết thúc CHÍNH vẫn là "không còn cửa sổ nào".
                var hardCap = DateTime.UtcNow.AddHours(12);
                // Cần 0 cửa sổ ở 2 vòng poll LIÊN TIẾP mới coi là đã đóng (tránh thoát nhầm lúc chuyển trang).
                int zeroPageStreak = 0;

                while (!session.IsClosed && DateTime.UtcNow < hardCap && !ct.IsCancellationRequested)
                {
                    await Task.WhenAny(session.Closed, Task.Delay(PollMs, ct)).ConfigureAwait(false);

                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }

                    if (session.OpenPageCount == 0)
                    {
                        if (++zeroPageStreak >= 2)
                        {
                            break;
                        }
                    }
                    else
                    {
                        zeroPageStreak = 0;
                    }

                    string json;
                    try { json = await session.CaptureCookiesJsonAsync().ConfigureAwait(false); }
                    catch { break; } // context đã đóng giữa chừng

                    // CHỈ lưu khi đã có cookie ĐĂNG NHẬP Shopee (tránh đè cookie hợp lệ cũ bằng cookie theo dõi).
                    if (!string.IsNullOrEmpty(json) && json != lastSaved && ShopeeLoginCookies.IsLoggedIn(json))
                    {
                        if (TrySaveCookie(json))
                        {
                            lastSaved = json;
                        }
                    }

                    // Nhịp theo dõi đơn "Chờ Lấy Hàng": tới hạn thì reload + đọc số. Lần đầu KHÔNG reload.
                    // Đang điều hướng xử lý đơn (_navigating) → BỎ QUA nhịp này (reload sẽ phá thao tác giữa chừng).
                    if (!_navigating && DateTime.UtcNow >= nextOrderCheck)
                    {
                        var count = await session.ReadToShipCountAsync(reload: !firstOrderCheck, ct).ConfigureAwait(false);
                        if (count is int n)
                        {
                            firstOrderCheck = false;
                            ToShipCount = n; // VM tự định dạng dòng hiển thị theo số này
                            nextOrderCheck = DateTime.UtcNow.AddMinutes(OrderIntervalMin); // đã đăng nhập → 30'
                        }
                        else
                        {
                            // Chưa đăng nhập / chưa đọc được → thử lại sớm, KHÔNG reload.
                            nextOrderCheck = DateTime.UtcNow.AddSeconds(OrderRetrySec);
                        }
                    }
                }

                // Lần bắt cookie CHỐT trước khi dispose (đăng nhập xong đóng cửa sổ ngay vẫn bắt kịp).
                try
                {
                    var json = await session.CaptureCookiesJsonAsync().ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(json) && json != lastSaved && ShopeeLoginCookies.IsLoggedIn(json))
                    {
                        if (TrySaveCookie(json))
                        {
                            lastSaved = json;
                        }
                    }
                }
                catch { /* browser đã chết hẳn — bỏ qua */ }

                // Kết quả trung thực (KHÔNG khẳng định "chưa đăng nhập").
                StatusText = lastSaved != null
                    ? "Đã lưu cookie đăng nhập vào tài khoản."
                    : "Chưa lưu được cookie. Nếu đã đăng nhập, phiên vẫn được giữ trong hồ sơ (lần sau mở lại vẫn còn).";
            }
        }
        catch (OperationCanceledException)
        {
            // Bị dừng chủ động (Dừng / thoát app) — không phải lỗi.
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
        finally
        {
            _session = null;
            lock (_lifecycleLock)
            {
                // Kết thúc bình thường / bị hủy → Stopped; giữ nguyên Error để còn hiển thị lỗi.
                if (State != SessionState.Error)
                {
                    State = SessionState.Stopped;
                }
            }
        }
    }

    private void SetStatus(SessionState state, string text)
    {
        StatusText = text;
        State = state;
    }

    private void SetError(string message)
    {
        LastError = message;
        StatusText = message;
        State = SessionState.Error;
    }

    /// <summary>
    /// Ghi cookie JSON vào ĐÚNG tài khoản của phiên (thread nền — SQLite an toàn) rồi phát
    /// <see cref="CookieSaved"/> để VM làm mới danh sách trên UI thread. Trả true nếu đã ghi.
    /// </summary>
    private bool TrySaveCookie(string cookieJson)
    {
        if (CookieJson.Deserialize(cookieJson).Count == 0)
        {
            return false; // JSON không chứa cookie nào
        }

        var acc = _services.Accounts.GetById(_accountId);
        if (acc is null)
        {
            return false; // tài khoản đã bị xóa
        }

        acc.Cookie = cookieJson;
        _services.Accounts.Update(acc);

        // VM nghe sự kiện này để dựng lại danh sách (instance trong Accounts có cookie mới) trên UI thread.
        CookieSaved?.Invoke(_accountId);
        return true;
    }
}
