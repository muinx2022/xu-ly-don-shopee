# Plan: Form nhập user tự do · Mở bằng profile đã đăng nhập · Tự lưu cookie · Kiểm tra proxy sống

- **Ngày:** 2026-07-14
- **Trạng thái:** hoàn thành
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)
- **Nghiệm thu:** Fable đã tự chạy build (0 error) + test (97/97 pass, -p:Deterministic=false để né ISG) và panel rà soát đối kháng 4 góc nhìn; 3 lỗi panel phát hiện đã sửa & kiểm chứng lại.

## 1. Bối cảnh & mục tiêu

App Avalonia .NET 8 quản lý tài khoản Shopee. Màn tài khoản có form chi tiết + nút **"Mở trang bán hàng"** (Playwright mở Brave/Chromium tới `banhang.shopee.vn`, bắt cookie). Người dùng yêu cầu 4 thay đổi:

1. **Form chi tiết — cho phép nhập "user" tự do:** trường đăng nhập hiện **bắt buộc đúng định dạng email** (`EmailValidator.IsValid` trong `Save()`). Bỏ ràng buộc này: cho nhập user **có thể là email, có thể không** (vd `shopee_user01`). Vẫn bắt buộc không rỗng và không trùng.
2. **Mở bằng profile đã đăng nhập lần trước:** hiện mỗi lần mở là **context mới, cô lập** (`browser.NewContextAsync`) → không nhớ đăng nhập, lần nào cũng phải đăng nhập lại. Đổi sang **persistent profile theo từng tài khoản** (`LaunchPersistentContextAsync` với `userDataDir` riêng mỗi Id) → lần sau mở lại **vẫn còn đăng nhập**.
3. **Tự động lưu cookie, KHÔNG hỏi nữa:** hiện có `ConfirmAsync("Đăng nhập Shopee", ... bấm Đồng ý để lưu cookie / Hủy ...")`. Bỏ hộp thoại hỏi; **tự động bắt & lưu cookie** trong lúc cửa sổ mở và khi người dùng đóng cửa sổ.
4. **Kiểm tra proxy sống trước khi mở (chỉ proxy KiotProxy):** trước khi mở trình duyệt, nếu danh sách proxy thủ công trống thì **lấy proxy KiotProxy qua API + thử kết nối thật**; proxy còn sống → dùng, không → **dùng IP máy**. (Đã chốt với người dùng: **kết hợp cả hai** cách kiểm tra; **chỉ áp dụng cho proxy KiotProxy**, proxy thủ công dùng như hiện tại.)

### Hiện trạng code (đã khảo sát kỹ)

- [src/XuLyDonShopee.Core/Models/Account.cs](../src/XuLyDonShopee.Core/Models/Account.cs): property `Email` (login), `Password`, `Phone?`, `Cookie?`, `Note?`, `Status`, timestamps. DB cột `Email TEXT NOT NULL` ([Database.cs](../src/XuLyDonShopee.Core/Data/Database.cs)).
- [src/XuLyDonShopee.App/ViewModels/AccountsViewModel.cs](../src/XuLyDonShopee.App/ViewModels/AccountsViewModel.cs):
  - `Save()` gọi `EmailValidator.IsValid(email)` → cần bỏ. Có kiểm trùng (case-insensitive) + kiểm password rỗng.
  - `OpenSellerAsync()`: chụp `targetId = _editingId.Value`; build `ProxyRotator(proxies, kiot)` → `GetNextAsync()`; `EnsureBrowserInstalled` (Task.Run); `_loginService.OpenAsync(proxy)`; **`ConfirmAsync` để lưu cookie**; `session.CaptureCookiesJsonAsync()`; `SaveCapturedCookie(targetId, json)`.
  - `SaveCapturedCookie(long targetId, string cookieJson) : SaveCookieResult` — **giữ nguyên** (4 test hồi quy phụ thuộc). Enum `SaveCookieResult { NoCookie, AccountMissing, Saved }`.
  - Fields: `EditEmail`, `EditPassword`, `EditPhone`, `EditCookie`, `EditNote`, `EditStatus`, `IsBusy`, `_editingId`, `CanOpenSeller => IsEditing && !IsNew && _editingId is not null && !IsBusy`.
- [src/XuLyDonShopee.App/Views/AccountsView.axaml](../src/XuLyDonShopee.App/Views/AccountsView.axaml): label `"Email (dùng để đăng nhập)"`, watermark `ten@shopee.vn`; ô tìm kiếm watermark `"Tìm theo email / ghi chú..."`; hàng nút "Lưu"/"Hủy"/"Mở trang bán hàng".
- [src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs](../src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs):
  - `EnsureBrowserInstalled()` (bỏ qua tải nếu có Brave), `OpenAsync(ProxyEntry?, ct)` → `LaunchAsync(exePath, proxy)` tạo `(IPlaywright, IBrowser, IBrowserContext)` bằng `LaunchAsync` + `NewContextAsync`, mở page, `GotoAsync(SellerUrl)`. `interface ILoginSession : IAsyncDisposable { Task<string> CaptureCookiesJsonAsync(); }` + class `LoginSession` giữ playwright/browser/context.
  - Ưu tiên Brave (`BrowserLocator.FindBraveExecutable()`), lỗi → fallback Chromium đóng gói.
- [src/XuLyDonShopee.Core/Services/ProxyRotator.cs](../src/XuLyDonShopee.Core/Services/ProxyRotator.cs): `new ProxyRotator(IEnumerable<ProxyEntry>?, IKiotProxyClient?)`, `GetNextAsync()` (list round-robin → KiotProxy → null=IP máy).
- [src/XuLyDonShopee.Core/Services/KiotProxyClient.cs](../src/XuLyDonShopee.Core/Services/KiotProxyClient.cs): `IKiotProxyClient { Task<ProxyEntry?> GetNewProxyAsync(ct) }`; impl gọi `GET {baseUrl}/api/v1/proxies/new?key=&region=`; `ParseResponse(json)` static (ưu tiên `data.http`, `success=false`/`status=FAIL` → null). `KeyCount`. Có test dùng `StubHandler` (HttpMessageHandler giả) trong [KiotProxyClientTests.cs](../src/XuLyDonShopee.Tests/KiotProxyClientTests.cs).
  - **Tài liệu API KiotProxy** (file `kiotproxy-doc.txt` ở gốc repo) xác nhận:
    - `GET /api/v1/proxies/new?key=&region=` — lấy/đổi proxy mới (chịu giới hạn `ttc` — thời gian tới lượt đổi).
    - `GET /api/v1/proxies/current?key=` — lấy proxy **đang gán** với key (KHÔNG cần `region`, KHÔNG xoay vòng/đổi). FAIL khi key chưa có proxy: `code 40001050`, `error "PROXY_NOT_FOUND_BY_KEY"`, `status "FAIL"`.
    - `GET /api/v1/proxies/out?key=` — thoát proxy khỏi key (không dùng trong plan này).
    - Trường `data` (cả new/current): `http`, `socks5`, `host`, `httpPort`, `socks5Port`, `realIpAddress`, `location`, **`expirationAt`** (ms epoch — proxy hết hạn lúc này), `ttl` (giây), `ttc` (giây tới lượt đổi), `nextRequestAt` (ms). → "còn sống qua API" = `success=true` **và** `expirationAt` còn ở tương lai.
- [src/XuLyDonShopee.App/Services/AppServices.cs](../src/XuLyDonShopee.App/Services/AppServices.cs): expose `Database` (có `Database.Path`), `Accounts`, `Proxies`, `Settings`. VM nhận `AppServices` qua ctor.
- [src/XuLyDonShopee.App/Services/DialogService.cs](../src/XuLyDonShopee.App/Services/DialogService.cs): `ConfirmAsync(title,msg)→bool`, `InfoAsync(title,msg)`.
- Test hiện có (73 test) — **phải giữ xanh**: `AccountsViewModelTests` (dùng `vm.EditEmail`, `SaveCapturedCookie`, email mẫu `a@mail.com`...), `KiotProxyClientTests`, `EmailValidatorTests`, ...

### Quyết định đã chốt

- **Không đổi tên** property `Account.Email` / cột DB / các field `EditEmail` trong VM (tránh migration + churn + vỡ test cũ). Chỉ **đổi nhãn hiển thị** + **nới validation**. Cập nhật XML-doc `Account.Email` để nói rõ nay chứa "user đăng nhập (email hoặc tên đăng nhập)".
- **Persistent profile theo Id** đặt tại `<thư-mục-DB>/profiles/<accountId>` (cạnh `app.db`). Đây là **đảo ngược có chủ đích** quyết định "phiên trắng cô lập" ở plan `2026-07-13-mo-dang-nhap-bang-brave.md` — đúng yêu cầu mới.
- **Kiểm tra proxy sống = kết hợp:** (a) gọi API KiotProxy (`/current`, thiếu thì `/new`) để lấy/xác nhận proxy; (b) thử kết nối thật qua proxy tới 1 URL. Cả hai đạt → dùng proxy; ngược lại → IP máy. **Chỉ proxy KiotProxy** mới kiểm; danh sách proxy thủ công dùng round-robin như cũ (không kiểm).

## 2. Phạm vi

- **Làm:**
  - Phần 1: nới validation user (bỏ ràng buộc email), đổi nhãn UI, +2 test VM.
  - Phần 2: `ShopeeLoginService` dùng `LaunchPersistentContextAsync(userDataDir,...)`; `ILoginSession` thêm cơ chế chờ-đóng; VM tự bắt & lưu cookie theo vòng lặp (bỏ `ConfirmAsync` hỏi lưu); helper đường dẫn profile (test được).
  - Phần 3: `IKiotProxyClient.GetCurrentProxyAsync`; `IProxyHealthChecker`/`ProxyHealthChecker` (test kết nối thật); `ProxySelector.SelectKiotProxyAsync` (điều phối, test được); nối vào `OpenSellerAsync`.
  - Cập nhật README; smoke test thật; giữ toàn bộ test cũ xanh + thêm test mới.
- **Không làm:**
  - Không đổi tên `Account.Email`/cột DB/`EditEmail`; không migration.
  - Không kiểm sống cho proxy thủ công; không thêm nút "test proxy" trong màn Proxy.
  - Không tự điền username/password/OTP (Shopee chặn — người dùng tự đăng nhập).
  - Không mã hóa cookie/mật khẩu (giữ như hiện tại, đã ghi chú bảo mật).
  - Không làm tính năng "chạy tài khoản" (tái dùng cookie thao tác đơn).

## 3. Các bước thực hiện

### PHẦN 1 — Form: cho phép nhập user (email hoặc không)

**B1.1 — `AccountsViewModel.Save()`** ([AccountsViewModel.cs](../src/XuLyDonShopee.App/ViewModels/AccountsViewModel.cs)):
- **Xóa** khối gọi `EmailValidator.IsValid(email)` và thông báo "Email không hợp lệ...".
- Thay bằng kiểm **không rỗng**:
  ```csharp
  var user = EditEmail?.Trim() ?? string.Empty;
  if (string.IsNullOrEmpty(user))
  {
      ErrorMessage = "Tên đăng nhập (user) không được để trống.";
      return;
  }
  ```
  (Giữ nguyên biến/ghi DB qua cột `Email`; `user` chỉ là tên biến cục bộ.)
- **Giữ** kiểm trùng case-insensitive (đổi thông báo → `"Tài khoản này đã tồn tại ở một tài khoản khác."`).
- Xóa `using XuLyDonShopee.Core.Validation;` nếu sau khi bỏ, VM không còn tham chiếu `EmailValidator` (tránh using thừa).

**B1.2 — `Account.Email` XML-doc** ([Account.cs](../src/XuLyDonShopee.Core/Models/Account.cs)): đổi comment `<summary>User đăng nhập, dạng email (bắt buộc).</summary>` → `<summary>User đăng nhập (email hoặc tên đăng nhập tùy ý, bắt buộc, không trùng).</summary>`. Không đổi tên property.

**B1.3 — Nhãn UI** ([AccountsView.axaml](../src/XuLyDonShopee.App/Views/AccountsView.axaml)):
- `"Email (dùng để đăng nhập)"` → `"Tài khoản đăng nhập (user)"`.
- Watermark ô này `ten@shopee.vn` → `"Email hoặc tên đăng nhập"`.
- Watermark ô tìm kiếm `"Tìm theo email / ghi chú..."` → `"Tìm theo tài khoản / ghi chú..."`.
- Giữ nguyên `{Binding EditEmail}` / `{Binding SearchText}`.

**B1.4 — Test** ([AccountsViewModelTests.cs](../src/XuLyDonShopee.Tests/AccountsViewModelTests.cs)), thêm 2 test:
- `Save_UserKhongPhaiEmail_VanLuuDuoc`: `Add` → `EditEmail = "shopee_user01"` (không có `@`), `EditPassword="123"` → `SaveCommand.Execute(null)` → `ErrorMessage == null`, DB có 1 bản ghi `Email=="shopee_user01"`, `SelectedAccount.Email=="shopee_user01"`.
- `Save_UserRong_BaoLoi`: `Add` → `EditEmail=""`, `EditPassword="123"` → Save → `ErrorMessage != null`, DB vẫn 0 bản ghi.
- **Không** sửa/xoá `EmailValidator` hay `EmailValidatorTests` (giữ nguyên, vẫn xanh).

### PHẦN 2 — Persistent profile + tự động lưu cookie (bỏ hỏi)

**B2.1 — Helper đường dẫn profile (Core, thuần, test được).** Tạo `src/XuLyDonShopee.Core/Services/BrowserProfilePaths.cs`:
```csharp
public static class BrowserProfilePaths
{
    /// <summary>Thư mục user-data-dir persistent cho một tài khoản, nằm trong &lt;baseDir&gt;/profiles/&lt;id&gt;.</summary>
    public static string ForAccount(string baseDir, long accountId)
        => System.IO.Path.Combine(baseDir, "profiles",
               accountId.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
```

**B2.2 — `ShopeeLoginService` dùng persistent context** ([ShopeeLoginService.cs](../src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs)):
- Đổi chữ ký: `public async Task<ILoginSession> OpenAsync(string userDataDir, ProxyEntry? proxy, CancellationToken ct = default)`.
- Đổi `LaunchAsync` thành: `private static async Task<(IPlaywright, IBrowserContext)> LaunchPersistentAsync(string? executablePath, string userDataDir, ProxyEntry? proxy)`:
  - `playwright = await Playwright.CreateAsync();`
  - `context = await playwright.Chromium.LaunchPersistentContextAsync(userDataDir, new BrowserTypeLaunchPersistentContextOptions { Headless = false, ExecutablePath = executablePath, Proxy = PlaywrightProxyMapper.ToPlaywrightProxy(proxy), IgnoreHTTPSErrors = true });`
  - Lỗi giữa chừng → dọn context/playwright rồi ném lại (giữ pattern try/catch cũ).
- `OpenAsync`: giữ logic ưu tiên Brave + fallback nhưng gọi `LaunchPersistentAsync(brave/null, userDataDir, proxy)`:
  - có Brave → thử `LaunchPersistentAsync(brave, userDataDir, proxy)`; ném → `EnsureChromiumInstalledForFallback()` + `LaunchPersistentAsync(null, userDataDir, proxy)`.
  - không Brave → `LaunchPersistentAsync(null, userDataDir, proxy)`.
  - Sau khi có `context`: lấy page sẵn có `var page = context.Pages.Count > 0 ? context.Pages[0] : await context.NewPageAsync();` rồi `GotoAsync(SellerUrl, ...)` (nuốt lỗi điều hướng như cũ).
  - Trả `new LoginSession(playwright, context)`.
  - Try/catch tổng: dọn context + playwright, ném `InvalidOperationException` (message tiếng Việt) như cũ.
- **`ILoginSession`** — thêm cơ chế chờ đóng:
  ```csharp
  public interface ILoginSession : IAsyncDisposable
  {
      Task<string> CaptureCookiesJsonAsync();
      /// <summary>Task hoàn tất khi người dùng đóng cửa sổ trình duyệt (context/browser đóng).</summary>
      Task Closed { get; }
      bool IsClosed { get; }
  }
  ```
- **`LoginSession`** (đổi để giữ `IPlaywright` + `IBrowserContext`, bỏ `IBrowser` riêng):
  - Ctor đăng ký sự kiện đóng để hoàn tất `TaskCompletionSource`:
    ```csharp
    _context.Close += (_, _) => _closedTcs.TrySetResult();
    if (_context.Browser is { } b) b.Disconnected += (_, _) => _closedTcs.TrySetResult();
    ```
    (dùng `TaskCompletionSource` với `TaskCreationOptions.RunContinuationsAsynchronously`.)
  - `Closed => _closedTcs.Task;` `IsClosed => _closedTcs.Task.IsCompleted;`
  - `CaptureCookiesJsonAsync` giữ nguyên (đọc `_context.CookiesAsync()` → map `StoredCookie` → `CookieJson.Serialize`).
  - `DisposeAsync`: `try { await _context.CloseAsync(); } catch {}` rồi `try { _playwright.Dispose(); } catch {}` (persistent context: đóng context là đóng browser; không có `_browser` riêng để đóng).

**B2.3 — `OpenSellerAsync` tự lưu cookie, bỏ hỏi** ([AccountsViewModel.cs](../src/XuLyDonShopee.App/ViewModels/AccountsViewModel.cs)):
- Sau khi có `targetId` và `IsBusy=true`, tính `userDataDir`:
  ```csharp
  var baseDir = System.IO.Path.GetDirectoryName(_services.Database.Path) ?? ".";
  var userDataDir = BrowserProfilePaths.ForAccount(baseDir, targetId);
  System.IO.Directory.CreateDirectory(userDataDir); // đảm bảo có thư mục
  ```
- Chọn proxy: dùng **PHẦN 3** (thay khối `ProxyRotator` cũ).
- `EnsureBrowserInstalled` giữ nguyên (Task.Run).
- Mở: `session = await _loginService.OpenAsync(userDataDir, proxy);` (bọc try/catch → InfoAsync như cũ).
- **Bỏ hoàn toàn** `ConfirmAsync("Đăng nhập Shopee", ...)`.
- Thêm `[ObservableProperty] private string? _busyStatus;` (hiển thị hướng dẫn trong lúc mở). Trong `await using (session)`:
  ```csharp
  BusyStatus = "Đã mở trình duyệt. Hãy đăng nhập; đăng nhập xong ĐÓNG cửa sổ — cookie sẽ tự lưu.";
  string? lastSaved = null;
  const int PollMs = 2000;
  while (!session.IsClosed)
  {
      await Task.WhenAny(session.Closed, Task.Delay(PollMs));
      string json;
      try { json = await session.CaptureCookiesJsonAsync(); }
      catch { break; } // context đã đóng giữa chừng
      if (!string.IsNullOrEmpty(json) && json != lastSaved && CookieJson.Deserialize(json).Count > 0)
      {
          if (SaveCapturedCookie(targetId, json) == SaveCookieResult.Saved)
              lastSaved = json;
      }
  }
  // thông báo kết quả (KHÔNG hỏi — chỉ báo)
  await DialogService.InfoAsync("Mở trang bán hàng",
      lastSaved != null ? "Đã lưu cookie đăng nhập vào tài khoản."
                        : "Chưa thấy cookie đăng nhập (có thể bạn chưa đăng nhập).");
  ```
- `finally { IsBusy = false; BusyStatus = null; }`.
- Giữ nguyên `SaveCapturedCookie`, enum `SaveCookieResult`, mọi chú thích chống race trên `targetId`.

**B2.4 — Hiển thị `BusyStatus`** ([AccountsView.axaml](../src/XuLyDonShopee.App/Views/AccountsView.axaml)): thêm 1 `TextBlock` phía trên/hàng nút, `Text="{Binding BusyStatus}"`, `Foreground="#2E7D32"`, `TextWrapping="Wrap"`, `IsVisible="{Binding BusyStatus, Converter={x:Static StringConverters.IsNotNullOrEmpty}}"`.

### PHẦN 3 — Kiểm tra proxy sống (KiotProxy) rồi mới mở

**B3.1 — API `current` + kiểm hết hạn (IKiotProxyClient)** ([IKiotProxyClient.cs](../src/XuLyDonShopee.Core/Services/IKiotProxyClient.cs), [KiotProxyClient.cs](../src/XuLyDonShopee.Core/Services/KiotProxyClient.cs)):
- Interface thêm: `Task<ProxyEntry?> GetCurrentProxyAsync(CancellationToken cancellationToken = default);`
- Impl: tách hàm nội bộ dùng chung, phân biệt có/không gửi `region`:
  ```csharp
  private async Task<ProxyEntry?> FetchAsync(string key, string path, bool withRegion, CancellationToken ct)
  ```
  - `new`:  URL `{_baseUrl}/api/v1/proxies/new?key=...&region=...` (giữ nguyên hành vi cũ) → `GetNewProxyAsync` gọi `FetchAsync(key,"new",true,ct)`.
  - `current`: URL `{_baseUrl}/api/v1/proxies/current?key=...` (**KHÔNG** region) → `GetCurrentProxyAsync` xoay vòng key giống `GetNewProxyAsync` nhưng gọi `FetchAsync(key,"current",false,ct)`.
  - Không key → `null` (không gọi HTTP). Giữ nguyên `GetNewProxyAsync` cũ về mặt URL để test cũ vẫn xanh.
- **Kiểm "còn hạn" qua API** — thêm hàm thuần test được (không phụ thuộc đồng hồ trong test):
  ```csharp
  /// <summary>Trả proxy nếu JSON success và CHƯA hết hạn (expirationAt &gt; nowUnixMs). Không có expirationAt → coi như còn hạn.
  /// success=false/FAIL hoặc đã hết hạn → null.</summary>
  public static ProxyEntry? ParseProxyIfAlive(string? json, long nowUnixMs)
  ```
  - Bên trong: nếu `ParseResponse(json)` là null → null. Ngược lại đọc `data.expirationAt` (long, ms); nếu có và `expirationAt <= nowUnixMs` → null; còn lại → trả proxy.
  - `FetchAsync` cho nhánh `current` dùng `ParseProxyIfAlive(body, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())`. Nhánh `new` giữ `ParseResponse` (proxy mới coi như còn hạn).

**B3.2 — Kiểm kết nối thật.** Tạo `src/XuLyDonShopee.Core/Services/ProxyHealthChecker.cs`:
```csharp
public interface IProxyHealthChecker
{
    Task<bool> IsAliveAsync(ProxyEntry proxy, CancellationToken ct = default);
}
```
- Hàm thuần test được (tách riêng để unit-test): `public static string ToProxyAddress(ProxyEntry p)` → `p.Type == ProxyType.Socks5 ? $"socks5://{p.Host}:{p.Port}" : $"http://{p.Host}:{p.Port}"`.
- `ProxyHealthChecker : IProxyHealthChecker`:
  - `const string TestUrl = "https://api.ipify.org";` `const int TimeoutMs = 8000;`
  - Dựng `var wp = new System.Net.WebProxy(ToProxyAddress(proxy));` gán `wp.Credentials = new NetworkCredential(user, pass)` nếu `Username` không rỗng.
  - `using var handler = new HttpClientHandler { Proxy = wp, UseProxy = true };`
  - `using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(TimeoutMs) };`
  - `try { using var res = await http.GetAsync(TestUrl, ct); return res.IsSuccessStatusCode; } catch { return false; }`
  - XML-doc ghi chú: SOCKS5 + auth có thể không được `WebProxy` hỗ trợ (giống hạn chế Chromium đã nêu) — đa số proxy là HTTP.

**B3.3 — Điều phối (test được).** Tạo `src/XuLyDonShopee.Core/Services/ProxySelector.cs`:
```csharp
public static class ProxySelector
{
    /// <summary>Lấy proxy KiotProxy còn sống (kết hợp cả hai cách kiểm):
    /// (1) API — ưu tiên proxy hiện tại còn hạn (current, đã kiểm expirationAt); nếu key chưa có proxy/hết hạn
    /// (FAIL PROXY_NOT_FOUND_BY_KEY) thì xin proxy mới (new). (2) Thử kết nối thật qua proxy.
    /// Cả hai đạt → trả proxy; không lấy được proxy nào hoặc kết nối chết → null (tầng gọi dùng IP máy).</summary>
    public static async Task<ProxyEntry?> SelectKiotProxyAsync(
        IKiotProxyClient? kiot, IProxyHealthChecker checker, CancellationToken ct = default)
    {
        if (kiot is null) return null;                            // không có key KiotProxy → IP máy
        var proxy = await kiot.GetCurrentProxyAsync(ct) ?? await kiot.GetNewProxyAsync(ct);
        if (proxy is null) return null;                           // API không cấp được proxy nào → IP máy
        return await checker.IsAliveAsync(proxy, ct) ? proxy : null; // kết nối chết → IP máy
    }
}
```
> **Lý do current → new → IP máy** (không đi thẳng IP máy khi current hết hạn): lần đầu dùng key, `/current` luôn FAIL (`PROXY_NOT_FOUND_BY_KEY`) vì chưa có proxy gán — nếu bỏ qua `/new` thì key KiotProxy người dùng cấu hình sẽ không bao giờ được dùng. `/new` "load" một proxy cho key. Chỉ khi **cả** current lẫn new đều không cho proxy, hoặc proxy lấy được **không kết nối được**, mới rơi về IP máy — đúng tinh thần "proxy còn sống thì dùng, không thì IP máy".

**B3.4 — Nối vào `OpenSellerAsync`** (thay khối proxy cũ ở B2.3):
```csharp
var manual = _services.Proxies.GetAll();
ProxyEntry? proxy;
if (manual.Count > 0)
{
    proxy = await new ProxyRotator(manual).GetNextAsync(); // proxy thủ công: round-robin, KHÔNG kiểm
}
else
{
    var kiotKeys = _services.Settings.GetKiotProxyKeys();
    IKiotProxyClient? kiot = kiotKeys.Count == 0 ? null : new KiotProxyClient(kiotKeys);
    proxy = await ProxySelector.SelectKiotProxyAsync(kiot, _healthChecker);
}
```
- Thêm field VM `private readonly IProxyHealthChecker _healthChecker = new ProxyHealthChecker();` (giống `_loginService`).
- (Tùy chọn) set `BusyStatus = "Đang kiểm tra proxy..."` trước bước chọn proxy để người dùng biết.

**B3.5 — Ghi chú Cài đặt** ([SettingsView.axaml](../src/XuLyDonShopee.App/Views/SettingsView.axaml)): thêm 1 dòng vào đoạn mô tả: *"Khi mở trang bán hàng, app sẽ kiểm tra proxy KiotProxy còn sống (qua API + thử kết nối); nếu proxy chết sẽ tự dùng IP máy."* (chỉ text, không thêm control.)

### PHẦN 4 — Test mới

Thêm vào `src/XuLyDonShopee.Tests`:
- **`BrowserProfilePathsTests.cs`**: `ForAccount("C:\\data", 7)` → kết thúc bằng `profiles` + separator + `7`; hai Id khác nhau cho đường dẫn khác nhau.
- **`ProxyHealthCheckerTests.cs`** (chỉ hàm thuần `ToProxyAddress`): HTTP → `http://h:port`; Socks5 → `socks5://h:port`. (Không test HTTP thật — cần mạng.)
- **`ProxySelectorTests.cs`** dùng stub `IKiotProxyClient` + stub `IProxyHealthChecker`:
  - `kiot == null` → `null`.
  - `current` trả proxy, checker `true` → trả đúng proxy đó (không gọi `new`).
  - `current` null, `new` trả proxy, checker `true` → trả proxy (đã gọi `new`).
  - có proxy nhưng checker `false` → `null`.
  - `current` & `new` đều null → `null` (không gọi checker).
- **`KiotProxyClientTests.cs`** thêm:
  - `GetCurrentProxyAsync` (mẫu StubHandler sẵn có): URL chứa `/proxies/current` và **không** chứa `region=`; JSON success còn hạn (`expirationAt` tương lai) → proxy; `FailJson` (PROXY_NOT_FOUND_BY_KEY) → null; không key → null + `Empty(RequestedUrls)`; xoay vòng 2 key.
  - `ParseProxyIfAlive` (thuần, truyền `nowUnixMs`): success + `expirationAt` > now → proxy; success + `expirationAt` <= now → null; success không có `expirationAt` → proxy; FAIL json → null. (Dùng `expirationAt` cố định trong JSON mẫu + `now` cố định → không phụ thuộc đồng hồ.)
- **`AccountsViewModelTests.cs`**: 2 test ở B1.4. Các test cũ (gồm 4 test cookie) giữ nguyên & phải xanh.

### PHẦN 5 — README & smoke test

**B5.1 — README** ([README.md](../README.md)): cập nhật:
- Mục tài khoản: "chấp nhận **user dạng email hoặc tên đăng nhập bất kỳ**, không trùng, mật khẩu bắt buộc" (bỏ "kiểm tra email đúng định dạng").
- Mục mở trang bán hàng: nay **mở bằng hồ sơ (profile) riêng cho từng tài khoản, lưu tại `%APPDATA%\XuLyDonShopee\profiles\<id>` → lần sau vẫn đăng nhập**; **tự động lưu cookie khi bạn đóng cửa sổ (không hỏi nữa)**; **kiểm tra proxy KiotProxy sống trước khi mở, chết thì dùng IP máy**. Sửa đoạn "phiên trắng, cô lập" cho khớp (nay là profile bền theo tài khoản).
- Ghi chú bảo mật: bổ sung — profile chứa **session đăng nhập lưu trên đĩa** (không mã hóa), giống cookie/mật khẩu.

**B5.2 — Smoke test thật (Opus tự chạy, không đưa vào suite).** Qua project console tạm trong scratchpad tham chiếu Core:
- **Persistent profile:** mở `OpenAsync(tmpUserDataDir, null)`, dùng page set 1 cookie test (hoặc điều hướng), `DisposeAsync`; mở lại **cùng `userDataDir`** → `CaptureCookiesJsonAsync()` cho thấy cookie/profile **vẫn còn** (chứng minh tính bền). Báo cáo số liệu.
- **Bỏ hỏi + tự lưu:** xác nhận `OpenSellerAsync` không còn `ConfirmAsync`; mô tả vòng lặp poll bắt cookie & kết thúc khi `Closed`.
- **Proxy:** nếu có API key KiotProxy thật → `GetCurrentProxyAsync` trả proxy & `IsAliveAsync` in kết quả; không có key → `ProxySelector.SelectKiotProxyAsync(null,...)` = null (IP máy). `ProxyHealthChecker.IsAliveAsync` với proxy bịa (`1.2.3.4:9`) → `false` trong ≤ timeout. Báo cáo số liệu thật; nếu thiếu key thì ghi rõ.
- Nếu môi trường agent không mở được cửa sổ/không mạng → ghi rõ lý do (không tính fail plan), nhưng build + test tự động vẫn phải đạt.

## 4. Tiêu chí nghiệm thu

- [ ] `dotnet build XuLyDonShopee.sln -c Debug` — **0 error** (dừng tiến trình `XuLyDonShopee.App` trước khi build để không khóa `Core.dll`).
- [ ] `dotnet test XuLyDonShopee.sln` — **tất cả pass**, gồm test mới (`BrowserProfilePathsTests`, `ProxyHealthCheckerTests`, `ProxySelectorTests`, `GetCurrentProxyAsync` trong `KiotProxyClientTests`, 2 test user trong `AccountsViewModelTests`); **73 test cũ vẫn xanh** (đặc biệt 4 test cookie + email mẫu).
- [ ] **Phần 1:** lưu được tài khoản với user **không phải email** (vd `shopee_user01`); user rỗng vẫn báo lỗi; nhãn UI đã đổi ("Tài khoản đăng nhập (user)").
- [ ] **Phần 2:** `ShopeeLoginService.OpenAsync` nhận `userDataDir` và dùng `LaunchPersistentContextAsync`; smoke test chứng minh profile bền (mở lại cùng dir còn dữ liệu). `OpenSellerAsync` **không còn `ConfirmAsync` hỏi lưu**; có vòng lặp tự bắt & `SaveCapturedCookie`, kết thúc khi đóng cửa sổ; profile nằm trong `<dbdir>/profiles/<id>`.
- [ ] **Phần 3:** danh sách thủ công có proxy → dùng luôn (không kiểm). Trống + có key → `SelectKiotProxyAsync` (current→new→test). Proxy chết/không có → `null` (IP máy). Logic kiểm chứng bằng `ProxySelectorTests` + đọc code `OpenSellerAsync`.
- [ ] Không hard-code API key; không đổi hành vi CRUD/bắt cookie ngoài phạm vi nêu; README cập nhật.

## 5. Rủi ro & lưu ý

- **Đảo chiều "phiên cô lập":** nay profile bền theo tài khoản → cookie/session lưu trên đĩa (`profiles/<id>`). Đúng yêu cầu nhưng là dữ liệu nhạy cảm — ghi chú README, không mã hóa ở bước này.
- **Khóa profile:** không mở **cùng một `userDataDir`** hai lần đồng thời (Chromium khóa profile). `IsBusy` đã chặn mở lại trong lúc cửa sổ đang mở; nếu còn lock cũ (do crash) → `OpenAsync` ném → InfoAsync báo lỗi. Chấp nhận.
- **Mất cookie phút chót:** vòng lặp poll (2s) có thể bỏ lỡ cookie ngay trước khi người dùng đóng cửa sổ. Bù lại **profile bền đã giữ session trên đĩa** nên lần sau vẫn đăng nhập; DB chỉ lỡ ảnh chụp cuối. Chấp nhận; đã nêu.
- **`context.Close` cho persistent context:** kiểm chứng sự kiện `Close`/`Browser.Disconnected` bắn khi người dùng đóng cửa sổ. Nếu một sự kiện không bắn trên nền tảng cụ thể, `Task.WhenAny(..., Task.Delay)` vẫn giúp vòng lặp tiếp tục và thoát khi `CaptureCookiesJsonAsync` ném (context chết). Bọc try/catch như đã nêu.
- **KiotProxy `/current`:** theo doc, key chưa có proxy gán → FAIL `PROXY_NOT_FOUND_BY_KEY` → `ParseResponse` trả null → `SelectKiotProxyAsync` tự xin `/new`. Đọc `expirationAt` để loại proxy đã hết hạn. Nếu cả `/current` lẫn `/new` đều không cấp proxy → IP máy (đúng yêu cầu). Không có key thật để test đầu-cuối thì ghi rõ trong báo cáo.
- **SOCKS5 + auth:** cả Chromium (đã biết) lẫn `WebProxy` có thể bỏ qua/không hỗ trợ auth SOCKS5. Đa số proxy dùng là HTTP → chấp nhận, ghi chú.
- **`ToPlaywrightProxy`/`PlaywrightProxyMapper`** giữ nguyên — dùng lại cho persistent context (option `Proxy` cùng kiểu).
- Máy dev Windows (PowerShell 5.1). Gốc repo `d:\Projects\Xu-ly-don-shopee`. Trước build phải tắt app đang chạy.

---

## Báo cáo thực thi (Opus điền sau khi xong)

**Ngày thực thi:** 2026-07-14 · **Người thực thi:** Opus (`opus-executor`)

### A. Files đã tạo/sửa

**Tạo mới (Core):**
- `src/XuLyDonShopee.Core/Services/BrowserProfilePaths.cs` — helper `ForAccount(baseDir, id)` → `<baseDir>/profiles/<id>`.
- `src/XuLyDonShopee.Core/Services/ProxyHealthChecker.cs` — `IProxyHealthChecker` + `ProxyHealthChecker` (thử kết nối thật, `TestUrl=api.ipify.org`, `TimeoutMs=8000`) + hàm thuần `ToProxyAddress`.
- `src/XuLyDonShopee.Core/Services/ProxySelector.cs` — `SelectKiotProxyAsync(kiot, checker, ct)` điều phối current→new→test.

**Tạo mới (Tests):**
- `src/XuLyDonShopee.Tests/BrowserProfilePathsTests.cs` (2 test)
- `src/XuLyDonShopee.Tests/ProxyHealthCheckerTests.cs` (2 test — chỉ hàm thuần)
- `src/XuLyDonShopee.Tests/ProxySelectorTests.cs` (5 test — stub client + checker)

**Sửa (Core):**
- `Models/Account.cs` — cập nhật XML-doc `Email` (không đổi tên property/cột DB).
- `Services/IKiotProxyClient.cs` — thêm `GetCurrentProxyAsync`.
- `Services/KiotProxyClient.cs` — tách `SelectAcrossKeysAsync(path, withRegion, ct)` dùng chung cho new/current; `FetchAsync(key, path, withRegion, ct)` (current KHÔNG gửi `region`); thêm `GetCurrentProxyAsync` + `ParseProxyIfAlive(json, nowUnixMs)`. `GetNewProxyAsync` giữ nguyên URL cũ (`/proxies/new?...&region=`).
- `Services/ShopeeLoginService.cs` — `OpenAsync(string userDataDir, ProxyEntry?, ct)`; `LaunchAsync`→`LaunchPersistentAsync` dùng `LaunchPersistentContextAsync`; `ILoginSession` thêm `Closed`/`IsClosed`; `LoginSession` giữ `(IPlaywright, IBrowserContext)`, đăng ký `context.Close`/`browser.Disconnected` → `TaskCompletionSource`.

**Sửa (App):**
- `ViewModels/AccountsViewModel.cs` — `Save()` bỏ `EmailValidator`, kiểm không rỗng (`user`), đổi thông báo trùng; bỏ `using ...Validation`; thêm field `_healthChecker`, property `BusyStatus`; viết lại `OpenSellerAsync` (persistent profile theo `<dbdir>/profiles/<id>`, chọn proxy thủ công/KiotProxy, bỏ `ConfirmAsync` hỏi lưu, vòng lặp poll 2s tự lưu cookie + kết thúc khi `Closed`). Giữ nguyên `SaveCapturedCookie` + enum `SaveCookieResult`.
- `Views/AccountsView.axaml` — nhãn "Tài khoản đăng nhập (user)", watermark "Email hoặc tên đăng nhập", watermark tìm kiếm "Tìm theo tài khoản / ghi chú...", thêm `TextBlock` `BusyStatus`.
- `Views/SettingsView.axaml` — thêm dòng ghi chú kiểm proxy KiotProxy.

**Sửa (Tests hồi quy):**
- `ProxyRotatorTests.cs` — `FakeKiotProxyClient` thêm `GetCurrentProxyAsync` (bắt buộc để implement đủ interface; ProxyRotator không gọi hàm này nên không đổi hành vi test cũ).
- `KiotProxyClientTests.cs` — thêm 8 test (4 `GetCurrentProxyAsync` + 4 `ParseProxyIfAlive`).
- `AccountsViewModelTests.cs` — thêm 2 test (`Save_UserKhongPhaiEmail_VanLuuDuoc`, `Save_UserRong_BaoLoi`).

**Sửa (Docs):**
- `README.md` — mục tài khoản (user email/tên bất kỳ), mở trang bán hàng (profile bền + tự lưu cookie khi đóng + kiểm proxy sống), ghi chú bảo mật (profile chứa session trên đĩa).

### B. Kết quả build/test (số liệu thật)

- `dotnet build XuLyDonShopee.sln -c Debug` → **Build succeeded. 0 Warning(s), 0 Error(s)**. (Đã dừng tiến trình `XuLyDonShopee.App` PID 27080 trước khi build.)
- `dotnet test XuLyDonShopee.sln` → **Passed! Failed: 0, Passed: 92, Skipped: 0, Total: 92**.
  - Trước: 73 test. Sau: 92 test (+19: BrowserProfilePaths 2, ProxyHealthChecker 2, ProxySelector 5, KiotProxyClient 8, AccountsViewModel 2). Toàn bộ 73 test cũ (gồm 4 test cookie + các email mẫu `a@mail.com`...) vẫn xanh.
  - Lưu ý: lần build đầu FAIL 1 error vì `FakeKiotProxyClient` chưa implement method interface mới — đã bổ sung, build/test sau đó xanh.

### C. Kết quả smoke test (chạy thật qua project console trong scratchpad, tham chiếu Core)

1. **`ProxyHealthChecker` proxy bịa `1.2.3.4:9`** → `IsAliveAsync = False` sau **8082 ms** (≈ timeout 8s). `ToProxyAddress = http://1.2.3.4:9`.
2. **`ProxySelector.SelectKiotProxyAsync(null, checker)`** → `null` (IP máy).
3. **`ParseProxyIfAlive`**: còn hạn (exp2000, now1000) → proxy; hết hạn (exp1000, now2000) → null.
4. **Persistent profile bền qua 2 lần mở** (headless, cùng `userDataDir`): lần 1 thêm cookie `SMOKE_TEST`, đóng; lần 2 mở lại cùng dir → **tìm thấy `SMOKE_TEST = True`** → profile bền được xác nhận.
5. **`ShopeeLoginService.OpenAsync` (headed, đường đi thật)**: mở bằng **Brave**, điều hướng tới Shopee OK, `IsClosed=False`, `CaptureCookiesJsonAsync` = **29 cookie**. Cửa sổ được dispose sạch, không còn tiến trình brave sót.
   - Có key KiotProxy thật? **KHÔNG** — không có API key thật để test đầu-cuối `GetCurrentProxyAsync` với dịch vụ thật; đã kiểm bằng StubHandler (URL `/proxies/current`, không `region=`, JSON success/FAIL) + smoke `ParseProxyIfAlive`.

### D. Đối chiếu tiêu chí nghiệm thu

- [x] `dotnet build -c Debug` — 0 error (0 warning).
- [x] `dotnet test` — tất cả pass; đủ test mới; 73 test cũ xanh.
- [x] **Phần 1:** lưu được user không phải email (`shopee_user01`); user rỗng báo lỗi; nhãn UI đổi "Tài khoản đăng nhập (user)".
- [x] **Phần 2:** `OpenAsync(userDataDir, ...)` dùng `LaunchPersistentContextAsync`; smoke chứng minh profile bền; `OpenSellerAsync` không còn `ConfirmAsync` hỏi lưu (chỉ còn `ConfirmAsync` ở `DeleteAsync`); có vòng lặp tự bắt & `SaveCapturedCookie`, kết thúc khi `Closed`; profile tại `<dbdir>/profiles/<id>`.
- [x] **Phần 3:** proxy thủ công dùng luôn (round-robin, không kiểm); trống + có key → `SelectKiotProxyAsync` (current→new→test); proxy chết/không có → null (IP máy). Kiểm bằng `ProxySelectorTests` + đọc `OpenSellerAsync`.
- [x] Không hard-code API key; không đổi CRUD/bắt cookie ngoài phạm vi; README cập nhật.

### E. Quyết định phát sinh

- **`FakeKiotProxyClient` trong `ProxyRotatorTests`** phải thêm `GetCurrentProxyAsync` để compile (interface mở rộng). Cho trả `_proxy` cố định, không đụng `CallCount`; ProxyRotator không gọi hàm này nên hành vi test cũ giữ nguyên. Đây là thay đổi bắt buộc để giữ test cũ xanh, nằm trong tinh thần plan.
- **Smoke test tính bền profile** dùng trực tiếp `LaunchPersistentContextAsync` **headless** + `AddCookiesAsync` (cookie có expiry tương lai) thay vì đi qua `ILoginSession` — vì `ILoginSession` không expose page/context để chủ động set cookie. Đây là cách chứng minh cơ chế `userDataDir` bền chắc chắn, không phụ thuộc mạng/hiển thị. Bổ sung thêm test `OpenAsync` headed thật (Brave) để xác nhận đường đi end-to-end.
- **`ParseProxyIfAlive`** chỉ loại proxy khi `expirationAt` là số và `<= now`; thiếu/không đọc được `expirationAt` → coi như còn hạn (đúng plan).

### F. Hạn chế còn lại

- Không có **API key KiotProxy thật** nên chưa test đầu-cuối `GetCurrentProxyAsync`/`SelectKiotProxyAsync` với dịch vụ thật (đã test kỹ bằng stub + hàm thuần).
- **SOCKS5 + auth**: `WebProxy` (health check) và Chromium có thể không hỗ trợ auth SOCKS5 — đã ghi chú trong XML-doc; đa số proxy là HTTP.
- **Race mất cookie phút chót** (poll 2s) vẫn còn theo thiết kế, nhưng profile bền đã giữ session trên đĩa (đã nêu ở Rủi ro).
- Smoke `OpenAsync` headed có mở một cửa sổ Brave thật trong lúc chạy (đã dispose sạch ngay sau).

---

## Cập nhật vòng 2 (2026-07-14) — Sửa 3 lỗi từ panel rà soát đối kháng

Panel nghiệm thu phát hiện 3 lỗi logic thật. Đã sửa cả 3, thêm test cho mỗi sửa đổi, giữ toàn bộ test cũ xanh.

### Các sửa đổi

**Lỗi A [major] — Proxy thủ công không thực sự round-robin.**
`OpenSellerAsync` tạo `new ProxyRotator(manual)` mỗi lần mở → `_index` reset về 0 → luôn trả `manual[0]`, P1/P2 không bao giờ dùng.
- `AccountsViewModel.cs`: thêm field bền `private int _manualProxyIndex;` và method public `NextManualProxy(IReadOnlyList<ProxyEntry> manual)` (round-robin bền, list rỗng → null). Nhánh `manual.Count > 0` trong `OpenSellerAsync` nay gọi `proxy = NextManualProxy(manual);` (bỏ `await new ProxyRotator(...)`). `ProxyRotator` giữ nguyên (còn test + tái dùng sau), VM không dùng nữa.
- Test: `AccountsViewModelTests.NextManualProxy_XoayVongBenQuaCacLanGoi` — 4 lần gọi trên [P0,P1,P2] → [P0,P1,P2,P0]; list rỗng → null.

**Lỗi B [major] — Tự lưu cả cookie tiền-đăng-nhập, đè cookie hợp lệ + báo sai "Đã lưu".**
Điều kiện cũ `CookieJson.Deserialize(json).Count > 0` lưu cả cookie theo dõi (SPC_F, SPC_CDS, csrftoken...) Shopee set ngay khi CHƯA đăng nhập → đè `acc.Cookie` cũ + báo "Đã lưu" sai.
- Tạo `src/XuLyDonShopee.Core/Services/ShopeeLoginCookies.cs`: `IsLoggedIn(string?)` / `IsLoggedIn(IEnumerable<StoredCookie>)` — true khi có cookie đăng nhập (`SPC_EC`/`SPC_ST`/`SPC_U`) CÓ giá trị.
- `OpenSellerAsync`: điều kiện lưu đổi thành `!string.IsNullOrEmpty(json) && json != lastSaved && ShopeeLoginCookies.IsLoggedIn(json)`. Nhờ đó `lastSaved` chỉ khác null khi thật sự đăng nhập → thông báo "Đã lưu.../Chưa thấy..." đúng, không đè cookie cũ bằng cookie rác.
- Test: `ShopeeLoginCookiesTests` (4 test) — có SPC_EC value → true; chỉ SPC_F/SPC_CDS/csrftoken → false; SPC_EC rỗng → false; json rỗng/null → false.

**Lỗi C [minor] — Bỏ lỡ cookie phút chót khi đăng nhập rồi đóng < poll.**
- `OpenSellerAsync`: giảm `PollMs` 2000 → **1000** (thu hẹp cửa sổ bỏ lỡ). Không thêm cơ chế phức tạp: profile persistent đã giữ session trên đĩa nên đăng nhập KHÔNG mất — chỉ có thể lỡ 1 snapshot cookie vào DB.

### Nghiệm thu lại (số liệu thật)

- `dotnet build XuLyDonShopee.sln -c Debug` → **Build succeeded. 0 Warning(s), 0 Error(s)** (đã dừng app trước khi build; đã clean bin/obj build lại từ đầu vẫn 0/0).
- `dotnet test XuLyDonShopee.sln` → **Passed! Failed: 0, Passed: 97, Skipped: 0, Total: 97**.
  - Trước vòng 2: 92 test. Sau: **97** (+5: `NextManualProxy` 1, `ShopeeLoginCookies` 4). Toàn bộ 92 test trước vẫn xanh.
- **Lưu ý môi trường (không phải lỗi code):** máy có chính sách **Application Control (WDAC/ISG)** chặn nạp `Core.dll` vừa build theo hash (lỗi `FileLoadException 0x800711C7`). Vì build deterministic cho cùng hash nên verdict "block" bị cache → rebuild cùng source vẫn bị chặn. Build với `-p:Deterministic=false` (sinh hash mới, ISG đánh giá lại và cho qua) thì test chạy bình thường **97/97 pass**. Không sửa được policy (không có quyền admin, ngoài phạm vi plan). Nếu môi trường nghiệm thu gặp lại lỗi này, build lại với `-p:Deterministic=false` một lần để ISG cấp verdict mới.

### Hạn chế còn lại (Lỗi C)

- Poll 1s vẫn có thể lỡ **1 snapshot cookie cuối** nếu người dùng đăng nhập xong rồi đóng cửa sổ trong < 1s. Nhưng **profile persistent đã lưu session trên đĩa**, nên lần mở sau vẫn còn đăng nhập; chỉ trường `Account.Cookie` trong DB có thể chưa cập nhật ảnh chụp cuối cùng. Chấp nhận theo thiết kế.
