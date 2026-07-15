# Plan: Nút "Kiểm tra" — về trang chủ Seller rồi kiểm tra đơn (Chờ Lấy Hàng) ngay

- **Ngày:** 2026-07-15
- **Trạng thái:** đang làm
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)

## 1. Bối cảnh & mục tiêu

**Yêu cầu người dùng:** thêm nút **"Kiểm tra"** vào hàng nút hành động của form chi tiết (ở khoảng
trống bên trái nút "Dừng" — trong ảnh là ô đỏ). Bấm nút → **điều hướng cửa sổ Brave của phiên về
trang chủ Seller** rồi **kiểm tra đơn ngay** (đọc số "Chờ Lấy Hàng" tại chỗ, không đợi chu kỳ 30').

Đây là kích hoạt **thủ công, ngay lập tức** của việc theo dõi đơn vốn đang chạy tự động mỗi 30'.

### Hiện trạng code (đã khảo sát 15/7, cây SẠCH sau commit `ff90e68`)

- **Hàng nút hành động** — `src/XuLyDonShopee.App/Views/AccountsView.axaml` (~dòng 141, vừa đưa lên
  trên header): `Grid ColumnDefinitions="Auto,Auto,*,Auto,Auto,Auto"`: Lưu(0), Hủy(1), spacer `*`(2),
  **Dừng(3)**, Xử lý đơn(4), Mở trang bán hàng(5). Ô đỏ nằm ở khoảng trống ngay TRÁI nút "Dừng" → nút
  mới đặt thành cột trước "Dừng".
- **ViewModel** — `src/XuLyDonShopee.App/ViewModels/AccountsViewModel.cs`:
  - Mẫu computed + command: `CanProcessOrders` / `ProcessOrdersCommand`; `CanStopSeller`; raise lại tại
    `OnIsEditingChanged`, `OnIsNewChanged`, `UpdateSelectedSessionStatus()`.
  - `_services.Sessions.Get(id)` trả `IAccountSession?`; `UpdateSelectedSessionStatus` đổ
    `OrderStatus = FormatOrderStatus(session?.ToShipCount)` (dòng "Chờ Lấy Hàng: N — kiểm lại sau 30'")
    và `BusyStatus = session?.StatusText`.
- **Phiên** — `src/XuLyDonShopee.App/Services/IAccountSession.cs` + `AccountSession.cs`:
  - `AccountSession`: `volatile ILoginSession? _session`; `volatile bool _navigating` (đang điều hướng
    xử lý đơn → vòng `RunAsync` bỏ nhịp đọc đơn tránh reload phá thao tác); `ToShipCount` (int?),
    `StatusText`, `State`; token dưới `_lifecycleLock`. Mẫu `ProcessOrdersAsync()` (guard Running +
    `!_navigating`, set cờ, StatusText, finally tắt cờ).
  - Vòng `RunAsync`: nhịp đọc đơn `if (!_navigating && DateTime.UtcNow >= nextOrderCheck)` →
    `session.ReadToShipCountAsync(reload: !firstOrderCheck, ct)` → gán `ToShipCount`.
- **Playwright** — `src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs`, class lồng `LoginSession`
  (ctor private, implement `ILoginSession`):
  - `const string SellerUrl = "https://banhang.shopee.vn/";`
  - `ReadToShipCountAsync(bool reload, ct)`: gate `IsLoggedIn`; `page = _context.Pages[0]`; reload nếu
    cần rồi `FindToShipTitleAsync` + `ShopeeDashboard.ParseToShipCount`; graceful null.
  - Mẫu điều hướng: `page.GotoAsync(SellerUrl, new PageGotoOptions { WaitUntil = DOMContentLoaded,
    Timeout = 30000 })` (đã dùng ở `OpenAsync`).
- **Stub test cần cập nhật khi thêm method vào `IAccountSession`:** `StubSession` trong
  `src/XuLyDonShopee.Tests/AccountRowViewModelTests.cs` và `src/XuLyDonShopee.Tests/AccountSessionManagerTests.cs`
  (mỗi cái đang có `public Task<bool> ProcessOrdersAsync() => Task.FromResult(false);`). **KHÔNG có**
  stub cho `ILoginSession` trong test (LoginSession là class lồng private) → thêm method vào
  `ILoginSession` không phá test nào, chỉ cần `LoginSession` implement.
- Nền test hiện tại: **269** pass.

### Quyết định đã chốt

- "Trang chủ" = `SellerUrl` (`https://banhang.shopee.vn/`) — nơi có to-do box "Chờ Lấy Hàng".
- "Check đơn hàng" ở bước này = **đọc số "Chờ Lấy Hàng" ngay** (cập nhật `ToShipCount` → UI hiện
  "Chờ lấy: N"). Đây là bản thủ công của nhịp 30' hiện có, KHÔNG thêm nghiệp vụ khác.
- Bật nút khi phiên **đang chạy** (`State == Running`) — bất kể ToShipCount là mấy (kiểm tra là làm
  tươi số, cho phép cả khi đang 0/chưa có). Không có phiên chạy → tắt nút (chưa có cửa sổ để đọc).
- Điều hướng về trang chủ bằng `GotoAsync(SellerUrl)` (như luồng mở đầu `OpenAsync` — chấp nhận được,
  giống gõ URL / bấm logo Home), KÈM khoảng dừng "kiểu người" trước/sau. KHÔNG cần click phần tử.
- Dùng cờ `_navigating` bao trùm thao tác này để nhịp đọc đơn 30' không reload chồng lên giữa chừng.

## 2. Phạm vi

- **Làm:** method điều hướng trang chủ trên `ILoginSession`/`LoginSession`; method
  `CheckOrdersAsync` trên `IAccountSession`/`AccountSession`; command + enable-state + nút UI; cập nhật
  2 stub test.
- **Không làm:** không đổi nhịp 30' tự động, không đổi luồng Xử lý đơn/human-login/cookie/proxy; không
  thêm nghiệp vụ đơn khác (chỉ đọc số Chờ Lấy Hàng); KHÔNG tự commit.

## 3. Các bước thực hiện

### Bước 1 — `ILoginSession.GoToSellerHomeAsync` (Core, điều hướng trang chủ)

`src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs`:

- Interface `ILoginSession`: thêm
  `Task GoToSellerHomeAsync(CancellationToken ct = default);`
  XML-doc: điều hướng tab hiện tại về trang chủ Seller (`SellerUrl`) để chuẩn bị đọc lại to-do box;
  best-effort, nuốt lỗi điều hướng (không ném).
- `LoginSession` implement:
  1. `page = _context.Pages.Count > 0 ? _context.Pages[0] : null`; null → return.
  2. `var rng = new Random();` dừng "kiểu người" `Task.Delay(rng.Next(600, 1800), ct)` trước.
  3. `try { await page.GotoAsync(SellerUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30000 }).ConfigureAwait(false); } catch { /* nuốt lỗi điều hướng */ }`
  4. Dừng "đọc trang" `Task.Delay(rng.Next(800, 2000), ct)` sau (để to-do box render).

### Bước 2 — `IAccountSession.CheckOrdersAsync` (App)

`src/XuLyDonShopee.App/Services/IAccountSession.cs` + `AccountSession.cs`:

- Interface: `Task<bool> CheckOrdersAsync();` — XML-doc: kiểm tra đơn NGAY (thủ công): về trang chủ
  Seller rồi đọc số "Chờ Lấy Hàng", cập nhật `ToShipCount`. Trả false nếu phiên chưa chạy / đang bận
  điều hướng / không đọc được. Không ném.
- `AccountSession` (theo mẫu `ProcessOrdersAsync`):
  1. Chụp `var s = _session;` + token dưới `_lifecycleLock` (nuốt `ObjectDisposedException` → false).
  2. `if (s is null || State != SessionState.Running || _navigating) return false;`
  3. `_navigating = true; StatusText = "Đang kiểm tra đơn — về trang chủ (kiểu người)...";`
  4. `try`:
     - `await s.GoToSellerHomeAsync(tok).ConfigureAwait(false);`
     - `var count = await s.ReadToShipCountAsync(reload: false, tok).ConfigureAwait(false);` (vừa
       GotoAsync nên KHÔNG reload lại).
     - `if (count is int n) { ToShipCount = n; StatusText = $"Đã kiểm tra: Chờ Lấy Hàng = {n}."; return true; }`
       `else { StatusText = "Chưa đọc được số đơn (có thể chưa đăng nhập) — thử lại."; return false; }`
  5. `catch (OperationCanceledException) { return false; }`
  6. `finally { _navigating = false; }`

### Bước 3 — ViewModel

`src/XuLyDonShopee.App/ViewModels/AccountsViewModel.cs`:

- `public bool CanCheckOrders => _editingId is long cid && _services.Sessions.Get(cid) is { State: SessionState.Running };`
- Raise `OnPropertyChanged(nameof(CanCheckOrders))` tại đúng 3 chỗ đang raise CanProcessOrders:
  `OnIsEditingChanged`, `OnIsNewChanged`, `UpdateSelectedSessionStatus()`.
- `[RelayCommand] private async Task CheckOrdersAsync()`: đọc `_editingId` vào biến cục bộ TRƯỚC await;
  `var session = _services.Sessions.Get(id)`; null → return; `await session.CheckOrdersAsync();`
  (kết quả hiển thị qua StatusText/ToShipCount của phiên → BusyStatus/OrderStatus tự cập nhật).

### Bước 4 — UI (nút "Kiểm tra" ở ô đỏ, trái nút "Dừng")

`src/XuLyDonShopee.App/Views/AccountsView.axaml`, hàng nút (~dòng 141):

- `ColumnDefinitions="Auto,Auto,*,Auto,Auto,Auto"` → `"Auto,Auto,*,Auto,Auto,Auto,Auto"`.
- Nút mới ở `Grid.Column="3"`, `Classes="secondary"`, `Content="Kiểm tra"`,
  `Command="{Binding CheckOrdersCommand}"`, `IsEnabled="{Binding CanCheckOrders}"`,
  `ToolTip.Tip="Về trang chủ Seller và kiểm tra số đơn Chờ Lấy Hàng ngay (không đợi chu kỳ 30')"`.
- Dời **Dừng → Column 4**, **Xử lý đơn → Column 5**, **Mở trang bán hàng → Column 6** (giữ nguyên
  Margin/Classes/Command/ToolTip của từng nút). Kiểm tra ở Column 3 KHÔNG cần Margin trái (sát spacer);
  Dừng ở Column 4 giữ `Margin="10,0,0,0"` để cách nút Kiểm tra.

### Bước 5 — Test + build

- Cập nhật 2 stub `StubSession`: thêm `public Task<bool> CheckOrdersAsync() => Task.FromResult(false);`
  trong `AccountRowViewModelTests.cs` và `AccountSessionManagerTests.cs`.
- `dotnet build XuLyDonShopee.sln -c Debug` (0 error/0 warning); `dotnet test` toàn bộ pass (nền 269;
  không đỏ test cũ). WDAC → rebuild `--no-incremental -p:Deterministic=false` rồi `dotnet test --no-build`.
- `CanCheckOrders` phụ thuộc manager thật (không stub được) như `CanProcessOrders`/`CanStopSeller` →
  nghiệm thu bằng đọc code, không unit-test enable-state.

## 4. Tiêu chí nghiệm thu

- [ ] Form chi tiết có nút "Kiểm tra" nằm ở khoảng trống bên TRÁI nút "Dừng"; enable khi phiên đang
      chạy (Running), disable khi không có phiên chạy.
- [ ] Bấm → phiên điều hướng về `SellerUrl` (GotoAsync) có dừng "kiểu người" trước/sau → đọc số Chờ
      Lấy Hàng ngay → cập nhật "Chờ lấy: N" + StatusText; `_navigating` chặn nhịp 30' reload chồng lên.
- [ ] Graceful: chưa đăng nhập / không đọc được / context đóng → false + StatusText rõ, không ném,
      không phá phiên.
- [ ] Build 0/0; `dotnet test` toàn bộ pass (269 nền + stub cập nhật).
- [ ] Chỉ sửa: `ShopeeLoginService.cs`, `IAccountSession.cs`, `AccountSession.cs`,
      `AccountsViewModel.cs`, `AccountsView.axaml`, 2 file test stub.

## 5. Rủi ro & lưu ý

- **Like-human** vẫn là ràng buộc: điều hướng bằng GotoAsync chấp nhận cho "về trang chủ" (giống gõ
  URL), NHƯNG phải có khoảng dừng ngẫu nhiên trước/sau; KHÔNG thêm reload/đọc dồn dập.
- `_navigating` bao trùm CheckOrders để không đụng nhịp 30' (bài học race reload). Nhớ `finally` tắt.
- Nút Kiểm tra bấm liên tiếp: guard `_navigating` đã chặn chạy chồng (giống ProcessOrders).
- Đọc số dùng lại `ReadToShipCountAsync(reload:false, ...)` — KHÔNG reload lần nữa (vừa GotoAsync xong).
- WDAC/ISG khi test như các plan trước. Smoke live cần tài khoản Shopee thật → không claim; người dùng
  smoke sau merge (bấm Kiểm tra khi một phiên đang mở & đã đăng nhập → xem trình duyệt về trang chủ và
  dòng "Chờ lấy: N" cập nhật).

---

## Báo cáo thực thi (Opus điền sau khi xong)

<Opus dán báo cáo cuối vào đây.>
