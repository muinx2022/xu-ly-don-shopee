# Plan: Xử lý MỘT đơn — Chuẩn bị hàng → tự mang ra bưu cục → In phiếu giao (tải + in máy)

- **Ngày:** 2026-07-15
- **Trạng thái:** hoàn thành (code; smoke live 1 đơn chờ người dùng — cần đơn thật + máy in mặc định)
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)
- **Nghiệm thu:** Fable tự chạy build 0/0 + test **382/382** (+76 ca helper thuần); đọc toàn bộ diff
  (ProcessFirstOrderAsync + DownloadAndPrintSlipAsync + helper — bám mẫu poll/fallback/click-verified);
  panel rà soát đối kháng 27 agent → **1 lỗi cao + 2 vừa + 1 thấp**, đã sửa: (1) tải GET-URL không kiểm
  định dạng → lưu HTML rác thành .pdf + báo thành công giả + chặn fallback → thêm `LooksPdf` (magic %PDF)
  + content-type, chỉ nhận PDF thật ở cả 2 nhánh; (2) FindDropoffOptionAsync fallback trả tổ tiên → siết
  chỉ `.dropoff-method-card`; (3) hủy giữa xử lý bị báo Failed → rethrow OCE. Sửa xong build 0/0, test
  382/382. **CHƯA smoke live** phần bắt-tab/tải/in — chờ người dùng chạy 1 đơn thật + gửi log + kiểm
  `D:\Phieu-giao-hang` để chốt tab awbprint là PDF hay HTML.

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

## Báo cáo thực thi (Opus điền — 2026-07-15)

### Baseline TRƯỚC khi sửa
- `dotnet test XuLyDonShopee.sln -c Debug` → **Passed: 306, Failed: 0** (đúng nền plan nêu). Lần chạy này
  KHÔNG bị WDAC chặn (0x800711C7), nên không cần `-p:Deterministic=false`.

### Đã hoàn thành theo từng bước

**Bước 1 — Cờ in im lặng + helper thuần**
- `src/XuLyDonShopee.Core/Services/BraveLaunchArgs.cs`: thêm `"--kiosk-printing"` vào danh sách args (kèm
  comment giải thích in im lặng máy in mặc định).
- `src/XuLyDonShopee.Tests/BraveLaunchArgsTests.cs`: thêm test `CoCoKioskPrinting` (assert args chứa
  `--kiosk-printing`).
- `src/XuLyDonShopee.Core/Services/ShopeeShippingNav.cs`: thêm các hàm thuần (XML-doc TV):
  `IsAllOrdersHref` (chứa `/portal/sale/order`), `IsAllOrdersText` (== "tất cả"), `IsPrepareOrderButtonText`
  (== "chuẩn bị hàng"), `IsConfirmArrangeButtonText` (== "xác nhận"), `IsPrintSlipButtonText`
  (== "in phiếu giao"), `IsDropoffTitleText` (CHỨA "tự mang hàng tới bưu cục"), `IsShipOrderModalTitle`
  (== "giao đơn hàng"), `IsDetailModalTitle` (== "thông tin chi tiết"), `ExtractOrderCode` (token cuối,
  giữ hoa/thường), `ExtractJobId` (regex `job_id=([^&]+)`), `SanitizeFileName` (giữ chữ/số/-_, ký tự lạ→_,
  cắt _ thừa, rỗng→"phieu").
  - **Ghi chú lệch plan (nhỏ, đã tự quyết):** plan liệt kê 5 helper bắt buộc; tôi thêm các helper hỗ trợ
    (`IsAllOrdersHref/Text`, `IsShipOrderModalTitle`, `IsDetailModalTitle`, `ExtractOrderCode`,
    `ExtractJobId`) — đều là hàm thuần trong ĐÚNG file `ShopeeShippingNav.cs` mà implementation cần tới,
    và đều có test. Không phát sinh file ngoài phạm vi.

**Bước 2 — Enum kết quả**
- `src/XuLyDonShopee.Core/Services/ArrangeShipmentResult.cs` (MỚI): enum 9 giá trị đúng như plan
  (`Ok, NoOrder, Failed, OrdersPageNotOpened, PrepareNotFound, ShipModalNotOpened, ConfirmFailed,
  DetailModalNotOpened, PrintFailed`), XML-doc TV.

**Bước 3 — `ProcessFirstOrderAsync`**
- Interface `ILoginSession` (trong `ShopeeLoginService.cs`): thêm
  `Task<ArrangeShipmentResult> ProcessFirstOrderAsync(string downloadDir, Action<string>? log = null, CancellationToken ct = default)`
  + XML-doc TV.
- `LoginSession` implement đầy đủ 8 bước:
  1. Lấy `page`; `rng`, `mx/my` như các method cũ.
  2. `GoToAllOrdersAsync` (kiểu người CÓ HIT-TEST: tìm link "Tất cả" `a[test-id='my orders new']` / duyệt
     `a.sidebar-submenu-item-link` khớp href `/portal/sale/order` + text "tất cả"; submenu cụp → click mục
     cha "Quản Lý Đơn Hàng" verified rồi tìm lại; chờ URL ~15s; fallback cuối `GotoAsync`). Không tới được →
     `OrdersPageNotOpened`.
  3. `FindFirstOrderCardAsync` (`a.order-card[data-testid='order-item']` → `[data-testid='order-item']` →
     `.order-card`, poll ~10s). Rỗng → `NoOrder`.
  4. Mã đơn từ `.order-sn` qua `ExtractOrderCode`. Bấm "Chuẩn bị hàng"
     (`button[data-testid='action-button-2']` → fallback `.order-actions`/card theo text) qua
     `TryHumanClickVisibleAsync`. Không được → `PrepareNotFound`.
  5. Chờ modal "Giao Đơn Hàng" (`WaitModalByTitleAsync`, `.eds-modal__title`/`.title`). Chọn dropoff
     (`[data-testid='dropoff-option']` / fallback text CHỨA; CHỈ click nếu class chưa có "selected", click
     lại vẫn an toàn). Bấm "Xác nhận" (`[data-testid='arrange-shipment-confirm']` / fallback text) verified.
     Không mở modal → `ShipModalNotOpened`; không bấm được Xác nhận → `ConfirmFailed`.
  6. Chờ modal "Thông Tin Chi Tiết" (~15s). Không mở → `DetailModalNotOpened`.
  7. Tìm "In phiếu giao" (`button[data-testid='print-button']` / fallback text). BẮT tab mới bằng
     `_context.RunAndWaitForPageAsync(...)` với click nút NGAY trong action (token 1 lần), timeout 20s. Không
     bắt được → `PrintFailed`. Trong `DownloadAndPrintSlipAsync`: (a) tải qua `newPage.APIRequest.GetAsync`
     (ghi file nếu Ok & body > 1000 bytes); (b) fallback CDP `Page.printToPDF` (base64 `data` → decode →
     ghi); in qua `newPage.EvaluateAsync("() => window.print()")` (bọc timeout 5s chống treo); đóng tab. Tên
     file: `{mã đơn}` > `{job_id}` > `phieu`, qua `SanitizeFileName` + `.pdf`, lưu `D:\Phieu-giao-hang`. Trả
     `Ok`.
  8. Try/catch ngoài → `Failed`. Tải/in thất bại KHÔNG hạ kết quả (chỉ log cảnh báo).
- MỌI bước gọi `L(...)` (callback log). MỌI click nghiệp vụ qua `TryHumanClickVisibleAsync` /
  `HumanMoveAndClickVerifiedAsync` (đều hit-test verified); dừng "đọc trang/modal" ngẫu nhiên xen kẽ.

**Bước 4 — Wire vào `AccountSession.ProcessOrdersAsync`**
- `src/XuLyDonShopee.App/Services/AccountSession.cs`: sau nhánh `pick == Ok`, gọi
  `ProcessFirstOrderAsync(@"D:\Phieu-giao-hang", log, tok)` với `log = m => _services.Log.Append(_logLabel, m)`;
  set `StatusText` theo switch enum đúng như plan; `return r is Ok or NoOrder`. Nhánh `pick != Ok` giữ nguyên
  `return false`. `_navigating`/`finally` GIỮ NGUYÊN.

**Bước 5 — Test + build**
- Thêm test thuần trong `ShopeeShippingNavTests.cs` cho toàn bộ helper mới (11 nhóm Theory).

### Kết quả kiểm chứng (lệnh thật đã chạy)
- `dotnet build XuLyDonShopee.sln -c Debug` → **Build succeeded. 0 Warning(s), 0 Error(s)**.
  - (Có 1 lỗi biên dịch trung gian đã sửa: `ICDPSession.SendAsync` trả `Task<JsonElement?>` chứ không phải
    `JsonElement` — đã đổi sang `res is JsonElement je && je.TryGetProperty(...)`.)
- `dotnet test XuLyDonShopee.sln -c Debug --no-build` → **Passed: 382, Failed: 0, Skipped: 0**
  (306 nền + 76 ca mới: 75 ca ShopeeShippingNav + 1 ca BraveLaunchArgs). Không bị WDAC chặn.
- Grep `\.ClickAsync\(|\.FillAsync\(|Mouse\.ClickAsync\(|\.CheckAsync\(|\.PressAsync\(|\.SetCheckedAsync\(`
  trong `ShopeeLoginService.cs` → KHÔNG có; chỉ còn `page.Keyboard.TypeAsync` DUY NHẤT ở luồng login CŨ
  (`HumanFillAsync`), không phải code mới. Code mới 100% click qua hit-test verified.
- `git status --short` → đúng 7 file trong plan (1 mới + 6 sửa), không dư.

### Vướng mắc / chưa kiểm chứng được (trung thực)
- **KHÔNG smoke live được** (đúng như plan dặn): môi trường không có phiên Shopee đăng nhập + đơn thật, và
  WDAC. Toàn bộ nhánh **bắt tab mới / tải phiếu / in / đóng tab** mới CHỈ được phủ bằng build + đối chiếu
  code với DOM (data-testid) người dùng cung cấp — CHƯA chạy thật.
- **`Page.printToPDF` ở chế độ headful (Brave thật qua CDP)**: một số bản Chromium từng chỉ hỗ trợ
  `printToPDF` ở headless. Nếu bản Brave hiện tại từ chối, nhánh (b) sẽ log "Render PDF fallback chưa được"
  và (nếu (a) cũng fail) log cảnh báo "CHƯA tải được phiếu" — KHÔNG phá đơn. Cần smoke để biết nhánh (a)
  GET-URL hay (b) render PDF thực sự ăn.
- **`window.print()` in im lặng** phụ thuộc máy có sẵn máy in mặc định; không có thì không ra giấy (vẫn
  best-effort, có log). Chưa kiểm được trên máy này.
- **Bản chất tab `awbprint`** (PDF trực tiếp hay HTML) vẫn chưa xác minh offline — thiết kế tải 2 tầng +
  log rõ từng nhánh để lần smoke đầu chỉ ra đúng cái gì xảy ra (xem log panel + `D:\Phieu-giao-hang`).

### Đề xuất cho Fable khi smoke
- Chạy 1 đơn thật, gửi lại các dòng log (nhãn tài khoản) quanh "Tab phiếu: ..." để biết URL + nhánh tải nào
  ăn; kiểm thư mục `D:\Phieu-giao-hang` có file `.pdf` (tên = mã đơn) và kích thước hợp lý.
- Nếu cả 2 nhánh tải đều fail: cân nhắc plan sau dùng `page.WaitForDownloadAsync` (nếu nút In kích hoạt
  download trực tiếp) hoặc lưu HTML rồi in, tuỳ bản chất tab `awbprint` quan sát được.

---

## Sửa theo nghiệm thu (2026-07-15) — panel rà soát đối kháng 27 agent

Fable nghiệm thu (build 0/0 + 382/382) rồi panel phản biện tìm 3 điểm, tất cả CHỈ trong
`src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs`. Đã sửa cả 3:

- **Lỗi 1 (CAO) — tải phiếu lưu nhầm HTML thành `.pdf` + chặn fallback:** trong
  `DownloadAndPrintSlipAsync`, thêm helper `LooksPdf(byte[])` (magic bytes `%PDF` = 25 50 44 46).
  - Nhánh (a) GET URL: chỉ nhận khi `resp.Ok && body.Length>0 && (content-type chứa "pdf" || LooksPdf(body))`
    → ghi + `downloaded=true`. Ngược lại log "GET URL trả nội dung KHÔNG phải PDF ... — bỏ, thử render PDF."
    và KHÔNG set `downloaded` (để fallback CDP chạy). Bỏ điều kiện cũ `Length>1000` ghi thẳng.
  - Nhánh (b) CDP `printToPDF`: chỉ ghi + `downloaded=true` khi `LooksPdf(bytes)`; ngược lại log cảnh báo,
    KHÔNG ghi. Không bao giờ ghi file rác; câu "CHƯA tải được phiếu" giữ nguyên khi cả 2 fail.
- **Lỗi 2 (VỪA) — `FindDropoffOptionAsync` fallback trả tổ tiên:** đổi selector fallback từ `"div,label,li"`
  sang `".dropoff-method-card"` (class thật `dropoff-method-card selected card`); duyệt card khớp
  `IsDropoffTitleText` + có bounding box → trả card đó; không thấy → null (thà không click còn hơn nhắm
  giữa div tổ tiên bọc cả modal).
- **Lỗi 3 (THẤP) — hủy giữa xử lý đơn bị báo Failed:** thêm `catch (OperationCanceledException) { throw; }`
  NGAY TRƯỚC bare `catch { return Failed; }` của `ProcessFirstOrderAsync` (để `ProcessOrdersAsync` ở App bắt
  OCE → dừng sạch). Trong `DownloadAndPrintSlipAsync`, thêm `catch (OperationCanceledException) { throw; }`
  trước các `catch` bọc thao tác ct: `WaitForLoadStateAsync`, GET URL (có `File.WriteAllBytesAsync(...,ct)`),
  CDP printToPDF (có `File.WriteAllBytesAsync(...,ct)`), `window.print()` (có `Task.Delay(...,ct)`).

**Kiểm chứng sau sửa:** `dotnet build XuLyDonShopee.sln -c Debug` → **0 Warning, 0 Error**;
`dotnet test --no-build` → **382 pass, 0 fail** (không bị WDAC chặn → không cần `-p:Deterministic=false`).
Chỉ đụng đúng `ShopeeLoginService.cs`; không commit (chờ Fable nghiệm thu lại).

- **Sửa sau smoke thật (nút In phiếu giao hiện muộn):** người dùng smoke phát hiện SAU khi bấm "Xác nhận",
  modal "Thông Tin Chi Tiết" đổi trạng thái (Shopee tạo vận đơn) và nút `button[data-testid='print-button']`
  chỉ HIỆN MUỘN → code cũ tìm nút single-shot ngay sau delay cố định nên trả `PrintFailed`. Đã thay lời gọi
  `FindPrintButtonAsync(detailModal)` (single-shot, đã XÓA vì thành dead-code) bằng helper mới
  `WaitPrintButtonAsync(page, 30000, ct)` — POLL page-level 400ms/vòng tới 30s (ưu tiên
  `button[data-testid='print-button']` có bounding box, fallback duyệt button khớp `IsPrintSlipButtonText`
  có bounding box), kèm 2 dòng log ("Chờ nút In phiếu giao xuất hiện..." / "Nút In phiếu giao đã sẵn sàng").
  Phần bắt tab (`RunAndWaitForPageAsync`) + `DownloadAndPrintSlipAsync` GIỮ NGUYÊN. Build **0/0**, test
  **382 pass, 0 fail**. Chỉ đụng `ShopeeLoginService.cs`; không commit.

### Sửa theo 3 lượt smoke tiếp (2026-07-15/16) — hoàn thiện luồng In phiếu + đóng modal

Người dùng smoke thật 3 lượt nữa, ra 3 nhóm sửa (đều trong `ShopeeLoginService.cs`, riêng nhóm 2 thêm
1 hàm thuần + test trong `ShopeeShippingNav.cs`/`ShopeeShippingNavTests.cs`):

1. **Nút "In phiếu giao" trong modal có box nhưng bị lớp che → click không ăn (bước 6):** đổi
   `WaitPrintButtonAsync` → `WaitPrintButtonClickableAsync` — ngoài "có bounding box" còn HIT-TEST tâm nút
   bằng `IsPointOnElementAsync` (chỉ trả khi `elementFromPoint` tại nút đúng là nút → lớp che "đang tạo vận
   đơn" đã tan). Bọc bước 6 thành VÒNG THỬ LẠI ~35s: mỗi vòng re-find nút TƯƠI (chống stale) → click bắt tab
   qua `RunAndWaitForPageAsync` (timeout 8s/lần); chống double-tab bằng cờ `clicked` + nhận tab awbprint mở
   muộn (`SafeUrlHasAwbprint`). Tổng quát hóa `WaitPrintButtonClickableAsync(page, textMatch, timeoutMs, ct)`
   để dùng lại cho cả nút modal ("in phiếu giao") lẫn nút tab phiếu ("in phiếu").
2. **Tab phiếu là HTML "Xem trước bản in" (nhãn SPX) có nút "In phiếu" RIÊNG — tải & in đúng cách
   (`DownloadAndPrintSlipAsync`):**
   - TẢI: ĐẢO thứ tự — ưu tiên CDP `Page.printToPDF` (render HTML nhãn thành PDF, `LooksPdf`-check) làm
     CHÍNH; GET URL chỉ fallback (vẫn %PDF/content-type check). Lưu `D:\Phieu-giao-hang` như cũ.
   - IN: THAY `window.print()` bằng CLICK nút "In phiếu" trên tab (kiểu người verified) — thêm hàm thuần
     `IsPrintButtonText` (== "in phiếu", KHÔNG khớp "in phiếu giao") + 7 ca test; chờ nút bấm được bằng
     `WaitPrintButtonClickableAsync(newPage, IsPrintButtonText, 15s, ct)` rồi `TryHumanClickVisibleAsync`
     (mx/my riêng theo viewport tab). Không tìm/không bấm được → fallback `window.print()`
     (`FirePrintFallbackAsync`). Với `--kiosk-printing` → in im lặng máy in mặc định.
3. **Modal "Thông Tin Chi Tiết" trên tab chính KHÔNG tự đóng sau khi in → thêm bước đóng X:** cuối
   `ProcessFirstOrderAsync`, sau `DownloadAndPrintSlipAsync`, gọi `CloseDetailModalAsync` (best-effort):
   re-find modal TƯƠI (đã đóng → thôi) → dò nút X (`.eds-modal__close` → icon trong `.eds-modal__header` →
   `[aria-label='Close'/'close'/'Đóng']`, chỉ nhận có box) → click verified → chờ modal biến mất ~5s; fallback
   `Escape` 1 lần; vẫn còn → L cảnh báo (KHÔNG hạ Ok). Rồi mới `return Ok`.

Mọi click nghiệp vụ (nút modal, dropoff, xác nhận, chuẩn bị, nút In phiếu tab, nút X) đều qua
`TryHumanClickVisibleAsync` (hit-test verified). Chỉ có `Keyboard.PressAsync("Escape")` là thao tác bàn
phím ĐÓNG modal (không phải click chuột nghiệp vụ — đã được duyệt). OCE rethrow giữ nguyên ở mọi khối ct.

**Kiểm chứng sau 3 lượt:** `dotnet build` → **0 Warning, 0 Error** (không dead-code); `dotnet test --no-build`
→ **389 pass, 0 fail** (382 nền + 7 ca `IsPrintButtonText`). Không bị WDAC chặn. Phạm vi: `ShopeeLoginService.cs`,
`ShopeeShippingNav.cs`, `ShopeeShippingNavTests.cs`. KHÔNG commit (chờ Fable nghiệm thu lại).

**CHƯA smoke được các nhánh mới** (không có phiên/đơn thật ở đây): thứ tự tải mới, click nút "In phiếu" trên
tab, và đóng modal X — cần người dùng smoke 1 đơn, gửi lại log + kiểm `D:\Phieu-giao-hang`.

### Sửa lượt smoke tiếp (2026-07-16) — tab phiếu mở MUỘN, RunAndWaitForPageAsync bỏ lỡ

Smoke báo "không In phiếu giao được, không bắt được tab phiếu" DÙ modal + nút "In phiếu giao" vẫn hiện đúng
(vận đơn đã tạo, có tracking number). Kết luận: nút BẤM ĐƯỢC nhưng tab phiếu mở MUỘN (Shopee gọi API tạo bản
in) → quá timeout ngắn của `RunAndWaitForPageAsync` + vòng retry re-click gây rối. Sửa (chỉ
`ShopeeLoginService.cs`, `ProcessFirstOrderAsync` bước 6): BỎ `RunAndWaitForPageAsync` + vòng retry, thay
bằng TÁCH 2 PHA có log kỹ:
- **PHA 1 — bấm đáng tin:** chụp `before = _browser.Contexts.SelectMany(c => c.Pages)` TRƯỚC khi bấm; bấm
  (re-find nút TƯƠI qua `WaitPrintButtonClickableAsync`) tới khi `clicked=true` hoặc ~20s. Không bấm được →
  L + `PrintFailed`.
- **PHA 2 — chờ tab (poll, ~25s):** poll MỌI context (`_browser.Contexts.SelectMany(...).FirstOrDefault(p =>
  p != page && !before.Contains(p))`) tìm trang MỚI — KHÔNG dùng `RunAndWaitForPageAsync`. Thấy → chờ tiếp
  ~10s tới khi URL có "awbprint" (tab có thể khởi đầu about:blank). Không thấy tab sau 25s → L + `PrintFailed`.
- Log từng pha: "Đã bấm In phiếu giao, chờ tab phiếu mở..." / "Đã bắt được tab phiếu (awbprint OK|URL chưa
  phải awbprint)" / "...KHÔNG thấy tab phiếu mở ra sau 25s." để smoke chỉ ra đúng chỗ hỏng.
- GIỮ NGUYÊN `DownloadAndPrintSlipAsync` + `CloseDetailModalAsync` + các helper (`SafeUrlHasAwbprint` vẫn
  dùng ở pha 2). Bỏ logic double-tab cũ của `RunAndWaitForPageAsync`; `RunAndWaitForPageAsync` không còn
  trong code (chỉ còn trong comment giải thích).

**Kiểm chứng:** `dotnet build` → **0 Warning, 0 Error** (không dead-code warning — `clicked` vẫn dùng ở
prepare/confirm, `RunAndWaitForPageAsync` đã gỡ khỏi code); `dotnet test --no-build` → **389 pass, 0 fail**.
Không WDAC chặn. Chỉ đụng `ShopeeLoginService.cs`. KHÔNG commit. Vẫn CHƯA smoke live nhánh 2-pha này.

### Sửa lượt smoke tiếp (2026-07-16) — chuột KHÔNG bấm được "In phiếu giao" (hit-test lần hai false-negative)

Smoke báo "chuột không bấm được In phiếu giao" (PHA 1 không bao giờ thành công) DÙ các click trước (Chuẩn bị
hàng/Xác nhận) ăn bình thường. Nguyên nhân: nút đã được `WaitPrintButtonClickableAsync` xác nhận CLICKABLE
(hit-test pass), nhưng `TryHumanClickVisibleAsync` lại HIT-TEST LẦN HAI lúc bấm → đôi lúc false-negative →
không nhả chuột. Sửa 2 file:
- **`BraveLaunchArgs.cs`:** thêm cờ `--disable-popup-blocking` (nút In phiếu giao mở tab bằng `window.open`
  → tránh bị chặn popup khiến tab không mở). Thêm test `CoCoDisablePopupBlocking` trong `BraveLaunchArgsTests`.
- **`ShopeeLoginService.cs` (bước 6):** PHA bấm thay `TryHumanClickVisibleAsync` bằng `HumanMoveAndClickAsync`
  (click KIỂU NGƯỜI THẲNG: chuột cong + down/trễ/up, KHÔNG kiểm hit-test lần hai — nút đã xác nhận clickable;
  vẫn like-human, KHÔNG native). `HumanMoveAndClickAsync` không trả cờ "clicked" → thành công THẬT xác nhận
  bằng TAB PHIẾU mở ra. Gộp bấm + chờ tab thành 1 vòng ~40s: mỗi vòng KIỂM tab đã mở TRƯỚC khi bấm lại
  (chống double-tab) → nếu chưa, re-find nút TƯƠI + `HumanMoveAndClickAsync` → chờ tab ~8s rồi lặp. Không
  thấy tab sau 40s → `PrintFailed`. GIỮ đoạn chờ URL awbprint (~10s) + `DownloadAndPrintSlipAsync` +
  `CloseDetailModalAsync`. Nút "In phiếu" trên TAB PHIẾU vẫn dùng `TryHumanClickVisibleAsync` (chưa có báo
  lỗi, KHÔNG đổi đợt này).

**Kiểm chứng:** `dotnet build` → **0 Warning, 0 Error**; `dotnet test --no-build` → **390 pass, 0 fail**
(389 nền + 1 ca `CoCoDisablePopupBlocking`). Không WDAC chặn. Không native click (grep
`.ClickAsync/.FillAsync/Mouse.ClickAsync/.CheckAsync/.SetCheckedAsync` = 0). Phạm vi: `BraveLaunchArgs.cs`,
`BraveLaunchArgsTests.cs`, `ShopeeLoginService.cs`. KHÔNG commit. CHƯA smoke live nhánh này.
