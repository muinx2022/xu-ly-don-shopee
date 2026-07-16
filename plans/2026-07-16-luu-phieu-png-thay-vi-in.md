# Plan: Lưu phiếu giao về thư mục (PNG chụp màn hình + PDF thật) thay vì in ngay

- **Ngày:** 2026-07-16
- **Trạng thái:** hoàn thành (chờ người dùng smoke với đơn thật)
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)

## 1. Bối cảnh & mục tiêu

Luồng xử lý đơn hiện tại, sau khi bắt được tab phiếu awbprint (`DownloadAndPrintSlipAsync`, `src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs` ~2279–2440):

1. Chờ `DOMContentLoaded` 8s (best-effort, nuốt timeout).
2. Render PDF bằng CDP `Page.printToPDF` → lưu `D:\Phieu-giao-hang\<mã đơn>.pdf` (có %PDF-check); fallback GET page-URL.
3. CLICK nút "In phiếu" trên tab (với cờ `--kiosk-printing` → in im lặng máy in mặc định); fallback `window.print()`.
4. Đóng tab phiếu.

**Yêu cầu mới của người dùng:** đến bước phiếu thì KHÔNG in ngay nữa — lưu phiếu về thư mục (chụp màn hình cũng được) để in sau.

**Lỗi thực tế phát hiện hôm nay (16/7):** cả 3 file PDF "tải thành công" (`260716SH3T8HVV.pdf`, `260716SHAGRTTA.pdf`, `260716SNA9761G.pdf`) đều đúng **1083 bytes**, mở ra chỉ thấy **một ô đen trên nền trắng, không có thông tin phiếu**. Chẩn đoán: `printToPDF` chạy ngay sau `DOMContentLoaded`, khi nội dung phiếu chưa vẽ; và nhiều khả năng phiếu nằm trong **iframe/embed** (trình xem PDF nền tối → ra "ô đen") mà `printToPDF` của trang bọc không render được. Tức là đường "tải PDF" hiện tại cho ra file rác dù log báo OK.

Mục tiêu: sau khi tab phiếu mở, **chờ nội dung phiếu thật sự hiện** → **chụp màn hình cả trang lưu PNG** (artifact CHÍNH, chắc chắn chụp đúng cái mắt thấy) + **cố lấy file PDF thật** (qua src của khung nhúng nếu có; render PDF chỉ còn là phụ, có ngưỡng chống lưu bản trắng) → **KHÔNG gửi lệnh in nào** → đóng tab. Tên file theo mã đơn, cùng thư mục `D:\Phieu-giao-hang`.

## 2. Phạm vi

- **Làm:**
  - Sửa `src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs`: thay `DownloadAndPrintSlipAsync` bằng `SaveSlipAsync` (chờ nội dung → chụp PNG → lấy PDF thật → đóng tab; bỏ toàn bộ khối in + `FirePrintFallbackAsync`).
  - Sửa `src/XuLyDonShopee.Core/Services/ShopeeShippingNav.cs`: xóa `IsPrintButtonText` (hết người dùng sau khi bỏ khối in).
  - Sửa `src/XuLyDonShopee.Core/Services/BraveLaunchArgs.cs`: bỏ cờ `--kiosk-printing` (chỉ phục vụ in im lặng — đã bỏ in; tránh việc người dùng Ctrl+P tay trong cửa sổ automation bị in thẳng không hỏi). **GIỮ NGUYÊN** `--disable-popup-blocking` (tab phiếu vẫn phải mở bằng window.open để chụp).
  - Cập nhật test tương ứng trong `src/XuLyDonShopee.Tests/` (chi tiết ở bước 5).
  - Hai vá log nhỏ phục vụ chẩn đoán (không đổi hành vi — chi tiết bước 6).
- **Không làm:**
  - KHÔNG đổi hành vi vòng lặp "1 đơn lỗi → dừng cả vòng" trong `AccountSession.ProcessOrdersAsync` (việc riêng, chưa chốt).
  - KHÔNG đụng logic bấm nút "In phiếu giao" trong modal (mở tab phiếu) — kể cả thời gian chờ 40s.
  - KHÔNG thêm retry cho đơn lỗi; KHÔNG xóa 3 file PDF trắng cũ trên đĩa.

## 3. Các bước thực hiện

1. **`ShopeeLoginService.cs` — đổi `DownloadAndPrintSlipAsync` → `SaveSlipAsync`** (đổi tên ở cả call site ~dòng 2245 và doc-comment ~2271–2278; cập nhật comment "TẢI/IN best-effort" ~2244 thành "CHỤP/LƯU best-effort"). Thứ tự mới bên trong:
   - **a. Chờ load:** `WaitForLoadStateAsync(LoadState.Load, timeout 10_000)` best-effort (thay `DOMContentLoaded` 8s), nuốt timeout như cũ, `OperationCanceledException` vẫn ném.
   - **b. Chờ nội dung phiếu (poll ≤ 25s, bước 500–800ms):** mỗi vòng `EvaluateAsync` một đoạn JS **CHỈ ĐỌC** DOM (tuyệt đối không vá/hook — quy tắc stealth của dự án), trả về JSON gồm: số `iframe`/`embed`/`object` + danh sách `src` của chúng (mỗi src cắt ≤ 120 ký tự), số ảnh `document.images` đã `complete` với kích thước thật > 0. Điều kiện "sẵn sàng": có ≥ 1 iframe/embed/object **hoặc** ≥ 1 ảnh complete. Đạt → chờ settle ngẫu nhiên 1.500–2.500ms (cho khung nhúng kịp vẽ) rồi đi tiếp. Hết 25s không đạt → vẫn đi tiếp nhưng log `"Không thấy dấu hiệu nội dung phiếu sau 25s — ảnh chụp có thể trắng."`.
   - **c. Log MỘT dòng chẩn đoán DOM** (lấy từ kết quả poll cuối): ví dụ `"Tab phiếu: 1 iframe (src=https://...), 0 embed, 3 ảnh."` — bắt buộc có, để nếu smoke vẫn trắng thì có dữ liệu tinh chỉnh vòng sau.
   - **d. CHỤP MÀN HÌNH (artifact CHÍNH):** `newPage.ScreenshotAsync(new PageScreenshotOptions { FullPage = true, Path = <thư mục>\<mã đơn>.png })`. Nếu ném exception → thử lại một lần với `FullPage = false`. Thành công → log `"Đã chụp phiếu (PNG): <path> (<N> bytes)."`; cả hai lần fail → log cảnh báo, đi tiếp (best-effort). Tên file dùng `ShopeeShippingNav.SanitizeFileName(baseName) + ".png"` (baseName như logic hiện có: mã đơn > job_id > "phieu").
   - **e. Lấy PDF thật (artifact PHỤ, best-effort, thử lần lượt tới khi được):**
     - **e1.** Nếu bước b thấy iframe/embed/object có `src` bắt đầu `http(s)`: GET src ĐẦU TIÊN đó qua `newPage.APIRequest.GetAsync` (context đã đăng nhập) — chỉ nhận khi content-type chứa "pdf" **hoặc** magic `%PDF` (dùng `LooksPdf` sẵn có) → lưu `<mã đơn>.pdf`, log `"Đã tải phiếu PDF thật (src khung nhúng): <path> (<N> bytes)."`. Chỉ GET MỘT lần (token phiếu dùng 1 lần — quy tắc đã có của dự án), lỗi/không phải PDF → log rồi sang e2.
     - **e2.** `Page.printToPDF` như code hiện tại (%PDF-check giữ nguyên) **nhưng thêm ngưỡng**: hằng `private const int MinRealSlipPdfBytes = 3000;` — kết quả `< 3000` bytes vẫn lưu nhưng log `"PDF render nghi TRẮNG (<N> bytes) — dùng file PNG thay thế."` và KHÔNG tính là "đã có PDF thật"; `>= 3000` → log như cũ.
     - **e3.** GET page-URL fallback giữ nguyên logic hiện có (%PDF/content-type check).
   - **f. Tổng kết:** coi là "đã lưu được phiếu" khi PNG lưu OK **hoặc** có PDF ≥ 3000 bytes. Không đạt cả hai → log `"Cảnh báo: CHƯA lưu được phiếu có nội dung — kiểm tra tay trong Brave."`. **KHÔNG** đổi giá trị trả về của `ArrangeShipmentAsync`/`ProcessFirstOrderAsync` (bước lưu vẫn best-effort như hiện nay — đơn đã arrange thành công).
   - **g. XÓA:** toàn bộ khối IN (~2391–2428), hàm `FirePrintFallbackAsync` (~2442 trở đi), và lời gọi `WaitPrintButtonClickableAsync(newPage, IsPrintButtonText, ...)`. Khối "ĐÓNG tab phiếu" giữ nguyên.
2. **`ShopeeShippingNav.cs`:** xóa `IsPrintButtonText` (~dòng 193) — sau bước 1 không còn call site (kiểm chứng bằng grep trước khi xóa). Cập nhật doc-comment của `WaitPrintButtonClickableAsync` trong `ShopeeLoginService.cs` (~2806–2816): giờ CHỈ dùng cho nút "In phiếu giao" trong modal.
3. **`BraveLaunchArgs.cs`:** bỏ dòng `"--kiosk-printing"` (~dòng 53) + cụm comment về in im lặng (~52–53). Giữ nguyên phần `--disable-popup-blocking` và comment của nó (~54–55).
4. **Doc-comment liên quan:** `ShopeeLoginService.cs` ~123–131 (mô tả `ProcessFirstOrderAsync`: bỏ "gửi lệnh in máy in mặc định", thay bằng "chụp PNG + lưu PDF về downloadDir"); `src/XuLyDonShopee.Core/Services/ArrangeShipmentResult.cs` dòng ~8 và ~14 (thay "tải file + gửi lệnh in" → "chụp/lưu phiếu là best-effort").
5. **Tests (`src/XuLyDonShopee.Tests/`):**
   - `BraveLaunchArgsTests.cs`: xóa assert `--kiosk-printing` (~dòng 36–39, cả comment); giữ/không đụng assert `--disable-popup-blocking`. Nếu có test đếm tổng số cờ thì cập nhật số.
   - `ShopeeShippingNavTests.cs`: xóa nhóm test `IsPrintButtonText_KhopChuanHoa` (~dòng 321–333, gồm các `[InlineData]` của nó — trong đó có dòng 326). KHÔNG đụng test của `IsPrintSlipButtonText` (nút modal, vẫn dùng).
6. **Hai vá log nhỏ (chẩn đoán các lần fail còn lại, không đổi hành vi):**
   - Trong vòng chờ tab phiếu (~2188–2233): thêm cờ `bool clickedPrint = false;` bật sau lần `HumanMoveAndClickAsync` đầu tiên; câu log fail (~2231) tách 2 trường hợp: đã bấm ≥ 1 lần → giữ `"Đã bấm In phiếu giao nhưng KHÔNG thấy tab phiếu mở ra — kiểm tra tay."`; CHƯA bấm được lần nào → `"Nút In phiếu giao KHÔNG bấm được trong 40s (chưa bấm được lần nào) — kiểm tra tay."`.
   - Catch tổng của `ArrangeShipmentAsync` (~2259–2263): trước `return ArrangeShipmentResult.Failed;` thêm `L("Xử lý đơn gặp lỗi bất ngờ: " + ex.Message);` (đổi `catch` → `catch (Exception ex)`).

## 4. Tiêu chí nghiệm thu

- [ ] `dotnet build XuLyDonShopee.sln` thành công, 0 warning mới.
- [ ] `dotnet test` xanh. (Máy có WDAC: nếu test DLL bị chặn `FileLoadException 0x800711C7` ĐỒNG LOẠT thì đó là chính sách máy, không phải lỗi code — ghi rõ hiện tượng vào báo cáo để Fable nhờ người dùng chạy tay.)
- [ ] `grep -r "kiosk-printing\|FirePrintFallbackAsync\|IsPrintButtonText" src/` không còn kết quả nào.
- [ ] Đọc lại `SaveSlipAsync`: không còn đường code nào gửi lệnh in (`window.print`, click "In phiếu"); PNG luôn được chụp (kể cả khi chờ-nội-dung timeout); có dòng log chẩn đoán DOM; mọi `EvaluateAsync` chỉ đọc DOM.
- [ ] Smoke thật (người dùng chạy app xử lý 1 đơn — executor KHÔNG tự chạy app được do WDAC): `D:\Phieu-giao-hang` có `<mã đơn>.png` NHÌN THẤY nội dung phiếu (không phải trang trắng/ô đen), không lệnh in nào ra máy in. Executor ghi rõ trong báo cáo là mục này chờ người dùng smoke.

## 5. Rủi ro & lưu ý

- **Không biết chắc DOM trang awbprint** (iframe? embed? canvas? ảnh?) — vì vậy dòng log chẩn đoán DOM (bước 1c) là BẮT BUỘC: nếu smoke vẫn ra ảnh trắng/ô đen thì dòng này là dữ liệu để tinh chỉnh vòng sau. Viết code phòng thủ: thiếu phần tử nào cũng không ném.
- `ScreenshotAsync FullPage=true` có thể lỗi với nội dung plugin/khung nhúng → bắt buộc có retry `FullPage=false`.
- Mọi thao tác thêm trên trang chỉ là ĐỌC (evaluate đếm phần tử/src, screenshot, printToPDF) — không click gì thêm trên tab phiếu, không vá/hook JS (quy tắc stealth: đừng over-patch).
- Token phiếu dùng 1 lần: GET src khung nhúng đúng 1 lần, luôn %PDF-check trước khi ghi (không lưu rác thành `.pdf`).
- Giữ mô hình best-effort hiện có: mọi lỗi ở bước lưu phiếu KHÔNG làm đơn bị tính fail (đơn đã arrange thành công trên Shopee).
- `OperationCanceledException` luôn được ném xuyên qua (không nuốt) — giữ đúng hành vi dừng sạch hiện tại.

---

## Báo cáo thực thi (Opus điền sau khi xong)

Opus thực thi đủ bước 1–6 (6 file sửa, đúng phạm vi): `SaveSlipAsync` thay `DownloadAndPrintSlipAsync` (chờ Load 10s → poll nội dung ≤25s → log chẩn đoán DOM → chụp PNG FullPage/retry → PDF e1 src khung nhúng/e2 printToPDF+ngưỡng 3000/e3 GET URL → đóng tab, không còn lệnh in); xóa `FirePrintFallbackAsync`, `IsPrintButtonText`, cờ `--kiosk-printing` + test tương ứng; 2 vá log (`clickedPrint`, catch tổng log `ex.Message`). Khác plan (đã duyệt): đọc kết quả probe bằng `EvaluateAsync<string>` + `JSON.stringify`; prefix log "Tab phiếu DOM:".

Nghiệm thu (Fable): tự build 0 warning + 382/382 test xanh (WDAC không chặn); panel rà soát đối kháng 2/3 phiếu chốt 3 lỗi thật → đã gửi Opus sửa và kiểm lại: (1) [cao] src khung nhúng bị cắt 120 ký tự trong JS rồi đem GET → giờ JS trả src nguyên vẹn, chỉ cắt khi ghi log; (2) refresh `newPage.Url` sau vòng poll để e3 không GET about:blank; (3) e1 log HTTP status khi resp không Ok. Build/test lại xanh sau sửa. Mục smoke với đơn thật: CHỜ NGƯỜI DÙNG chạy app xử lý 1 đơn, kỳ vọng `D:\Phieu-giao-hang\<mã đơn>.png` thấy nội dung phiếu; nếu vẫn trắng, lấy dòng log "Tab phiếu DOM: ..." để tinh chỉnh vòng sau.
