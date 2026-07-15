# Plan: Xử lý MỘT đơn — Chuẩn bị hàng → tự mang ra bưu cục → In phiếu giao (tải + in máy)

- **Ngày:** 2026-07-15
- **Trạng thái:** đang làm
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)
- **Nghiệm thu:** (Fable điền sau)

## 1. Bối cảnh & yêu cầu

Nối tiếp luồng "Xử lý đơn": SAU khi đặt địa chỉ lấy hàng thành công (plan trước), xử lý **đơn đầu tiên**
trong danh sách đơn chờ:
1. Cùng menu "Quản Lý Đơn Hàng", bấm **"Tất cả"** (`/portal/sale/order`) — trang tự vào tab "Chờ xử lý",
   KHÔNG cần bấm tab.
2. Lấy **đơn ĐẦU TIÊN** → bấm **"Chuẩn bị hàng"** (`button[data-testid='action-button-2']`).
3. Modal **"Giao Đơn Hàng"** hiện → chọn **"Tôi sẽ tự mang hàng tới Bưu cục"** (`[data-testid='dropoff-option']`,
   mặc định đã `selected`) → bấm **"Xác nhận"** (`[data-testid='arrange-shipment-confirm']`).
4. Modal **"Thông Tin Chi Tiết"** hiện → bấm **"In phiếu giao"** (`button[data-testid='print-button']`).
5. Bấm "In phiếu giao" mở **tab MỚI** hiển thị phiếu (URL dạng
   `https://banhang.shopee.vn/awbprint?job_id=...&shop_id=...&lang=vi&first_time=1`).
   - **NGAY tại bước này** (kẻo mất token): **tải phiếu về `D:\Phieu-giao-hang`** + **gửi lệnh in thẳng
     máy in mặc định** (im lặng) → **đóng tab phiếu** → quay về danh sách.

**Plan này CHỈ làm MỘT đơn** (chạy 1 lần sau đặt địa chỉ) để smoke phần mới lạ nhất (bắt tab + tải + in).
Vòng lặp mọi đơn + đổi địa chỉ không-mặc-định để **plan sau**.

### Quyết định người dùng (đã chốt)
- In: **tải file + IN THẲNG máy in mặc định** (im lặng) → thêm cờ Brave `--kiosk-printing` + gọi `window.print()`.
- Log: đã có panel + file (plan trước) → **mọi bước gọi log** để smoke live thấy rõ.

### Hiện trạng code (đã khảo sát 15/7)
- `ILoginSession` (Core, `ShopeeLoginService.cs`) đã có `OpenShippingAddressSettingsAsync`,
  `SetPickupAddressAsync`; `AccountSession.ProcessOrdersAsync` gọi lần lượt 2 method đó rồi báo StatusText.
- Primitive kiểu người sẵn có trong `LoginSession`: `HumanMoveAndClickVerifiedAsync` (hit-test),
  `TryHumanClickVisibleAsync` (scroll + verified), `FindEditAddressModalAsync`/`WaitEditAddressModalAsync`
  (mẫu chờ modal theo `.eds-modal__box` + `.title`), `HasBoundingBoxAsync`. `_context` là `IBrowserContext`
  (nối CDP), `_context.Pages[0]` là trang chính.
- `BraveLaunchArgs.BuildBraveArgs` (thuần, có test `BraveLaunchArgsTests`) dựng cờ launch — thêm cờ mới ở đây.
- Chưa có logging từ Core; App có `ActivityLog` (plan trước). → truyền **callback log** vào method mới.

## 2. Phạm vi
- **Làm:** cờ `--kiosk-printing`; helper thuần so text (Core) + test; method `ProcessFirstOrderAsync` trên
  `ILoginSession`/`LoginSession` (điều hướng "Tất cả" → đơn đầu → Chuẩn bị hàng → dropoff → Xác nhận →
  In phiếu giao → bắt tab, tải, in, đóng tab), trả enum `ArrangeShipmentResult`; nhận `downloadDir` +
  `Action<string>? log`; wire vào `ProcessOrdersAsync` (chạy 1 lần sau đặt địa chỉ) + log qua `ActivityLog`.
- **Không làm:** CHƯA vòng lặp mọi đơn; CHƯA đổi địa chỉ không-mặc-định (plan sau). KHÔNG dùng
  ClickAsync/Fill/native cho nghiệp vụ (click qua `TryHumanClickVisibleAsync`). KHÔNG tự commit.

## 3. Các bước thực hiện

### Bước 1 — Cờ in im lặng + helper thuần so text (Core)
- `BraveLaunchArgs.cs`: thêm `"--kiosk-printing"` vào danh sách args (in im lặng ra máy in mặc định khi
  gọi `window.print()`). Cập nhật `BraveLaunchArgsTests` nếu test assert danh sách/độ dài args.
- `ShopeeShippingNav.cs` (helper thuần, XML-doc TV) + test trong `ShopeeShippingNavTests.cs`:
  - `IsPrepareOrderButtonText(s)` → normalize == `"chuẩn bị hàng"`.
  - `IsConfirmArrangeButtonText(s)` → normalize == `"xác nhận"`.
  - `IsPrintSlipButtonText(s)` → normalize == `"in phiếu giao"`.
  - `IsDropoffTitleText(s)` → normalize **chứa** `"tự mang hàng tới bưu cục"` (title dropoff option).
  - `SanitizeFileName(s)` → bỏ ký tự không hợp lệ tên file (giữ chữ/số/`-_`, thay khác bằng `_`), trim;
    rỗng → `"phieu"`. (Dùng đặt tên file phiếu theo mã đơn.)

### Bước 2 — Enum kết quả (Core, file mới)
Tạo `src/XuLyDonShopee.Core/Services/ArrangeShipmentResult.cs`, XML-doc TV:
```csharp
public enum ArrangeShipmentResult
{
    Ok,            // đã xử lý xong 1 đơn (đã bấm In phiếu giao + tải/đóng tab)
    NoOrder,       // không còn đơn nào trong danh sách chờ xử lý → dừng vòng (plan sau dùng)
    Failed,        // lỗi bất ngờ / không có trang/phiên
    OrdersPageNotOpened,   // không mở được trang "Tất cả"
    PrepareNotFound,       // không thấy/không bấm được "Chuẩn bị hàng"
    ShipModalNotOpened,    // modal "Giao Đơn Hàng" không mở
    ConfirmFailed,         // không bấm được "Xác nhận"
    DetailModalNotOpened,  // modal "Thông Tin Chi Tiết" không mở
    PrintFailed,           // không bấm được "In phiếu giao" / không bắt được tab phiếu
}
```
(Ghi chú: tải phiếu THẤT BẠI KHÔNG coi là fail cả đơn — đơn đã được arrange; chỉ log cảnh báo. `Ok` nghĩa
là đã qua bước In phiếu giao. Việc tải/in là best-effort có log.)

### Bước 3 — `ProcessFirstOrderAsync` trên `ILoginSession`/`LoginSession`
Interface: `Task<ArrangeShipmentResult> ProcessFirstOrderAsync(string downloadDir, Action<string>? log = null, CancellationToken ct = default);`
XML-doc: xử lý ĐƠN ĐẦU TIÊN trong danh sách "Tất cả" (tab Chờ xử lý) — kiểu người, graceful không ném.

`LoginSession` implement (mọi click qua `TryHumanClickVisibleAsync`; `void L(string m) => log?.Invoke(m);`
gọi ở mỗi bước; dừng "đọc trang" ngẫu nhiên giữa bước như các method cũ; bọc try/catch ngoài → `Failed`):

1. `page = _context.Pages.Count>0 ? _context.Pages[0] : null;` null → `Failed`. `rng`, `mx,my` như cũ.
2. **Điều hướng "Tất cả"** (L("Về danh sách đơn (Tất cả)...")): tìm link
   `a[href='/portal/sale/order'][test-id='my orders new']` (fallback: duyệt `a.sidebar-submenu-item-link`
   khớp text "tất cả"; nếu submenu cụp → mở mục cha "Quản Lý Đơn Hàng" như `OpenShippingAddressSettingsAsync`).
   Click verified; chờ URL chứa `/portal/sale/order` (poll ~15s). Fallback cuối `GotoAsync(
   "https://banhang.shopee.vn/portal/sale/order")`. Không tới được → `OrdersPageNotOpened`.
   - Dừng "đọc trang" + chờ danh sách render.
3. **Tìm đơn ĐẦU TIÊN**: poll ~10s tìm card đơn đầu — `a.order-card[data-testid='order-item']` (đầu tiên).
   KHÔNG có card nào (list rỗng / "No Data") → **`NoOrder`** (L("Không còn đơn để xử lý.")).
4. **Bấm "Chuẩn bị hàng"** trong card đó: `button[data-testid='action-button-2']` (fallback: button trong
   `.order-actions` khớp `IsPrepareOrderButtonText`). Click verified. Không có/không click được →
   `PrepareNotFound`. L("Bấm Chuẩn bị hàng cho đơn <mã>").
   - Lấy **mã đơn** để đặt tên file: từ `.order-sn` trong card (text "Mã đơn hàng 260715..." → tách phần
     mã), qua `SanitizeFileName`. Không có → dùng mốc thời gian (truyền vào? — xem lưu ý thời gian).
5. **Modal "Giao Đơn Hàng"**: chờ `.eds-modal__box` có `.eds-modal__title` == "giao đơn hàng" (poll ~10s).
   Không mở → `ShipModalNotOpened`. Dừng "đọc modal".
   - **Chọn dropoff**: tìm `[data-testid='dropoff-option']` (fallback: card có title khớp
     `IsDropoffTitleText`). Nếu CHƯA có class `selected` → click verified; đã `selected` → bỏ qua (đã đúng).
   - **Bấm "Xác nhận"**: `[data-testid='arrange-shipment-confirm']` (fallback text `IsConfirmArrangeButtonText`).
     Click verified. Không được → `ConfirmFailed` (Hủy modal nếu có nút Hủy — best-effort). L("Đã xác nhận giao đơn.")
6. **Modal "Thông Tin Chi Tiết"**: chờ `.eds-modal__box` title == "thông tin chi tiết" (poll ~15s — có thể
   lâu do tạo vận đơn). Không mở → `DetailModalNotOpened`. Dừng "đọc modal".
7. **Bấm "In phiếu giao" + BẮT TAB MỚI + tải + in + đóng**:
   - Tìm `button[data-testid='print-button']` (fallback text `IsPrintSlipButtonText`). Không thấy → `PrintFailed`.
   - **Bắt tab mới** bằng `_context.RunAndWaitForPageAsync(async () => { click verified nút In phiếu giao; })`
     (timeout ~20s). Không bắt được page → `PrintFailed`. Gọi `newPage`.
   - `await newPage.WaitForLoadStateAsync(DOMContentLoaded)` (bọc try, timeout ngắn). `url = newPage.Url`;
     L($"Tab phiếu: {url}").
   - `Directory.CreateDirectory(downloadDir)`. Tên file: `{maDon}.pdf` (hoặc mốc thời gian) trong `downloadDir`.
   - **TẢI (best-effort, log rõ):**
     a. Thử `resp = await newPage.APIRequest.GetAsync(url)` (dùng context đã đăng nhập). Nếu `resp.Ok` và
        `body.Length` hợp lý → ghi bytes ra file. L($"Đã tải phiếu: {path} ({n} bytes)").
     b. Nếu (a) fail/rỗng/không phải file → fallback **CDP `Page.printToPDF`** trên `newPage`
        (`session = await _context.NewCDPSessionAsync(newPage); res = await session.SendAsync("Page.printToPDF", ...)`;
        base64 `data` → decode → ghi file). L("Tải bằng render PDF (fallback)."). Lỗi cả 2 → L cảnh báo, KHÔNG fail đơn.
   - **IN (best-effort):** `await newPage.EvaluateAsync("() => window.print()")` (đã có `--kiosk-printing` →
     in im lặng máy in mặc định). Bọc try/catch → L kết quả/cảnh báo.
   - **Đóng tab:** `await newPage.CloseAsync()` (bọc try). L("Đã đóng tab phiếu.").
   - Trả `Ok`.
8. Mọi lỗi bất ngờ → `Failed`. KHÔNG ném xuyên phiên.

**Lưu ý thời gian:** đặt tên file theo mã đơn (không cần mốc thời gian) — tránh phụ thuộc `DateTime` khó
test; nếu không có mã đơn thì `newPage`… dùng job_id trích từ URL làm tên (regex `job_id=([^&]+)`) → sanitize.

### Bước 4 — Wire vào `AccountSession.ProcessOrdersAsync` (chạy 1 lần) + log
Sau khi `SetPickupAddressAsync` trả `Ok` (nhánh đặt địa chỉ thành công hiện có), gọi thêm:
```csharp
var log = (Action<string>)(m => _services.Log.Append(_logLabel, m));
StatusText = "Đang xử lý đơn đầu tiên...";
var r = await s.ProcessFirstOrderAsync(@"D:\Phieu-giao-hang", log, tok).ConfigureAwait(false);
StatusText = r switch {
    ArrangeShipmentResult.Ok => "Đã xử lý 1 đơn (đã bấm In phiếu giao).",
    ArrangeShipmentResult.NoOrder => "Không còn đơn nào để xử lý.",
    ArrangeShipmentResult.OrdersPageNotOpened => "Không mở được danh sách đơn (Tất cả).",
    ArrangeShipmentResult.PrepareNotFound => "Không bấm được Chuẩn bị hàng.",
    ArrangeShipmentResult.ShipModalNotOpened => "Không mở được ô Giao Đơn Hàng.",
    ArrangeShipmentResult.ConfirmFailed => "Không bấm được Xác nhận giao đơn.",
    ArrangeShipmentResult.DetailModalNotOpened => "Không mở được Thông Tin Chi Tiết.",
    ArrangeShipmentResult.PrintFailed => "Không In phiếu giao được (không bắt được tab phiếu).",
    _ => "Xử lý đơn gặp lỗi — kiểm tra tay trong Brave.",
};
```
(Giữ nguyên `return okPickup;`? — KHÔNG: ProcessOrders bây giờ trả true nếu `r == Ok || r == NoOrder`
(coi như xong đợt), ngược lại false. Đối chiếu logic hiện tại + `_navigating`/finally GIỮ NGUYÊN.)

### Bước 5 — Test + build
- Test thuần trong `ShopeeShippingNavTests.cs`: các `Is...ButtonText`/`IsDropoffTitleText` (khớp/không,
  hoa-thường/space/xuống dòng) + `SanitizeFileName` (bỏ ký tự lạ, giữ chữ số -_, rỗng→"phieu").
- `BraveLaunchArgsTests`: cập nhật khớp có `--kiosk-printing`.
- Enum `ArrangeShipmentResult` không cần test riêng. KHÔNG test được new-tab/tải/in (không headless ở đây).
- `dotnet build` 0/0; `dotnet test` toàn bộ pass (nền hiện tại **306** + ca mới). WDAC → `-p:Deterministic=false`.
- Grep: không có `.ClickAsync(`/`.FillAsync(`/`Mouse.ClickAsync(`/`.CheckAsync(` mới cho nghiệp vụ; click
  đều qua `TryHumanClickVisibleAsync`.

## 4. Tiêu chí nghiệm thu
- [ ] Sau đặt địa chỉ Ok: app về "Tất cả" → lấy đơn ĐẦU → Chuẩn bị hàng → chọn "tự mang ra bưu cục" →
      Xác nhận → "In phiếu giao"; MỌI click qua hit-test kiểu người; có dừng ngẫu nhiên.
- [ ] Bắt được tab phiếu, TẢI phiếu về `D:\Phieu-giao-hang` (thử GET URL, fallback render PDF), **gửi lệnh
      in máy in mặc định** (`--kiosk-printing` + `window.print()`), rồi ĐÓNG tab.
- [ ] MỖI bước ghi log (panel + file) để smoke live thấy rõ; tải/in lỗi → chỉ cảnh báo, KHÔNG fail đơn đã arrange.
- [ ] Không còn đơn → trả `NoOrder`, StatusText báo "không còn đơn".
- [ ] Graceful mọi nhánh (selector đổi → enum tương ứng, KHÔNG ném); like-human; không native click.
- [ ] Build 0/0; test 306 nền + ca mới pass.
- [ ] Chỉ tạo/sửa: `ArrangeShipmentResult.cs` (mới), `ShopeeLoginService.cs`, `ShopeeShippingNav.cs`,
      `BraveLaunchArgs.cs`, `AccountSession.cs`, `ShopeeShippingNavTests.cs`, `BraveLaunchArgsTests.cs`.

## 5. Rủi ro & lưu ý
- **Bản chất tab `awbprint` chưa xác minh offline** (PDF trực tiếp hay HTML): thiết kế tải 2 tầng (GET
  URL → fallback render PDF) + in `window.print()`; **log rõ từng nhánh** để smoke live chỉ ra đúng cái gì
  xảy ra. Fable sẽ nhờ người dùng smoke 1 đơn + gửi lại log + kiểm file trong `D:\Phieu-giao-hang`.
- **Token 1 lần:** phải bắt tab + tải NGAY trong `RunAndWaitForPageAsync` (không rời đi làm việc khác).
  Nếu GET URL lần 2 bị từ chối (token đã tiêu khi tab tự load) → fallback render PDF cứu được.
- **`--kiosk-printing`** in ra MÁY IN MẶC ĐỊNH của Windows — cần máy đã cài sẵn máy in mặc định; nếu không
  có, `window.print()` có thể không ra giấy (vẫn không phá luồng — best-effort, log).
- `RunAndWaitForPageAsync` bắt cả trang mở bằng `window.open()` (nút In không phải `<a target=_blank>`).
- Shopee đổi DOM → mọi selector có fallback text + enum trả về; KHÔNG ném xuyên phiên.
- Smoke live cần phiên Shopee + đơn thật — Opus KHÔNG claim đã chạy live; phủ bằng build + test thuần +
  đọc code đối chiếu DOM người dùng cung cấp.
- WDAC/ISG khi test như plan trước.

---

## Báo cáo thực thi (Opus điền sau khi xong)

(để trống)
