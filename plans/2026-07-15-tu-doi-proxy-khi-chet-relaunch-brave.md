# Plan: Tự đổi proxy khi chết — nhịp kiểm ~10' + relaunch Brave giữ phiên

- **Ngày:** 2026-07-15
- **Trạng thái:** hoàn thành (code; smoke live chờ người dùng — cần chạy qua ≥1–2 lần xoay proxy 30')
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)
- **Nghiệm thu:** Fable tự chạy build (0/0) + test (**303/303**, +8 ca ProxyWatchdog); đọc toàn bộ diff
  RunAsync + kiểm bất biến vòng đời (StopAsync khi relaunch, manager chỉ gỡ khi Stopped); chạy panel rà
  soát đối kháng 27 agent → **2 lỗi thật + 1 lệch nhỏ**, đã sửa (mở rộng phạm vi thêm `ShopeeLoginService.cs`):
  (1) dispose→reopen cùng hồ sơ không chờ tiến trình cũ thoát → thêm `WaitForExitAsync` 6s trong
  `DisposeAsync` + settle+retry (≤3 lần) đường relaunch; (2) Stop giữa relaunch bị hiểu nhầm thành Error
  (OpenAsync bọc OCE thành InvalidOperationException) → phân biệt hủy bằng `ct.IsCancellationRequested`;
  (3) hardCap 12h reset mỗi relaunch → chuyển ra ngoài vòng (cap tuyệt đối). Sửa xong build 0/0, test 303/303.

## 1. Bối cảnh & yêu cầu

KiotProxy **xoay proxy mới ~30'**. Khi xoay, proxy đang gán cho Brave **chết**, Brave mất mạng (không tải
được trang) — nhưng tiến trình Brave thường VẪN sống nên phiên không có tín hiệu kết thúc, cứ chạy với
proxy chết. **Yêu cầu người dùng:** *cứ ~10' kiểm tra proxy; nếu proxy không còn sống thì lấy proxy mới
và gán cho Brave.*

### Hiện trạng (đã khảo sát 15/7 bằng workflow đọc 4 mảng — bản đồ chính xác)

- **Proxy nướng vào Brave lúc launch** qua cờ `--proxy-server=host:port` (`BraveLaunchArgs.BuildBraveArgs`,
  BraveLaunchArgs.cs:52-56). Brave mở bằng `Process.Start` rồi **attach CDP** (`ConnectOverCDP`,
  `ShopeeLoginService.OpenAsync`:190,218-234) vào context persistent mặc định (`--user-data-dir`).
  ⇒ **KHÔNG có đường đổi proxy khi Brave đang chạy** (CDP không có lệnh đổi upstream cho context attach;
  không dùng `LaunchPersistentContext` có tham số Proxy). **Đổi proxy = phải relaunch Brave.**
- **Relaunch KHÔNG mất đăng nhập:** `userDataDir = BrowserProfilePaths.ForAccount(baseDir, _accountId)`
  (AccountSession.cs:328-330) là hồ sơ persistent trên đĩa; cookie/phiên nằm trong đó ⇒ mở lại cùng
  `userDataDir` với `--proxy-server` mới vẫn còn đăng nhập Shopee.
- **Kiểm proxy sống & lấy proxy mới đã có sẵn:**
  - `IProxyHealthChecker.IsAliveAsync(ProxyEntry, ct)` (ProxyHealthChecker.cs:35) — GET `api.ipify.org`
    QUA proxy, timeout 8s, mọi lỗi → false. Chạy từ tiến trình app (không đụng Brave) ⇒ gọi định kỳ được.
  - `ProxySelector.SelectKiotProxyAsync(IKiotProxyClient?, IProxyHealthChecker, ct)` (ProxySelector.cs:15)
    — ưu tiên `/current` (sticky) rồi mới `/new`, sau đó `IsAliveAsync`; trả proxy sống hoặc null.
    **KiotProxy xoay ~30' ⇒ sau khi xoay, `/current` đã trả proxy MỚI** nên thường KHÔNG tốn `/new`
    (an toàn cho key dùng chung — chỉ `/new` khi `/current` null).
  - Proxy KiotProxy là **IP-whitelist, KHÔNG user/pass** (`ParseResponse` chỉ lấy `data.http`=host:port).
- **Vòng đời phiên (`AccountSession.RunAsync`, 320-514):** chọn proxy 1 lần (337-354, biến CỤC BỘ `proxy`,
  KHÔNG có field) → `OpenAsync(userDataDir, proxy, ct)` (371) → `_session = session` (379) →
  `await using (session)` bọc toàn bộ (381) → vòng while poll 1s (408-472) với nhịp đọc đơn 30' theo mẫu
  deadline `nextOrderCheck` + cờ `_navigating` (444-471) → khi thoát while: bắt cookie CHỐT (474-486) →
  hết `using` dispose (kill Brave) → `finally` (502-513) đặt `State=Stopped`.
  **Cạm bẫy:** `AccountSessionManager.OnSessionChanged` GỠ phiên khỏi dict khi `State==Stopped`
  (AccountSessionManager.cs:134-138) ⇒ **relaunch nội bộ TUYỆT ĐỐI không được để mạch rơi vào `finally`
  đó** (không được để State thành Stopped giữa chừng).
- Field sẵn: `_healthChecker` (AccountSession.cs:31, inject từ manager), `_lifecycleLock` (37),
  `_cts` (38), `volatile _session` (40), `volatile _navigating` (44), `_nextManualProxy` (35).
- `IsAliveAsync` có thể **false-negative** khi mạng máy chập chờn → relaunch thừa → đổi IP vô cớ (lộ
  pattern với Shopee) ⇒ phải xác nhận chết 2 lần.

## 2. Quyết định thiết kế (Fable chốt)

- **Cơ chế gán proxy mới = RELAUNCH Brave** (đường duy nhất khớp hạ tầng; profile giữ đăng nhập nên
  thường không phải login lại; đánh đổi: cửa sổ đóng/mở lại + trang tải lại ~vài giây mỗi lần xoay).
  *(Phương án "mượt không đóng cửa sổ" = tự viết proxy forwarder localhost — lớn & rủi ro hơn nhiều,
  KHÔNG làm ở plan này; ghi lại làm hướng nâng cấp sau nếu việc đóng/mở gây khó chịu.)*
- **Chỉ áp dụng cho phiên nguồn KiotProxy** (có `_kiotClient`): key riêng của tài khoản (acc.ProxyKey)
  hoặc key chung trong Cài đặt. **Bỏ qua** phiên proxy thủ công (danh sách tĩnh) và IP máy (proxy null)
  — đúng phạm vi yêu cầu ("kiot proxy").
- **Chu kỳ ~10' có jitter 9–11'** (`rng.Next(540,660)`s) — tránh trùng đều mốc xoay 30' và tránh pattern
  quá đều. ("tầm 10'" = xấp xỉ.)
- **Xác nhận chết 2 lần** (kiểm lại sau ~5s) trước khi relaunch — chống false-negative.
- **Lấy proxy thay thế qua `ProxySelector.SelectKiotProxyAsync`** (ưu tiên `/current`, an toàn key chung);
  chỉ relaunch khi proxy thay thế **sống VÀ khác endpoint** proxy cũ.

## 3. Các bước thực hiện

### Bước 1 — Helper thuần `ProxyWatchdog` (Core, test được)

Tạo `src/XuLyDonShopee.Core/Services/ProxyWatchdog.cs` — static class, XML-doc tiếng Việt:

```csharp
public static class ProxyWatchdog
{
    /// <summary>
    /// Kiểm proxy hiện tại còn sống; nếu CHẾT (xác nhận 2 lần, cách nhau recheckDelayMs) thì lấy proxy
    /// thay thế qua ProxySelector.SelectKiotProxyAsync (ưu tiên /current sticky). Trả proxy MỚI để
    /// relaunch, hoặc null khi: current==null (IP máy) / còn sống / vừa hồi phục ở lần kiểm 2 / không
    /// lấy được proxy nào / proxy thay thế TRÙNG endpoint cũ (chưa xoay xong — chờ chu kỳ sau).
    /// </summary>
    public static async Task<ProxyEntry?> TryGetReplacementAsync(
        IKiotProxyClient kiot, IProxyHealthChecker checker, ProxyEntry? current,
        int recheckDelayMs, CancellationToken ct = default)
    {
        if (current is null) return null;                              // IP máy — không có gì để canh
        if (await checker.IsAliveAsync(current, ct).ConfigureAwait(false)) return null;   // còn sống
        if (recheckDelayMs > 0) await Task.Delay(recheckDelayMs, ct).ConfigureAwait(false);
        if (await checker.IsAliveAsync(current, ct).ConfigureAwait(false)) return null;   // hồi phục (false-negative)
        var repl = await ProxySelector.SelectKiotProxyAsync(kiot, checker, ct).ConfigureAwait(false);
        if (repl is null) return null;                                // API không cấp được proxy sống
        return ProxyEndpointsEqual(repl, current) ? null : repl;      // trùng endpoint cũ → chờ chu kỳ sau
    }

    /// <summary>So khớp endpoint proxy theo Host (không phân biệt hoa/thường) + Port.</summary>
    public static bool ProxyEndpointsEqual(ProxyEntry a, ProxyEntry b)
        => string.Equals(a.Host, b.Host, System.StringComparison.OrdinalIgnoreCase) && a.Port == b.Port;
}
```

### Bước 2 — Field + tách chọn proxy trong `AccountSession`

`src/XuLyDonShopee.App/Services/AccountSession.cs`:

- Thêm field: `private volatile ProxyEntry? _currentProxy;` (proxy đang nướng vào Brave — watchdog kiểm
  cái này) và `private volatile IKiotProxyClient? _kiotClient;` (client nguồn KiotProxy của phiên; null
  nếu phiên KHÔNG dùng KiotProxy → watchdog tắt).
- Hằng: `private const int ProxyRecheckDelayMs = 5000;`
- **Tách chọn proxy** thành helper dùng chung cho lần mở đầu (giữ NGUYÊN thứ tự 4 ưu tiên 340-354), đồng
  thời set `_kiotClient`:
  ```csharp
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
          return _nextManualProxy(manual);
      }
      var kiotKeys = _services.Settings.GetKiotProxyKeys();
      _kiotClient = kiotKeys.Count == 0 ? null : new KiotProxyClient(kiotKeys);
      return _kiotClient is null ? null
          : await ProxySelector.SelectKiotProxyAsync(_kiotClient, _healthChecker, ct).ConfigureAwait(false);
  }
  ```

### Bước 3 — Tái cấu trúc `RunAsync`: vòng relaunch NỘI BỘ + nhịp watchdog (RỦI RO CAO)

Mục tiêu: bọc "mở Brave + vòng while poll" trong **một vòng lặp ngoài** để relaunch được với proxy mới,
**giữ `State=Running` xuyên suốt**, KHÔNG rơi vào `finally` khi relaunch. Cấu trúc đích (giữ nguyên mọi
logic cũ bên trong, chỉ thêm khung ngoài + block watchdog):

```
try {
    acc = GetById; userDataDir = ForAccount(...); CreateDirectory;
    SetStatus(Opening,"Đang kiểm tra proxy...");
    _currentProxy = await SelectProxyAsync(acc, ct);      // set _kiotClient + _currentProxy
    ct.ThrowIfCancellationRequested();
    SetStatus(Opening,"Đang chuẩn bị trình duyệt..."); installCode...; if(!=0){SetError;return;}

    var proxyRng = new Random();
    bool firstOpen = true;

    while (!ct.IsCancellationRequested) {                 // ===== VÒNG RELAUNCH NGOÀI =====
        bool relaunchForProxy = false;

        SetStatus(Opening, firstOpen ? "Đang mở cửa sổ trình duyệt..." : "Đang mở lại trình duyệt với proxy mới...");
        ILoginSession session;
        try { session = await _loginService.OpenAsync(userDataDir, _currentProxy, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }     // để catch ngoài xử như HỦY (không SetError)
        catch (Exception ex) { SetError(ex.Message); return; }
        _session = session;

        try {                                             // thay cho 'await using(session)'
            // (4) TryHumanLogin — GIỮ NGUYÊN (graceful; relaunch khi đã login → no-op)
            // (5) SetStatus(Running,...); if (firstOpen) ToShipCount = null;
            // ... khai báo lastSaved / PollMs / nextOrderCheck / firstOrderCheck / hardCap / zeroPageStreak (GIỮ NGUYÊN) ...
            var nextProxyCheck = DateTime.UtcNow.AddSeconds(proxyRng.Next(540, 660));   // ~9–11'

            while (!session.IsClosed && DateTime.UtcNow < hardCap && !ct.IsCancellationRequested) {
                await Task.WhenAny(session.Closed, Task.Delay(PollMs, ct));
                if (ct.IsCancellationRequested) break;
                // ... zeroPageStreak (GIỮ NGUYÊN) ...
                // ... cookie capture (GIỮ NGUYÊN) ...
                // ... nhịp đọc đơn 30' (GIỮ NGUYÊN) ...

                // ===== NHỊP WATCHDOG PROXY (~10') =====
                if (_kiotClient is not null && !_navigating && DateTime.UtcNow >= nextProxyCheck) {
                    _navigating = true;                    // loại trừ với đọc đơn / thao tác tay suốt lượt kiểm
                    try {
                        var replacement = await ProxyWatchdog.TryGetReplacementAsync(
                            _kiotClient, _healthChecker, _currentProxy, ProxyRecheckDelayMs, ct).ConfigureAwait(false);
                        if (replacement is not null) { _currentProxy = replacement; relaunchForProxy = true; }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { /* watchdog lỗi (mạng/API) → bỏ qua, thử lại chu kỳ sau */ }
                    finally { _navigating = false; }

                    nextProxyCheck = DateTime.UtcNow.AddSeconds(proxyRng.Next(540, 660));
                    if (relaunchForProxy) { SetStatus(Running,"Proxy cũ chết — đang đổi proxy, mở lại trình duyệt..."); break; }
                }
            }

            // Bắt cookie CHỐT trước khi dispose (GIỮ NGUYÊN khối 474-486).
            // Câu StatusText tổng kết (489-491): CHỈ đặt khi !relaunchForProxy (relaunch thì giữ status "đang đổi proxy").
        }
        finally {
            try { await session.DisposeAsync().ConfigureAwait(false); } catch { }   // kill Brave
            if (ReferenceEquals(_session, session)) _session = null;
        }

        if (!relaunchForProxy) break;                     // kết thúc bình thường → ra ngoài → finally → Stopped
        firstOpen = false;                                // vòng sau: relaunch với _currentProxy mới
    }
}
catch (OperationCanceledException) { /* Dừng chủ động — không lỗi */ }
catch (Exception ex) { SetError(ex.Message); }
finally { _session = null; lock(_lifecycleLock){ if (State != SessionState.Error) State = SessionState.Stopped; } }
```

**Ràng buộc bắt buộc khi tái cấu trúc:**
- KHÔNG đổi hành vi các nhịp cũ (cookie 1s, đọc đơn 30', zeroPageStreak x2, hardCap 12h, bắt cookie chốt).
- `State` phải là **Running** suốt các lần relaunch — chỉ về Stopped ở `finally` ngoài cùng khi mạch kết
  thúc thật (relaunchForProxy=false / hủy / lỗi). KHÔNG để relaunch chạm `finally` ngoài giữa chừng.
- Watchdog **chỉ chạy khi `_kiotClient != null`** và **`!_navigating`**; bật `_navigating` suốt lượt kiểm
  (health-check tối đa ~8s×2 + 5s ⇒ ~21s) để loại trừ với đọc đơn và nút Kiểm tra/Xử lý đơn.
- Trong lúc relaunch, `_session` có thời điểm null → `CheckOrdersAsync`/`ProcessOrdersAsync` tự trả false
  graceful (đã gate `State==Running && _session!=null && !_navigating`) — KHÔNG được để State rời Running.
- `catch (OperationCanceledException)` quanh `OpenAsync` phải **re-throw** để mạch hủy đi vào catch ngoài
  (xử như Dừng), KHÔNG SetError.
- `ToShipCount` chỉ reset ở lần mở ĐẦU (`firstOpen`); relaunch giữ số cũ (nhịp đọc đơn sẽ tự làm mới).

### Bước 4 — KHÔNG quên ràng buộc & like-human

- Relaunch chỉ xảy ra khi `!_navigating` ⇒ không cắt ngang thao tác điều hướng/xử lý đơn kiểu người.
- KHÔNG thêm thao tác JS/stealth; luồng mở Brave & auth proxy giữ nguyên `OpenAsync`.
- KHÔNG spam `/new`: watchdog dùng `SelectKiotProxyAsync` (ưu tiên `/current`); chỉ khi proxy thật sự chết.

### Bước 5 — Test + build

- **Tạo `src/XuLyDonShopee.Tests/ProxyWatchdogTests.cs`** (theo mẫu `ProxySelectorTests` — có sẵn
  `StubKiot`/`StubChecker`; watchdog gọi `IsAliveAsync` trên `current` (tối đa 2 lần) rồi
  `SelectKiotProxyAsync` (gọi `IsAliveAsync` trên ứng viên) ⇒ dùng checker theo **hàng đợi kết quả** cho
  đúng thứ tự). Ca kiểm (recheckDelayMs=0):
  1. `current==null` → null; kiot KHÔNG bị gọi.
  2. current SỐNG (IsAlive lần 1 true) → null; kiot KHÔNG gọi; chỉ 1 lần IsAlive.
  3. current CHẾT lần 1, SỐNG lần 2 (hồi phục) → null; kiot KHÔNG gọi.
  4. current CHẾT cả 2 lần + `/current` trả proxy KHÁC endpoint, ứng viên SỐNG → trả proxy mới đó.
  5. current CHẾT cả 2 + `SelectKiotProxyAsync` trả null (API không cấp / ứng viên chết) → null.
  6. `ProxyEndpointsEqual`: cùng host+port → true; khác port → false; khác host → false; host hoa/thường → true.
- `dotnet build XuLyDonShopee.sln -c Debug` 0/0; `dotnet test` toàn bộ pass (nền hiện tại **295**, +ca mới).
  WDAC chặn → build lại `-p:Deterministic=false` rồi `dotnet test --no-build`.
- Grep: không thêm `.ClickAsync/.FillAsync/Mouse.ClickAsync/native` (không liên quan bước này nhưng vẫn giữ).

## 4. Tiêu chí nghiệm thu

- [ ] Cứ ~10' (jitter 9–11'), phiên nguồn KiotProxy tự kiểm proxy đang gán còn sống không
      (`IsAliveAsync`), **xác nhận chết 2 lần** rồi mới hành động.
- [ ] Proxy chết → lấy proxy thay thế (ưu tiên `/current`, không spam `/new`) → **relaunch Brave** với
      proxy mới; hồ sơ persistent giữ đăng nhập (không phải login lại trong trường hợp thường).
- [ ] Relaunch giữ `State=Running` xuyên suốt, **KHÔNG bị `AccountSessionManager` gỡ phiên** (không chạm
      `finally` ngoài giữa chừng); Dừng / đóng cửa sổ / hủy vẫn kết thúc về Stopped đúng như cũ.
- [ ] Watchdog chỉ chạy khi `_kiotClient != null` & `!_navigating`; KHÔNG relaunch khi đang đọc đơn / xử
      lý đơn; proxy thủ công & IP máy → không đụng.
- [ ] Không đổi hành vi các nhịp cũ (cookie, đọc đơn 30', bắt cookie chốt, hardCap).
- [ ] Build 0/0; test 295 nền + ca ProxyWatchdog mới pass.
- [ ] Chỉ sửa/tạo: `ProxyWatchdog.cs` (mới), `AccountSession.cs`, `ProxyWatchdogTests.cs` (mới).

## 5. Rủi ro & lưu ý

- **Tái cấu trúc `RunAsync` là phần rủi ro nhất** (đụng vòng đời phiên lõi). Sai một nhịp → phiên tự
  Stop/không Stop được, rò tiến trình Brave, hoặc mất nhịp đọc đơn. Fable sẽ soi rất kỹ (đọc diff toàn
  bộ + panel đối kháng) + nhờ người dùng smoke lâu (chờ qua ít nhất 1–2 lần xoay proxy 30').
- **IsAliveAsync kiểm qua `api.ipify.org`, không qua Brave/Shopee:** proxy tới ipify được chưa chắc
  Shopee vào được, và ngược lại. Nhưng đây đúng là tín hiệu "KiotProxy giết IP cũ khi xoay". Chấp nhận ở
  bước này; nếu sau cần chắc hơn có thể probe qua Brave (`page.Goto`) — KHÔNG làm bây giờ.
- **Downtime tối đa ~10'**: nếu proxy chết ngay sau một nhịp kiểm, phải chờ tới nhịp sau. Chấp nhận theo
  yêu cầu ("tầm 10'"). Muốn nhanh hơn → giảm chu kỳ (đánh đổi gọi mạng nhiều hơn).
- **Key KiotProxy dùng chung nhiều tài khoản:** watchdog ưu tiên `/current` (không ép `/new`) nên không
  vô cớ xoay proxy của tài khoản khác. `/new` chỉ chạm khi `/current` null (khi đó cả nhóm cùng key đều
  cần proxy mới) → chấp nhận được.
- **Bắt đầu phiên trên IP máy vì KiotProxy đang down** (`_currentProxy==null` dù `_kiotClient!=null`):
  watchdog trả null (không có gì để canh) → KHÔNG tự "thăng cấp" lên proxy khi KiotProxy hồi. Đúng phạm
  vi yêu cầu (chỉ đổi khi proxy CHẾT); ghi làm hướng nâng cấp nếu cần.
- **Đổi IP giữa phiên** có thể khiến Shopee bắt đăng nhập lại / cắt phiên (rủi ro nghiệp vụ, không phải
  lỗi code) — nhưng KiotProxy vốn xoay 30' nên rủi ro này đã tồn tại; tính năng chỉ giúp Brave tiếp tục
  chạy thay vì treo với proxy chết.
- **Nhiều phiên song song:** mỗi `AccountSession` có nhịp watchdog riêng, dùng chung 1 `_healthChecker`
  (stateless) — an toàn; nhưng nhiều phiên có thể relaunch gần nhau (tốn tài nguyên nhất thời) — chấp nhận.
- WDAC/ISG khi test như các plan trước.

---

## Báo cáo thực thi (Opus điền sau khi xong)

**Ngày thực thi:** 2026-07-15 · **Người thực thi:** Opus (`opus-executor`)

### Đã hoàn thành (từng bước)

- **Baseline (trước khi sửa):** `dotnet build -c Debug -p:Deterministic=false` → 0 Warning / 0 Error;
  `dotnet test --no-build` → **Passed 295 / Failed 0** (đúng nền plan ghi).
- **Bước 1 — ProxyWatchdog (Core):** Tạo mới `src/XuLyDonShopee.Core/Services/ProxyWatchdog.cs` — static
  class, XML-doc tiếng Việt. `TryGetReplacementAsync(kiot, checker, current, recheckDelayMs, ct)` đúng logic
  plan: `current==null`→null; sống lần 1→null; delay recheck; sống lần 2 (hồi phục)→null;
  `SelectKiotProxyAsync` lấy thay thế; null→null; trùng endpoint (`ProxyEndpointsEqual`)→null; khác→trả proxy
  mới. `ProxyEndpointsEqual` so Host (OrdinalIgnoreCase) + Port.
- **Bước 2 — Field + SelectProxyAsync (App):** Trong `src/XuLyDonShopee.App/Services/AccountSession.cs` thêm
  `private volatile ProxyEntry? _currentProxy;`, `private volatile IKiotProxyClient? _kiotClient;`,
  `private const int ProxyRecheckDelayMs = 5000;`. Tách `SelectProxyAsync(Account? acc, CancellationToken ct)`
  giữ NGUYÊN thứ tự 4 ưu tiên (key riêng → thủ công → key chung → IP máy) và set `_kiotClient` (null khi thủ
  công/không key ⇒ watchdog tắt).
- **Bước 3 — Tái cấu trúc RunAsync (App):** Bọc "mở Brave + vòng poll" trong **vòng relaunch ngoài**
  `while (!ct.IsCancellationRequested)`. Đổi `await using(session)` → `try { … } finally { DisposeAsync;
  if (ReferenceEquals(_session, session)) _session = null; }` để dispose mỗi vòng. Thêm block watchdog trong
  vòng poll (điều kiện `_kiotClient is not null && !_navigating && UtcNow >= nextProxyCheck`, bật `_navigating`
  suốt lượt kiểm, đặt lại `nextProxyCheck` jitter `Next(540,660)`s, `break` để relaunch khi có proxy mới).
  `OpenAsync` bọc `catch (OperationCanceledException){ throw; }` trước `catch (Exception){ SetError; return; }`.
  Watchdog cũng re-throw OCE. `ToShipCount=null` gate theo `firstOpen`. Câu StatusText tổng kết gate theo
  `!relaunchForProxy`. GIỮ NGUYÊN: cookie 1s, zeroPageStreak≥2, nhịp đọc đơn 30' + `_navigating` +
  firstOrderCheck/OrderRetrySec, hardCap 12h, bắt cookie chốt. `finally` NGOÀI cùng mới đặt State=Stopped.
- **Bước 4 — Ràng buộc like-human:** Không thêm thao tác JS/stealth; luồng `OpenAsync` giữ nguyên; relaunch chỉ
  khi `!_navigating`; watchdog dùng `SelectKiotProxyAsync` (ưu tiên `/current`, không spam `/new`).
- **Bước 5 — Test:** Tạo `src/XuLyDonShopee.Tests/ProxyWatchdogTests.cs` — 8 ca (6 ca plan + 2 ca bổ sung:
  "ứng viên chết → null" và "trùng endpoint → null chờ chu kỳ sau"). Dùng `QueueChecker` (hàng đợi kết quả
  IsAlive theo đúng thứ tự gọi) + `StubKiot` (đếm CurrentCalls/NewCalls).

### Kết quả kiểm chứng (số liệu thật)

- `dotnet build XuLyDonShopee.sln -c Debug -p:Deterministic=false` → **Build succeeded, 0 Warning, 0 Error**.
- `dotnet test XuLyDonShopee.sln -c Debug --no-build` → **Passed 303 / Failed 0 / Skipped 0** (295 nền + 8 mới).
- `dotnet test --filter FullyQualifiedName~ProxyWatchdogTests` → **Passed 8 / Failed 0**.
- `git status --short`: đúng 3 file — `M AccountSession.cs`, `?? ProxyWatchdog.cs`, `?? ProxyWatchdogTests.cs`.
- Grep `ClickAsync|FillAsync|Mouse.ClickAsync` trong AccountSession.cs → **0 match** (không thêm thao tác máy).
- WDAC: build `-p:Deterministic=false` + `dotnet test --no-build` chạy được, KHÔNG gặp 0x800711C7 lần này.

### Đối chiếu tiêu chí nghiệm thu

- [x] ~10' (jitter `Next(540,660)`s = 9–11') tự kiểm proxy; xác nhận chết 2 lần (2× IsAliveAsync + delay 5s).
- [x] Proxy chết → `SelectKiotProxyAsync` (ưu tiên `/current`) → relaunch Brave cùng `userDataDir` (giữ đăng nhập).
- [x] Relaunch giữ State=Running/Opening (không Stopped giữa chừng); chỉ Stopped ở finally NGOÀI (relaunch=false /
      hủy / lỗi). Đã xác nhận `AccountSessionManager.OnSessionChanged` chỉ gỡ khi `State==Stopped`.
- [x] Watchdog chỉ chạy khi `_kiotClient != null` & `!_navigating`; bật `_navigating` suốt lượt kiểm; proxy thủ
      công (`_kiotClient=null`) & IP máy (`_currentProxy=null`) → không đụng.
- [x] Không đổi hành vi các nhịp cũ (đọc lại toàn bộ RunAsync đối chiếu bản gốc).
- [x] Build 0/0; test 303 (295 nền + 8 ca ProxyWatchdog) pass.
- [x] Chỉ tạo/sửa 3 file đúng phạm vi.

### Vướng mắc / chưa làm được

- **KHÔNG smoke live** (đúng yêu cầu): môi trường không có phiên Shopee/KiotProxy thật. Toàn bộ kiểm chứng là
  build + test + đọc/đối chiếu code. Hành vi relaunch thực tế (đóng/mở cửa sổ, giữ đăng nhập, downtime tới ~10')
  cần người dùng smoke qua ít nhất 1–2 lần xoay proxy 30' như plan đã nêu ở mục Rủi ro.

### Đề xuất (nếu có)

- Không có điều chỉnh plan cần thiết. Thêm 2 ca test ngoài 6 ca plan (ứng viên chết / trùng endpoint) để phủ
  đủ nhánh của `TryGetReplacementAsync` — trong phạm vi ProxyWatchdog, không mở rộng ngoài plan.

---

## Sửa theo nghiệm thu (panel đối kháng — 2 lỗi + 1 lệch do vòng relaunch mới)

**Ngày:** 2026-07-15 · Fable chỉ đạo, Opus sửa. Sửa 2 file: `ShopeeLoginService.cs` + `AccountSession.cs`.

- **LỖI 1 (nghiêm trọng) — dispose→reopen cùng hồ sơ không chờ tiến trình cũ thoát:**
  - *1A* `src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs` — `LoginSession.DisposeAsync`: sau `Kill`,
    TRƯỚC `_process.Dispose()`, thêm `await _process.WaitForExitAsync` với `CancellationTokenSource(6s)`
    (nuốt lỗi/timeout) để tiến trình Brave nhả khóa hồ sơ trước khi mở lại cùng `userDataDir`.
  - *1B* `src/XuLyDonShopee.App/Services/AccountSession.cs` — khối mở Brave trong vòng relaunch: thay
    try/catch đơn bằng vòng `for (attempt=1; session is null; attempt++)` với `MaxReopenAttempts=3`;
    đường relaunch (`!firstOpen`) có settle `Task.Delay(800–1500ms)` trước mỗi lần thử + retry
    `Task.Delay(2000*attempt)` khi mở hụt; lần mở ĐẦU giữ nguyên (lỗi → SetError + return ngay).
- **LỖI 2 (vừa) — Stop giữa relaunch bị hiểu nhầm thành Error:** `OpenAsync` bọc MỌI exception (kể cả OCE)
  thành `InvalidOperationException` nên nhánh `catch (OperationCanceledException)` riêng quanh `OpenAsync` là
  code chết. Sửa: bỏ nhánh đó; trong `catch (Exception)` phân biệt hủy bằng `ct.IsCancellationRequested` →
  `throw new OperationCanceledException(ct)` để catch NGOÀI xử như Dừng (về Stopped sạch, không Error).
  (Delay settle/retry vẫn `catch (OperationCanceledException){ throw; }` vì Task.Delay ném OCE thật.)
- **LỆCH 3 (nhỏ) — hardCap 12h reset mỗi lần relaunch:** chuyển `var hardCap = DateTime.UtcNow.AddHours(12);`
  RA NGOÀI vòng relaunch (cạnh `proxyRng`/`firstOpen`, trước `while`) → cap tuyệt đối 12h tính từ đầu phiên
  cho mọi lần relaunch. Các biến khác (`nextOrderCheck`, `firstOrderCheck`, `lastSaved`, `zeroPageStreak`,
  `nextProxyCheck`) GIỮ trong vòng (reset mỗi lần mở lại — đúng).

**Kiểm chứng sau sửa:** `dotnet build -c Debug -p:Deterministic=false` → **0 Warning / 0 Error** (vòng
`for (…; session is null; …)` khiến flow-analysis narrow `session` non-null sau vòng → KHÔNG sinh cảnh báo
nullable). `dotnet test --no-build` → **Passed 303 / Failed 0 / Skipped 0**. Vẫn chỉ đụng đúng phạm vi:
`ShopeeLoginService.cs` (thêm 1B WaitForExit), `AccountSession.cs`, + 2 file mới của việc gốc.
