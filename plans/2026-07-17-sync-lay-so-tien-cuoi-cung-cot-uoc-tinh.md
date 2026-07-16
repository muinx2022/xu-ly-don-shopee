# Plan: Sync lấy thêm "Số tiền cuối cùng" từ trang chi tiết đơn → cột "Ước tính" ở màn Đơn hàng

- **Ngày:** 2026-07-17
- **Trạng thái:** hoàn thành (đã merge; chờ người dùng smoke — bước này CHẬM với shop nhiều đơn, lần đầu lâu)

## Báo cáo nghiệm thu (Fable)

Opus (worktree) làm đủ A/B/C: 2 cột `final_amount`/`final_amount_text` (EnsureColumn idempotent cho DB cũ) + COALESCE giữ số cũ khi update null + GetOrderSnsWithFinalAmount; SyncAllOrdersAsync nhận `IReadOnlySet<string>? ordersWithFinalAmount`, sau quét mỗi trang mở chi tiết đơn KHÁC "Đã hủy" & CHƯA có final_amount (click "Xem chi tiết" ưu tiên text → bắt tab mới, fallback NewPage+Goto) → đọc `[type='FinalAmount'] .amount` → đóng đúng tab chi tiết trong finally (không đụng tab danh sách gốc) → per-đơn độc lập lỗi, OCE ném xuyên; cột "Ước tính" sau "Tổng tiền" ở view + CSV (11→12 cột). Fable đọc bắt/đóng tab + COALESCE xác nhận; build 0 warning + 478/478 (tổng sau merge); panel đối kháng 0 finding thật (3 phát hiện rò-tab bị bác — edge case tab đến muộn, đóng theo khi browser đóng). Smoke: CHỜ NGƯỜI DÙNG.
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)

## 1. Bối cảnh & mục tiêu

Luồng Sync (phần 1, commit `a1c479d`, hàm `SyncAllOrdersAsync` trong `ShopeeLoginService.cs`) hiện quét mỗi trang danh sách tab "Tất cả" bằng JS chỉ-đọc, lấy các trường cơ bản (mã đơn, người mua, tổng tiền, trạng thái...). Người dùng muốn lấy THÊM **"Số tiền cuối cùng"** — chỉ có ở TRANG CHI TIẾT của từng đơn:

- Với **đơn KHÔNG phải trạng thái "Đã hủy"**: từ trang danh sách, mở trang chi tiết đơn đó (người dùng chỉ: click nút **"Xem chi tiết"** — `button[data-testid='action-button-1']`, text "Xem chi tiết" — nút này mở **TAB MỚI**).
- Trên tab chi tiết, lấy **"Số tiền cuối cùng"** từ card `type="FinalAmount"`: `.amount` (ví dụ `₫292.010`). DOM người dùng cung cấp: `div.cardStyle[type='FinalAmount'] ... div.amount` (title "Số tiền cuối cùng").
- **Lấy xong → ĐÓNG tab → tiếp tục sync** đơn kế / trang kế.
- Số tiền này thêm vào **cột mới "Ước tính"** đặt **NGAY SAU cột "Tổng tiền"** ở màn "Đơn hàng" (và cột tương ứng khi xuất CSV).

Đơn "Đã hủy" → KHÔNG mở chi tiết, "Ước tính" để trống.

## 2. Phạm vi

- **Làm:** cột DB `final_amount` + upsert; DTO `SyncedOrder` thêm trường; `SyncAllOrdersAsync` thêm bước mở-chi-tiết-lấy-FinalAmount-đóng-tab; màn Đơn hàng cột "Ước tính"; CSV thêm cột.
- **Không làm:** không đổi các trường sync hiện có; không mở chi tiết đơn "Đã hủy"; không đụng luồng Xử lý đơn/lưu phiếu.

## 3. Các bước thực hiện

### A. DB + DTO

1. `Database.Initialize`: `ALTER TABLE orders ADD COLUMN final_amount INTEGER` (bọc an toàn: SQLite không có `ADD COLUMN IF NOT EXISTS` — kiểm cột tồn tại qua `PRAGMA table_info(orders)` rồi mới ALTER; hoặc try/catch nuốt "duplicate column"). Kèm `final_amount_text TEXT` (giữ nguyên văn "₫292.010").
2. `SyncedOrder`: thêm `long? FinalAmount`, `string? FinalAmountText`.
3. `OrdersRepository.UpsertMany`: thêm 2 cột vào INSERT/UPDATE. **Quan trọng (giữ dữ liệu):** khi UPDATE, nếu `FinalAmount` mới là NULL (đơn lần này không lấy được / bỏ qua) thì **GIỮ giá trị cũ** (dùng `COALESCE($finalAmount, final_amount)`), không ghi đè NULL — tránh xóa số đã lấy được ở lần trước. `OrderRow`/`OrderRowViewModel` đọc thêm 2 trường.

### B. `SyncAllOrdersAsync` — mở chi tiết lấy FinalAmount (best-effort, có tối ưu)

1. Sau khi quét xong danh sách MỖI TRANG (đã có mã đơn + trạng thái + href chi tiết), với TỪNG đơn thỏa:
   - Trạng thái KHÁC "Đã hủy" (dùng chuẩn hóa text; đơn hủy bỏ qua).
   - **CHƯA có `final_amount` trong DB** (tối ưu tốc độ — xem mục 3): App truyền vào tập order_sn đã có final_amount, hoặc Core trả danh sách cần-lấy để App quyết; executor chọn cách sạch (gợi ý: `SyncAllOrdersAsync` nhận `Func<string,bool> needFinalAmount` do App cấp từ DB, hoặc App lọc sau — nhưng lấy-trong-lúc-duyệt cần biết ngay; đơn giản nhất: repository có `GetOrderSnsWithFinalAmount(accountId)` → App truyền HashSet vào Core). Ghi rõ cách chọn.
   → mở chi tiết:
     - Cơ chế mở tab: ưu tiên **click "Xem chi tiết" kiểu người** (`button[data-testid='action-button-1']` trong card đơn đó, hit-test) → BẮT TAB MỚI (mẫu bắt tab awbprint trong `SaveSlipAsync`: quét `_browser.Contexts.SelectMany(c=>c.Pages)` tìm page mới, poll có deadline). Nếu sau ~8s không thấy tab → **fallback**: mở URL chi tiết từ `href` của order-card (`/portal/sale/order/<id>`) bằng `context.NewPageAsync()` + `GotoAsync` (đọc dữ liệu, chấp nhận kém human hơn cho bước CHỈ-ĐỌC này). Executor cân, ghi rõ.
     - Trên tab chi tiết: chờ card FinalAmount render (poll ≤ ~15s), `EvaluateAsync` CHỈ-ĐỌC lấy text `.amount` trong phần tử `[type='FinalAmount']` (fallback: card có title chứa "Số tiền cuối cùng"). Parse qua `ShopeeShippingNav.ParseVndAmount`. Không thấy sau deadline → để null, log nhẹ.
     - ĐÓNG tab chi tiết (`page.CloseAsync`), gán `FinalAmount`/`FinalAmountText` vào `SyncedOrder` tương ứng.
   - Mọi bước bọc try/catch riêng từng đơn (1 đơn lỗi không phá cả lượt sync); OCE ném xuyên; giữ tab danh sách (page gốc) không đóng nhầm.
2. Log tiến trình: `"Lấy số tiền cuối cùng: {đã lấy}/{cần lấy} đơn (trang {n})..."` mỗi ~5 đơn để người dùng thấy tiến độ (bước này CHẬM).
3. Sau khi xử lý FinalAmount cho các đơn cần-lấy của trang → sang trang kế như cũ.

### C. Màn Đơn hàng + CSV

1. `OrderRowViewModel`: thuộc tính `EstimateText` = `final_amount_text` (hoặc format `₫` từ `final_amount`; ưu tiên text gốc nếu có, không thì format số, rỗng nếu null).
2. `OrdersView.axaml`: thêm cột **"Ước tính"** NGAY SAU cột "Tổng tiền".
3. `OrderCsvExporter`: thêm cột "Ước tính" (sau "Tổng tiền") vào `Headers` + `OrderExportRow` + `AppendRow` + `ToExportRow`. Cập nhật test CSV (số cột đổi).

### D. Kiểm chứng

1. Build 0 warning; test pass (điều chỉnh OrderCsvExporterTests do thêm cột; OrdersRepositoryTests cho final_amount upsert + COALESCE giữ cũ; ParseVndAmount đã có).
2. Grep: cột final_amount trong Initialize + upsert (COALESCE khi update); cột Ước tính sau Tổng tiền ở view + CSV.
3. Smoke thật (người dùng): sync tài khoản có đơn không-hủy → log "Lấy số tiền cuối cùng: ..."; màn Đơn hàng cột "Ước tính" có số cho đơn không-hủy, trống cho đơn hủy; sync lần 2 nhanh hơn (đơn đã có final_amount bỏ qua mở chi tiết). Executor ghi rõ chờ người dùng.

## 4. Tiêu chí nghiệm thu

- [ ] Build 0 warning; test pass (+ test mới).
- [ ] Đọc code: chỉ mở chi tiết đơn KHÁC "Đã hủy" và CHƯA có final_amount; bắt/đóng tab đúng (không đóng nhầm tab danh sách); UPDATE dùng COALESCE giữ final_amount cũ khi lần này null; evaluate chỉ ĐỌC; OCE ném xuyên; per-đơn lỗi độc lập; cột "Ước tính" đúng vị trí (sau Tổng tiền) ở view + CSV.
- [ ] Smoke: chờ người dùng.

## 5. Rủi ro & lưu ý

- **CHẬM:** mỗi đơn không-hủy = mở + đọc + đóng 1 tab. Shop nhiều đơn → sync rất lâu. **Mặc định CHỈ lấy đơn CHƯA có final_amount** (lần đầu lâu, các lần sau nhanh). Nếu người dùng muốn LÀM MỚI số cho đơn cũ, đó là hành vi khác (chưa làm — ghi nhận; có thể thêm nút "làm mới số tiền" sau).
- Bắt tab mới dễ lẫn với các tab khác (phiếu awbprint...) — dùng deadline + nhận đúng tab chi tiết (URL chứa `/portal/sale/order/` hoặc có card FinalAmount). Đóng đúng tab vừa mở, tuyệt đối không đóng tab danh sách gốc.
- Nút "Xem chi tiết" xuất hiện ở CẢ đơn thường lẫn package — định vị nút trong đúng card đơn đang xét (không vớ nút đơn khác).
- Cột `final_amount` thêm bằng ALTER — với DB đã có dữ liệu phải chạy được (kiểm cột tồn tại trước khi ALTER, không làm hỏng bảng cũ).
- Thao tác trang kiểu người + hit-test cho click "Xem chi tiết"; evaluate chỉ đọc; không vá/hook JS.
- Hai phiên Claude cùng repo — executor `git status` trước khi sửa; thấy lạ → DỪNG báo.

---

## Báo cáo thực thi (Opus điền sau khi xong)

<Opus dán báo cáo cuối vào đây hoặc Fable tổng hợp lại sau nghiệm thu.>
