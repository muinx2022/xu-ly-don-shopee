# Plan: Nút "Xử lý đơn" — mở Cài đặt vận chuyển → tab Địa Chỉ (kiểu người)

- **Ngày:** 2026-07-15
- **Trạng thái:** đang làm
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)
- **Ghi chú:** Bước ĐẦU của luồng "xử lý đơn" (các bước nghiệp vụ sau tab Địa Chỉ sẽ có plan riêng).

## 1. Bối cảnh & mục tiêu

**Yêu cầu người dùng:**

1. Form chi tiết tài khoản, **giữa nút "Dừng" và nút "Mở trang bán hàng"**, thêm nút **"Xử lý đơn"** —
   chỉ enable khi số đơn **"Chờ lấy hàng" > 0** (của phiên đang chạy của tài khoản đang chọn).
2. Bấm nút → trong cửa sổ Brave của phiên đó: tìm và bấm **"Cài Đặt Vận Chuyển"** ở menu trái
   (nhóm "Quản Lý Đơn Hàng"); khi trang cài đặt vận chuyển mở ra thì bấm tab **"Địa Chỉ"**.
3. **RÀNG BUỘC XUYÊN SUỐT (người dùng nhấn mạnh):** MỌI thao tác trên trang bán hàng phải giống người
   ở mức cao nhất — di chuột theo đường cong, click down→trễ→up, có khoảng **dừng/chờ ngẫu nhiên kiểu
   "người đọc trang"** giữa các bước. **CẤM** `ElementHandle.ClickAsync()`/`FillAsync()` thẳng của
   Playwright cho thao tác nghiệp vụ (chỉ chấp nhận `GotoAsync` ở fallback cuối ghi rõ bên dưới).

### Hiện trạng code (đã khảo sát 15/7, sau commit nền `1b5a680`)

- **Hàng nút hành động** — `src/XuLyDonShopee.App/Views/AccountsView.axaml` (~dòng 299):
  `Grid ColumnDefinitions="Auto,Auto,*,Auto,Auto"`: Lưu (0), Hủy (1), spacer (2), **Dừng (3,
  `StopCommand`/`CanStopSeller`)**, **Mở trang bán hàng (4, `OpenSellerCommand`/`CanOpenSeller`)**.
- **VM** — `src/XuLyDonShopee.App/ViewModels/AccountsViewModel.cs`:
  - `CanOpenSeller`/`CanStopSeller` là computed property dựa `_editingId` + `_services.Sessions.IsRunning(id)`;
    được raise lại tại `OnIsEditingChanged`, `OnIsNewChanged` và `UpdateSelectedSessionStatus()`
    (hàm này chạy khi manager phát `Changed`/đổi dòng chọn; cũng đổ `BusyStatus = session?.StatusText`).
- **Phiên** — `src/XuLyDonShopee.App/Services/IAccountSession.cs` + `AccountSession.cs`:
  - `AccountSession` giữ `volatile ILoginSession? _session`, `CancellationTokenSource? _cts`
    (dưới `_lifecycleLock`), `ToShipCount` (int?), `StatusText`, `State`.
  - Vòng `RunAsync`: poll 1s bắt cookie; **nhịp đọc đơn**: `if (DateTime.UtcNow >= nextOrderCheck)` →
    `session.ReadToShipCountAsync(reload: !firstOrderCheck, ct)` — **CÓ reload trang** → nguy cơ race
    reload đúng lúc đang điều hướng xử lý đơn (xem Bước 3).
  - Manager `AccountSessionManager.Get(id)` trả `IAccountSession?`.
- **Playwright** — `src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs`, class lồng `LoginSession`
  (implement `ILoginSession`, ctor private):
  - Đã có sẵn primitive kiểu người (private static trong `LoginSession`):
    `HumanMoveAndClickAsync(page, el, mx, my, rng, ct)` — chuột cong từng điểm (5–25ms/điểm) + jitter
    đích + click down 40–120ms rồi up, trả vị trí chuột mới; `HumanFillAsync` (gõ từng ký tự,
    `HumanTyping.NextCharDelayMs`). `TryHumanLoginAsync` là mẫu cách dùng (khởi tạo `mx,my` ngẫu nhiên +
    `Random` riêng).
  - Trang: `_context.Pages[0]`; mẫu graceful + selector-fallback: `ReadToShipCountAsync`/`FindToShipTitleAsync`.
- Mẫu helper thuần + test: `ShopeeDashboard.ParseToShipCount` + `ShopeeDashboardTests`.
- Test hiện tại: **166/166 pass**. Có các stub implement `IAccountSession` trong
  `src/XuLyDonShopee.Tests/AccountSessionManagerTests.cs` (và có thể file test khác — grep
  `IAccountSession` trong Tests) → thêm member mới vào interface PHẢI cập nhật stub.

### DOM thật do người dùng cung cấp (rút gọn phần cần)

Menu trái (sidebar) — mục đích: link "Cài Đặt Vận Chuyển":

```html
<li class="sidebar-menu-box ps_menu_order">
  <div class="sidebar-menu-item"><span class="sidebar-menu-item-text">Quản Lý Đơn Hàng</span>…</div>
  <ul class="sidebar-submenu with-sidebar-panel">
    …
    <li><div class="sidebar-submenu-item">
      <a href="/portal/all-settings/shipping" class="sidebar-submenu-item-link" test-id="order shipping setting">
        …<span>Cài Đặt Vận Chuyển</span>…
      </a></div></li>
  </ul>
</li>
```

Trang Cài đặt vận chuyển — thanh tab (eds-tabs), tab cần bấm là "Địa Chỉ" (tab đang active mặc định là
"Đơn vị vận chuyển"):

```html
<div class="eds-tabs eds-tabs-line …">
  <div class="eds-tabs__nav">…
    <div class="eds-tabs__nav-tabs">
      <div class="eds-tabs__nav-tab"><div><span>Địa Chỉ</span><div class="eds-badge-x">…</div></div></div>
      <div class="eds-tabs__nav-tab active">Đơn vị vận chuyển</div>
      <div class="eds-tabs__nav-tab">Chứng từ vận chuyển</div>
    </div>…
```

Lưu ý: `InnerText` của tab "Địa Chỉ" có thể kèm whitespace/text badge → so khớp phải chuẩn hóa
(helper thuần, Bước 1). Shopee có thể đổi class/href → mọi selector đều cần fallback + graceful.

## 2. Phạm vi

- **Làm:** nút UI + enable-state; method mới trên `IAccountSession`/`AccountSession`;
  method điều hướng kiểu người mới trên `ILoginSession`/`LoginSession`; helper thuần so khớp
  menu/tab + unit test; cập nhật stub test; chống race reload-khi-đang-điều-hướng.
- **Không làm:**
  - CHƯA làm nghiệp vụ gì SAU khi tab Địa Chỉ mở (plan sau).
  - Không đổi luồng chọn proxy, human-login, bắt cookie, đọc số đơn (ngoài cờ chống race ghi ở Bước 3).
  - Không thêm nút vào danh sách trái / hàng nút "đã chọn" — CHỈ form chi tiết.
  - KHÔNG tự commit (Fable commit sau nghiệm thu).

## 3. Các bước thực hiện

### Bước 1 — Helper thuần so khớp menu/tab (Core, test được)

Tạo `src/XuLyDonShopee.Core/Services/ShopeeShippingNav.cs` — static class, XML-doc tiếng Việt:

- `static string NormalizeUiText(string? s)`: null→rỗng; thay mọi chuỗi whitespace (kể cả xuống dòng)
  bằng 1 space, `Trim()`, `ToLowerInvariant()`.
- `static bool IsShippingSettingHref(string? href)`: href chứa `"/portal/all-settings/shipping"`.
- `static bool IsShippingSettingText(string? s)`: normalize == `"cài đặt vận chuyển"`.
- `static bool IsOrderMenuText(string? s)`: normalize == `"quản lý đơn hàng"`.
- `static bool IsAddressTabText(string? s)`: normalize **chứa** `"địa chỉ"` (InnerText tab có thể kèm
  rác badge; hai tab còn lại "đơn vị vận chuyển"/"chứng từ vận chuyển" không chứa chuỗi này).

### Bước 2 — `ILoginSession.OpenShippingAddressSettingsAsync` (điều hướng kiểu người)

`src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs`:

- Interface `ILoginSession`: thêm
  `Task<bool> OpenShippingAddressSettingsAsync(CancellationToken ct = default);`
  XML-doc: mở "Cài Đặt Vận Chuyển" ở menu trái rồi bấm tab "Địa Chỉ" — **toàn bộ bằng thao tác kiểu
  người**; graceful không bao giờ ném, trả false khi không làm được (selector đổi/trang chưa đăng
  nhập/context đóng).
- `LoginSession` implement, theo khung sau (tái dùng nguyên primitive có sẵn):
  1. `page = _context.Pages.Count > 0 ? _context.Pages[0] : null`; null → false. `var rng = new Random();`
     khởi tạo `(mx,my)` ngẫu nhiên trong viewport (giống `TryHumanLoginAsync`).
  2. **Dừng kiểu người đọc trang** trước khi bắt đầu: `Task.Delay(rng.Next(800, 2500), ct)`.
  3. **Tìm link "Cài Đặt Vận Chuyển"** (poll 300–500ms, deadline ~10s), thử theo thứ tự:
     `a.sidebar-submenu-item-link[href*='/portal/all-settings/shipping']` →
     `a[test-id='order shipping setting']` → duyệt tất cả `a.sidebar-submenu-item-link`, khớp
     `IsShippingSettingText(InnerText)`. Lấy element có `BoundingBoxAsync() != null` (đang hiển thị).
     - Nếu chưa thấy/không hiển thị (submenu có thể đang đóng): tìm mục cha — `li.ps_menu_order
       div.sidebar-menu-item`, fallback duyệt `.sidebar-menu-item` khớp `IsOrderMenuText` — click
       kiểu người (`HumanMoveAndClickAsync`, cập nhật `mx,my`), chờ `rng.Next(500, 1500)`ms rồi tìm lại.
  4. **Click link kiểu người** (`HumanMoveAndClickAsync`), rồi **chờ trang cài đặt mở**: poll 200–300ms,
     deadline ~20s, điều kiện: `page.Url` chứa `/portal/all-settings/shipping` **hoặc** xuất hiện
     `.eds-tabs__nav-tab` — SPA có thể đổi route không load document mới, KHÔNG dùng WaitForNavigation.
     - **Fallback cuối** (chỉ khi bước 3 không tìm được link sau khi đã thử mở mục cha):
       `page.GotoAsync("https://banhang.shopee.vn/portal/all-settings/shipping")` (WaitUntil
       DOMContentLoaded, timeout 30s, nuốt lỗi) — kém human hơn, chỉ là đường thoát hiếm khi Shopee
       đổi DOM menu.
  5. **Dừng đọc trang** `rng.Next(800, 2500)`ms. **Tìm tab "Địa Chỉ"**: poll deadline ~10s, duyệt
     `.eds-tabs__nav-tab`, khớp `IsAddressTabText(InnerText)`.
     - Tab tìm được đã có class `active` (kiểm qua `GetAttributeAsync("class")`) → coi như XONG, true.
     - Chưa active → click kiểu người → true.
  6. Mọi nhánh lỗi/hết deadline → false. Bọc toàn bộ try/catch trả false (mẫu `ReadToShipCountAsync`).
  KHÔNG dùng `ElementHandle.ClickAsync()` / `page.Mouse.ClickAsync()` thẳng — chỉ `HumanMoveAndClickAsync`.

### Bước 3 — `IAccountSession.ProcessOrdersAsync` + chống race reload

`src/XuLyDonShopee.App/Services/IAccountSession.cs` + `AccountSession.cs`:

- Interface: thêm `Task<bool> ProcessOrdersAsync();` — XML-doc: bước đầu xử lý đơn: điều hướng kiểu
  người tới Cài đặt vận chuyển → tab Địa Chỉ trong phiên đang chạy; false nếu phiên chưa chạy/lỗi.
- `AccountSession`:
  - Field mới `private volatile bool _navigating;` — bật trong lúc xử lý đơn.
  - Trong `RunAsync`, nhịp đọc đơn đổi điều kiện thành
    `if (!_navigating && DateTime.UtcNow >= nextOrderCheck)` — khi đang điều hướng thì KHÔNG
    `ReadToShipCountAsync` (hàm này có reload → sẽ phá điều hướng đang chạy giữa chừng).
  - `public async Task<bool> ProcessOrdersAsync()`:
    1. Chụp `var s = _session;` và token: `CancellationToken tok; lock (_lifecycleLock) { tok = _cts?.Token ?? default; }`
       (nuốt `ObjectDisposedException` → return false). `s is null || State != SessionState.Running` → false.
    2. `_navigating = true; StatusText = "Đang mở Cài đặt vận chuyển → Địa Chỉ (kiểu người)...";`
    3. `var ok = await s.OpenShippingAddressSettingsAsync(tok).ConfigureAwait(false);`
    4. `StatusText = ok ? "Đã mở tab Địa Chỉ (Cài đặt vận chuyển)." : "Không mở được Cài đặt vận chuyển — thao tác tay trong cửa sổ Brave.";`
    5. `finally { _navigating = false; }`; bọc try/catch (OperationCanceledException → false, im lặng).

### Bước 4 — ViewModel + UI

- `AccountsViewModel.cs`:
  - `public bool CanProcessOrders => _editingId is long pid && _services.Sessions.Get(pid) is { State: SessionState.Running, ToShipCount: > 0 };`
    (cần `using XuLyDonShopee.App.Services;` — đã có sẵn trong file).
  - Raise `OnPropertyChanged(nameof(CanProcessOrders))` tại đúng 3 chỗ đang raise CanOpenSeller/CanStopSeller:
    `OnIsEditingChanged`, `OnIsNewChanged`, `UpdateSelectedSessionStatus()`.
  - `[RelayCommand] private async Task ProcessOrdersAsync()`: **đọc `_editingId` vào biến cục bộ TRƯỚC
    await** (bài học cũ: field mutable sau await dài); `var session = _services.Sessions.Get(id)`; null
    → return; `await session.ProcessOrdersAsync();` — kết quả hiển thị tự nhiên qua StatusText của phiên
    (`UpdateSelectedSessionStatus` đổ về `BusyStatus`), KHÔNG mở modal.
- `AccountsView.axaml` — hàng nút (~dòng 299):
  - `ColumnDefinitions="Auto,Auto,*,Auto,Auto"` → `"Auto,Auto,*,Auto,Auto,Auto"`.
  - Nút mới ở `Grid.Column="4"`, `Margin="10,0,0,0"`, `Classes="accentOutline"`, `Content="Xử lý đơn"`,
    `Command="{Binding ProcessOrdersCommand}"`, `IsEnabled="{Binding CanProcessOrders}"`,
    `ToolTip.Tip="Mở Cài đặt vận chuyển → tab Địa Chỉ trong phiên đang chạy (bật khi Chờ lấy hàng > 0)"`.
  - Nút "Mở trang bán hàng" dời sang `Grid.Column="5"` (giữ nguyên phần còn lại).

### Bước 5 — Test + build

- Tạo `src/XuLyDonShopee.Tests/ShopeeShippingNavTests.cs`: normalize (null/rỗng/space thừa/xuống
  dòng/hoa-thường); `IsShippingSettingHref` đúng/sai; `IsShippingSettingText`/`IsOrderMenuText`
  khớp cả "Cài Đặt Vận Chuyển"/"CÀI đặt  vận chuyển\n"; `IsAddressTabText`: "Địa Chỉ", "địa chỉ\n​…"
  kèm rác → true; "Đơn vị vận chuyển"/"Chứng từ vận chuyển"/null → false.
- Grep `IAccountSession` trong `src/XuLyDonShopee.Tests/` → mọi stub thêm
  `public Task<bool> ProcessOrdersAsync() => Task.FromResult(false);`.
- `dotnet build XuLyDonShopee.sln -c Debug` (0 error/0 warning); `dotnet test` toàn bộ pass
  (**nền hiện tại 166**, không được đỏ test cũ). WDAC chặn → xử lý theo mục Rủi ro.
- Enable-state của nút (CanProcessOrders) không unit-test được vì `AppServices.Sessions` là manager
  thật không inject stub — chấp nhận (CanStopSeller hiện cũng vậy), nghiệm thu bằng đọc code.

## 4. Tiêu chí nghiệm thu

- [ ] Form chi tiết có nút "Xử lý đơn" nằm GIỮA "Dừng" và "Mở trang bán hàng"; disable khi: không có
      phiên chạy / ToShipCount null / = 0; enable khi phiên Running và ToShipCount > 0.
- [ ] Bấm nút → phiên click menu "Cài Đặt Vận Chuyển" rồi tab "Địa Chỉ"; **mọi click qua
      `HumanMoveAndClickAsync`** (chuột cong + down/trễ/up) + có dừng ngẫu nhiên kiểu người giữa các
      bước; KHÔNG có `ClickAsync()` thẳng cho nghiệp vụ (soi code xác nhận); `GotoAsync` chỉ ở fallback
      đã ghi.
- [ ] Graceful: selector đổi / trang không mở được → trả false, StatusText báo rõ, KHÔNG ném, KHÔNG
      phá phiên (vòng bắt cookie vẫn chạy tiếp); trong lúc điều hướng KHÔNG bị reload bởi nhịp đọc đơn
      (`_navigating` chặn).
- [ ] Build 0 error/0 warning; `dotnet test` toàn bộ pass (166 nền + test mới).
- [ ] Chỉ sửa các file: `AccountsView.axaml`, `AccountsViewModel.cs`, `IAccountSession.cs`,
      `AccountSession.cs`, `ShopeeLoginService.cs`, `ShopeeShippingNav.cs` (mới),
      `ShopeeShippingNavTests.cs` (mới), stub trong file test hiện có, (tùy chọn README).

## 5. Rủi ro & lưu ý

- **Like-human là ràng buộc số 1** (người dùng nhấn mạnh riêng): nếu phải đánh đổi giữa "nhanh/chắc"
  và "giống người", chọn giống người. Không thêm vá JS stealth (bài học cũ: over-patch tự lộ bot).
- **Race reload:** quên chặn nhịp đọc đơn khi đang điều hướng → reload phá thao tác giữa chừng (lỗi
  chập chờn khó lần). Cờ `_navigating` là bắt buộc, nhớ `finally` tắt.
- **Shopee đổi DOM bất kỳ lúc nào** → mọi selector có fallback theo text (helper thuần) + deadline +
  trả false graceful; KHÔNG ném xuyên phiên.
- **Smoke live cần tài khoản Shopee đăng nhập thật** — môi trường Opus không có ⇒ Opus KHÔNG claim đã
  chạy live (bài học cũ); phủ bằng: unit test helper, đọc code đối chiếu DOM người dùng cung cấp, build
  + full test. Fable sẽ nhờ người dùng smoke thật sau khi merge.
- WDAC/ISG khi test: `0x800711C7` → build lại `-p:Deterministic=false` rồi `dotnet test --no-build`;
  fail đồng loạt cùng lỗi này là policy máy, không phải code.
- `Random` dùng instance cục bộ trong method (không dùng `Random.Shared` để đồng bộ style file hiện có).

---

## Báo cáo thực thi (Opus điền sau khi xong)

<Opus dán báo cáo cuối vào đây.>
