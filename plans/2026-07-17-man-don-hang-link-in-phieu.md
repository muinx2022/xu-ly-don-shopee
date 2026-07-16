# Plan: Màn "Đơn hàng" — thêm link "In phiếu" mở file PDF phiếu đã tải lúc xử lý đơn

- **Ngày:** 2026-07-17
- **Trạng thái:** đang làm
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`, worktree)

## 1. Bối cảnh & mục tiêu

Màn "Đơn hàng" (đã có, commit `dbc550c`) hiển thị bảng đơn đã sync. Lúc "Xử lý đơn", app tải phiếu giao PDF về thư mục `D:\Phieu-giao-hang` với tên = mã đơn (xem `SaveSlipAsync`/`ProcessFirstOrderAsync` trong `ShopeeLoginService.cs`: `downloadDir = @"D:\Phieu-giao-hang"`, tên file = `ShopeeShippingNav.SanitizeFileName(orderCode) + ".pdf"`, orderCode chính là mã đơn `order_sn`).

Người dùng yêu cầu: thêm **link "In phiếu"** ở mỗi dòng bảng Đơn hàng; bấm → **mở file PDF phiếu đã tải** của đúng đơn đó (bằng ứng dụng PDF mặc định của Windows). Đơn chưa xử lý / không có file → báo nhẹ, không lỗi.

## 2. Phạm vi

- **Làm:** `OrdersView.axaml` (thêm cột/link), `OrderRowViewModel` (đường dẫn file phiếu + command mở), tách hằng thư mục phiếu dùng chung, helper mở file bằng shell. **Làm trong WORKTREE.**
- **Không làm:** không đổi luồng tải phiếu/xử lý đơn; không đụng các file mà việc khác đang sửa (AccountSession/AccountsViewModel/IAccountSession — nút tự mở phiên); không thêm cột dữ liệu DB (đường dẫn suy ra từ order_sn lúc hiển thị, không lưu).

## 3. Các bước thực hiện

1. **Khảo sát:** `SaveSlipAsync`/`ProcessFirstOrderAsync` để lấy CHÍNH XÁC: thư mục phiếu (`D:\Phieu-giao-hang`) + cách dựng tên file (`SanitizeFileName(order_sn) + ".pdf"`). `SanitizeFileName` ở `ShopeeShippingNav` (Core) — tái dùng. Kiểm xem đã có hằng thư mục phiếu dùng chung chưa; CHƯA thì tách hằng `public const string SlipDownloadDir = @"D:\Phieu-giao-hang";` vào một chỗ Core dùng chung (ví dụ `ShopeeShippingNav` hoặc lớp hằng phù hợp) và cho `ProcessFirstOrderAsync` dùng hằng đó thay literal (đồng bộ nguồn — nếu sau này đổi thư mục thì cả 2 nơi theo). Đây là thay đổi nhỏ ở call site tải phiếu, KHÔNG đổi hành vi.
2. **`OrderRowViewModel`:**
   - Thuộc tính `SlipPath` (string) = `Path.Combine(SlipDownloadDir, SanitizeFileName(OrderSn) + ".pdf")`.
   - `[RelayCommand] OpenSlip()`: nếu `File.Exists(SlipPath)` → mở bằng shell (`Process.Start(new ProcessStartInfo(SlipPath){ UseShellExecute = true })`, bọc try/catch nuốt lỗi → báo qua callback/StatusMessage của OrdersViewModel); không tồn tại → báo `"Chưa có file phiếu đã tải cho đơn {OrderSn} (đơn chưa xử lý hoặc phiếu chưa tải)."`. Cách đưa thông báo ra màn: OrderRowViewModel không giữ tham chiếu ngược VM lớn — dùng một `Action<string>? notify` truyền vào lúc tạo row, hoặc expose sự kiện; executor chọn cách gọn nhất theo pattern OrdersViewModel hiện có (OrdersViewModel tạo các OrderRowViewModel — có thể truyền callback set StatusMessage). Ghi rõ cách chọn.
   - KHÔNG kiểm `File.Exists` khi render mỗi dòng (tốn IO khi bảng nhiều dòng) — chỉ kiểm lúc bấm. Link luôn hiện.
3. **`OrdersView.axaml`:** thêm cột "Phiếu" (hoặc gộp vào cột thao tác) chứa link/nút "In phiếu" (`Classes` kiểu link như nút "Xem chi tiết" của pattern, hoặc HyperlinkButton/Button link-style Avalonia) `Command="{Binding OpenSlipCommand}"`. Chữ "In phiếu".
4. **Mở file bằng shell:** nếu dự án đã có helper mở file/đường dẫn (grep `UseShellExecute`/`Process.Start`) thì tái dùng; chưa có thì viết gọn trong command (Core-free — đây là App layer). Bọc try/catch: lỗi mở (không có app PDF mặc định...) → báo qua notify, KHÔNG ném.

## 4. Tiêu chí nghiệm thu

- [ ] Build 0 warning; test pass toàn bộ (thêm test cho `SlipPath` dựng đúng từ order_sn nếu tách được thuần — ví dụ order_sn có ký tự lạ → sanitize đúng).
- [ ] Đọc code: đường dẫn phiếu = cùng hằng với nơi tải phiếu (không lệch thư mục/tên); OpenSlip kiểm File.Exists lúc bấm, nuốt lỗi mở, báo rõ khi thiếu file; không kiểm IO khi render; không đụng file ngoài phạm vi.
- [ ] Smoke thật (người dùng): đơn đã xử lý (có PDF trong D:\Phieu-giao-hang) → bấm "In phiếu" mở đúng file PDF; đơn chưa có phiếu → báo "Chưa có file phiếu...". Executor ghi rõ chờ người dùng.

## 5. Rủi ro & lưu ý

- **Thư mục/tên file phải KHỚP TUYỆT ĐỐI** nơi tải phiếu — nếu lệch (sanitize khác, thư mục khác) thì luôn báo "chưa có file". Dùng CHUNG hằng + CHUNG hàm SanitizeFileName với `SaveSlipAsync`.
- `Process.Start` ShellExecute mở PDF bằng app mặc định — KHÔNG phải chạy binary tự build nên WDAC không chặn; vẫn bọc try/catch phòng máy không có app PDF mặc định.
- Order_sn trong DB đã là mã đơn "sạch" nhưng vẫn qua SanitizeFileName cho khớp cách đặt tên lúc lưu.
- **Worktree:** đường dẫn plan tương đối `plans/…`; tuyệt đối không đọc/ghi cây chính. Khi gộp sẽ không xung đột với việc nút-tự-mở-phiên (khác file).

---

## Báo cáo thực thi (Opus điền sau khi xong)

<Opus dán báo cáo cuối vào đây hoặc Fable tổng hợp lại sau nghiệm thu.>
