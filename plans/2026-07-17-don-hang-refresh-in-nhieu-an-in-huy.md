# Plan: Màn Đơn hàng — (A) tự refresh sau sync, (B) nút "In nhiều đơn" phiếu Chờ lấy hàng, (C) ẩn "In phiếu" cho đơn Đã hủy

- **Ngày:** 2026-07-17
- **Trạng thái:** chờ (làm SAU khi `2026-07-17-don-hang-loc-shop-go-de-tim.md` merge — cùng đụng OrdersView/OrdersViewModel)
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)

## 1. Bối cảnh & mục tiêu

Ba yêu cầu người dùng cho màn "Đơn hàng" (`OrdersView.axaml`(+`.cs`), `OrdersViewModel.cs`, `OrderRowViewModel.cs`):

- **A. Tự refresh sau sync:** hiện màn Đơn hàng chỉ truy vấn lại DB khi (i) `MainViewModel` điều hướng SANG index Đơn hàng (`_ordersVm.Reload()`), hoặc (ii) đổi bộ lọc/`Refresh` nút "Làm mới". Nếu người dùng ĐANG Ở màn Đơn hàng lúc sync xong (sync chạy nền ở `AccountSession.SyncOrdersAsync` → `OrdersRepository.UpsertMany`), hoặc click lại đúng tab đang mở (nav index không đổi → `OnSelectedNavIndexChanged` không bắn), màn KHÔNG tự nạp lại → thấy dữ liệu cũ; đổi ComboBox mới thấy. Cần: **sync xong tự động refresh** màn Đơn hàng.
- **B. Nút "In nhiều đơn":** phiếu giao đã tải về là file PDF ở thư mục hóa đơn (`Settings.GetInvoiceFolder()`, tên `<mã đơn>.pdf`). Người dùng muốn một nút in HÀNG LOẠT: in **toàn bộ đơn "Chờ lấy hàng" đang có trong list hiển thị** — gửi từng file PDF tới máy in mặc định.
- **C. Ẩn "In phiếu" cho đơn Đã hủy:** cột "Phiếu" hiện luôn có nút "In phiếu"; đơn "Đã hủy" chưa qua xử lý nên KHÔNG có phiếu → ẩn nút cho dòng Đã hủy.

## 2. Phạm vi

- **Làm:** A (event refresh: `AppServices`/manager + `AccountSession` phát tín hiệu sau sync + `OrdersViewModel` nghe → Reload), B (nút + lệnh in hàng loạt + in 1 file PDF), C (ẩn nút theo trạng thái) — trong `OrdersView.axaml`(+`.cs`), `OrdersViewModel.cs`, `OrderRowViewModel.cs`, `AccountSession.cs`, `AppServices.cs`.
- **Không làm:** KHÔNG đụng luồng sync/xử lý đơn lõi (chỉ THÊM tín hiệu sau khi UpsertMany xong); KHÔNG đổi cột khác/màu/double-click/lọc-shop (việc trước); KHÔNG tự động in (chỉ in khi bấm nút).

## 3. Các bước thực hiện

### A. Tự refresh màn Đơn hàng sau khi sync

1. **Tín hiệu "đơn đã đổi":** thêm event trên tầng dùng chung mà cả `AccountSession` (phát) lẫn `OrdersViewModel` (nghe) đều thấy — ưu tiên `AppServices` (đã giữ `Sessions`, `Orders`, `Log`...): thêm `event Action? OrdersChanged;` + method `RaiseOrdersChanged()` (hoặc để manager phát). Khảo sát mẫu event hiện có (`Sessions.Changed`/`CookieSaved`) để theo phong cách + marshal.
2. **Phát sau sync:** trong `AccountSession.SyncOrdersAsync`, NGAY SAU `_services.Orders.UpsertMany(...)` thành công (đã ghi DB), gọi `_services.RaiseOrdersChanged()`. (Chỉ phát khi thực sự có ghi — hoặc luôn phát sau sync; đơn giản: luôn phát sau UpsertMany.)
3. **Nghe + refresh:** `OrdersViewModel` constructor subscribe `OrdersChanged` → **marshal về UI thread** (event phát từ thread nền của phiên — `Dispatcher.UIThread.Post`) → gọi `Reload()` (giữ bộ lọc hiện tại; `Reload` đã dựng lại options + Apply). Chống nhấp nháy: Reload giữ SelectedAccount/SelectedStatus như hiện có.
4. **Bổ sung "vào màn luôn refresh" (phòng ca click cùng tab):** cân nhắc `OrdersView` `OnAttachedToVisualTree`/`Loaded` → `vm.Refresh()` để mỗi lần màn hiện lại (kể cả cùng nav index nếu view tái tạo) đều tươi. Executor chọn nếu gọn; không thì bỏ (event ở A.3 đã phủ ca sync-xong).

### B. Nút "In nhiều đơn" (phiếu Chờ lấy hàng trong list)

1. **In 1 file PDF (helper):** hàm gửi một file PDF tới máy in mặc định Windows — `Process.Start(new ProcessStartInfo { FileName = pdfPath, Verb = "print", UseShellExecute = true })` (in bằng app PDF mặc định tới máy in mặc định), bọc try/catch. Khảo sát: dự án từng có in (đã gỡ kiosk-printing) — nay in FILE PDF, verb "print" là cách đơn giản/bền nhất; ghi rõ hạn chế (phụ thuộc app PDF mặc định hỗ trợ verb print; không hoàn toàn im lặng tùy app). Tách helper (App layer) để tái dùng.
2. **Lệnh in hàng loạt:** `[RelayCommand] PrintPendingSlips()` trên `OrdersViewModel`: duyệt `Rows` (danh sách ĐANG hiển thị — đã theo bộ lọc), lọc đơn có **trạng thái "Chờ lấy hàng"** (dùng chuẩn hóa text — chứa "chờ lấy hàng"); với mỗi đơn: `SlipPath` tồn tại → in; không tồn tại → đếm "thiếu file". Có delay nhỏ giữa các lệnh in (tránh dội máy in). Xong → `StatusMessage = $"Đã gửi in {đã in} phiếu Chờ lấy hàng (thiếu file: {thiếu})."`.
3. **Nút UI:** thêm nút "In nhiều đơn" trên thanh lọc/thao tác màn Đơn hàng (cạnh "Làm mới"/"Xuất CSV"), tooltip rõ "In toàn bộ phiếu (PDF) của đơn Chờ lấy hàng trong danh sách đang hiển thị". Bật khi có ít nhất 1 đơn (kiểm lúc bấm).

### C. Ẩn "In phiếu" cho đơn Đã hủy

1. `OrderRowViewModel`: thêm thuộc tính `bool CanPrintSlip` = trạng thái KHÔNG phải hủy (chuẩn hóa: không chứa "hủy"). (Hoặc rộng hơn: chỉ true cho các trạng thái CÓ THỂ có phiếu — nhưng theo yêu cầu tối thiểu: ẩn cho "Đã hủy"; executor làm đúng yêu cầu = ẩn khi chứa "hủy".)
2. `OrdersView.axaml`: nút "In phiếu" trong cột "Phiếu" thêm `IsVisible="{Binding CanPrintSlip}"` → dòng Đã hủy không hiện nút.

## 4. Tiêu chí nghiệm thu

- [ ] Build 0 warning; test pass toàn bộ (+ test thuần: lọc đơn "Chờ lấy hàng" trong danh sách để in; `CanPrintSlip` theo trạng thái; nếu tách được).
- [ ] Đọc code: A — sync xong phát tín hiệu, OrdersViewModel nghe + marshal UI thread + Reload; B — in đúng tập "Chờ lấy hàng" đang hiển thị, in-1-file bọc lỗi, báo số đã in/thiếu; C — ẩn nút In phiếu khi trạng thái chứa "hủy". Không đụng luồng sync lõi/cột khác.
- [ ] Smoke thật (người dùng): sync xong đang ở màn Đơn hàng → tự thấy đơn mới (không cần đổi lọc); bấm "In nhiều đơn" → các phiếu Chờ lấy hàng ra máy in; dòng Đã hủy không còn nút In phiếu. Executor ghi rõ chờ người dùng.

## 5. Rủi ro & lưu ý

- **Event refresh phát từ thread nền** (phiên sync ở Task.Run) → OrdersViewModel PHẢI marshal `Dispatcher.UIThread.Post` trước khi đụng `Rows`/ObservableCollection (bài học marshal UI thread).
- Reload sau mỗi sync: nếu người dùng đang gõ ô tìm kiếm/đang xem, Reload giữ bộ lọc (SelectedAccount/Status/SearchText) — KHÔNG reset ô tìm; kiểm `Reload` giữ `SearchText` (hiện `Reload` không đụng SearchText → OK).
- **In PDF verb "print"**: phụ thuộc app PDF mặc định (Edge/Acrobat...) hỗ trợ; máy không có app PDF mặc định → lệnh in lỗi (bọc try/catch, đếm lỗi, không sập). WDAC chặn *binary tự build*, KHÔNG chặn mở/in file PDF bằng app hệ thống.
- In nhiều đơn: nhiều lệnh print liên tiếp có thể mở nhiều cửa sổ app PDF (tùy app) → delay nhỏ giữa các lệnh; ghi nhận hạn chế, không cố in-im-lặng-tuyệt-đối (ngoài phạm vi).
- "Chờ lấy hàng" nhận diện bằng chuẩn hóa CHỨA (bền biến thể); đơn Chờ lấy hàng CHƯA có file PDF (chưa xử lý/chưa tải phiếu) → bỏ qua + đếm thiếu.
- Cùng đụng OrdersView/OrdersViewModel với việc "lọc shop" → plan này CHỜ việc đó merge rồi mới giao (tránh xung đột).

---

## Báo cáo thực thi (Opus điền sau khi xong)

<Opus dán báo cáo cuối vào đây hoặc Fable tổng hợp lại sau nghiệm thu.>
