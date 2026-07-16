using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using XuLyDonShopee.App.ViewModels;
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

    // Proxy đang NƯỚNG vào Brave (đặt lúc launch qua --proxy-server) — watchdog kiểm cái này còn sống không.
    private volatile ProxyEntry? _currentProxy;

    // Client nguồn KiotProxy của phiên (null nếu phiên KHÔNG dùng KiotProxy: proxy thủ công / IP máy) →
    // watchdog CHỈ chạy khi client này != null.
    private volatile IKiotProxyClient? _kiotClient;

    // Chờ trước lần kiểm lại (xác nhận proxy chết lần 2) để chống false-negative khi mạng chập chờn.
    private const int ProxyRecheckDelayMs = 5000;

    // Nhãn tài khoản gắn vào mỗi dòng log (phân biệt nguồn khi nhiều phiên chạy song song). Mặc định
    // "TK {id}" để log phát TRƯỚC khi đọc được email (chọn proxy, chuẩn bị trình duyệt) vẫn có nhãn;
    // RunAsync cập nhật thành email khi đã đọc tài khoản.
    private string _logLabel;

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
        _logLabel = $"TK {accountId}";
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
    /// Xử lý đơn: trong phiên đang chạy, điều hướng KIỂU NGƯỜI tới "Cài Đặt Vận Chuyển" → tab "Địa Chỉ"
    /// (bước 1), rồi <b>đặt địa chỉ lấy hàng</b> theo tỉnh mặc định của tài khoản
    /// (<see cref="AccountsViewModel.DefaultPickupAddress"/> nếu chưa chọn) (bước 2). Bật cờ
    /// <see cref="_navigating"/> bao trùm cả 2 bước để vòng <see cref="RunAsync"/> KHÔNG reload đọc đơn
    /// giữa chừng (phá thao tác). Graceful: phiên chưa chạy / bị hủy / lỗi → false, KHÔNG ném.
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
            // Bước 1: mở Cài đặt vận chuyển → tab Địa Chỉ. Kết quả phân biệt bước hỏng để báo StatusText đúng.
            var nav = await s.OpenShippingAddressSettingsAsync(tok).ConfigureAwait(false);
            if (nav != ShippingNavResult.Ok)
            {
                StatusText = nav switch
                {
                    ShippingNavResult.PageNotOpened =>
                        "Không mở được trang Cài đặt vận chuyển (click không ăn / trang không chuyển) — thao tác tay trong cửa sổ Brave.",
                    ShippingNavResult.AddressTabNotFound =>
                        "Đã mở Cài đặt vận chuyển nhưng không thấy tab \"Địa Chỉ\" — Shopee có thể đã đổi giao diện, thao tác tay trong Brave.",
                    _ => "Không mở được Cài đặt vận chuyển — thao tác tay trong cửa sổ Brave.",
                };
                return false;
            }

            // Bước 2: đặt địa chỉ lấy hàng theo tỉnh mặc định của tài khoản (null/rỗng → mặc định app).
            var acc = _services.Accounts.GetById(_accountId);
            var province = string.IsNullOrWhiteSpace(acc?.PickupAddress)
                ? AccountsViewModel.DefaultPickupAddress
                : acc!.PickupAddress!;

            StatusText = $"Đang chọn địa chỉ lấy hàng ({province})...";
            var pick = await s.SetPickupAddressAsync(province, tok).ConfigureAwait(false);
            StatusText = pick switch
            {
                SetPickupResult.Ok => $"Đã đặt địa chỉ lấy hàng: {province}.",
                SetPickupResult.AddressNotFound =>
                    $"Không thấy địa chỉ ở {province} trong danh sách — kiểm tra tay trong cửa sổ Brave.",
                SetPickupResult.EditModalNotOpened =>
                    $"Mở được danh sách nhưng không sửa được địa chỉ ({province}) — shop có thể bị khóa sửa, kiểm tra tay.",
                SetPickupResult.CheckboxNotFound =>
                    $"Mở được ô Sửa địa chỉ nhưng không thấy mục \"Đặt làm địa chỉ lấy hàng\" — kiểm tra tay trong Brave.",
                SetPickupResult.CheckboxClickFailed =>
                    $"Không tick được \"Đặt làm địa chỉ lấy hàng\" ({province}) — kiểm tra tay trong cửa sổ Brave.",
                SetPickupResult.SaveFailed =>
                    $"Đã tick nhưng chưa Lưu được địa chỉ lấy hàng ({province}) — kiểm tra tay trong cửa sổ Brave.",
                _ => $"Không đặt được địa chỉ lấy hàng ({province}) — kiểm tra tay trong cửa sổ Brave.",
            };
            if (pick != SetPickupResult.Ok)
            {
                return false;
            }

            // Bước 3: xử lý LẦN LƯỢT MỌI đơn — lặp ProcessFirstOrderAsync (mỗi vòng: điều hướng "Tất cả" →
            // quét đơn đầu có "Chuẩn bị hàng" → arrange → In phiếu → đóng modal) TỚI KHI hết đơn (NoOrder).
            // Đơn đã arrange MẤT nút "Chuẩn bị hàng" nên vòng tự dừng khi mọi đơn xử lý xong. Mọi bước ghi log
            // qua ActivityLog (panel + file) để smoke live thấy rõ.
            var log = (Action<string>)(m => _services.Log.Append(_logLabel, m));
            const int MaxOrders = 200;              // chốt chặn an toàn (tránh lặp vô hạn nếu 1 đơn kẹt ở "Chuẩn bị hàng")
            var loopRng = new Random();
            int done = 0;
            ArrangeShipmentResult last = ArrangeShipmentResult.NoOrder;
            while (done < MaxOrders)
            {
                StatusText = $"Đang xử lý đơn thứ {done + 1}...";
                last = await s.ProcessFirstOrderAsync(@"D:\Phieu-giao-hang", log, tok).ConfigureAwait(false);
                if (last == ArrangeShipmentResult.NoOrder)
                {
                    break;                          // hết đơn cần "Chuẩn bị hàng"
                }
                if (last != ArrangeShipmentResult.Ok)
                {
                    break;                          // lỗi ở 1 đơn (PrintFailed/ConfirmFailed/...) → dừng, báo bước lỗi
                }
                done++;
                // Dừng ngẫu nhiên kiểu người giữa các đơn.
                try { await Task.Delay(loopRng.Next(1500, 3500), tok).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
            }

            StatusText = last switch
            {
                ArrangeShipmentResult.NoOrder =>
                    done > 0 ? $"Đã xử lý xong {done} đơn. Không còn đơn nào cần xử lý."
                             : "Không có đơn nào cần xử lý.",
                ArrangeShipmentResult.Ok => $"Đã xử lý {done} đơn (đạt chốt chặn {MaxOrders}).", // hiếm khi tới cap
                ArrangeShipmentResult.OrdersPageNotOpened => $"Đã xử lý {done} đơn; không mở được danh sách đơn.",
                ArrangeShipmentResult.PrepareNotFound     => $"Đã xử lý {done} đơn; không bấm được Chuẩn bị hàng ở đơn kế.",
                ArrangeShipmentResult.ShipModalNotOpened  => $"Đã xử lý {done} đơn; không mở được ô Giao Đơn Hàng ở đơn kế.",
                ArrangeShipmentResult.ConfirmFailed       => $"Đã xử lý {done} đơn; không Xác nhận được ở đơn kế.",
                ArrangeShipmentResult.DetailModalNotOpened=> $"Đã xử lý {done} đơn; không mở được Thông Tin Chi Tiết ở đơn kế.",
                ArrangeShipmentResult.PrintFailed         => $"Đã xử lý {done} đơn; không In phiếu giao được ở đơn kế.",
                _ => $"Đã xử lý {done} đơn; gặp lỗi ở đơn kế — kiểm tra tay trong Brave.",
            };
            return last is ArrangeShipmentResult.NoOrder or ArrangeShipmentResult.Ok || done > 0;
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
    /// Kiểm tra đơn NGAY (thủ công): trong phiên đang chạy, về trang chủ Seller (Goto như người gõ URL —
    /// KHÔNG click máy) rồi đọc lại số "Chờ Lấy Hàng" ngay, cập nhật <see cref="ToShipCount"/> — không đợi
    /// chu kỳ theo dõi 30'. Bật cờ <see cref="_navigating"/> để vòng <see cref="RunAsync"/> KHÔNG reload đọc
    /// đơn giữa chừng và để loại trừ với <see cref="ProcessOrdersAsync"/> (hai thao tác không chạy chồng nhau
    /// trên cùng trang). Graceful: phiên chưa chạy / đang bận / bị hủy / không đọc được → false, KHÔNG ném.
    /// KHÔNG đổi <see cref="ToShipCount"/> khi không đọc được (giữ số cũ).
    /// </summary>
    public async Task<bool> CheckOrdersAsync()
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

        // _navigating: đang có lượt điều hướng chạy dở (bấm lặp / xử lý đơn) → bỏ qua, không chạy chồng nhau.
        if (s is null || State != SessionState.Running || _navigating)
        {
            return false;
        }

        _navigating = true;
        StatusText = "Đang về trang chủ để kiểm tra đơn...";
        try
        {
            // Về trang chủ (Goto) + đọc lại số ngay (reload:false vì trang vừa load) — gộp trong Core.
            var count = await s.GoHomeAndReadToShipCountAsync(tok).ConfigureAwait(false);
            if (count is int n)
            {
                ToShipCount = n; // VM tự định dạng dòng hiển thị theo số này
                StatusText = $"Đã kiểm tra: Chờ Lấy Hàng = {n}.";
                return true;
            }

            // Bị hủy giữa chừng (người dùng bấm Dừng): Core nuốt OperationCanceledException và trả null —
            // KHÔNG đè thông báo "Đang dừng..." bằng câu báo lỗi gây hiểu lầm.
            if (tok.IsCancellationRequested)
            {
                return false;
            }

            // Không đọc được → GIỮ nguyên số cũ (KHÔNG đổi ToShipCount).
            StatusText = "Không đọc được số đơn — có thể chưa đăng nhập xong, kiểm tra cửa sổ Brave.";
            return false;
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
    /// Chọn proxy theo thứ tự ưu tiên (GIỮ NGUYÊN 4 mức của luồng cũ) và ĐỒNG THỜI set
    /// <see cref="_kiotClient"/> — client nguồn KiotProxy của phiên để watchdog canh proxy:
    /// (1) API key KiotProxy RIÊNG của tài khoản → sticky riêng mỗi tài khoản (watchdog BẬT),
    /// (2) danh sách proxy thủ công → round-robin BỀN, chia sẻ giữa các phiên (watchdog TẮT: <c>_kiotClient=null</c>),
    /// (3) danh sách API key KiotProxy CHUNG trong Cài đặt (watchdog BẬT nếu có key),
    /// (4) IP máy (null → watchdog TẮT).
    /// </summary>
    private async Task<ProxyEntry?> SelectProxyAsync(Account? acc, CancellationToken ct)
    {
        var manual = _services.Proxies.GetAll();
        if (!string.IsNullOrWhiteSpace(acc?.ProxyKey))
        {
            _kiotClient = new KiotProxyClient(new[] { acc!.ProxyKey! });
            return await ProxySelector.SelectKiotProxyAsync(_kiotClient, _healthChecker, ct).ConfigureAwait(false);
        }
        if (manual.Count > 0)
        {
            _kiotClient = null;                                   // proxy thủ công → không canh
            return _nextManualProxy(manual);                      // round-robin BỀN, KHÔNG kiểm
        }
        var kiotKeys = _services.Settings.GetKiotProxyKeys();
        _kiotClient = kiotKeys.Count == 0 ? null : new KiotProxyClient(kiotKeys);
        return _kiotClient is null ? null
            : await ProxySelector.SelectKiotProxyAsync(_kiotClient, _healthChecker, ct).ConfigureAwait(false);
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

            // Gắn nhãn email cho log (thay mặc định "TK {id}") để dòng log dễ nhận nguồn.
            if (!string.IsNullOrWhiteSpace(acc?.Email))
            {
                _logLabel = acc!.Email;
            }

            // Hồ sơ persistent riêng cho tài khoản này → mở lại vẫn còn đăng nhập.
            var baseDir = Path.GetDirectoryName(_services.Database.Path) ?? ".";
            var userDataDir = BrowserProfilePaths.ForAccount(baseDir, _accountId);
            Directory.CreateDirectory(userDataDir);

            // 1) Chọn proxy theo thứ tự ưu tiên (GIỮ NGUYÊN 4 mức) + set _kiotClient để watchdog canh.
            SetStatus(SessionState.Opening, "Đang kiểm tra proxy...");
            _currentProxy = await SelectProxyAsync(acc, ct).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            // 2) Đảm bảo trình duyệt đã cài (tải lần đầu ~150MB) — chạy nền.
            SetStatus(SessionState.Opening, "Đang chuẩn bị trình duyệt...");
            var installCode = await Task.Run(() => _loginService.EnsureBrowserInstalled(), ct).ConfigureAwait(false);
            if (installCode != 0)
            {
                SetError("Không cài được trình duyệt. Kiểm tra mạng rồi thử lại.");
                return;
            }

            // Random riêng cho nhịp watchdog: jitter 2–3' (kiểm proxy thường xuyên để proxy chết được phát hiện
            // & đổi trong vài phút — tránh trang bán hàng chết tới ~10' rồi người dùng phải Dừng/mở lại tay).
            var proxyRng = new Random();
            bool firstOpen = true;
            // Chốt chặn TUYỆT ĐỐI: cap an toàn 12h tính từ ĐẦU phiên, áp cho MỌI lần relaunch (KHÔNG reset mỗi
            // lần mở lại). Tín hiệu kết thúc CHÍNH vẫn là "không còn cửa sổ nào".
            var hardCap = DateTime.UtcNow.AddHours(12);

            // ===== VÒNG RELAUNCH NGOÀI =====
            // Mỗi vòng = một lần mở Brave (với _currentProxy hiện tại) + vòng poll bên trong. Khi proxy chết,
            // watchdog đặt relaunchForProxy=true → thoát poll → dispose Brave → quay lại mở LẠI với proxy mới.
            // State GIỮ Running/Opening xuyên suốt, KHÔNG rơi vào finally NGOÀI giữa chừng (nếu State thành
            // Stopped, AccountSessionManager sẽ GỠ phiên khỏi dict).
            while (!ct.IsCancellationRequested)
            {
                bool relaunchForProxy = false;

                // 3) Mở cửa sổ trình duyệt (profile persistent) tới trang bán hàng.
                SetStatus(SessionState.Opening,
                    firstOpen ? "Đang mở cửa sổ trình duyệt..." : "Đang mở lại trình duyệt với proxy mới...");
                // Lần mở ĐẦU: lỗi → SetError + return ngay (giữ hành vi cũ). ĐƯỜNG RELAUNCH: settle + retry vì hồ
                // sơ persistent vừa dispose có thể chưa nhả khóa hẳn (dù DisposeAsync đã WaitForExit). Phân biệt
                // HỦY bằng ct.IsCancellationRequested (không bằng loại exception) vì OpenAsync bọc MỌI lỗi kể cả
                // OperationCanceledException thành InvalidOperationException.
                const int MaxReopenAttempts = 3;
                ILoginSession? session = null;
                for (int attempt = 1; session is null; attempt++)
                {
                    if (!firstOpen)
                    {
                        // Relaunch: chờ settle để khóa hồ sơ nhả nốt (biên an toàn thêm sau WaitForExit).
                        try { await Task.Delay(proxyRng.Next(800, 1500), ct).ConfigureAwait(false); }
                        catch (OperationCanceledException) { throw; }
                    }
                    try
                    {
                        session = await _loginService.OpenAsync(userDataDir, _currentProxy, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        if (ct.IsCancellationRequested) throw new OperationCanceledException(ct); // Dừng → catch NGOÀI xử như HỦY
                        if (firstOpen || attempt >= MaxReopenAttempts) { SetError(ex.Message); return; }
                        SetStatus(SessionState.Opening, $"Mở lại trình duyệt chưa được (thử {attempt}/{MaxReopenAttempts})...");
                        try { await Task.Delay(2000 * attempt, ct).ConfigureAwait(false); }
                        catch (OperationCanceledException) { throw; }
                    }
                }

                _session = session; // expose BraveProcess cho Plan B (focus cửa sổ)

                try
                {
                    // 4) Tự đăng nhập KIỂU NGƯỜI bằng user/password của tài khoản này. Graceful: đã đăng nhập
                    //    sẵn / không thấy ô → bỏ qua. (Relaunch khi đã login trong hồ sơ → thường no-op.)
                    if (acc is not null && !string.IsNullOrEmpty(acc.Password))
                    {
                        SetStatus(SessionState.Running, "Đang tự đăng nhập (kiểu người)...");
                        try { await session.TryHumanLoginAsync(acc.Email, acc.Password, ct).ConfigureAwait(false); }
                        catch { /* không phá luồng — người dùng tự nhập tay nếu cần */ }
                    }

                    // 5) Tự bắt & lưu cookie trong lúc cửa sổ mở; kết thúc khi người dùng đóng hết cửa sổ.
                    SetStatus(SessionState.Running,
                        "Đã mở trình duyệt. Đăng nhập xong app sẽ tự theo dõi đơn mỗi 30'; đóng cửa sổ để dừng.");
                    if (firstOpen)
                    {
                        ToShipCount = null; // reset CHỈ ở lần mở đầu; relaunch giữ số cũ (nhịp đọc đơn tự làm mới).
                    }

                    string? lastSaved = null;
                    const int PollMs = 1000;
                    const int OrderIntervalMin = 30;
                    const int OrderRetrySec = 30;
                    var nextOrderCheck = DateTime.UtcNow;
                    bool firstOrderCheck = true;
                    // Cần 0 cửa sổ ở 2 vòng poll LIÊN TIẾP mới coi là đã đóng (tránh thoát nhầm lúc chuyển trang).
                    int zeroPageStreak = 0;
                    // Nhịp watchdog proxy: kiểm proxy đang gán còn sống không, jitter 2–3' (hồi nhanh khi proxy xoay).
                    var nextProxyCheck = DateTime.UtcNow.AddSeconds(proxyRng.Next(120, 180));

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
                            // GIỮ cờ _navigating suốt lượt đọc (có thể kéo dài ~38s: reload 30s + poll 8s) để
                            // loại trừ HAI CHIỀU với nút Kiểm tra / Xử lý đơn — không cho Goto/click tay chạy
                            // chồng lên lượt reload đang bay trên cùng trang (hai bên cùng fail ảo).
                            _navigating = true;
                            int? count;
                            try
                            {
                                count = await session.ReadToShipCountAsync(reload: !firstOrderCheck, ct).ConfigureAwait(false);
                            }
                            finally
                            {
                                _navigating = false;
                            }

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

                        // ===== NHỊP WATCHDOG PROXY (~10') =====
                        // CHỈ với phiên nguồn KiotProxy (_kiotClient != null) và khi KHÔNG đang điều hướng
                        // (loại trừ với đọc đơn / nút Kiểm tra / Xử lý đơn). Bật _navigating SUỐT lượt kiểm
                        // (health-check tối đa ~8s×2 + 5s ⇒ ~21s) để không thao tác nào chạy chồng lên.
                        if (_kiotClient is not null && !_navigating && DateTime.UtcNow >= nextProxyCheck)
                        {
                            _navigating = true;
                            try
                            {
                                var replacement = await ProxyWatchdog.TryGetReplacementAsync(
                                    _kiotClient, _healthChecker, _currentProxy, ProxyRecheckDelayMs, ct).ConfigureAwait(false);
                                if (replacement is not null)
                                {
                                    _currentProxy = replacement;
                                    relaunchForProxy = true;
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                throw; // Dừng chủ động → để catch NGOÀI xử như HỦY.
                            }
                            catch
                            {
                                /* watchdog lỗi (mạng/API) → bỏ qua, thử lại chu kỳ sau */
                            }
                            finally
                            {
                                _navigating = false;
                            }

                            nextProxyCheck = DateTime.UtcNow.AddSeconds(proxyRng.Next(120, 180));
                            if (relaunchForProxy)
                            {
                                SetStatus(SessionState.Running, "Proxy cũ chết — đang đổi proxy, mở lại trình duyệt...");
                                break; // thoát poll → dispose Brave → relaunch với proxy mới
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

                    // Kết quả trung thực (KHÔNG khẳng định "chưa đăng nhập"). Relaunch đổi proxy → GIỮ status
                    // "đang đổi proxy" (đã đặt ở trên), KHÔNG đè bằng câu tổng kết.
                    if (!relaunchForProxy)
                    {
                        StatusText = lastSaved != null
                            ? "Đã lưu cookie đăng nhập vào tài khoản."
                            : "Chưa lưu được cookie. Nếu đã đăng nhập, phiên vẫn được giữ trong hồ sơ (lần sau mở lại vẫn còn).";
                    }
                }
                finally
                {
                    // Dispose (kill Brave) sau MỖI vòng — dù kết thúc bình thường hay để relaunch với proxy mới.
                    try { await session.DisposeAsync().ConfigureAwait(false); } catch { /* đã chết — bỏ qua */ }
                    if (ReferenceEquals(_session, session))
                    {
                        _session = null;
                    }
                }

                if (!relaunchForProxy)
                {
                    break; // kết thúc bình thường (đóng cửa sổ / hết giờ) → ra ngoài → finally NGOÀI → Stopped
                }
                firstOpen = false; // vòng sau: relaunch với _currentProxy mới
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
        _services.Log.Append(_logLabel, text);
    }

    private void SetError(string message)
    {
        LastError = message;
        StatusText = message;
        State = SessionState.Error;
        _services.Log.Append(_logLabel, "LỖI: " + message);
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
