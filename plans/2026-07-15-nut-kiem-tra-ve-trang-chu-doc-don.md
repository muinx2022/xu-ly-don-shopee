# Plan: Nút "Kiểm tra" — về trang chủ Seller rồi đọc lại số đơn ngay

- **Ngày:** 2026-07-15
- **Trạng thái:** đang làm
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)

## 1. Bối cảnh & mục tiêu

**Yêu cầu người dùng (kèm screenshot):** trong hàng nút hành động của form chi tiết tài khoản (đã dời
lên đầu form, commit `ff90e68`), thêm **1 nút "Kiểm tra"** vào chỗ trống ngay TRƯỚC nút "Dừng". Bấm vào
thì trong phiên Brave đang chạy của tài khoản đó: **chuyển ra trang chủ** (Seller Centre
`https://banhang.shopee.vn/`), sau đó **thực hiện việc check đơn hàng** — đọc lại số "Chờ Lấy Hàng"
ngay, không chờ nhịp theo dõi 30 phút.

**RÀNG BUỘC XUYÊN SUỐT:** thao tác trên trang bán hàng giống người mức cao nhất. Riêng việc "về trang
chủ": người dùng KHÔNG cung cấp DOM logo/nav nên **quyết định đã chốt** — điều hướng bằng
`page.GotoAsync(SellerUrl)` (tương đương người gõ URL / bấm bookmark, hành vi bình thường, KHÔNG phải
click máy vào element); kèm khoảng dừng "đọc trang" ngẫu nhiên trước/sau. KHÔNG thêm click nghiệp vụ
nào trong việc này.

### Hiện trạng code (cây chính sạch tại commit `ff90e68`, nền test **269** pass)

- Hàng nút — `src/XuLyDonShopee.App/Views/AccountsView.axaml` (~dòng 163):
  `Grid ColumnDefinitions="Auto,Auto,*,Auto,Auto,Auto" Margin="0,0,0,16"`: Lưu (0), Hủy (1), spacer (2),
  Dừng (3, không margin trái), Xử lý đơn (4, Margin 10), Mở trang bán hàng (5, Margin 10). Ngay dưới là
  3 TextBlock thông báo (ErrorMessage/BusyStatus/OrderStatus).
- VM — `src/XuLyDonShopee.App/ViewModels/AccountsViewModel.cs`: mẫu `CanStopSeller`
  (`_editingId` + `Sessions.IsRunning`), `CanProcessOrders`, `ProcessOrdersCommand` (đọc `_editingId`
  vào biến cục bộ TRƯỚC await); 3 chỗ raise: `OnIsEditingChanged`, `OnIsNewChanged`,
  `UpdateSelectedSessionStatus()`. `FormatOrderStatus(int?)` dựng dòng "Chờ Lấy Hàng: N — kiểm lại sau 30'".
- Phiên — `IAccountSession`/`AccountSession`: mẫu `ProcessOrdersAsync()` (guard
  `s is null || State != Running || _navigating` → false; `_navigating` bật trong lúc thao tác để vòng
  `RunAsync` KHÔNG chạy nhịp đọc đơn; chụp token `_cts` dưới lock, nuốt `ObjectDisposedException`).
  `ToShipCount` là `[ObservableProperty]` — set xong VM tự cập nhật dòng "Chờ lấy: N" + OrderStatus.
- Playwright — `ShopeeLoginService.cs` / `LoginSession`: `SellerUrl` (const
  `https://banhang.shopee.vn/`), `ReadToShipCountAsync(reload, ct)` (gate IsLoggedIn; reload=false thì
  KHÔNG reload; tự poll to-do box ~8s; graceful null), mẫu dừng ngẫu nhiên `rng.Next(800, 2500)`.
- Test stub `IAccountSession` trong `src/XuLyDonShopee.Tests/` (AccountSessionManagerTests +
  AccountRowViewModelTests) — thêm member interface PHẢI cập nhật stub.

## 2. Phạm vi

- **Làm:** nút "Kiểm tra" + enable-state; `IAccountSession.CheckOrdersAsync()`;
  `ILoginSession.GoHomeAndReadToShipCountAsync(ct)`; cập nhật stub test.
- **Không làm:** không đổi nhịp theo dõi 30' của `RunAsync` (lịch cũ giữ nguyên — sau khi kiểm tra tay,
  lần poll định kỳ kế tiếp vẫn theo lịch cũ, chấp nhận); không đụng luồng Xử lý đơn / cookie / proxy;
  KHÔNG tự commit.

## 3. Các bước thực hiện

### Bước 1 — `ILoginSession.GoHomeAndReadToShipCountAsync` (Core)

`src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs`:

- Interface: `Task<int?> GoHomeAndReadToShipCountAsync(CancellationToken ct = default);` — XML-doc: về
  trang chủ Seller (Goto như người gõ URL) rồi đọc số "Chờ Lấy Hàng" từ to-do box; graceful không ném,
  null khi chưa đăng nhập/không đọc được.
- `LoginSession` implement:
  1. `page = _context.Pages.Count > 0 ? _context.Pages[0] : null`; null → null. `rng` cục bộ.
  2. Dừng "đọc trang" `rng.Next(800, 2500)`ms.
  3. `page.GotoAsync(SellerUrl, DOMContentLoaded, timeout 30s)` — nuốt lỗi điều hướng (vẫn thử đọc tiếp).
  4. Dừng `rng.Next(800, 2500)`ms.
  5. `return await ReadToShipCountAsync(reload: false, ct)` — trang vừa load nên KHÔNG reload nữa
     (hàm này tự gate IsLoggedIn + poll to-do box).
  6. Toàn bộ bọc try/catch → null.

### Bước 2 — `IAccountSession.CheckOrdersAsync` (App)

`src/XuLyDonShopee.App/Services/IAccountSession.cs` + `AccountSession.cs` (theo đúng mẫu
`ProcessOrdersAsync`):

- Interface: `Task<bool> CheckOrdersAsync();` — XML-doc: về trang chủ + đọc lại số "Chờ Lấy Hàng" ngay
  (kiểm tra tay, không chờ nhịp 30'); false nếu phiên chưa chạy / đang bận thao tác khác / không đọc được.
- `AccountSession.CheckOrdersAsync()`:
  1. Chụp `s = _session` + token như ProcessOrdersAsync; guard
     `s is null || State != SessionState.Running || _navigating` → false.
  2. `_navigating = true; StatusText = "Đang về trang chủ để kiểm tra đơn...";`
  3. `var count = await s.GoHomeAndReadToShipCountAsync(tok).ConfigureAwait(false);`
  4. `count is int n` → `ToShipCount = n;` `StatusText = $"Đã kiểm tra: Chờ Lấy Hàng = {n}.";` trả true.
     Ngược lại → `StatusText = "Không đọc được số đơn — có thể chưa đăng nhập xong, kiểm tra cửa sổ Brave.";`
     trả false. (KHÔNG đổi `ToShipCount` khi null — giữ số cũ.)
  5. `catch OperationCanceledException` → false; `finally { _navigating = false; }`.

### Bước 3 — ViewModel + UI

- `AccountsViewModel.cs`:
  - `public bool CanCheckOrders => _editingId is long cid && _services.Sessions.IsRunning(cid);`
    (chỉ cần phiên đang chạy — không cần ToShip > 0).
  - Raise `OnPropertyChanged(nameof(CanCheckOrders))` tại đúng 3 chỗ đang raise CanStopSeller/CanProcessOrders.
  - `[RelayCommand] private async Task CheckOrdersAsync()`: đọc `_editingId` vào biến cục bộ trước
    await; `Sessions.Get(id)` null → return; `await session.CheckOrdersAsync();`.
- `AccountsView.axaml` — hàng nút:
  - `ColumnDefinitions="Auto,Auto,*,Auto,Auto,Auto"` → `"Auto,Auto,*,Auto,Auto,Auto,Auto"`.
  - Nút mới `Grid.Column="3"`, `Classes="secondary"`, `Content="Kiểm tra"`,
    `Command="{Binding CheckOrdersCommand}"`, `IsEnabled="{Binding CanCheckOrders}"`,
    `ToolTip.Tip="Về trang chủ Seller rồi đọc lại số đơn Chờ Lấy Hàng ngay (bật khi phiên đang chạy)"`.
  - Dừng dời sang column 4 và THÊM `Margin="10,0,0,0"`; Xử lý đơn → column 5; Mở trang bán hàng →
    column 6 (giữ nguyên margin/thuộc tính còn lại).

### Bước 4 — Test + build

- Cập nhật mọi stub `IAccountSession` trong Tests: thêm
  `public Task<bool> CheckOrdersAsync() => Task.FromResult(false);`.
- Không có logic thuần mới đáng test riêng (GoHome là Playwright); đảm bảo **toàn bộ 269 test cũ xanh**.
- `dotnet build XuLyDonShopee.sln -c Debug` 0 error/0 warning; WDAC chặn test → rebuild
  `--no-incremental -p:Deterministic=false` rồi `dotnet test --no-build` (có thể vài lần).

## 4. Tiêu chí nghiệm thu

- [ ] Nút "Kiểm tra" nằm giữa "Hủy" và "Dừng" (đầu cụm nút phải), bật khi phiên đang chạy, tắt khi không.
- [ ] Bấm nút → phiên Goto trang chủ Seller → đọc số "Chờ Lấy Hàng" → `ToShipCount` cập nhật ngay
      (dòng "Chờ lấy: N" ở danh sách + OrderStatus đổi theo); StatusText báo kết quả rõ.
- [ ] Không đọc được (chưa đăng nhập...) → StatusText báo, giữ nguyên số cũ, phiên không chết.
- [ ] Trong lúc kiểm tra: `_navigating` chặn nhịp poll định kỳ; bấm lặp/bấm khi đang Xử lý đơn → bỏ qua.
- [ ] Build 0/0; toàn bộ test pass (269 nền).
- [ ] Chỉ sửa: `ShopeeLoginService.cs`, `IAccountSession.cs`, `AccountSession.cs`,
      `AccountsViewModel.cs`, `AccountsView.axaml`, stub test.

## 5. Rủi ro & lưu ý

- Dùng chung cờ `_navigating` với Xử lý đơn → hai thao tác loại trừ lẫn nhau (đúng ý: không chạy 2 luồng
  trên cùng trang).
- `GotoAsync` là ngoại lệ có chủ đích của ràng buộc like-human (người gõ URL); KHÔNG thêm click máy nào.
- Sau Goto, trang cần thời gian render to-do box — `ReadToShipCountAsync` đã tự poll ~8s, đừng poll thêm.
- WDAC như các plan trước; smoke live cần tài khoản thật → không claim.

---

## Báo cáo thực thi (Opus điền sau khi xong)

<Opus dán báo cáo cuối vào đây.>
