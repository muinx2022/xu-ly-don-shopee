# Plan: Mở Brave qua CDP + chống nhận diện bot Shopee (hạ tầng)

- **Ngày:** 2026-07-14
- **Trạng thái:** hoàn thành
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)
- **Ghi chú:** Đây là **Plan 1/2**. Plan 2 (`2026-07-14-tu-dang-nhap-kieu-nguoi.md`) sẽ xếp chồng: tự điền user+pass kiểu người + tự bấm đăng nhập. Plan này CHỈ làm hạ tầng CDP + stealth + proxy.
- **Nghiệm thu:** Fable tự chạy build (0 error) + test (104/104) + panel rà soát đối kháng 3 góc; panel bắt 3 lỗi thật (treo nền, mất cookie phút chót, stealth tự lộ) — đã sửa & kiểm chứng lại. Hạn chế: vòng poll không có unit test riêng (kiểm bằng đọc code + smoke); proxy IP/auth chưa test với proxy sống; nhánh Chromium fallback chưa smoke.

## 1. Bối cảnh & mục tiêu

Tính năng "Mở trang bán hàng" hiện dùng `ShopeeLoginService.OpenAsync(userDataDir, proxy)` → `LaunchPersistentContextAsync` (Playwright tự khởi chạy Brave). Vấn đề người dùng gặp:
- Playwright tự launch → thêm cờ `--enable-automation` → hiện thanh **"Brave is being controlled by automated test software"** VÀ đặt `navigator.webdriver = true`.
- **Shopee nhận diện bot** (mở lần 2 đã bị dính). `navigator.webdriver` là dấu hiệu bị check đầu tiên/rẻ nhất.

**Quyết định đã chốt với người dùng:**
- **Chuyển hẳn sang CDP:** app tự chạy **Brave thật** (`--remote-debugging-port` + `--user-data-dir` riêng từng tài khoản + `--proxy-server` + cờ stealth), rồi Playwright nối vào qua `ConnectOverCDPAsync`. Brave chạy như trình duyệt người dùng bình thường (không cờ automation).
- **Chống nhận diện bot (best-effort):** cờ launch stealth + init script vá các dấu hiệu (`navigator.webdriver`, plugins, languages, window.chrome, WebGL...). **Không hứa 100%** né được Shopee (anti-bot còn dò CDP, fingerprint, hành vi, IP) — làm best-effort, chỉnh dần.
- **Proxy auth qua CDP:** proxy đặt qua `--proxy-server`; proxy có user:pass thì xử lý auth qua CDP (`Fetch.authRequired`) — không hiện hộp thoại đăng nhập proxy.
- **Giữ IP lâu nhất, KHÔNG xoay liên tục:** luôn ưu tiên `/current` (proxy sticky đang gán key); chỉ gọi `/new` khi không có current; tránh ép rotation.
- **Giữ nguyên:** persistent profile theo tài khoản, tự bắt & lưu cookie (poll + gate cookie đăng nhập), kiểm proxy KiotProxy còn sống, KHÔNG hộp thoại hỏi lưu.

### Hiện trạng code liên quan (đã khảo sát)

- [ShopeeLoginService.cs](../src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs): `OpenAsync(string userDataDir, ProxyEntry? proxy, ct)` → `LaunchPersistentAsync(exePath, userDataDir, proxy)` (dùng `LaunchPersistentContextAsync`, hiện có thêm `IgnoreDefaultArgs = ["--enable-automation"]` — sẽ **bỏ**, thay bằng CDP). `interface ILoginSession { Task<string> CaptureCookiesJsonAsync(); Task Closed; bool IsClosed; }` + class `LoginSession` (giữ IPlaywright + IBrowserContext, sự kiện `context.Close`). `EnsureBrowserInstalled`, `DescribeBrowser`, `BrowserLocator.FindBraveExecutable()`, `EnsureChromiumInstalledForFallback()`.
- [AccountsViewModel.cs](../src/XuLyDonShopee.App/ViewModels/AccountsViewModel.cs) `OpenSellerAsync`: tính `userDataDir` qua `BrowserProfilePaths.ForAccount`, chọn proxy (`NextManualProxy` cho thủ công / `ProxySelector.SelectKiotProxyAsync` cho KiotProxy), gọi `_loginService.OpenAsync(userDataDir, proxy)`, rồi vòng lặp poll bắt cookie (`ShopeeLoginCookies.IsLoggedIn` gate) → `SaveCapturedCookie`. **Giữ nguyên** — chữ ký `OpenAsync(userDataDir, proxy)` KHÔNG đổi.
- [ProxySelector.cs](../src/XuLyDonShopee.Core/Services/ProxySelector.cs): `SelectKiotProxyAsync(kiot, checker)` = `GetCurrentProxyAsync() ?? GetNewProxyAsync()` rồi kiểm kết nối. Đã ưu tiên current (sticky).
- [ProxyHealthChecker.cs](../src/XuLyDonShopee.Core/Services/ProxyHealthChecker.cs): `ToProxyAddress(ProxyEntry)` → `http://h:p` / `socks5://h:p` (tái dùng cho `--proxy-server`).
- [PlaywrightProxyMapper.cs](../src/XuLyDonShopee.Core/Services/PlaywrightProxyMapper.cs): không dùng cho CDP nữa (proxy giờ qua `--proxy-server`), nhưng GIỮ (không xóa).
- `Microsoft.Playwright` đã có; `ConnectOverCDPAsync`, `IBrowser`, `IBrowserContext`, `NewCDPSessionAsync` đều có trong Playwright .NET.

## 2. Phạm vi

- **Làm:**
  - `ShopeeLoginService.OpenAsync` mở Brave thật qua CDP (thay `LaunchPersistentContextAsync`): tự launch process Brave (hoặc Chromium đóng gói fallback) với cờ stealth + `--user-data-dir` + `--remote-debugging-port` + `--proxy-server`, chờ CDP sẵn sàng, `ConnectOverCDPAsync`, thêm init script stealth, xử lý auth proxy qua CDP.
  - Hàm thuần `BuildBraveArgs(...)` (test được).
  - `LoginSession` quản lý vòng đời: sở hữu process Brave → `Closed` hoàn tất khi process thoát; `DisposeAsync` ngắt CDP + kill process.
  - Init script stealth (hằng JS) + cờ launch stealth.
  - Proxy: `--proxy-server` từ proxy đã chọn; auth qua CDP nếu có user:pass. Giữ IP lâu (không ép `/new`).
  - Test hàm thuần + smoke test thật (navigator.webdriver=false, không banner, profile bền, proxy áp dụng).
  - Cập nhật README.
- **Không làm (để Plan 2):** tự điền user/pass, gõ kiểu người, tự bấm đăng nhập.
- **Không làm:** giải captcha/OTP (người dùng tự làm); ghim proxy riêng theo từng tài khoản (giữ như hiện tại); đổi luồng cookie/CRUD.

## 3. Các bước thực hiện

### Bước 1 — Hàm thuần dựng tham số launch Brave (test được)

Trong `ShopeeLoginService` (hoặc file mới `BraveLaunchArgs.cs`):
```csharp
public static IReadOnlyList<string> BuildBraveArgs(string userDataDir, int remoteDebuggingPort, ProxyEntry? proxy)
```
Trả về danh sách tham số dòng lệnh:
- `--remote-debugging-port={remoteDebuggingPort}`
- `--user-data-dir={userDataDir}`
- **Stealth:** `--disable-blink-features=AutomationControlled`
- `--no-first-run`, `--no-default-browser-check`, `--disable-features=Translate,AutomationControlled`
- `--start-maximized`
- Nếu `proxy != null`: `--proxy-server={ProxyHealthChecker.ToProxyAddress(proxy)}` (http/socks5 theo Type).
- **KHÔNG** thêm `--enable-automation`, **KHÔNG** `--headless`, **KHÔNG** `--remote-debugging-pipe`.
- (Không nhét user:pass vào `--proxy-server` — Chromium không hỗ trợ; auth xử lý qua CDP ở Bước 4.)

**Test** (`BraveLaunchArgsTests`): chứa `--disable-blink-features=AutomationControlled`, `--user-data-dir=...`, `--remote-debugging-port=...`; KHÔNG chứa `--enable-automation`/`--headless`; có proxy → chứa `--proxy-server=http://...`; proxy null → KHÔNG có `--proxy-server`.

### Bước 2 — Launch process Brave + chờ CDP sẵn sàng

Trong `ShopeeLoginService`:
1. Xác định exe: `var exe = BrowserLocator.FindBraveExecutable();` nếu null → dùng Chromium đóng gói của Playwright (`playwright.Chromium.ExecutablePath` sau khi `Playwright.CreateAsync()`), sau khi `EnsureBrowserInstalled()` đã tải. (Nhánh fallback vẫn chạy qua CDP như Brave.)
2. Chọn cổng debug: dùng `--remote-debugging-port=0` rồi đọc cổng thật từ file `{userDataDir}/DevToolsActivePort` (dòng đầu là cổng) — cách chuẩn của Chromium, tránh đụng cổng. Poll file này tối đa ~15s.
   - (Phương án thay thế nếu đọc file khó: tự tìm cổng trống bằng `TcpListener(IPAddress.Loopback, 0)` → lấy port → đóng → truyền vào `--remote-debugging-port={port}`. Chấp nhận race nhỏ.)
3. `Process.Start(new ProcessStartInfo(exe) { ArgumentList = { ...BuildBraveArgs... }, UseShellExecute = false })`.
4. Chờ endpoint CDP sẵn sàng: poll `GET http://127.0.0.1:{port}/json/version` tới khi 200 (timeout ~15s). Lỗi/timeout → kill process, ném `InvalidOperationException` tiếng Việt.

### Bước 3 — Kết nối CDP + init script stealth

1. `var browser = await playwright.Chromium.ConnectOverCDPAsync($"http://127.0.0.1:{port}");`
2. `var context = browser.Contexts.Count > 0 ? browser.Contexts[0] : await browser.NewContextAsync();` (Brave chạy `--user-data-dir` → có sẵn context mặc định = profile persistent).
3. **Init script stealth** — thêm TRƯỚC khi điều hướng: `await context.AddInitScriptAsync(StealthJs);` với `StealthJs` (hằng) vá:
   - `Object.defineProperty(navigator,'webdriver',{get:()=>false});`
   - `navigator.languages` = `['vi-VN','vi','en-US','en']`;
   - `window.chrome = window.chrome || { runtime: {} };`
   - `navigator.plugins`/`mimeTypes` có phần tử giả (length>0);
   - patch `navigator.permissions.query` cho `notifications` trả `denied` không lộ;
   - (tùy chọn) spoof WebGL `getParameter` vendor/renderer.
   Ghi chú: Brave thật đã đặt `navigator.webdriver=false` sẵn (không có cờ automation); init script là lớp bảo hiểm + vá các tell khác.
4. Lấy page: `var page = context.Pages.Count > 0 ? context.Pages[0] : await context.NewPageAsync();` → `GotoAsync(SellerUrl, {Timeout=60000, WaitUntil=DOMContentLoaded})` (nuốt lỗi điều hướng).

### Bước 4 — Auth proxy qua CDP (khi proxy có user:pass)

Chỉ khi `proxy?.Username` không rỗng:
- Tạo CDP session: `var cdp = await context.NewCDPSessionAsync(page);`
- `await cdp.SendAsync("Fetch.enable", { handleAuthRequests: true });`
- Bắt sự kiện `Fetch.authRequired` → nếu `authChallenge.source == "Proxy"` → `Fetch.continueWithAuth` với `authChallengeResponse = { response: "ProvideCredentials", username, password }`.
- Bắt `Fetch.requestPaused` → `Fetch.continueRequest` (để không chặn request thường).
- (KiotProxy không auth → bỏ qua nhánh này.) Nếu Playwright .NET CDP event API khác dự kiến, Opus tự dùng đúng API `ICDPSession` và ghi rõ ở "Quyết định phát sinh". Auth proxy là phần khó nhất — nếu không có proxy-auth thật để test thì ghi rõ; KiotProxy (không auth) vẫn phải chạy.

### Bước 5 — Vòng đời phiên (`LoginSession`) sở hữu process

Sửa `LoginSession` để giữ: `IPlaywright`, `IBrowser` (CDP), `IBrowserContext`, `Process braveProcess`.
- `Closed`: hoàn tất `TaskCompletionSource` khi **bất kỳ**: `braveProcess.Exited` (người dùng đóng cửa sổ → process thoát), `browser.Disconnected`, hoặc `context.Close`. (`braveProcess.EnableRaisingEvents = true`.)
- `IsClosed` như cũ.
- `CaptureCookiesJsonAsync`: `context.CookiesAsync()` → map `StoredCookie` → `CookieJson.Serialize` (GIỮ NGUYÊN).
- `DisposeAsync`: `try browser.CloseAsync()` (ngắt CDP), rồi nếu `!braveProcess.HasExited` → `braveProcess.Kill(entireProcessTree: true)`, rồi `playwright.Dispose()`. Bọc try/catch từng cái.

### Bước 6 — Ghép vào `OpenAsync` + giữ ViewModel nguyên

- `OpenAsync(string userDataDir, ProxyEntry? proxy, ct)` giữ CHỮ KÝ, đổi ruột: launch Brave qua CDP (Bước 2-5). Ưu tiên Brave; không có Brave → Chromium đóng gói (đã `EnsureBrowserInstalled`), cùng cơ chế CDP. Lỗi → dọn (kill process/dispose) + ném `InvalidOperationException` tiếng Việt.
- **Bỏ** `LaunchPersistentAsync` cũ (LaunchPersistentContextAsync) và dòng `IgnoreDefaultArgs` (không cần — ta không để Playwright launch nữa).
- `AccountsViewModel.OpenSellerAsync` **không đổi** (vẫn gọi `OpenAsync(userDataDir, proxy)`, vòng poll + gate cookie giữ nguyên).

### Bước 7 — Proxy: giữ IP lâu nhất

- `ProxySelector.SelectKiotProxyAsync` giữ `GetCurrentProxyAsync() ?? GetNewProxyAsync()` (đã ưu tiên current/sticky). Bổ sung chú thích: KHÔNG ép `/new` khi đã có current; chỉ `/new` khi current null.
- (Không thêm ghim proxy theo tài khoản — theo lựa chọn "giữ như hiện tại".)

### Bước 8 — Test + Smoke + README

- **Test thuần:** `BraveLaunchArgsTests` (Bước 1). Giữ toàn bộ test cũ xanh.
- **Smoke test thật (Opus tự chạy):**
  1. Launch Brave qua CDP tới `banhang.shopee.vn`, `page.EvaluateAsync<bool>("navigator.webdriver")` → **false**; kiểm KHÔNG có thanh "controlled by automated test software" (chụp UIAutomation/screenshot nếu khả thi; nếu không, xác nhận qua `navigator.webdriver=false` + không truyền `--enable-automation`).
  2. Profile bền: mở lại cùng `userDataDir` → cookie/profile còn.
  3. Proxy: nếu có proxy (KiotProxy/thủ công), `page` điều hướng tới trang show IP (vd api.ipify.org) qua proxy → IP = IP proxy, không phải IP máy.
  4. `CaptureCookiesJsonAsync` chạy không ném.
  - Ghi số liệu thật; môi trường chặn (WDAC/ISG — xem lưu ý) thì ghi rõ, không tính fail plan nếu build+test tự động đạt.
- **README:** cập nhật: mở bằng **CDP + Brave thật** (không banner automation, đỡ bị nhận diện bot); proxy qua `--proxy-server` (auth qua CDP); giữ IP ổn định; **cảnh báo:** không đảm bảo 100% né anti-bot.

## 4. Tiêu chí nghiệm thu

- [ ] `dotnet build XuLyDonShopee.sln -c Debug` → 0 error, 0 warning (dừng app trước khi build; nếu ISG chặn test host → build lại `-p:Deterministic=false`, có thể vài lần — KHÔNG phải lỗi code).
- [ ] `dotnet test` → tất cả pass (gồm `BraveLaunchArgsTests`); test cũ vẫn xanh.
- [ ] `OpenAsync` mở Brave qua **process thật + ConnectOverCDP** (không còn `LaunchPersistentContextAsync`); `BuildBraveArgs` không chứa `--enable-automation`/`--headless`.
- [ ] Smoke: `navigator.webdriver === false`, không banner automation; profile bền; proxy áp dụng (IP = proxy); cookie bắt được. (Nếu môi trường chặn cửa sổ → giải thích.)
- [ ] Vòng đời: đóng cửa sổ Brave → `Closed` hoàn tất → vòng poll thoát; `DisposeAsync` kill process không để lại tiến trình Brave mồ côi.
- [ ] ViewModel `OpenSellerAsync` không đổi hành vi (vẫn tự lưu cookie, gate `IsLoggedIn`).

## 5. Rủi ro & lưu ý

- **Không đảm bảo né được anti-bot Shopee.** CDP vẫn có thể bị dò ở tầng cao; fingerprint/hành vi/IP vẫn là yếu tố. Đây là best-effort; Plan 2 (gõ kiểu người) + giữ IP ổn định bổ trợ thêm. Ghi rõ trong README + báo cáo.
- **Sở hữu process Brave:** phải kill sạch khi dispose (`Kill(entireProcessTree:true)`) để không để Brave mồ côi giữ khóa `--user-data-dir` (mở lần sau sẽ lỗi khóa profile). Đây là điểm dễ sai nhất — test kỹ.
- **Đọc `DevToolsActivePort`:** file chỉ xuất hiện sau khi Brave khởi động xong; phải poll có timeout, đừng đọc ngay.
- **Auth proxy qua CDP:** phần khó; API `ICDPSession` của Playwright .NET cần dùng đúng. KiotProxy không auth nên luồng chính không phụ thuộc phần này; nếu không test được auth thật thì ghi rõ.
- **Brave Shields / fingerprint randomization** của Brave có thể gây fingerprint bất nhất giữa các lần — chấp nhận, dùng cấu hình Brave mặc định.
- **Khóa profile:** không mở cùng `userDataDir` hai lần đồng thời (`IsBusy` đã chặn ở ViewModel).
- **WDAC/ISG máy dev** chặn nạp DLL mới build khi test (`FileLoadException 0x800711C7`) — không phải lỗi code; build lại `-p:Deterministic=false`, có thể phải chạy vài lần cho từng DLL.
- Máy dev Windows (PowerShell 5.1); Brave tại `C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe`. Gốc repo `d:\Projects\Xu-ly-don-shopee`.

---

## Báo cáo thực thi (Opus điền sau khi xong)

**Ngày thực thi:** 2026-07-14 · **Người thực thi:** Opus (`opus-executor`)

### Files tạo/sửa

| File | Thay đổi |
|---|---|
| `src/XuLyDonShopee.Core/Services/BraveLaunchArgs.cs` | **Tạo mới.** Hàm thuần `BraveLaunchArgs.BuildBraveArgs(userDataDir, remoteDebuggingPort, proxy)` → danh sách cờ launch stealth + `--user-data-dir` + `--remote-debugging-port` + `--proxy-server`. KHÔNG có `--enable-automation`/`--headless`/`--remote-debugging-pipe`; KHÔNG nhét user:pass vào `--proxy-server`. |
| `src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs` | **Viết lại ruột.** Bỏ `LaunchPersistentAsync`/`LaunchPersistentContextAsync` + cờ `IgnoreDefaultArgs`. `OpenAsync` (giữ nguyên chữ ký) nay: chọn exe Brave→fallback Chromium đóng gói, xóa `DevToolsActivePort` cũ, `Process.Start` với `BuildBraveArgs(..., port=0, ...)`, đọc cổng thật từ `DevToolsActivePort`, chờ `/json/version`, `ConnectOverCDPAsync`, `AddInitScriptAsync(StealthJs)`, auth proxy qua CDP nếu có user:pass, rồi `GotoAsync(SellerUrl)`. Thêm hằng `StealthJs`, helper `WaitForDevToolsPortAsync`/`WaitForCdpEndpointAsync`/`SetupProxyAuthAsync`. `LoginSession` giờ sở hữu `Process` — `Closed` hoàn tất khi `process.Exited`/`browser.Disconnected`/`context.Close`; `DisposeAsync` ngắt CDP rồi `Kill(entireProcessTree:true)`. |
| `src/XuLyDonShopee.Core/Services/ProxySelector.cs` | Thêm chú thích Bước 7 (giữ IP lâu: ưu tiên `/current`, chỉ `/new` khi current null). Logic không đổi. |
| `src/XuLyDonShopee.Tests/BraveLaunchArgsTests.cs` | **Tạo mới.** 6 test cho `BuildBraveArgs`. |
| `README.md` | Mô tả cơ chế CDP + Brave thật + stealth (không banner, `navigator.webdriver=false`), proxy qua `--proxy-server` (auth qua CDP), giữ IP ổn định, **cảnh báo không đảm bảo 100% né anti-bot**. |

*Giữ nguyên (không đổi):* `AccountsViewModel.OpenSellerAsync` (vòng poll cookie + gate `ShopeeLoginCookies.IsLoggedIn`), `PlaywrightProxyMapper.cs` (giữ, không dùng cho CDP), chữ ký `OpenAsync(userDataDir, proxy)`.

### Số liệu kiểm chứng thật

- **Build:** `dotnet build XuLyDonShopee.sln -c Debug` → **Build succeeded, 0 Warning, 0 Error** (Time 00:00:04.35). Đã dừng `XuLyDonShopee.App` trước khi build. KHÔNG gặp WDAC/ISG lần này (không cần `-p:Deterministic=false`).
- **Test:** `dotnet test XuLyDonShopee.sln -c Debug` → **Passed! Failed: 0, Passed: 103, Skipped: 0, Total: 103** (2 s). = 97 test cũ + 6 `BraveLaunchArgsTests`. Không có `FileLoadException 0x800711C7`.
- **Smoke test thật** (harness ngoài repo, gọi `OpenAsync` thật + nối CDP client thứ 2 quan sát trang; Brave `C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe`):
  - `DescribeBrowser` = `Brave (...)`; `OpenAsync` trả về sau ~1.7 s, sinh 7 tiến trình Brave.
  - `navigator.webdriver` = **False** ✓
  - `navigator.plugins.length` = **5**, `navigator.languages[0]` = **vi-VN**, `window.chrome` = **True** → init script stealth chạy đúng ✓
  - `userAgent` KHÔNG chứa `HeadlessChrome` (không headless, không cờ automation) ✓
  - `CaptureCookiesJsonAsync()` không ném, trả JSON 2152 ký tự (shopee đã set cookie thật → điều hướng banhang.shopee.vn có thật) ✓
  - Profile bền: thư mục `Default` tồn tại trong `userDataDir` sau khi chạy ✓
  - `DisposeAsync` → **0 tiến trình Brave** còn lại (kill cả cây, không mồ côi) ✓
  - **Đường đóng cửa sổ:** kill cây Brave từ ngoài (mô phỏng người dùng đóng) → `session.Closed` hoàn tất trong <8 s, `IsClosed=True` → vòng poll ViewModel sẽ thoát đúng ✓

### Đối chiếu tiêu chí nghiệm thu

- [x] Build 0 error/0 warning (dừng app trước; không cần `-p:Deterministic=false`).
- [x] `dotnet test` toàn bộ pass (103/103, gồm `BraveLaunchArgsTests`).
- [x] `OpenAsync` mở Brave qua **process thật + ConnectOverCDP** (không còn `LaunchPersistentContextAsync`); `BuildBraveArgs` không chứa `--enable-automation`/`--headless`.
- [x] Smoke: `navigator.webdriver===false`, không cờ automation, không headless; profile bền; cookie bắt được. (Proxy IP: xem Hạn chế.)
- [x] Vòng đời: đóng cửa sổ → `Closed` hoàn tất; `DisposeAsync` kill sạch, không Brave mồ côi.
- [x] ViewModel `OpenSellerAsync` không đổi (vẫn tự lưu cookie, gate `IsLoggedIn`).

### Quyết định phát sinh

1. **Cổng CDP:** dùng phương án chính của plan — `--remote-debugging-port=0` rồi đọc `DevToolsActivePort` (dòng đầu). Có **xóa file cũ trước launch** để không đọc nhầm cổng phiên trước; nếu tiến trình thoát sớm khi poll → ném lỗi tiếng Việt gợi ý "hồ sơ đang bị khóa". Không dùng phương án TcpListener.
2. **Bỏ runtime-fallback Brave→Chromium khi Brave lỗi:** theo Bước 6 ("không có Brave → Chromium; Lỗi → dọn + ném"). Vẫn giữ nhánh "không có Brave → Chromium đóng gói". Nếu có Brave nhưng launch/CDP lỗi thì kill + ném `InvalidOperationException` (khác hành vi cũ vốn tự fallback sang Chromium).
3. **API CDP Playwright .NET 1.49** (đã tra bằng reflection): `ICDPSession.Event("Fetch.authRequired").OnEvent += (sender, JsonElement? e) => …`; `SendAsync(method, Dictionary<string,object>)` trả `Task<JsonElement?>`. Handler event là đồng bộ nên gửi lệnh CDP kiểu fire-and-forget (`_ = SafeSendAsync(...)`), nuốt lỗi khi session ngắt.
4. **Init script** vá cả `Object.getPrototypeOf(navigator).webdriver` (ngoài `navigator.webdriver`) vì một số bản Chromium đặt getter trên prototype.
5. **Auth proxy:** khi `handleAuthRequests:true`, Fetch chặn **mọi** request → xử lý cả `Fetch.requestPaused` (`continueRequest`) lẫn `Fetch.authRequired` (`continueWithAuth` ProvideCredentials khi source=="Proxy") để không treo request thường.

### Hạn chế / chưa kiểm chứng

- **Proxy IP thật + auth proxy qua CDP:** môi trường không có proxy sống / không có KiotProxy key, nên **chưa** chạy được kiểm "IP = IP proxy" và **chưa** xác minh `Fetch.authRequired` với proxy có user:pass thật. Đã unit-test `--proxy-server=http/socks5` (có/không user:pass) và code auth qua CDP đã viết theo đúng API `ICDPSession`; cần proxy auth thật để nghiệm thu đầy đủ (đúng như plan cho phép ghi rõ). KiotProxy không auth nên luồng chính không phụ thuộc phần này.
- **Banner automation:** xác nhận gián tiếp qua `navigator.webdriver=false` + không truyền `--enable-automation` + userAgent không `HeadlessChrome` (không chụp UIAutomation).
- **Nền tảng:** smoke chỉ chạy trên Windows với Brave thật; nhánh Chromium-đóng-gói fallback chưa smoke (chỉ dùng khi máy không có Brave).
- Theo plan: **không đảm bảo 100% né anti-bot Shopee** (còn CDP/fingerprint/hành vi/IP). Đây là hạ tầng best-effort; Plan 2 (gõ kiểu người) bổ trợ.

---

## Vòng 2 — Sửa 3 lỗi từ panel rà soát đối kháng (2026-07-14)

Hướng sửa chung: **không dựa vào "tiến trình Brave chết" làm tín hiệu kết thúc, mà dựa vào "không còn cửa sổ (Pages)"** — vì Brave có thể chạy nền sau khi đóng cửa sổ.

### LỖI 1 [major] — Vòng poll treo vô hạn nếu Brave chạy nền → ĐÃ SỬA
- Thêm `int OpenPageCount { get; }` vào `ILoginSession`; `LoginSession` trả `_context.Pages.Count` (try/catch → 0 nếu context ngắt). File: `ShopeeLoginService.cs`.
- `AccountsViewModel.OpenSellerAsync`: vòng poll đổi thành `while (!session.IsClosed && DateTime.UtcNow < deadline)` với `deadline = UtcNow + 15 phút` (chốt chặn cứng, không bao giờ treo vĩnh viễn). Tín hiệu đóng chính = `OpenPageCount == 0` ở **2 vòng poll LIÊN TIẾP** (`zeroPageStreak >= 2` mới `break`) để tránh thoát nhầm lúc Pages chớp 0 khi chuyển trang. `await using` vẫn dispose → `Kill(entireProcessTree)` dọn cả Brave nền.

### LỖI 2 [major] — Mất cookie + báo "Chưa thấy" sai khi đóng ngay sau đăng nhập → ĐÃ SỬA
- Thêm **lần bắt cookie CHỐT** sau vòng lặp, TRƯỚC khi ra khỏi `await using` (try/catch): khi thoát vì hết cửa sổ, browser thường VẪN sống → bắt được cookie đăng nhập phút chót.
- Sửa **thông báo** trung thực: nếu không lưu được → *"Chưa lưu được cookie vào tài khoản. Nếu bạn đã đăng nhập, phiên vẫn được giữ trong hồ sơ trình duyệt (lần sau mở lại vẫn còn đăng nhập)."* (KHÔNG còn khẳng định "chưa đăng nhập").
- Sửa **BusyStatus**: *"Đã mở trình duyệt. Hãy đăng nhập — cookie sẽ tự lưu; xong thì đóng cửa sổ."* (bỏ nhấn mạnh "ĐÓNG cửa sổ để lưu").

### LỖI 3 [minor, quan trọng với chống bot] — StealthJs tự lộ là bot → ĐÃ SỬA
- **Bỏ hẳn `AddInitScriptAsync` + hằng `StealthJs`.** Đã gỡ toàn bộ vá tự lộ: ghi đè `navigator.plugins`/`mimeTypes` (plugin giả không phải `Plugin`), hook `WebGLRenderingContext.getParameter` + `navigator.permissions.query` (mất `"[native code]"`), `Object.defineProperty(navigator,'webdriver')` (tạo own-property = tell), `window.chrome=…`. Brave thật vốn đã sạch.
- Locale VN chuyển sang cờ `--lang=vi-VN` trong `BuildBraveArgs` (thêm test `CoCoLocaleTiengViet`).
- Cập nhật XML-doc của `ShopeeLoginService` cho khớp (không còn nói "init script vá plugins/WebGL").

### Nghiệm thu lại — số liệu thật
- **Build:** `dotnet build XuLyDonShopee.sln -c Debug` → **0 Warning, 0 Error** (đã dừng `XuLyDonShopee.App` + `brave` trước build; không dính WDAC/ISG).
- **Test:** `dotnet test` → **104 pass / 0 fail** (trước sửa: 103; +1 = `BraveLaunchArgsTests.CoCoLocaleTiengViet`). Test cũ vẫn xanh.
- **Smoke Brave thật (code mới):** `navigator.webdriver=False`; `navigator.hasOwnProperty('webdriver')=**False**` (không còn own-property); `navigator.languages[0]=vi-VN` (từ `--lang`, không hook JS); `navigator.plugins[0] instanceof Plugin = **True**` (plugin thật); `navigator.permissions.query` là **native** (`[native code]`, không bị hook); đóng cửa sổ → vòng poll thoát; `DisposeAsync` → **0 Brave mồ côi**.
- **Kiểm thuật toán vòng poll (tất định, `FakeSession` điều khiển `OpenPageCount`, sao đúng vòng của ViewModel):**
  - KB1 (Brave chạy nền, Pages 1,1,0,0): thoát vì **`OpenPageCount==0 x2`** — KHÔNG treo.
  - KB1b (đăng nhập PHÚT CHÓT, browser còn sống): loop không kịp lưu (`savedInLoop=False`) nhưng **lần bắt cookie CHỐT lưu được** (`savedFinal=True`) — đúng mục tiêu LỖI 2.
  - KB2 (browser chết khi đóng): thoát vì `CaptureThrow` — KHÔNG treo.
  - Real smoke cũng xác nhận **DEADLINE 15 phút** là chốt chặn cứng (loop thoát ở deadline khi Pages kẹt >0, rồi dispose kill sạch).

### Hạn chế còn lại (vòng 2)
- Nhánh thoát `OpenPageCount==0 x2` khó tái hiện bằng Brave thật vì Chromium tự thoát khi đóng cửa sổ cuối (khi đó thoát qua `IsClosed`/`CaptureThrow`); nhánh Pages==0 phục vụ đúng ca "Brave chạy nền" — đã chứng minh tất định bằng `FakeSession` (vòng poll trong `OpenSellerAsync` gắn `DialogService`/services nên không unit-isolate được nếu không tách hàm; giữ inline đúng như chỉ dẫn).
- Proxy IP thật / auth proxy qua CDP: vẫn chưa có proxy sống để nghiệm thu (như vòng 1).
