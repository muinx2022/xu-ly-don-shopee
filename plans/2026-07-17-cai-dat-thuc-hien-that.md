# Plan: Cài đặt — thực hiện THẬT (Thư mục lưu hóa đơn + Chu kỳ theo dõi đơn); bỏ Xử lý đơn hàng & Giao diện

- **Ngày:** 2026-07-17
- **Trạng thái:** đang làm
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`, worktree)

## 1. Bối cảnh & mục tiêu

Màn "Cài đặt" (`SettingsView.axaml`) hiện TOÀN MOCKUP TĨNH: 3 section (a) XỬ LÝ ĐƠN HÀNG (3 toggle AutoPrint/NotifyNewOrder/AutoConfirm — không tác dụng thật), (b) TỰ ĐỘNG HÓA (slider chu kỳ quét TĨNH không kéo được + "Thư mục lưu hóa đơn" chỉ hiển thị, nút "Chọn…" chưa làm gì), (c) GIAO DIỆN (chọn Chủ đề Sáng/Tối/Theo hệ thống — không đổi theme thật).

Quyết định người dùng (đã chốt):
- **BỎ hẳn** section (a) XỬ LÝ ĐƠN HÀNG và (c) GIAO DIỆN (không làm theme sáng/tối/theo hệ thống).
- **Làm HOẠT ĐỘNG THẬT** 2 mục:
  1. **Thư mục lưu hóa đơn** — chọn thư mục được; **mặc định = thư mục trong dữ liệu app** (cạnh app.db, tức `<thư mục chứa app.db>\Phieu-giao-hang`); và **lúc Xử lý đơn phải LƯU phiếu vào thư mục cấu hình này** (hiện hardcode `ShopeeShippingNav.SlipDownloadDir = D:\Phieu-giao-hang` — chưa dùng config). Link "In phiếu" ở màn Đơn hàng cũng phải mở theo thư mục này.
  2. **Chu kỳ theo dõi đơn (phút)** — cho cấu hình số phút giữa các lần tự đọc số "Chờ Lấy Hàng" (hiện cố định 30' trong `AccountSession.RunAsync` — `OrderIntervalMin = 30`).

## 2. Phạm vi

- **Làm:** `SettingsView.axaml`(+`.cs`), `SettingsViewModel.cs`, `SettingsRepository.cs` (2 khóa mới), `DialogService.cs` (folder picker), `AccountSession.cs` (đọc thư mục + chu kỳ từ config), `OrderRowViewModel.cs` (SlipPath theo thư mục config). Hằng đường dẫn mặc định (Core).
- **Không làm:** KHÔNG đụng màn Đơn hàng `OrdersView.axaml`/converter (việc màu trạng thái đang làm ở đó — tránh xung đột); KHÔNG đụng luồng sync/xử lý đơn lõi (chỉ đổi NGUỒN thư mục + chu kỳ, không đổi hành vi bước). **Làm trong WORKTREE.**

## 3. Các bước thực hiện

### A. Config (Core + repository)

1. **Mặc định thư mục hóa đơn:** thêm helper Core trả `<thư mục chứa app.db>\Phieu-giao-hang` (dùng `Path.GetDirectoryName(Database.Path)`). Đặt ở nơi hợp lý (ví dụ `Database.DefaultInvoiceDir()` hoặc hằng/hàm cạnh `SlipDownloadDir`). `ShopeeShippingNav.SlipDownloadDir` (D:\Phieu-giao-hang) GIỮ nhưng KHÔNG còn là nguồn khi xử lý đơn — nguồn là config (fallback default = thư mục app).
2. **`SettingsRepository`:** thêm 2 khóa `invoice_folder` (TEXT, rỗng/thiếu → default thư mục app) và `order_interval_minutes` (INT, thiếu/lạ → 30, kẹp min 1, max vd 1440). Thêm `GetInvoiceFolder()` (trả config hoặc default, tạo thư mục nếu chưa có — hoặc để bên gọi tạo), `SetInvoiceFolder(path)`, `GetOrderIntervalMinutes()`, `SetOrderIntervalMinutes(int)`. Có thể gom vào một record `AppGeneralSettings` thuần + test (mẫu `AutoRunSettings`) — executor chọn, ghi rõ.

### B. Dùng config trong luồng

1. **Xử lý đơn dùng thư mục config:** `AccountSession.ProcessOrdersAsync` hiện gọi `ProcessFirstOrderAsync(ShopeeShippingNav.SlipDownloadDir, ...)`. Đổi: đọc `_services.Settings.GetInvoiceFolder()` MỘT LẦN đầu `ProcessOrdersAsync` (chụp vào biến trước vòng lặp — không đọc lại giữa await) rồi truyền vào. Tạo thư mục nếu chưa có (best-effort).
2. **Chu kỳ theo dõi đơn:** `AccountSession.RunAsync` đọc `_services.Settings.GetOrderIntervalMinutes()` khi bắt đầu phiên (chụp vào biến local thay hằng `OrderIntervalMin = 30`). Đổi giữa chừng chỉ áp ở lần mở kế (chấp nhận — đơn giản; ghi rõ).
3. **Link "In phiếu" (OrderRowViewModel.SlipPath):** hiện dùng `ShopeeShippingNav.SlipDownloadDir`. Đổi đọc `_services.Settings.GetInvoiceFolder()` (OrderRowViewModel do OrdersViewModel tạo — truyền thư mục vào constructor giống `notify`, HOẶC OrderRowViewModel giữ tham chiếu để đọc lúc bấm). Executor chọn cách ít rủi ro (ưu tiên truyền thư mục vào lúc tạo row — OrdersViewModel đọc config khi nạp danh sách).

### C. UI Cài đặt

1. **Bỏ** section "XỬ LÝ ĐƠN HÀNG" (3 toggle) và section "GIAO DIỆN" (chọn theme) khỏi `SettingsView.axaml`. Gỡ các binding mồ côi tương ứng trong `SettingsViewModel` (AutoPrint/NotifyNewOrder/AutoConfirm; theme) — hoặc giữ property nếu chỗ khác dùng (grep; nhiều khả năng không).
2. **Thư mục lưu hóa đơn (thật):** ô hiển thị đường dẫn hiện tại (`InvoiceFolder`) + nút "Chọn…" mở **folder picker** (`DialogService` qua `TopLevel.StorageProvider.OpenFolderPickerAsync`), chọn xong lưu config + cập nhật hiển thị. Nút "Mở thư mục" (tùy chọn, mở Explorer) — không bắt buộc.
3. **Chu kỳ theo dõi đơn:** đổi slider tĩnh → `NumericUpDown` (Minimum 1, Maximum 1440, đơn vị phút) bind `OrderIntervalMinutes`; nút/auto "Lưu". Ghi chú "Áp dụng cho các phiên mở sau khi lưu".
4. `SettingsViewModel`: nạp config lúc `Reload()`; lưu khi đổi (hoặc nút Lưu). Chuẩn hóa giá trị (kẹp min/max) trước khi ghi.

### D. Kiểm chứng

1. Build 0 warning; test pass (+ test thuần cho AppGeneralSettings parse/normalize + default invoice dir nếu tách được).
2. Grep: xử lý đơn + SlipPath + RunAsync chu kỳ đều đọc từ Settings (không còn hardcode 30'/D:\Phieu-giao-hang làm NGUỒN); SettingsView không còn section Xử lý đơn hàng/Giao diện.
3. Smoke thật (người dùng): Cài đặt đổi thư mục hóa đơn → Xử lý đơn lưu phiếu vào thư mục mới; link "In phiếu" mở đúng; đổi chu kỳ → phiên mở sau đọc số theo chu kỳ mới. Executor ghi rõ chờ người dùng.

## 5. Rủi ro & lưu ý

- **Nguồn thư mục phải NHẤT QUÁN 3 nơi:** xử lý đơn (lưu), link In phiếu (mở), và mặc định — tất cả qua `Settings.GetInvoiceFolder()`. Nếu lệch thì In phiếu tìm nhầm chỗ. Test/đọc kỹ.
- Đổi thư mục giữa lúc đang có phiếu cũ ở thư mục cũ → link In phiếu của đơn cũ (phiếu ở thư mục cũ) sẽ báo "chưa có file" (đường dẫn mới). Chấp nhận — ghi nhận; phiếu cũ vẫn ở thư mục cũ.
- Chu kỳ đọc số: đọc config lúc mở phiên; KHÔNG cần nóng-đổi giữa phiên đang chạy (đơn giản, tránh phức tạp).
- `OrderRowViewModel` đang được việc "màu trạng thái" TRÁNH đụng — việc này đụng OrderRowViewModel (SlipPath). Hai việc KHÔNG giao file khác (màu: OrdersView.axaml + converter). Khi gộp không xung đột.
- **Worktree:** không đụng cây chính; `git status` xác nhận trước khi sửa.

---

## Báo cáo thực thi (Opus điền sau khi xong)

<Opus dán báo cáo cuối vào đây hoặc Fable tổng hợp lại sau nghiệm thu.>
