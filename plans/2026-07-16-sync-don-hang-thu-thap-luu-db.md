# Plan: Sync Đơn hàng (phần 1) — nút Sync, duyệt tab "Tất cả" mọi trang, lưu đơn vào DB

- **Ngày:** 2026-07-16
- **Trạng thái:** hoàn thành (chờ người dùng smoke — nếu chỉ sync được 1 trang, gửi dòng "Chẩn đoán pager" để tinh chỉnh selector)
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)

## 1. Bối cảnh & mục tiêu

Người dùng yêu cầu (đã chốt qua hỏi-đáp): thêm mục **"Sync Đơn hàng"** ở chi tiết tài khoản → app vào **Quản lý đơn hàng / Tất cả, tab "Tất cả"** → duyệt **TOÀN BỘ các trang** danh sách đơn (có chốt chặn an toàn) → **lưu thông tin đơn về DB** (app.db). Phần màn xem + xuất Excel/CSV là plan 2 (`2026-07-16-sync-don-hang-man-xem-xuat-csv.md`), làm SAU plan này.

Người dùng cung cấp DOM thật của danh sách đơn tab "Tất cả" — cấu trúc mỗi đơn (`a.order-card[data-testid='order-item']`):

| Dữ liệu | Selector/nguồn | Ví dụ |
|---|---|---|
| ID đơn Shopee | `href` của thẻ `a` — `/portal/sale/order/<số>` | 237900524283161 |
| Mã đơn hàng | `.order-sn` (bỏ tiền tố "Mã đơn hàng ") | 260716T6NPV58S |
| Người mua | `.buyer-username` | quynhsuugiacshoppi |
| Sản phẩm (CÓ THỂ NHIỀU) | mỗi `.item`: `.item-name`, `.item-description` (bỏ "Variation: "), `.item-amount` (bỏ "x"), `.item-image` src | Giày Búp Bê... / Bệt ĐEN GBB04 Nơ Dây,37 / 1 |
| Tổng tiền | `.total-price` | ₫166.500 |
| Thanh toán | `.payment-method` | Thanh toán khi nhận hàng |
| Trạng thái | `.status-info-col .status` | Đã hủy / Chờ lấy hàng |
| Mô tả trạng thái | `.status-description` | Đã hủy tự động bởi hệ thống Shopee |
| Lý do hủy | text `.eds-popover__content` trong cột trạng thái chứa "Lý do hủy:" (phần tử ẩn `display:none` — `textContent` vẫn đọc được) | Hủy đơn hàng vì hành vi giao dịch bất thường. |
| Kênh/ĐVVC | `.maksed-channel-name` / `.fulfilment-channel-name` | Nhanh / SPX Express |
| Mã vận đơn | `.tracking-number` (có thể không có) | SPXVN068067521447 |

Lưu ý: đơn "Chờ lấy hàng" có biến thể card dạng package (`.package-list-of-package-level-order-card`) nhưng các selector con (item/name/price/status/tracking) giống nhau — JS quét theo selector con TRONG từng card là phủ cả hai dạng. **DOM phần PHÂN TRANG chưa có** (người dùng chưa gửi) — bước phân trang phải viết PHÒNG THỦ + log chẩn đoán (chi tiết ở C).

## 2. Phạm vi

- **Làm:**
  - Bảng `orders` + repository (đặt cùng pattern với repo hiện có của dự án — khảo sát `Database.cs`/repo Accounts trước khi viết).
  - DTO + method `SyncAllOrdersAsync` trong `ShopeeLoginService` (Core, KHÔNG đụng DB — chỉ trả dữ liệu).
  - `AccountSession.SyncOrdersAsync()` (App): điều hướng, gọi thu thập, upsert DB, log tiến trình.
  - Nút "Sync Đơn hàng" (icon + tooltip) trong hàng nút form chi tiết + command trong `AccountsViewModel`.
  - Tổng quát hóa hàm ensure-tab hiện có để dùng cho tab "Tất cả".
- **Không làm:** màn xem đơn + xuất CSV (plan 2); không đụng luồng Xử lý đơn/lưu phiếu; không xóa đơn cũ trong DB (chỉ upsert).

## 3. Các bước thực hiện

### A. DB — bảng `orders` + repository

1. Khảo sát `src/XuLyDonShopee.Core/Data/Database.cs` (Initialize tạo bảng thế nào) + repo hiện có (Accounts/Proxies) để theo đúng pattern.
2. Bảng `orders` (tạo trong `Initialize()` với `CREATE TABLE IF NOT EXISTS`):
   - `id INTEGER PRIMARY KEY AUTOINCREMENT`
   - `account_id INTEGER NOT NULL` (id tài khoản trong bảng accounts)
   - `order_sn TEXT NOT NULL` (mã đơn) — `UNIQUE(account_id, order_sn)`
   - `shopee_order_id TEXT` (số trong href)
   - `buyer_username TEXT`
   - `items_json TEXT` (mảng JSON `{name, variation, amount, image}`) + `item_count INTEGER` + `item_summary TEXT` (tên item đầu, để plan 2 hiển thị nhanh)
   - `total_price INTEGER` (parse "₫166.500" → 166500; parse lỗi → NULL) + `total_price_text TEXT` (giữ nguyên văn)
   - `payment_method TEXT`, `status TEXT`, `status_description TEXT`, `cancel_reason TEXT`
   - `channel TEXT`, `carrier TEXT`, `tracking_number TEXT`
   - `synced_at TEXT` (ISO-8601 lúc sync), `created_at TEXT`, `updated_at TEXT`
3. `OrdersRepository`: `UpsertMany(accountId, IEnumerable<SyncedOrder>, DateTime syncedAt)` → INSERT ... ON CONFLICT(account_id, order_sn) DO UPDATE (cập nhật mọi cột dữ liệu + updated_at/synced_at, giữ created_at); trả `(int inserted, int updated)`. Thêm `CountByAccount(accountId)` (plan 2 dùng). Test thuần cho parse tiền + upsert (SQLite in-memory nếu pattern test hiện có cho phép — xem `Database` test hiện trạng; không có thì test parse tiền là tối thiểu).

### B. Core — DTO + `SyncAllOrdersAsync` (`ShopeeLoginService.cs`)

1. DTO `SyncedOrder` (Core/Models hoặc cạnh service — theo pattern): OrderSn, ShopeeOrderId, BuyerUsername, ItemsJson, ItemCount, ItemSummary, TotalPriceText, TotalPrice (long?), PaymentMethod, Status, StatusDescription, CancelReason, Channel, Carrier, TrackingNumber.
2. **Tổng quát hóa ensure-tab**: đổi `EnsureToShipTabAsync` thành private `EnsureOrderListTabAsync(page, string tabTestId, Func<string?,bool> textMatch, string tabLabel, mx, my, rng, L, ct)` (logic giữ NGUYÊN — chỉ tham số hóa testid/text/log label); `EnsureToShipTabAsync` giữ chữ ký cũ, gọi hàm mới với `l1-tab-toship`/`IsToShipTabText`/"Chờ lấy hàng" (KHÔNG đổi hành vi Xử lý đơn). Thêm `ShopeeShippingNav.IsAllTabText` (chuẩn hóa == "tất cả") + test.
3. `public async Task<SyncOrdersResult> SyncAllOrdersAsync(Action<string>? log, CancellationToken ct)` (thêm vào `ILoginSession`): trả `record SyncOrdersResult(List<SyncedOrder> Orders, int Pages, bool ReachedPageCap)`:
   - Điều hướng: page = `_context.Pages[0]`; nếu URL chưa phải trang danh sách → `GoToAllOrdersAsync`; nếu ĐÃ ở đó → `GotoAsync(AllOrdersUrl)` reload sạch (mẫu bước 1 `ProcessFirstOrderAsync`) → kiểm `IsAllOrdersHref` → `EnsureOrderListTabAsync(l1-tab-all, IsAllTabText, "Tất cả")` → delay đọc trang.
   - Mỗi trang: `EvaluateAsync<string>` MỘT đoạn JS CHỈ-ĐỌC quét mọi `a[data-testid='order-item']` theo bảng selector ở mục 1 (bọc từng card trong try để 1 card lạ không phá cả trang; item lấy MẢNG; cancel reason: tìm trong cột status phần tử `.eds-popover__content` có textContent chứa "Lý do hủy:", cắt bỏ tiền tố), trả `JSON.stringify(mảng)`; C# parse `JsonDocument` → List<SyncedOrder> (parse tiền: bỏ mọi ký tự không phải số). Log `"Sync trang {n}: {m} đơn."`.
   - Khử trùng lặp theo OrderSn trong phiên sync (trang có thể trùng khi Shopee đổi dữ liệu giữa chừng).
   - **Phân trang (PHÒNG THỦ — chưa có DOM pager thật):** tìm nút "trang sau" lần lượt qua các selector khả dĩ: `.eds-pager button.eds-pager__button-next`, `[class*='pager'] button:last-of-type`, `button[class*='next']`, `li.eds-pager__next button` — nhận nút có bounding box và KHÔNG disabled (`disabled`/`aria-disabled`/class chứa `disabled`). TÌM THẤY → click kiểu người (`TryHumanClickVisibleAsync`) → chờ danh sách ĐỔI (poll ≤10s: order_sn ĐẦU TIÊN của trang khác đi, hoặc số card đổi) → delay ngẫu nhiên 1500–3500ms → quét tiếp. KHÔNG thấy nút / nút disabled → hết trang, dừng. LẦN ĐẦU không thấy pager → log MỘT dòng chẩn đoán DOM pager (đếm + liệt kê class các phần tử khớp `[class*='pager'],[class*='pagination']`, cắt gọn) — dữ liệu tinh chỉnh selector sau lần chạy đầu.
   - Chốt chặn: `private const int MaxSyncPages = 20;` — chạm cap → dừng, `ReachedPageCap = true`, log rõ.
   - OCE ném xuyên; exception khác → trả những gì đã gom được + log lỗi (best-effort, KHÔNG mất dữ liệu đã quét).
4. Mọi thao tác: điều hướng/click kiểu người có hit-test như chuẩn dự án; JS chỉ ĐỌC.

### C. App — `AccountSession.SyncOrdersAsync()` + nút UI

1. `AccountSession.SyncOrdersAsync()` (public, thêm vào `IAccountSession`): guard `_navigating` + State Running như `ProcessOrdersAsync` (loại trừ lẫn nhau với Xử lý đơn/Kiểm tra); StatusText "Đang sync đơn hàng (tab Tất cả)..."; gọi `s.SyncAllOrdersAsync(log, tok)`; xong → `OrdersRepository.UpsertMany(...)` → StatusText + log: `"Sync xong: {tổng} đơn / {pages} trang — thêm {inserted} mới, cập nhật {updated}."` (+ " (chạm chốt chặn 20 trang)" nếu cap); lỗi → log + StatusText, không ném. finally reset `_navigating`.
2. `AccountsViewModel`: `SyncOrdersCommand` (+ `CanSyncOrders` — bật khi phiên tài khoản đang chọn chạy, mẫu `CanCheckOrders`); gọi session tương ứng.
3. `AccountsView.axaml`: thêm nút icon vào hàng nút form (nhóm phải, cạnh "Kiểm tra"): glyph text-only gợi ý `⇊` hoặc `⇅` (executor chọn glyph đơn sắc render tốt, ghi rõ), `Classes="secondary formIcon"`, `ToolTip.Tip="Sync Đơn hàng — vào Quản lý đơn hàng tab Tất cả, duyệt mọi trang và lưu thông tin đơn về máy"`.

### D. Kiểm chứng

1. Build 0 warning; test pass (402 + test mới A.3/B.2).
2. Grep: `SyncAllOrdersAsync` có trong interface + hiện thực; `EnsureToShipTabAsync` vẫn được `ProcessFirstOrderAsync` gọi với hành vi cũ; bảng orders tạo trong Initialize.
3. Smoke thật (người dùng): bấm nút Sync trên tài khoản đang chạy → log "Sync trang 1: N đơn." ... tổng kết "thêm X mới, cập nhật Y"; bấm lần 2 → chủ yếu "cập nhật"; kiểm DB (plan 2 sẽ có màn xem; tạm thời tin log + có thể xem bằng SQLite tool). Nếu shop nhiều trang mà chỉ sync 1 trang → gửi dòng log chẩn đoán pager để tinh chỉnh selector. Executor ghi rõ mục này chờ người dùng.

## 5. Rủi ro & lưu ý

- **Selector phân trang là ĐOÁN CÓ CƠ SỞ** (chưa có DOM thật) — bắt buộc code phòng thủ + log chẩn đoán pager; KHÔNG được lặp vô hạn (cap 20 trang + điều kiện danh-sách-phải-ĐỔI sau click).
- Danh sách render dần — chờ ổn định trước khi quét mỗi trang (mẫu chờ + poll số card như `FindFirstProcessableOrderAsync`).
- Card 2 dạng (thường/package) — JS quét selector con trong card, bọc try từng card.
- `SyncOrdersAsync` phải loại trừ với `ProcessOrdersAsync`/`CheckOrdersAsync` qua `_navigating` (không 2 luồng chuột trên 1 trang).
- Core KHÔNG tham chiếu DB App — Core trả DTO, App lưu.
- Hai phiên Claude có thể cùng mở repo — executor `git status` trước khi sửa (HEAD ghi trong prompt); thấy thay đổi lạ → DỪNG báo.

---

## Báo cáo thực thi (Opus điền sau khi xong)

Opus làm đủ A/B/C: bảng `orders` (UNIQUE account_id+order_sn) + `OrdersRepository.UpsertMany` transaction (giữ created_at khi cập nhật) + `CountByAccount`; DTO `SyncedOrder` + `SyncOrdersResult`; `SyncAllOrdersAsync` (điều hướng/reload → tab "Tất cả" qua `EnsureOrderListTabAsync` — tổng quát hóa từ `EnsureToShipTabAsync`, wrapper giữ nguyên hành vi Xử lý đơn → vòng trang: chờ danh sách ổn định → ScanOrdersJs chỉ-đọc → khử trùng OrderSn → nút trang-sau phòng thủ 4 selector + điều kiện danh-sách-đổi + cap 20 trang + log chẩn đoán pager); `AccountSession.SyncOrdersAsync` (guard _navigating, upsert, tổng kết "Sync xong: N đơn / M trang — thêm X mới, cập nhật Y"); nút Sync `⇊` + `CanSyncOrders`. 25 test mới (OrdersRepository 6, IsAllTabText 8, ParseVndAmount 11). 7 điểm làm khác plan đều trong khuôn khổ, đã duyệt.

Nghiệm thu (Fable): tự build 0 warning + 428/428 test; panel đối kháng 3 lát chốt 2 finding mức thấp — (1) nhánh không-có-trang trả kết quả rỗng im lặng → Fable vá 1 dòng log; (2) guard `_navigating` check-then-set không nguyên tử — mẫu CÓ SẴN từ trước ở CheckOrders/ProcessOrders, cửa sổ micro-giây, GHI NỢ sửa cả cụm trong việc riêng; 4 phát hiện khác bị bác (đã có guard danh-sách-đổi chặn lặp). Smoke thật: CHỜ NGƯỜI DÙNG — bấm ⇊ trên tài khoản đang chạy; nếu shop nhiều trang mà chỉ sync 1 trang, gửi dòng "Chẩn đoán pager (không thấy nút trang sau): ..." để tinh chỉnh selector (DOM pager chưa có thật).
