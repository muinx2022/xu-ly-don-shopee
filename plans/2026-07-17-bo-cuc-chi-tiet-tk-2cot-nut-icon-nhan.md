# Plan: Bố cục lại phần chi tiết tài khoản — dàn card 2 cột + nút icon kèm nhãn chữ

- **Ngày:** 2026-07-17
- **Trạng thái:** đang làm
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`, worktree)

## 1. Bối cảnh & mục tiêu

Người dùng phản hồi phần "Chi tiết tài khoản" nhìn chưa ổn ở 2 điểm (đã chốt qua hỏi-đáp): (a) **trống trải / lệch trái** — form `MaxWidth=760 HorizontalAlignment=Left` để mảng trống lớn bên phải trên màn rộng (rộng hơn Full HD); (b) **hàng nút icon khó hiểu** — 6 nút chỉ có glyph, không biết nút nào là gì nếu không rê chuột. Hướng chọn: **"Icon kèm nhãn chữ"** — dàn các card thành 2 cột (tận dụng bề ngang, hết trống) + nút có nhãn chữ + nhóm nút rõ ràng.

Hiện trạng (`src/XuLyDonShopee.App/Views/AccountsView.axaml`): cột chi tiết = ScrollViewer chứa StackPanel dọc: header + hàng nút icon (`formIcon`: ✓ ↶ ⟳ ■ ► ↗) + các dòng thông báo + 4 card dọc (THÔNG TIN ĐĂNG NHẬP / COOKIE / PROXY / ĐỊA CHỈ LẤY HÀNG). Style `formIcon` + nút `Classes` màu (accent/secondary/accentOutline) đã có. Hàng dưới cột chi tiết là panel log đen (RowDefinitions "*,220").

## 2. Phạm vi

- **Làm:** chỉ `AccountsView.axaml` (bố cục lại vùng ScrollViewer form + hàng nút). Nếu cần style cục bộ mới thì thêm trong `UserControl.Styles`.
- **Không làm:** KHÔNG đụng ViewModel/binding (giữ nguyên mọi `{Binding ...}`, Command, IsEnabled, x:Name); KHÔNG đụng panel log (hàng "*,220" giữ nguyên); KHÔNG đụng cột danh sách trái; KHÔNG đổi 4 nút icon panel danh sách (việc khác). Đây là thay đổi thuần trình bày.

## 3. Các bước thực hiện

### A. Form dàn 2 cột (hết trống-lệch-trái)

1. Vùng form trong ScrollViewer: bỏ `MaxWidth=760 Left` gây lệch. Thay bằng bố cục **lưới 2 cột** cho các card, `HorizontalAlignment=Stretch` với `MaxWidth` rộng hơn (ví dụ 1100–1200) để lấp bề ngang mà không quá dài; căn giữa phần thừa nếu vượt trần.
2. Xếp 4 card thành lưới 2 cột (`Grid ColumnDefinitions="*,16,*"` hoặc `UniformGrid Columns=2` + khoảng cách): 
   - Cột trái: THÔNG TIN ĐĂNG NHẬP, PROXY.
   - Cột phải: COOKIE, ĐỊA CHỈ LẤY HÀNG.
   - (Executor cân đối chiều cao/nhóm cho đẹp; card ĐĂNG NHẬP đang chứa 2 ô trên 1 dòng có thể cao hơn — sắp cặp sao cho 2 cột cân.)
3. Header (Chi tiết tài khoản + badge trạng thái) + hàng nút + các dòng thông báo (ErrorMessage/BusyStatus/OrderStatus) giữ **trải ngang trên cùng** (full bề rộng form), phía trên lưới card.

### B. Hàng nút icon KÈM NHÃN CHỮ + nhóm rõ

1. Mỗi nút: giữ glyph HIỆN CÓ + thêm **nhãn chữ** cạnh (icon + text trên cùng dòng, ví dụ `✓ Lưu`). Giữ nguyên `Command`/`IsEnabled`/`Classes` màu (accent/secondary/accentOutline). Bỏ/điều chỉnh style `formIcon` (vuông nhỏ) thành nút có cả icon+chữ (rộng theo nội dung, cao ~32–34). Tooltip dài giữ nguyên.
   - Lưu: `✓ Lưu` (accent) · Hủy: `↶ Hủy` (secondary) · Kiểm tra: `⟳ Kiểm tra` (secondary) · Sync: `⇊ Sync` (secondary) · Xử lý đơn: `► Xử lý đơn` (accentOutline) · Mở trang bán hàng: `↗ Mở trang` (accentOutline).
2. **Nhóm nút:** tách trực quan 2 nhóm — nhóm "sửa bản ghi" (Lưu/Hủy) bên trái, ngăn cách (khoảng trống co giãn `*` hoặc separator) rồi nhóm "thao tác phiên" (Kiểm tra / Sync / Xử lý đơn / Mở trang) bên phải. Bố cục hiện có đã dùng `ColumnDefinitions="Auto,Auto,*,Auto,Auto,Auto,Auto"` — giữ ý tưởng khoảng `*` ở giữa, chỉ đổi Content sang icon+chữ và giãn cột cho vừa.
3. Nếu 6 nút icon+chữ quá dài 1 hàng trên form hẹp → cho phép `WrapPanel` xuống hàng 2 (executor cân theo MaxWidth mới; ưu tiên 1 hàng nếu đủ chỗ).

### C. Kiểm chứng

1. `dotnet build` 0 warning (XAML compile bắt lỗi binding). `dotnet test` pass toàn bộ (không có test UI, nhưng không được vỡ).
2. Grep: mọi Command binding còn đủ (Save/Cancel/CheckOrders/Sync/ProcessOrders/OpenSeller/ClearLog); x:Name LogList còn; không đụng binding VM.

## 4. Tiêu chí nghiệm thu

- [ ] Build 0 warning; test pass.
- [ ] Đọc code: form dàn 2 cột card (hết neo-trái-lệch), nút có icon+chữ + nhóm rõ, không đổi Command/IsEnabled/x:Name/binding nào; panel log + cột danh sách nguyên.
- [ ] Smoke thị giác (người dùng): mở app xem phần chi tiết — cân đối, không còn mảng trống phải lớn; các nút đọc được nghĩa ngay (icon+chữ). Executor ghi rõ chờ người dùng.

## 5. Rủi ro & lưu ý

- **Thuần trình bày** — tuyệt đối không đổi tên binding/Command/x:Name (đặc biệt `x:Name="LogList"` cho auto-scroll code-behind). Sau sửa build ngay (compiled-bindings bắt sai).
- Card ĐĂNG NHẬP có nút 👁 hiện/ẩn mật khẩu — giữ nguyên cấu trúc con của nó khi chuyển vào lưới.
- Bề rộng form KHÔNG được phụ thuộc NỘI DUNG từng tài khoản (kẻo nút/card xê dịch khi chuyển chọn) — dùng cột `*`/MaxWidth cố định, không `Auto` theo nội dung.
- Glyph giữ bộ text-only đã dùng (tránh emoji màu). Nhãn chữ tiếng Việt có dấu.
- **Worktree:** đường dẫn plan tương đối; không đụng cây chính. Gộp sẽ không xung đột với việc nút-tự-mở-phiên (đụng .cs) hay In phiếu (đụng OrdersView) — việc này chỉ đụng AccountsView.axaml.

---

## Báo cáo thực thi (Opus điền sau khi xong)

<Opus dán báo cáo cuối vào đây hoặc Fable tổng hợp lại sau nghiệm thu.>
