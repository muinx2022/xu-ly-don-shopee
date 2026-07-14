# Plan: Đăng nhập Shopee bằng Brave (ưu tiên Brave, fallback Chromium đóng gói)

- **Ngày:** 2026-07-13
- **Trạng thái:** hoàn thành
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)

## 1. Bối cảnh & mục tiêu

Tính năng "Mở trang bán hàng" (xem plan `2026-07-13-mo-trang-ban-hang-bat-cookie.md`) hiện mở **Chromium đóng gói của Playwright** (tự tải ~150MB lần đầu về `%LOCALAPPDATA%\ms-playwright`). Người dùng muốn mở bằng **Brave** (đã cài sẵn trên máy).

**Quyết định đã chốt với người dùng:**
- **Ưu tiên Brave, thiếu thì Chromium:** nếu tìm thấy Brave trên máy → mở Brave; không có (vd Linux chưa cài) → tự động dùng Chromium đóng gói.
- **Giữ phiên sạch, cô lập từng tài khoản** như hiện tại (mỗi lần một phiên trắng riêng, không lẫn cookie giữa các tài khoản). KHÔNG dùng hồ sơ Brave thật của người dùng.

**Hiện trạng đã khảo sát:**
- Brave có trên máy dev tại `C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe` (version 150.1.92.139).
- [src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs](../src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs):
  - `EnsureBrowserInstalled()` gọi `Microsoft.Playwright.Program.Main(new[]{"install","chromium"})` → tải Chromium đóng gói.
  - `OpenAsync(ProxyEntry?, ct)` mở `playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions{ Headless=false })` (không set `ExecutablePath`) → dùng Chromium đóng gói.
- Playwright cho phép mở BẤT KỲ trình duyệt nền Chromium nào qua `BrowserTypeLaunchOptions.ExecutablePath`. Trỏ tới `brave.exe` là mở Brave; cookie vẫn bắt qua cùng API context (không đổi phần bắt cookie).

## 2. Phạm vi

- **Làm:**
  - Thêm `BrowserLocator` (Core): dò đường dẫn Brave đa nền tảng (Windows/Linux/macOS), hàm lõi thuần → test được.
  - Sửa `ShopeeLoginService`: `OpenAsync` set `ExecutablePath` = Brave nếu tìm thấy; `EnsureBrowserInstalled` **bỏ qua tải Chromium** khi đã có Brave (đỡ 150MB); có Brave mà mở lỗi thì **fallback** sang Chromium đóng gói (tải nếu cần) rồi thử lại.
  - Test `BrowserLocatorTests` (hàm lõi).
  - Smoke test thật xác nhận mở đúng **Brave** trên máy dev.
  - Cập nhật README.
- **Không làm:**
  - Dùng hồ sơ Brave thật/persistent profile (ngoài phạm vi, phá cô lập).
  - Thêm tùy chọn chọn trình duyệt trong màn hình Cài đặt (người dùng chọn phương án "ưu tiên Brave tự động", không cần UI chọn).
  - Đổi logic proxy/bắt cookie/CRUD.
  - Hỗ trợ trình duyệt khác ngoài Brave + Chromium đóng gói (Chrome/Edge không nằm trong yêu cầu).

## 3. Các bước thực hiện

### Bước 1 — Core: BrowserLocator (dò Brave, test được)

Tạo `src/XuLyDonShopee.Core/Services/BrowserLocator.cs`:
- Hàm lõi test được:
  ```csharp
  internal static string? FindFirstExisting(IEnumerable<string> candidates, Func<string,bool> exists)
  ```
  Trả về đường dẫn đầu tiên tồn tại (bỏ qua null/rỗng), hoặc `null`.
- API công khai:
  ```csharp
  public static string? FindBraveExecutable()
  ```
  Dựng danh sách ứng viên theo HĐH rồi gọi `FindFirstExisting(candidates, File.Exists)`:
  - **Windows** (`OperatingSystem.IsWindows()`):
    - `%ProgramFiles%\BraveSoftware\Brave-Browser\Application\brave.exe`
    - `%ProgramFiles(x86)%\BraveSoftware\Brave-Browser\Application\brave.exe`
    - `%LOCALAPPDATA%\BraveSoftware\Brave-Browser\Application\brave.exe`
  - **Linux** (`OperatingSystem.IsLinux()`):
    - `/usr/bin/brave-browser`, `/usr/bin/brave-browser-stable`, `/usr/bin/brave`,
    - `/opt/brave.com/brave/brave-browser`, `/snap/bin/brave`
  - **macOS** (`OperatingSystem.IsMacOS()`):
    - `/Applications/Brave Browser.app/Contents/MacOS/Brave Browser`
  - Env var rỗng thì bỏ ứng viên đó (không ném). Trả `null` nếu không thấy.

### Bước 2 — Core: ShopeeLoginService dùng Brave + fallback

Sửa `src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs`:

1. **`EnsureBrowserInstalled()`** — bỏ qua tải khi đã có Brave:
   ```csharp
   public int EnsureBrowserInstalled()
   {
       // Có Brave → không cần tải Chromium đóng gói.
       if (BrowserLocator.FindBraveExecutable() != null) return 0;
       try { return Microsoft.Playwright.Program.Main(new[] { "install", "chromium" }); }
       catch { return -1; }
   }
   ```
2. **`OpenAsync`** — mở Brave nếu có, kèm fallback:
   - Tách phần mở thành helper nội bộ:
     ```csharp
     private async Task<(IPlaywright, IBrowser, IBrowserContext)> LaunchAsync(string? executablePath, ProxyEntry? proxy)
     ```
     Tạo playwright, `Chromium.LaunchAsync(new BrowserTypeLaunchOptions{ Headless=false, ExecutablePath = executablePath })` (null = Chromium đóng gói), tạo context với proxy + `IgnoreHTTPSErrors=true`. Dọn tài nguyên nếu lỗi rồi ném.
   - Trong `OpenAsync`:
     - `var brave = BrowserLocator.FindBraveExecutable();`
     - Nếu `brave != null`: thử `LaunchAsync(brave, proxy)`; nếu ném (Brave phiên bản không tương thích Playwright, v.v.) → **fallback**: `EnsureChromiumInstalledForFallback()` (gọi `Program.Main(["install","chromium"])`, không phụ thuộc kết quả BrowserLocator), rồi `LaunchAsync(null, proxy)`.
     - Nếu `brave == null`: `LaunchAsync(null, proxy)` (Chromium đóng gói — đã được `EnsureBrowserInstalled` tải trước ở tầng gọi).
     - Sau khi có browser/context: mở page, `GotoAsync(SellerUrl, ...)` (giữ nguyên, nuốt lỗi điều hướng), trả `LoginSession`.
   - Bọc toàn bộ trong try/catch tổng như hiện tại: lỗi cuối cùng → ném `InvalidOperationException` message tiếng Việt.
3. (Tùy chọn, giúp nghiệm thu) thêm `public static string DescribeBrowser()` trả `"Brave (<path>)"` hoặc `"Chromium đóng gói của Playwright"` để log/kiểm tra — không bắt buộc.
4. Giữ nguyên `CaptureCookiesJsonAsync`, `ILoginSession`, `DisposeAsync`, và luồng `OpenSellerAsync` trong ViewModel (không cần đổi ViewModel — `EnsureBrowserInstalled` + `OpenAsync` tự lo chọn browser).

### Bước 3 — Test

Thêm `src/XuLyDonShopee.Tests/BrowserLocatorTests.cs`:
- `FindFirstExisting`: danh sách có phần tử khớp predicate → trả đúng phần tử đầu tiên khớp; không phần tử nào khớp → `null`; bỏ qua null/chuỗi rỗng; thứ tự ưu tiên đúng (khớp phần tử sau nhưng có phần tử trước khớp → trả phần tử trước).
- (Không test `FindBraveExecutable` phụ thuộc máy thật — chỉ test hàm lõi.)

### Bước 4 — Smoke test thật & README

1. **Smoke test (Opus tự chạy, không đưa vào suite):** trên máy dev có Brave — chạy `ShopeeLoginService.OpenAsync(null)` và xác nhận trình duyệt mở ra **là Brave** (bằng chứng cụ thể, chọn cách khả thi):
   - kiểm `EnsureBrowserInstalled()` trả 0 **ngay lập tức, không tải gì** (vì có Brave); và/hoặc
   - sau khi mở, tìm tiến trình con `brave.exe` đang chạy, hoặc log `DescribeBrowser()` in ra đường dẫn Brave; và
   - `CaptureCookiesJsonAsync()` sau khi vào banhang.shopee.vn trả về > 0 cookie, đóng sạch.
   Báo cáo bằng chứng thật (tiến trình/đường dẫn/số cookie). Nếu môi trường không mở được cửa sổ, ghi rõ lý do.
2. **README:** cập nhật: app ưu tiên mở **Brave** nếu đã cài (không phải tải 150MB); nếu không có Brave sẽ tự tải & dùng Chromium đóng gói; nếu Brave mở lỗi sẽ fallback Chromium.

## 4. Tiêu chí nghiệm thu

- [ ] `dotnet build XuLyDonShopee.sln` — 0 error.
- [ ] `dotnet test` — toàn bộ pass (gồm `BrowserLocatorTests`); các test cũ vẫn xanh.
- [ ] Trên máy dev (có Brave): smoke test chứng minh mở đúng **Brave** (tiến trình `brave.exe` / đường dẫn Brave) và bắt được cookie; `EnsureBrowserInstalled()` không tải Chromium khi đã có Brave.
- [ ] Khi không có Brave (mô phỏng qua unit test của `FindFirstExisting` trả null): logic rơi về nhánh Chromium đóng gói (đọc code xác nhận; không cần gỡ Brave thật).
- [ ] Không đổi hành vi bắt cookie/proxy/CRUD; `OpenSellerAsync` không cần sửa (hoặc sửa tối thiểu, giải thích nếu có).

## 5. Rủi ro & lưu ý

- **Brave không chính thức với Playwright:** Brave tự cập nhật, có thể lên phiên bản Playwright chưa hỗ trợ → `LaunchAsync` ném. Đã có **fallback** sang Chromium đóng gói để không kẹt; ghi rõ hành vi fallback trong README.
- **Brave Shields** có thể chặn vài request; đăng nhập Shopee thường vẫn được. Không tắt Shields ở plan này (phiên sạch dùng cấu hình mặc định Brave).
- **ExecutablePath = null** nghĩa là Playwright dùng Chromium đóng gói của nó — phải đảm bảo đã cài (nhánh không-Brave đã gọi `EnsureBrowserInstalled` tải trước; nhánh fallback tự cài trong `OpenAsync`).
- Phiên vẫn **sạch, cô lập** (Playwright tạo user-data-dir tạm riêng mỗi lần) — đúng yêu cầu, không đụng profile Brave thật.
- Máy dev Windows (PowerShell 5.1). Trước khi build phải đảm bảo không còn tiến trình `XuLyDonShopee.App` đang mở (khóa `Core.dll`). Gốc repo: `d:\Projects\Xu-ly-don-shopee`.

---

## Báo cáo thực thi (Opus điền sau khi xong)

**Ngày thực thi:** 2026-07-13 · **Người thực thi:** Opus (`opus-executor`)

### Files đã tạo/sửa

**Tạo mới (Core):**
- `src/XuLyDonShopee.Core/Services/BrowserLocator.cs` — `public static string? FindBraveExecutable()` (dò Brave theo HĐH Windows/Linux/macOS) + hàm lõi thuần `internal static string? FindFirstExisting(IEnumerable<string>, Func<string,bool>)`.

**Tạo mới (Tests):**
- `src/XuLyDonShopee.Tests/BrowserLocatorTests.cs` — 5 test cho `FindFirstExisting` (khớp phần tử đầu; không khớp → null; bỏ qua null/rỗng/khoảng trắng; ưu tiên phần tử trước khi nhiều phần tử khớp; danh sách rỗng → null).

**Sửa:**
- `src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs`:
  - `EnsureBrowserInstalled()` — nếu có Brave thì `return 0` ngay, KHÔNG tải Chromium.
  - `OpenAsync()` — tìm Brave; có Brave → `LaunchAsync(brave)`; nếu ném → fallback `EnsureChromiumInstalledForFallback()` + `LaunchAsync(null)`; không có Brave → `LaunchAsync(null)`. Try/catch tổng dọn tài nguyên + ném `InvalidOperationException` tiếng Việt.
  - Thêm helper `private static Task<(IPlaywright,IBrowser,IBrowserContext)> LaunchAsync(string? executablePath, ProxyEntry? proxy)` (set `ExecutablePath`; null = Chromium đóng gói; tự dọn tài nguyên nếu lỗi rồi ném lại).
  - Thêm `private static void EnsureChromiumInstalledForFallback()`.
  - Thêm `public static string DescribeBrowser()` (log/kiểm tra: `"Brave (<path>)"` hoặc `"Chromium đóng gói của Playwright"`).
  - Giữ nguyên `ILoginSession`, `LoginSession`, `CaptureCookiesJsonAsync`, `DisposeAsync`.
- `src/XuLyDonShopee.Core/XuLyDonShopee.Core.csproj` — thêm `<InternalsVisibleTo Include="XuLyDonShopee.Tests" />` để test hàm `internal FindFirstExisting`.
- `README.md` — mô tả ưu tiên Brave (khỏi tải 150MB), fallback Chromium, phiên trắng cô lập.

**Không đụng tới:** `PlaywrightProxyMapper`, `StoredCookie`/`CookieJson`, `AccountsViewModel` (`OpenSellerAsync` không cần sửa — `EnsureBrowserInstalled` + `OpenAsync` tự lo chọn browser), phần proxy/bắt cookie/CRUD.

### Kết quả kiểm chứng (số liệu thật)

- **`dotnet build XuLyDonShopee.sln -c Debug`:** Build succeeded — **0 Warning, 0 Error** (đã dừng tiến trình `XuLyDonShopee.App` PID 31196 trước khi build để không khóa `Core.dll`).
- **`dotnet test XuLyDonShopee.sln --no-build`:** **Passed! Failed: 0, Passed: 73, Skipped: 0, Total: 73** (256 ms). Tăng 68 → 73 (thêm 5 test `BrowserLocatorTests`); test cũ vẫn xanh.
- **Smoke test Bước 4 — CHẠY THẬT, CHỨNG MINH MỞ BRAVE** (project console tạm trong scratchpad tham chiếu Core, gọi đúng `ShopeeLoginService`):
  - `BrowserLocator.FindBraveExecutable()` → `C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe`.
  - `ShopeeLoginService.DescribeBrowser()` → `Brave (C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe)`.
  - `EnsureBrowserInstalled()` → **code=0 trong 167 ms** (tức thì, KHÔNG tải gì — đúng nhánh "có Brave").
  - Tiến trình `brave.exe`: **TRƯỚC = 0 → SAU khi `OpenAsync(null)` = 10**, cả 10 PID đều trỏ `...\Brave-Browser\Application\brave.exe` (bằng chứng cửa sổ mở ra chính là Brave, không phải Chromium đóng gói).
  - `CaptureCookiesJsonAsync()` → **29 cookie**, JSON 7039 ký tự, cookie đầu `SPC_CDS @ banhang.shopee.vn`. Không ném exception.
  - Sau `DisposeAsync`: tiến trình `brave.exe` = **0** (đóng sạch, phiên cô lập).

### Đối chiếu tiêu chí nghiệm thu

- [x] Build 0 error.
- [x] `dotnet test` toàn bộ pass (73/73, gồm `BrowserLocatorTests`); test cũ vẫn xanh.
- [x] Smoke test chứng minh mở đúng **Brave** (10 tiến trình `brave.exe` đúng đường dẫn + `DescribeBrowser` in path Brave) và bắt được 29 cookie; `EnsureBrowserInstalled()` trả 0 trong 167ms không tải Chromium.
- [x] Không có Brave → rơi về Chromium đóng gói: xác nhận qua đọc code (`OpenAsync`: `if (brave != null) ... else LaunchAsync(null)`; `EnsureBrowserInstalled`: `if (FindBraveExecutable() != null) return 0; else install`) + unit test `FindFirstExisting` trả null (2 case).
- [x] Không đổi hành vi bắt cookie/proxy/CRUD; `OpenSellerAsync` (ViewModel) không sửa.

### Quyết định phát sinh

- **`InternalsVisibleTo`:** plan yêu cầu `FindFirstExisting` là `internal` nhưng phải test được → thêm `<InternalsVisibleTo Include="XuLyDonShopee.Tests" />` vào Core csproj (bổ sung tối thiểu, cần thiết để thỏa cả hai ràng buộc; không mở rộng phạm vi).
- **`DescribeBrowser()`:** hiện thực hạng mục tùy chọn ở Bước 2.3 để phục vụ nghiệm thu (in đường dẫn Brave).
- **`LaunchAsync` để `static`:** không phụ thuộc trạng thái instance nên đặt static cho gọn; không ảnh hưởng hành vi.

### Chưa làm được / hạn chế

- Không có hạng mục nào bỏ dở — mọi bước trong phạm vi plan đã hoàn thành và kiểm chứng thật.
- Chưa chạy end-to-end đăng nhập thật (cần người dùng nhập mật khẩu/OTP) — ngoài phạm vi smoke test; đường ống browser→cookie đã chứng minh chạy bằng Brave.
- Rủi ro đã nêu trong plan giữ nguyên: Brave tự cập nhật có thể vượt phiên bản Playwright hỗ trợ → đã có fallback Chromium; Brave Shields không tắt (dùng cấu hình mặc định).
