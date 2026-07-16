# Plan: Chờ vận đơn lâu hơn (5'), bỏ qua đơn lỗi chạy tiếp, lấy PDF phiếu gốc từ blob

- **Ngày:** 2026-07-16
- **Trạng thái:** đang làm
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)

## 1. Bối cảnh & mục tiêu

Sau plan `2026-07-16-luu-phieu-png-thay-vi-in.md` (đã hoàn thành, commit `3d2e0ef`), lần chạy thật 15:48 cho 3 dữ kiện mới (log `%APPDATA%\XuLyDonShopee\logs\hoatdong-20260716.log`):

1. Đơn 1 (`260716SNBQXAXN`) trọn vẹn: PNG 164KB **có đầy đủ nội dung phiếu** (fix chờ-nội-dung đã ăn). Log chẩn đoán cho biết cấu trúc tab phiếu: `Tab phiếu DOM: 1 iframe (src=blob:https://banhang.shopee.vn/af9a6790-...#toolbar=0&navpanes=0), 0 embed, 0 object, 0 ảnh.` — tức **phiếu là một file PDF gốc do Shopee tạo, nhúng qua iframe với blob: URL** (trình xem PDF).
2. Bản PDF hiện tại (e2 `printToPDF` trang bọc, 163KB) **KHÔNG dùng được để in**: dính thanh toolbar đen + nền tối của trình xem, phiếu bị CẮT mất mép phải (trình xem đang cuộn). Người dùng yêu cầu "chụp lại ĐÚNG phiếu giao" → phải lấy **file PDF gốc từ blob URL** (chỉ chứa phiếu, đúng khổ giấy in).
3. Đơn 2 (`260716SPBBSXK0`): `Nút In phiếu giao KHÔNG bấm được trong 40s (chưa bấm được lần nào)` → `PrintFailed` → **cả vòng dừng**, 2 đơn còn lại không được chạy. Người dùng xác nhận nguyên nhân: **Shopee tạo mã vận đơn có thể LÂU** (40s không đủ) và yêu cầu: (a) chờ tới khi nút "In phiếu giao" sẵn sàng, (b) đơn nào lỗi thì bỏ qua chạy tiếp, không dừng cả vòng.

Ba mục tiêu của plan này:
- **A.** Nới thời gian chờ nút "In phiếu giao" từ 40s → **300s (5 phút)**, có log tiến trình + log chẩn đoán trạng thái nút khi hết hạn.
- **B.** Vòng xử lý đơn (`ProcessOrdersAsync`): đơn lỗi thì **ghi log, bỏ qua, chạy tiếp đơn kế**; chỉ dừng khi lỗi 3 lần LIÊN TIẾP (chống lặp vô hạn) hoặc hết đơn; tổng kết cuối vòng ghi cả vào ActivityLog.
- **C.** Lấy **PDF phiếu gốc** từ blob URL của iframe làm artifact in chính; PNG cả trang giữ nguyên làm bản nhìn nhanh; printToPDF chỉ còn fallback cuối.

## 2. Phạm vi

- **Làm:** 3 mục A/B/C trên, trong 2 file: `src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs` (A, C) và `src/XuLyDonShopee.App/Services/AccountSession.cs` (B); test kèm theo nếu tách được hàm thuần.
- **Không làm:**
  - KHÔNG làm tính năng "in lại phiếu cho đơn đã arrange nhưng lỡ fail" (đơn PrintFailed vẫn phải in tay — ghi nhận làm sau nếu cần).
  - KHÔNG đổi thời gian chờ modal "Thông Tin Chi Tiết" (15s — các run thật đều mở kịp).
  - KHÔNG đụng bước đặt địa chỉ, vòng theo dõi 30', `CheckOrdersAsync`.
  - KHÔNG thêm cờ trình duyệt mới, không vá/hook JS (chỉ ĐỌC).

## 3. Các bước thực hiện

### A. Chờ nút "In phiếu giao" tới 5 phút (`ShopeeLoginService.cs`, vùng ~2188–2237)

1. Thêm hằng `private const int PrintButtonWaitSeconds = 300;` (kèm doc: Shopee tạo vận đơn có thể mất vài phút — yêu cầu người dùng 16/7). `printDeadline = DateTime.UtcNow.AddSeconds(PrintButtonWaitSeconds);` thay cho `AddSeconds(40)`.
2. Trong vòng chờ, log tiến trình mỗi ~30s MỘT dòng: `"Vẫn chờ nút In phiếu giao (đã Ns) — Shopee đang tạo vận đơn..."` (đếm bằng biến thời điểm log gần nhất, không log dồn dập). Câu log fail hiện có giữ nguyên nhưng thay "40s" bằng số giây thực của hằng (nội suy `{PrintButtonWaitSeconds}s`).
3. Khi HẾT HẠN mà `newPage` vẫn null: trước khi `return PrintFailed`, chạy MỘT lần `EvaluateAsync` CHỈ-ĐỌC trên `page` để log chẩn đoán trạng thái nút, ví dụ: nút `button[data-testid='print-button']` có tồn tại không / thuộc tính `disabled` / text nút / `elementFromPoint` tại tâm nút trả về `tag.class` gì (phần tử che). Log MỘT dòng: `"Chẩn đoán nút In phiếu giao: ..."`. Bọc try/catch nuốt lỗi (OCE vẫn ném) — chẩn đoán fail không được phá luồng.

### B. Bỏ qua đơn lỗi, chạy tiếp (`AccountSession.cs`, `ProcessOrdersAsync` ~262–303)

1. Thay hành vi `break` khi `last != Ok && last != NoOrder` bằng:
   - Ghi log qua ActivityLog: `"Bỏ qua đơn lỗi ({tên bước lỗi}) — tiếp tục đơn kế."` (tên bước lấy từ mapping thông điệp lỗi sẵn có; có thể tách hàm map `ArrangeShipmentResult → chuỗi mô tả bước lỗi` dùng chung cho cả StatusText).
   - `failCount++`; `consecutiveFails++`; đơn Ok thì `consecutiveFails = 0`.
   - `continue` vòng lặp (vẫn giữ delay ngẫu nhiên 1500–3500ms giữa các đơn).
2. **Chống lặp vô hạn:** nếu `consecutiveFails >= 3` → dừng vòng, StatusText nêu rõ `"Dừng vì lỗi 3 đơn liên tiếp"`. Lý do cần guard: đơn fail TRƯỚC khi arrange (PrepareNotFound/ShipModalNotOpened/ConfirmFailed/Failed) vẫn còn nút "Chuẩn bị hàng" → vòng sau sẽ chọn lại CHÍNH đơn đó; 3 lần liên tiếp không tiến triển = có vấn đề hệ thống, dừng để người xem. (Đơn fail SAU arrange — PrintFailed/DetailModalNotOpened — mất nút "Chuẩn bị hàng" nên vòng sau tự sang đơn kế, không lặp.)
3. `MaxOrders` 200 giữ nguyên. Điều kiện thoát vòng còn lại: `NoOrder` (hết đơn).
4. Tổng kết cuối vòng: StatusText dạng `"Đã xử lý X đơn, bỏ qua Y đơn lỗi."` (Y=0 thì giữ câu cũ) VÀ ghi cùng nội dung vào ActivityLog (`_services.Log.Append`) — hiện StatusText không vào file log, mất dấu vết (đã thấy trong điều tra sáng nay).
5. **Nên (không bắt buộc):** tách quyết định vòng lặp thành hàm thuần static testable, ví dụ `static (bool stop, int newConsecutive) NextLoopDecision(ArrangeShipmentResult last, int consecutiveFails)` + unit test các nhánh (Ok reset, fail tăng, 3 liên tiếp dừng, NoOrder dừng). Nếu tách làm rối code thì bỏ qua, ghi rõ trong báo cáo.

### C. Lấy PDF phiếu gốc từ blob (`ShopeeLoginService.cs`, `SaveSlipAsync`)

1. Thêm bước **e0** (TRƯỚC e1, sau khi có `embedSrcs`/`firstHttpSrc`): nếu src đầu tiên của khung nhúng bắt đầu bằng `blob:` (case-insensitive):
   - Cắt bỏ fragment: phần từ ký tự `#` trở đi (blob URL trong log có `#toolbar=0&navpanes=0`).
   - `EvaluateAsync<string>` một hàm JS async CHỈ-ĐỌC: `fetch(blobUrl)` → `arrayBuffer` → chuyển base64 (chunk nhỏ tránh tràn stack `btoa`), trả chuỗi base64; lỗi trả chuỗi rỗng. (fetch tài nguyên blob cùng trang = đọc, không vá gì.)
   - C# `Convert.FromBase64String` → `LooksPdf` check → lưu `<mã đơn>.pdf`, `pdfReal = true`, log `"Đã tải phiếu PDF GỐC (blob): {path} ({N} bytes)."`. Không phải PDF/rỗng → log rồi rơi xuống e1/e2/e3 như cũ.
   - Biến chọn src cho e0/e1: src blob lấy từ `embedSrcs` (bản đầy đủ, KHÔNG cắt 120 — đã fix ở plan trước); `firstHttpSrc` hiện chỉ nhận http(s) — thêm biến riêng `firstBlobSrc` tương tự.
2. e1 (GET http src)/e2 (printToPDF + ngưỡng)/e3 (GET page-URL) giữ nguyên thứ tự sau e0. Cập nhật doc-comment `SaveSlipAsync` mô tả e0.
3. PNG: GIỮ NGUYÊN chụp FullPage cả trang (bản 15:48 đã cho thấy đầy đủ nội dung) — KHÔNG chuyển sang chụp element iframe (trình xem PDF có nền tối + có thể đang cuộn → crop iframe KHÔNG cho "đúng phiếu"; bản "đúng phiếu" chính là PDF gốc e0).

### D. Cập nhật doc/test

1. Doc-comment `ProcessFirstOrderAsync`/`ProcessOrdersAsync` cập nhật hành vi mới (chờ 5', bỏ qua đơn lỗi).
2. `dotnet build` + `dotnet test` xanh; test mới cho hàm thuần B.5 nếu tách.

## 4. Tiêu chí nghiệm thu

- [ ] `dotnet build XuLyDonShopee.sln -c Debug`: 0 lỗi, 0 warning mới.
- [ ] `dotnet test`: toàn bộ pass (382+ test; WDAC chặn đồng loạt 0x800711C7 → ghi rõ báo cáo).
- [ ] Đọc code: không còn hằng 40s; log tiến trình mỗi ~30s; chẩn đoán nút khi hết 5'; `ProcessOrdersAsync` không còn `break` ngay khi 1 đơn lỗi (trừ 3-liên-tiếp); e0 blob đứng trước e1; PNG FullPage giữ nguyên; mọi evaluate chỉ đọc; OCE ném xuyên mọi nhánh mới.
- [ ] Smoke thật (người dùng chạy): một loạt đơn trong đó có đơn Shopee tạo vận đơn chậm → app chờ (thấy log tiến trình 30s) tới khi nút sẵn sàng rồi xử lý tiếp; nếu 1 đơn lỗi hẳn → log "Bỏ qua đơn lỗi..." và các đơn sau VẪN được xử lý; thư mục có `<mã đơn>.pdf` là PHIẾU GỐC (mở ra chỉ thấy phiếu, không toolbar/nền đen, không cắt mép). Executor ghi rõ mục này chờ người dùng.

## 5. Rủi ro & lưu ý

- **Nhịp click trong 5 phút chờ:** vòng hiện tại sau mỗi lần nút clickable sẽ click rồi chờ tab 8s; nếu nút chưa clickable thì chỉ poll (không click). Giữ nguyên cấu trúc đó — KHÔNG thêm click dồn dập; mỗi lần click thật vẫn có log riêng. Chống double-tab (kiểm tab đã mở trước khi click lại) giữ nguyên.
- **Hủy giữa chừng:** người dùng bấm Dừng/đóng cửa sổ trong 5 phút chờ → `ct` phải cắt được ngay (các `Task.Delay`/vòng poll đều truyền `ct` — giữ nguyên mẫu hiện có).
- **fetch blob có thể fail** (blob đã revoke, CSP): phải nuốt lỗi trong JS (trả rỗng) + try/catch C#, rơi xuống e1/e2/e3 — tuyệt đối không phá luồng best-effort.
- **base64 chunk:** dùng vòng lặp chunk (vd 0x8000 bytes) khi `btoa` để không tràn call stack với file lớn.
- **`consecutiveFails` phải reset khi Ok** — kẻo 3 lỗi RẢI RÁC trong 200 đơn cũng dừng oan.
- Hai phiên Claude có thể cùng mở repo — trước khi sửa, executor chạy `git status` xác nhận cây sạch; thấy thay đổi lạ thì DỪNG và báo, không tự ý làm đè.

---

## Báo cáo thực thi (Opus điền sau khi xong)

<Opus dán báo cáo cuối vào đây hoặc Fable tổng hợp lại sau nghiệm thu.>
