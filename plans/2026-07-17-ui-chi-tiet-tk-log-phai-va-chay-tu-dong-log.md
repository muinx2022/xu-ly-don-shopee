# Plan: Chi tiết TK — dồn field về trái, log sang phải; màn Chạy tự động — thêm panel log bên phải

- **Ngày:** 2026-07-17
- **Trạng thái:** hoàn thành (đã push; chờ người dùng xem)
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)

## 1. Bối cảnh & mục tiêu

Hai chỉnh giao diện người dùng yêu cầu:

**A. Chi tiết tài khoản (`AccountsView.axaml`, cột chi tiết = `Grid.Column=1` của grid gốc `340,*`):** hiện bố cục là các card DÀN 2 CỘT (Đăng nhập+Proxy | Cookie+Địa chỉ) ở hàng trên, **panel log (nền đen) ở HÀNG DƯỚI** (`RowDefinitions="*,220"`). Người dùng muốn đổi thành: **DỒN HẾT field về BÊN TRÁI** (các card xếp DỌC 1 cột), **log chuyển sang BÊN PHẢI** (cột đứng) — tức cột chi tiết chia 2 cột NGANG: trái = field (dọc), phải = log.

**B. Màn Chạy tự động (`AutoRunView.axaml` + `AutoRunViewModel`):** hiện chỉ có ô cấu hình + nút Bắt đầu/Dừng + trạng thái (`CurrentPhase`). Người dùng muốn **thêm panel log bên PHẢI** để theo dõi autorun đang chạy shop nào, làm gì (log ghi qua `_services.Log` — `AutoRunService` dùng `LogSource="Chạy tự động"` + log per-account theo email).

## 2. Phạm vi

- **Làm:** `AccountsView.axaml` (bố cục lại cột chi tiết: field trái + log phải), `AutoRunView.axaml`(+`.cs`) + `AutoRunViewModel.cs` (thêm panel log + binding + auto-scroll). Tái dùng style/panel log đen sẵn có.
- **Không làm:** KHÔNG đổi binding/logic field (chỉ SẮP LẠI); KHÔNG đổi cột danh sách trái, màn Đơn hàng, luồng autorun/scheduler; KHÔNG đụng `FilteredLogEntries`/lọc-log-theo-tài-khoản (giữ nguyên, chỉ đổi vị trí panel). **Làm trên cây chính** (không agent song song lúc giao).

## 3. Các bước thực hiện

### A. Chi tiết TK — field trái, log phải

1. Cột chi tiết (`Grid.Column=1`) đổi từ `RowDefinitions="*,220"` (form trên / log dưới) sang **`ColumnDefinitions="*,<W>"`** (field trái / log phải). `<W>` = bề rộng cột log hợp lý (ví dụ `380` hoặc `*` tỉ lệ — executor chọn để log đủ đọc, field còn rộng; ưu tiên cố định ~360–400 để log không quá hẹp).
2. **Cột trái (field):** ScrollViewer chứa StackPanel DỌC — bỏ `Grid ColumnDefinitions="*,16,*"` dàn 2 cột card; xếp các card **1 cột dọc** theo thứ tự: header (Chi tiết tài khoản + badge) + hàng nút + 3 dòng thông báo (Error/Busy/Order) + card THÔNG TIN ĐĂNG NHẬP + COOKIE + PROXY + ĐỊA CHỈ LẤY HÀNG. Giữ NGUYÊN mọi binding/Command/x:Name/nội dung card (đặc biệt card Đăng nhập có nút 👁). `MaxWidth` form phù hợp (không quá rộng khi cột trái to; ví dụ MaxWidth ~640 neo trái, hoặc Stretch — executor cân để không trông trống).
3. **Cột phải (log):** DI CHUYỂN panel log đen HIỆN CÓ (Border nền `#0F1113`, tiêu đề "Nhật ký — {Email}", nút Xóa `logClear`, `x:Name="LogList"` bind `FilteredLogEntries`, LogPath) từ hàng dưới sang cột phải. Bo góc/margin hợp lý (thẻ log như hiện tại — giữ `Margin`/`CornerRadius` đã có ở đợt trước). **GIỮ `x:Name="LogList"`** (code-behind auto-scroll). Log vẫn LỌC theo tài khoản đang chọn (FilteredLogEntries) — không đổi.
4. Kiểm code-behind `AccountsView.axaml.cs` auto-scroll vẫn tìm `LogList` (không đổi tên) — chỉ đổi vị trí trong cây, tên giữ.

### B. Màn Chạy tự động — panel log bên phải

1. `AutoRunViewModel.cs`: thêm `public ObservableCollection<LogEntry> LogEntries => _services.Log.Entries;` (TOÀN BỘ nhật ký — mọi shop + autorun, để theo dõi tiến trình) + `public string LogPath => _services.Log.CurrentLogPath;` (mẫu `AccountsViewModel`). (KHÔNG lọc theo tài khoản ở màn này — autorun chạy nhiều shop; hiển thị toàn bộ.)
2. `AutoRunView.axaml`: bố cục 2 cột `ColumnDefinitions="*,<W>"` — trái = phần cấu hình/nút/trạng thái hiện có; phải = **panel log đen** (tái dùng style panel log của `AccountsView`: nền `#0F1113`, chữ `#EDEDED` Consolas, `ListBoxItem` sát dòng, nút Xóa tối, LogPath) bind `LogEntries`, `x:Name` riêng (vd `AutoRunLogList`). Tiêu đề "Nhật ký hoạt động".
3. `AutoRunView.axaml.cs`: thêm auto-scroll xuống cuối khi có dòng mới (mẫu `AccountsView.axaml.cs` — subscribe `LogEntries.CollectionChanged`, marshal UI, ScrollIntoView cuối). Nút Xóa → `_services.Log.Clear()` (hoặc command trong VM; executor chọn — có thể thêm `[RelayCommand] ClearLog` gọi `_services.Log.Clear()`).
4. Nếu style panel log đang là style CỤC BỘ trong `AccountsView` (không dùng lại được ở `AutoRunView`) → chép style cần thiết vào `AutoRunView` `UserControl.Styles` HOẶC nâng lên style dùng chung (`Controls.axaml`) nếu gọn — executor chọn cách ít rủi ro, ghi rõ (không phá màn khác).

## 4. Tiêu chí nghiệm thu

- [ ] Build 0 warning; test pass toàn bộ (thuần UI, không vỡ test).
- [ ] Đọc code: A — cột chi tiết là 2 cột ngang (field dọc trái, log phải), mọi binding/Command/`x:Name="LogList"` giữ nguyên, log vẫn lọc theo tài khoản; B — màn Chạy tự động có panel log phải bind toàn bộ `LogEntries`, auto-scroll, không đụng scheduler/config logic.
- [ ] Smoke thị giác (người dùng): chi tiết TK — field gọn bên trái, log đen bên phải, chọn shop thì log đổi theo; Chạy tự động — bên phải hiện nhật ký, khi chạy thấy log các shop cuộn xuống. Executor ghi rõ chờ người dùng.

## 5. Rủi ro & lưu ý

- **Thuần trình bày + thêm binding log** — KHÔNG đổi logic field/scheduler; build (compiled-bindings) bắt sai binding. Giữ `x:Name="LogList"` (AccountsView) cho auto-scroll.
- Cột chi tiết đổi Row→Column: kiểm mọi thứ trong cột trái không bị tràn/ép (card Đăng nhập 2 ô Tên+Mật khẩu trên 1 dòng — trong cột trái hẹp hơn có thể chật; executor cân MaxWidth/để 2 ô xuống dòng nếu cần).
- Màn Chạy tự động log TOÀN BỘ (không lọc) — khác màn Tài khoản (lọc theo TK); đây là chủ ý (autorun nhiều shop).
- Style panel log: nếu chép cục bộ sang AutoRunView, đảm bảo không trùng key gây lỗi; nếu nâng lên chung, kiểm không đổi màn Tài khoản.
- Cây chính (không agent song song); `git status` sạch trước khi sửa.

---

## Báo cáo thực thi (Opus điền sau khi xong)

<Opus dán báo cáo cuối vào đây hoặc Fable tổng hợp lại sau nghiệm thu.>
