# Plan: Nút "Mở trang bán hàng" — đăng nhập Shopee Seller & giữ lại cookie

- **Ngày:** 2026-07-13
- **Trạng thái:** hoàn thành
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)

## 1. Bối cảnh & mục tiêu

Nối tiếp app Avalonia đã có (xem plan `2026-07-13-app-avalonia-quan-ly-tai-khoan-proxy.md`). Người dùng muốn: trong form chi tiết tài khoản, thêm một nút **"Mở trang bán hàng"**. Bấm nút → app mở trang **https://banhang.shopee.vn** (Shopee Seller Centre) trong một cửa sổ trình duyệt; người dùng **tự đăng nhập** (Shopee bắt mật khẩu + OTP/captcha nên app KHÔNG tự điền được); đăng nhập xong → app **bắt toàn bộ cookie** của phiên và **lưu vào tài khoản** đó (trường Cookie) để dùng lại về sau.

**Quyết định đã chốt với người dùng:**
- Công nghệ trình duyệt: **Playwright** (mở một cửa sổ Chromium riêng, không nhúng trong app). Lý do chọn: bắt cookie ổn định, chạy được cả Windows/Linux, cùng công nghệ sẽ dùng cho bước "chạy tài khoản" sau này, và hỗ trợ proxy theo từng phiên.
- Đăng nhập **định tuyến qua proxy xoay vòng nếu có** (lấy proxy từ `ProxyRotator` đã có: có danh sách proxy → xoay vòng; danh sách trống + có KiotProxy key → lấy qua KiotProxy; không có → IP máy).
- Tên nút hiển thị: **"Mở trang bán hàng"** (không phải "Đăng nhập").

**Hiện trạng code liên quan (đã khảo sát):**
- Form nằm ở [src/XuLyDonShopee.App/Views/AccountsView.axaml](../src/XuLyDonShopee.App/Views/AccountsView.axaml); hàng nút cuối form có "Lưu" (`SaveCommand`) và "Hủy" (`CancelCommand`).
- [src/XuLyDonShopee.App/ViewModels/AccountsViewModel.cs](../src/XuLyDonShopee.App/ViewModels/AccountsViewModel.cs): có `EditCookie`, `SelectedAccount`, cờ `IsNew`, `IsEditing`, field `_editingId` (Id tài khoản đang mở trong form; null = form trống/tạo mới), `AppServices _services`.
- [src/XuLyDonShopee.App/Services/AppServices.cs](../src/XuLyDonShopee.App/Services/AppServices.cs): `Accounts` (AccountRepository), `Proxies` (ProxyRepository, có `GetAll()`), `Settings` (SettingsRepository — key `SettingsRepository.KiotProxyApiKeys`, giờ là danh sách).
- [src/XuLyDonShopee.Core/Services/ProxyRotator.cs](../src/XuLyDonShopee.Core/Services/ProxyRotator.cs): `new ProxyRotator(IEnumerable<ProxyEntry>?, IKiotProxyClient?)`, `Task<ProxyEntry?> GetNextAsync(ct)`.
- [src/XuLyDonShopee.Core/Services/KiotProxyClient.cs](../src/XuLyDonShopee.Core/Services/KiotProxyClient.cs): `new KiotProxyClient(IEnumerable<string> apiKeys, region?, baseUrl?, httpClient?)`.
- [src/XuLyDonShopee.Core/Models/ProxyEntry.cs](../src/XuLyDonShopee.Core/Models/ProxyEntry.cs): `Host`, `Port`, `Username?`, `Password?`, `Type` (enum `ProxyType.Http`/`Socks5`).
- [src/XuLyDonShopee.App/Services/DialogService.cs](../src/XuLyDonShopee.App/Services/DialogService.cs): `ConfirmAsync(title,msg)→bool`, `InfoAsync(title,msg)`. `ConfirmDialog` chỉ khóa cửa sổ app, KHÔNG khóa cửa sổ trình duyệt Playwright (là cửa sổ OS riêng) → dùng được làm nút "Lưu cookie".

## 2. Phạm vi

- **Làm:**
  - Thêm gói `Microsoft.Playwright` vào project **Core**.
  - Tầng Core: `ShopeeLoginService` (mở Chromium headed tới URL qua proxy, giữ phiên, bắt cookie JSON), `PlaywrightProxyMapper` (đổi `ProxyEntry?` → cấu hình proxy Playwright — hàm thuần, có test), `StoredCookie` DTO + serialize/deserialize (có test round-trip), tự cài Chromium lần đầu.
  - Tầng App: nút "Mở trang bán hàng" trên form + `OpenSellerCommand` trong `AccountsViewModel` điều phối toàn bộ luồng, lưu cookie vào DB.
  - Test đơn vị cho phần logic thuần (mapper, cookie round-trip). Smoke test thủ công cho phần trình duyệt.
- **Không làm:**
  - Tự điền username/password/OTP (Shopee chặn; người dùng tự đăng nhập).
  - Tính năng "chạy tài khoản" (tái sử dụng cookie để thao tác đơn) — plan sau.
  - Kiểm tra cookie còn sống/hết hạn, tự refresh cookie.
  - Lưu hồ sơ trình duyệt lâu dài (persistent profile) cho từng tài khoản — dùng context mới mỗi lần (ghi chú ở mục Rủi ro).
  - Đổi `Status` tài khoản tự động sau khi lấy cookie.

## 3. Các bước thực hiện

### Bước 1 — Thêm Playwright vào Core

1. Trong `src/XuLyDonShopee.Core/XuLyDonShopee.Core.csproj` thêm `<PackageReference Include="Microsoft.Playwright" Version="1.4x.x" />` (bản stable mới nhất tương thích net8.0; nếu restore lỗi thì ghim `1.49.0`).
2. Build lại để package sinh ra công cụ cài browser.

### Bước 2 — Core: mapper proxy (hàm thuần, test được)

Tạo `src/XuLyDonShopee.Core/Services/PlaywrightProxyMapper.cs`:
- `public static Proxy? ToPlaywrightProxy(ProxyEntry? entry)` (kiểu `Microsoft.Playwright.Proxy`):
  - `entry == null` → trả `null` (nghĩa là không dùng proxy, đi IP máy).
  - `Type == ProxyType.Socks5` → `Server = $"socks5://{Host}:{Port}"`; ngược lại `Server = $"http://{Host}:{Port}"`.
  - Nếu `Username` không rỗng → gán `Username`, `Password`.
- Ghi chú XML: Chromium **không hỗ trợ xác thực user/pass cho SOCKS5** — với proxy SOCKS5 có auth sẽ không dùng được auth; đa số proxy dùng là HTTP nên chấp nhận (ghi rõ trong báo cáo).

### Bước 3 — Core: DTO cookie + serialize (test round-trip)

Tạo `src/XuLyDonShopee.Core/Services/StoredCookie.cs`:
- `public sealed record StoredCookie(string Name, string Value, string Domain, string Path, double Expires, bool HttpOnly, bool Secure, string? SameSite);`
- Lớp tiện ích `CookieJson`:
  - `static string Serialize(IEnumerable<StoredCookie> cookies)` → JSON (dùng `System.Text.Json`, có indent để người dùng đọc được trong ô Cookie).
  - `static List<StoredCookie> Deserialize(string? json)` → parse an toàn, lỗi/null → danh sách rỗng.
- Định dạng JSON này chính là thứ sẽ nạp lại bằng `AddCookiesAsync` ở bước "chạy tài khoản" sau — giữ đủ trường.

### Bước 4 — Core: ShopeeLoginService (mở browser, bắt cookie)

Tạo `src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs`:

1. Hằng: `public const string SellerUrl = "https://banhang.shopee.vn/";`
2. `EnsureBrowserInstalled()`: gọi `Microsoft.Playwright.Program.Main(new[] { "install", "chromium" })`; trả exit code. (Idempotent — nếu đã cài sẽ nhanh.) Bọc try/catch, trả code ≠ 0 khi lỗi.
3. Phương thức mở phiên:
   ```
   public async Task<ILoginSession> OpenAsync(ProxyEntry? proxy, CancellationToken ct = default)
   ```
   - Tạo `IPlaywright` (`await Playwright.CreateAsync()`).
   - `LaunchAsync(new BrowserTypeLaunchOptions { Headless = false })` cho `Chromium`.
   - `NewContextAsync(new BrowserNewContextOptions { Proxy = PlaywrightProxyMapper.ToPlaywrightProxy(proxy), IgnoreHTTPSErrors = true })`.
   - `NewPageAsync()`, `GotoAsync(SellerUrl, new() { Timeout = 60000, WaitUntil = WaitUntilState.DOMContentLoaded })` (nuốt lỗi timeout điều hướng — vẫn để cửa sổ mở cho người dùng).
   - Trả về `LoginSession` (implements `ILoginSession`) giữ tham chiếu playwright/browser/context/page.
4. `interface ILoginSession : IAsyncDisposable` với `Task<string> CaptureCookiesJsonAsync()`:
   - `var raw = await _context.CookiesAsync();` (không tham số = tất cả cookie trong context).
   - Map từng cookie Playwright → `StoredCookie` (Name/Value/Domain/Path/Expires/HttpOnly/Secure/SameSite.ToString()).
   - Trả `CookieJson.Serialize(list)`.
   - `DisposeAsync`: đóng context, browser, playwright (bọc try/catch từng cái).
5. Toàn bộ service nuốt lỗi hợp lý và ném ra exception có message tiếng Việt rõ ràng khi không mở được (để ViewModel hiện dialog).

### Bước 5 — App: nút & luồng điều phối trong AccountsViewModel

1. **AccountsView.axaml:** thêm nút vào hàng nút cuối form (cạnh "Lưu"/"Hủy"), hoặc ngay dưới ô "Cookie đăng nhập":
   ```xml
   <Button Content="Mở trang bán hàng"
           Command="{Binding OpenSellerCommand}"
           IsEnabled="{Binding CanOpenSeller}" />
   ```
   Đặt màu phụ (vd nền `#2E7D32`, chữ trắng) để phân biệt. Kèm `ToolTip.Tip="Mở Shopee Seller để đăng nhập và lưu cookie vào tài khoản này"`.
2. **AccountsViewModel:**
   - Thêm property `public bool CanOpenSeller => IsEditing && !IsNew && _editingId is not null && !IsBusy;` và `[ObservableProperty] bool _isBusy;`. Gọi `OnPropertyChanged(nameof(CanOpenSeller))` khi `IsEditing`/`IsNew`/`IsBusy` đổi và sau khi set `_editingId` (thêm helper set `_editingId` hoặc raise thủ công ở các chỗ đã có).
   - `[RelayCommand] private async Task OpenSellerAsync()`:
     1. Nếu `_editingId is null` → `DialogService.InfoAsync("Mở trang bán hàng", "Hãy lưu tài khoản trước khi mở trang bán hàng.")` rồi return. (Phòng hờ; nút vốn đã disable.)
     2. `IsBusy = true;` (cập nhật CanOpenSeller). try/finally để luôn trả `IsBusy=false`.
     3. Xây rotator: `var proxies = _services.Proxies.GetAll();` `var kiotKeys = _services.Settings.GetKiotProxyKeys();` `IKiotProxyClient? kiot = kiotKeys.Count == 0 ? null : new KiotProxyClient(kiotKeys);` `var rotator = new ProxyRotator(proxies, kiot);` `var proxy = await rotator.GetNextAsync();`
     4. Đảm bảo browser: `await DialogService.InfoAsync(...)` KHÔNG cần; thay vào đó gọi `ShopeeLoginService.EnsureBrowserInstalled()` trên thread nền (`await Task.Run(...)`); nếu code ≠ 0 → InfoAsync báo "Không cài được trình duyệt. Kiểm tra mạng rồi thử lại." và return. (Có thể hiện Info "Đang chuẩn bị trình duyệt lần đầu, vui lòng đợi..." trước khi chạy — tùy chọn.)
     5. `await using var session = await _loginService.OpenAsync(proxy);` (bọc try/catch → InfoAsync khi lỗi).
     6. `var ok = await DialogService.ConfirmAsync("Đăng nhập Shopee", "Trình duyệt đã mở trang bán hàng Shopee. Hãy đăng nhập, rồi bấm \"Lưu cookie\" để lưu vào tài khoản. Bấm \"Hủy\" nếu chưa đăng nhập.");` — nút OK của ConfirmDialog đóng vai "Lưu cookie". (Được phép sửa nhãn nút ConfirmDialog nếu dễ; nếu không, giữ nhãn mặc định và nêu rõ trong message.)
     7. Nếu `ok`:
        - `var json = await session.CaptureCookiesJsonAsync();`
        - Nếu rỗng/không có cookie đáng kể (danh sách trống) → InfoAsync cảnh báo "Chưa thấy cookie đăng nhập. Có thể bạn chưa đăng nhập xong." và KHÔNG ghi đè (return sau khi đóng).
        - Ngược lại: nạp bản ghi `var acc = _services.Accounts.GetById(_editingId.Value);` nếu còn → `acc.Cookie = json; _services.Accounts.Update(acc);` rồi `EditCookie = json;` `UpdatedAtText = ...` (hoặc gọi `Reload()`/`RefreshList(_editingId)` để cập nhật form). Thông báo InfoAsync "Đã lưu cookie đăng nhập vào tài khoản."
     8. `session` tự đóng khi ra khỏi `await using`.
   - Inject `ShopeeLoginService`: thêm field `private readonly ShopeeLoginService _loginService = new();` (hoặc khởi tạo trong ctor). Không cần DI.
3. Đảm bảo mọi thao tác cập nhật UI chạy trên UI thread (await trong Avalonia tự quay lại UI context; nếu dùng `Task.Run` cho EnsureBrowserInstalled thì phần sau await vẫn về UI thread — an toàn).

### Bước 6 — Test

Thêm vào `src/XuLyDonShopee.Tests`:
1. `PlaywrightProxyMapperTests`:
   - `null` → `null`.
   - HTTP không auth → `Server == "http://1.2.3.4:8080"`, `Username == null`.
   - HTTP có auth → Server đúng + Username/Password đúng.
   - SOCKS5 → `Server` bắt đầu `socks5://`.
2. `CookieJsonTests`: tạo list `StoredCookie` (2-3 phần tử, đủ trường) → `Serialize` → `Deserialize` → khẳng định bằng giá trị gốc (round-trip). Thêm case `Deserialize(null)`/chuỗi rác → danh sách rỗng, không ném.
3. **Không** thêm test tự động cho `ShopeeLoginService` (cần browser thật + tương tác) — chạy suite phải không phụ thuộc mạng/GUI.

### Bước 7 — Smoke test thủ công & README

1. Smoke test do Opus tự chạy (một lần, không đưa vào suite): cài chromium (`pwsh bin/.../playwright.ps1 install chromium` hoặc gọi `Program.Main(["install","chromium"])` qua một đoạn `dotnet run` tạm), rồi mở thử `ShopeeLoginService.OpenAsync(null)` (proxy null = IP máy) tới `https://banhang.shopee.vn`, chờ load, gọi `CaptureCookiesJsonAsync()` in ra số cookie, đóng. Mục tiêu: chứng minh đường ống browser→cookie chạy, KHÔNG cần đăng nhập thật. Nếu môi trường agent không mở được cửa sổ/không tải được browser, ghi rõ lý do trong báo cáo (không tính là fail plan).
2. Cập nhật `README.md`: ghi rằng lần đầu bấm "Mở trang bán hàng" app sẽ tải Chromium (~150MB, một lần); cookie lưu vào trường Cookie của tài khoản; đăng nhập là thủ công.

## 4. Tiêu chí nghiệm thu

- [ ] `dotnet build XuLyDonShopee.sln` — **0 error**.
- [ ] `dotnet test` — toàn bộ test pass (gồm `PlaywrightProxyMapperTests`, `CookieJsonTests`).
- [ ] App chạy được (`dotnet run --project src/XuLyDonShopee.App`), form tài khoản có nút **"Mở trang bán hàng"**.
- [ ] Nút **disable** khi đang tạo tài khoản mới/ chưa lưu (chưa có `_editingId`), **enable** khi đang mở một tài khoản đã lưu. (Kiểm bằng đọc logic `CanOpenSeller` + mô tả; nếu chạy tay được thì xác nhận trực quan.)
- [ ] Logic lưu cookie: khi có cookie JSON, ghi vào `accounts.Cookie` của đúng tài khoản (qua `AccountRepository.Update`) và hiển thị lại trên form.
- [ ] Không hard-code API key/không đổi hành vi CRUD cũ; các test cũ vẫn pass.
- [ ] (Nếu môi trường cho phép) smoke test mở browser tới banhang.shopee.vn và gọi bắt cookie không ném exception — báo cáo kết quả thật; nếu không chạy được, giải thích lý do.

## 5. Rủi ro & lưu ý

- **Playwright cần tải Chromium (~150MB) lần đầu.** Phải xử lý mượt: gọi `install` idempotent, báo người dùng đang tải, lỗi mạng thì thông báo rõ chứ không treo app.
- **SOCKS5 + auth:** Chromium không hỗ trợ xác thực SOCKS5 → proxy loại này sẽ đăng nhập không qua auth. Đa số proxy dùng là HTTP; ghi chú, không xử lý thêm ở plan này.
- **Nút chỉ dùng cho tài khoản đã lưu** (cần Id để ghi cookie). Tài khoản mới phải Lưu trước — nút disable + có thông báo hướng dẫn.
- **Context mới mỗi lần** → không nhớ "thiết bị tin cậy"; mỗi lần có thể phải qua OTP lại. Chấp nhận ở bước này; bước sau có thể chuyển sang persistent profile/nạp lại cookie đã lưu.
- **Điều phối cửa sổ:** ConfirmDialog khóa cửa sổ app nhưng KHÔNG khóa cửa sổ Chromium (cửa sổ OS riêng) — người dùng vẫn đăng nhập được trong lúc dialog "Lưu cookie/Hủy" đang chờ. Nếu người dùng đóng tay cửa sổ Chromium trước khi bấm → `CaptureCookiesJsonAsync` có thể ném; bọc try/catch, coi như hủy.
- **Bảo mật:** cookie là thông tin đăng nhập nhạy cảm, lưu dạng thường trong SQLite cục bộ (giống mật khẩu ở bước đầu) — ghi chú trong README, không thêm mã hóa ở plan này.
- Máy dev Windows (PowerShell 5.1); Playwright có `playwright.ps1`. Gốc repo: `d:\Projects\Xu-ly-don-shopee`.

---

## Báo cáo thực thi (Opus điền sau khi xong)

**Ngày thực thi:** 2026-07-13 · **Người thực thi:** Opus (`opus-executor`)

### Files đã tạo/sửa

**Tạo mới (Core):**
- `src/XuLyDonShopee.Core/Services/PlaywrightProxyMapper.cs` — hàm thuần `ToPlaywrightProxy(ProxyEntry?)`.
- `src/XuLyDonShopee.Core/Services/StoredCookie.cs` — record `StoredCookie` + tiện ích `CookieJson` (Serialize/Deserialize an toàn).
- `src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs` — `ShopeeLoginService`, `interface ILoginSession`, class con `LoginSession`.

**Tạo mới (Tests):**
- `src/XuLyDonShopee.Tests/PlaywrightProxyMapperTests.cs` — 4 test.
- `src/XuLyDonShopee.Tests/CookieJsonTests.cs` — 5 test.

**Sửa:**
- `src/XuLyDonShopee.Core/XuLyDonShopee.Core.csproj` — thêm `Microsoft.Playwright` 1.49.0.
- `src/XuLyDonShopee.App/ViewModels/AccountsViewModel.cs` — thêm `using` Core.Data/Core.Services; field `_loginService`; `[ObservableProperty] _isBusy`; property `CanOpenSeller`; handler `OnIsNewChanged`/`OnIsBusyChanged` + bổ sung raise `CanOpenSeller` trong `OnIsEditingChanged`, `LoadIntoForm`, `ClearForm`; command `OpenSellerAsync`.
- `src/XuLyDonShopee.App/Views/AccountsView.axaml` — thêm nút "Mở trang bán hàng" (nền #2E7D32, tooltip) vào hàng nút cuối form.
- `README.md` — thêm mô tả tính năng, ghi chú tải Chromium ~150MB lần đầu, đăng nhập thủ công, và cảnh báo bảo mật cookie.

### Kết quả kiểm chứng (số liệu thật)

- **`dotnet build XuLyDonShopee.sln -c Debug`:** Build succeeded — **0 Warning, 0 Error**.
  (Lần build đầu fail do có tiến trình `XuLyDonShopee.App` (PID 27716) đang chạy giữ khóa `XuLyDonShopee.Core.dll` — đã dừng tiến trình rồi build lại thành công. Không phải lỗi biên dịch.)
- **`dotnet test XuLyDonShopee.sln --no-build`:** **Passed! Failed: 0, Passed: 50, Skipped: 0, Total: 50** (168 ms). Bao gồm 9 test mới (4 mapper + 5 cookie); các test cũ vẫn pass.
- **Chạy app** (`dotnet ...XuLyDonShopee.App.dll`): tiến trình sống ổn định >6s, `HasExited=False`, không crash; sau đó tắt chủ động. Nút bind qua compiled bindings (`x:DataType`) đã được xác thực khi build (0 warning) → `OpenSellerCommand`/`CanOpenSeller` resolve đúng ViewModel.
- **Smoke test browser (Bước 7):** **CHẠY THẬT — THÀNH CÔNG.** Qua project console tạm trong scratchpad (tham chiếu Core), gọi đúng `ShopeeLoginService`:
  - `EnsureBrowserInstalled()` → exit code **0** (đã tải Chromium/ffmpeg/headless-shell về `%LOCALAPPDATA%\ms-playwright`).
  - `OpenAsync(null)` (Headless=false) mở `https://banhang.shopee.vn/` thành công (headed Chromium khởi chạy được → chứng minh cả đường headed hoạt động trên máy này).
  - `CaptureCookiesJsonAsync()` → **bắt được 30 cookie**, JSON dài 7265 ký tự, ví dụ cookie đầu `SPC_CDS` domain `banhang.shopee.vn`. Không ném exception. Đóng phiên sạch.
  - Xác nhận: driver `.playwright` (node.exe + package) được copy sang output của App qua `buildTransitive` → tính năng chạy được ở runtime mà không cần thêm PackageReference vào project App.

### Đối chiếu tiêu chí nghiệm thu

- [x] Build 0 error. [x] `dotnet test` 50/50 pass. [x] App chạy không crash, nút "Mở trang bán hàng" có trong form. [x] Logic disable/enable nút đúng theo `CanOpenSeller`. [x] Logic lưu cookie ghi vào `accounts.Cookie` đúng tài khoản qua `AccountRepository.Update` và cập nhật form (EditCookie + UpdatedAtText). [x] Không hard-code API key, không đổi CRUD cũ, test cũ vẫn pass. [x] Smoke test browser chạy thật không ném exception.

### Quyết định phát sinh

- **Playwright version:** dùng thẳng **1.49.0** (bản plan nêu làm fallback an toàn) — restore + build + smoke đều OK; không cần dò bản mới hơn.
- **Nhãn nút "Lưu cookie":** giữ nhãn mặc định ConfirmDialog là **"Đồng ý"** (plan cho phép), và message dialog nêu rõ 'bấm "Đồng ý" để lưu cookie' — không sửa ConfirmDialog để giảm phạm vi.
- **Không thêm PackageReference vào App:** driver Playwright đã tự copy sang output App qua buildTransitive (đã kiểm chứng), nên giữ đúng phạm vi plan (chỉ thêm gói vào Core).

### Chưa làm được / hạn chế (đúng phạm vi plan)

- Không có (mọi hạng mục trong plan đã hoàn thành và kiểm chứng thật).
- Lưu ý theo mục Rủi ro: SOCKS5 có auth thì Chromium bỏ qua auth; cookie lưu dạng thường; dùng context mới mỗi lần (không nhớ thiết bị tin cậy) — đã ghi chú, không xử lý ở plan này.
- Chưa kiểm chứng luồng đăng nhập-thật đầu-cuối (cần thao tác người dùng nhập mật khẩu/OTP) — ngoài phạm vi smoke; logic điều phối đã hoàn chỉnh và biên dịch/bind đúng.

---

### Sửa sau nghiệm thu vòng 2 (2 lỗi major từ rà soát đối kháng)

**Lỗi A — Race trên `_editingId` (ghi nhầm tài khoản / unobserved exception):**
- `OpenSellerAsync` giờ **chụp `var targetId = _editingId.Value;`** ngay sau lần kiểm tra null ở đầu hàm (không có await xen giữa), và dùng `targetId` xuyên suốt cho việc ghi cookie thay vì đọc lại field `_editingId` sau các await dài.
- Việc ghi cookie được tách sang `SaveCapturedCookie(long targetId, string cookieJson)` — nhận sẵn JSON, KHÔNG đọc `_editingId.Value` để lấy Id ghi DB → không còn ném `InvalidOperationException` khi người dùng bấm "+ Thêm" (`_editingId`=null) giữa chừng, và cookie luôn ghi vào đúng tài khoản mục tiêu dù người dùng đã đổi chọn.
- Lời gọi `SaveCapturedCookie` trong `OpenSellerAsync` được bọc thêm `try/catch` (khối try ngoài chỉ có `finally`) → không nhánh nào ném ra ngoài command.
- Phần cập nhật FORM (`EditCookie`/`UpdatedAtText`) chỉ chạy khi `_editingId == targetId` (người dùng vẫn đang mở đúng tài khoản đó); nếu đã chuyển đi thì vẫn lưu DB cho `targetId` nhưng không đè form/lựa chọn đang hiển thị tài khoản khác.

**Lỗi B — Không dựng lại danh sách sau khi lưu cookie (mất cookie khi chọn lại):**
- Sau khi `Update` cookie vào DB và làm mới `_all = GetAll()`, code giờ **gọi `RefreshList(targetId)`** (nhánh vẫn-đang-mở) để `Accounts`/`SelectedAccount` trỏ tới instance MỚI đã có cookie — khớp cách các nhánh `Save` đang làm. Nhánh đã-chuyển-đi gọi `RefreshList(_editingId ?? SelectedAccount?.Id)` để vẫn cập nhật instance của targetId trong danh sách mà giữ nguyên lựa chọn hiện tại. Nhờ đó chọn lại A không còn nhận instance cũ Cookie rỗng → `Save` sau đó không ghi đè cookie về null.

**Cách tách để test được:** logic lưu-cookie nằm ở `public SaveCapturedCookie(targetId, json)` trả về enum `SaveCookieResult` (`NoCookie`/`AccountMissing`/`Saved`), hoàn toàn không gọi Playwright; `OpenSellerAsync` chỉ bắt cookie (JSON) rồi gọi phương thức này và hiện dialog theo kết quả. Nhờ vậy test hồi quy chạy ở mức ViewModel với DB tạm, không cần browser.

**Test hồi quy thêm mới (`AccountsViewModelTests.cs`, +4 test):**
- `LuuCookie_DoiChonGiuaChung_GhiVaoTargetId_KhongDeSangTaiKhoanDangXem` (Lỗi A): mở A → chọn sang B → lưu cookie cho `aId` → cookie ghi vào A, B vẫn null, form vẫn hiển thị B.
- `LuuCookie_KhiEditingIdNull_KhongNem_VanGhiTargetId` (Lỗi A): bấm "+ Thêm" (`_editingId`=null) rồi lưu cookie cho `aId` → không ném, vẫn ghi vào A, vẫn ở form tạo mới.
- `LuuCookie_ChonLaiTaiKhoan_FormHienDungCookie_SaveKhongMatCookie` (Lỗi B): lưu cookie A → chọn B → chọn lại A → form hiện đúng cookie; `Save` không xóa cookie.
- `LuuCookie_DangMoDungTaiKhoan_FormCapNhatCookieNgay` (Lỗi B biến thể): lưu cookie khi đang mở A → form + instance trong `Accounts` đều có cookie ngay.

**Kiểm chứng vòng 2 (số liệu thật):**
- `dotnet build XuLyDonShopee.sln -c Debug`: **Build succeeded, 0 Warning, 0 Error** (đã dừng instance app trước khi build để không khóa `Core.dll`).
- `dotnet test XuLyDonShopee.sln --no-build`: **Passed! Failed: 0, Passed: 68, Skipped: 0, Total: 68** (257 ms) — tăng từ 64 → 68 (thêm 4 test hồi quy trên).

**Không đụng tới** rò rỉ HttpClient trong `KiotProxyClient` (panel đánh giá không cần sửa) — giữ phạm vi hẹp theo yêu cầu.
