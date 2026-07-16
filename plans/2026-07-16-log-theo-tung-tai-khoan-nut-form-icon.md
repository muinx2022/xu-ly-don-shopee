# Plan: Log theo TỪNG tài khoản nằm trong cột chi tiết, bỏ khoảng trống phải, hàng nút form dùng icon

- **Ngày:** 2026-07-16
- **Trạng thái:** hoàn thành (chờ người dùng xem bằng mắt)
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)

## 1. Bối cảnh & mục tiêu

Sau plan `2026-07-16-gon-giao-dien-tai-khoan.md` (đã merge, commit `eae85d4`), người dùng chạy thật (luồng xử lý ĐÃ ỔN) và yêu cầu chỉnh tiếp giao diện (2 tin nhắn, có ảnh):

1. "List tk thì để nguyên cột thứ 2, cột thứ 3 là thông tin tk và log, **log này là của TỪNG tài khoản** chứ không phải log chung. **Bỏ phần bên phải thừa** đi." → Panel log rời khỏi hàng đáy toàn-ngang, chuyển vào **trong cột chi tiết** (dưới form); nội dung log **lọc theo tài khoản đang chọn**; cột danh sách trở lại cao hết màn; vùng trống bên phải form (do form Width cố định 720) phải hết.
2. "Phần button chỗ này cũng dùng icon, không dùng text nữa" (ảnh khoanh hàng nút form: Lưu thay đổi / Hủy / Kiểm tra / Dừng / Xử lý đơn / Mở trang bán hàng).

Hiện trạng (đã khảo sát):
- `AccountsViewModel.cs`: `LogEntries => _services.Log.Entries` (pass-through collection CHUNG mọi phiên, dòng 46), `LogPath => _services.Log.CurrentLogPath` (49), `ClearLog() => _services.Log.Clear()` (53), có `partial void OnSelectedRowChanged(...)` (204). `LogEntry` có `Source` = email/nhãn tài khoản của phiên phát log.
- `ActivityLog.cs` (`src/XuLyDonShopee.App/Services/`): `Entries` mutate CHỈ trên UI thread qua `uiPost` (Append/Clear); cap ring-buffer 500; đã có test `ActivityLogTests.cs`.
- `AccountsView.axaml`: Grid gốc `RowDefinitions="*,Auto"` — hàng 1 là panel log đen toàn-ngang (`x:Name="LogList"`, code-behind auto-scroll `FindControl("LogList")`); form chi tiết `StackPanel Width="720"`; hàng nút form là các Button text.

## 2. Phạm vi

- **Làm:** `AccountsView.axaml`, `AccountsViewModel.cs`, `ActivityLog.cs` + test `ActivityLogTests.cs`.
- **Không làm:** KHÔNG đổi cách GHI log (file trên đĩa vẫn chung một file, mọi phiên vẫn Append như cũ); KHÔNG đổi `LogEntry`/cap 500; KHÔNG đụng luồng xử lý đơn; KHÔNG đụng cột danh sách (trừ việc nó cao trở lại do bỏ hàng log đáy) và các nút icon đã làm đợt trước.

## 3. Các bước thực hiện

### A. Lọc log theo tài khoản đang chọn (`AccountsViewModel.cs`)

1. Thêm `public ObservableCollection<LogEntry> FilteredLogEntries { get; } = new();` — nguồn hiển thị MỚI của panel log.
2. Trong constructor: subscribe `_services.Log.Entries.CollectionChanged` (event luôn nổ trên UI thread vì ActivityLog mutate qua uiPost — ghi comment rõ):
   - `Action == Add`: với từng entry mới, nếu khớp bộ lọc (xem bước 3) → `FilteredLogEntries.Add(entry)`.
   - Mọi action khác (Remove do cap ring-buffer, Reset do Clear...) → **rebuild toàn bộ** `FilteredLogEntries` từ `_services.Log.Entries` theo bộ lọc (≤500 phần tử — rẻ, đơn giản, không lệch trạng thái).
3. Bộ lọc: entry khớp khi `SelectedRow is not null && entry.Source == SelectedRow.Email`. Chưa chọn tài khoản → filtered RỖNG.
4. Trong `OnSelectedRowChanged` hiện có: gọi rebuild filtered (thêm private method `RebuildFilteredLog()` dùng chung cho 2 chỗ). LƯU Ý bài học dự án (memory `viewmodel-mutable-field-after-await`): mọi thao tác này ĐỒNG BỘ trên UI thread, KHÔNG await xen giữa đọc `SelectedRow` và dùng nó — chụp `var email = SelectedRow?.Email;` một lần đầu method rồi dùng biến cục bộ.
5. `ClearLog()` đổi thành: có `SelectedRow` → `_services.Log.Clear(SelectedRow.Email)` (overload mới, xóa các dòng của riêng tài khoản đó — xem B); chưa chọn → giữ `Clear()` toàn bộ. (Filtered tự cập nhật qua event.)

### B. `ActivityLog.cs` — overload xóa theo nguồn + test

1. Thêm `public void Clear(string source)`: qua `_uiPost`, remove khỏi `Entries` mọi entry có `Source == source` (duyệt ngược chỉ số để remove an toàn). KHÔNG đụng file trên đĩa (như `Clear()` hiện tại). Doc-comment tiếng Việt tương tự.
2. `ActivityLogTests.cs`: thêm test cho `Clear(source)` — seed vài entry 2 nguồn khác nhau (uiPost đồng bộ `a => a()` như các test hiện có), gọi `Clear("a")` → chỉ còn entry nguồn "b"; `Clear()` vẫn xóa hết (ca cũ giữ nguyên).

### C. `AccountsView.axaml` — log vào cột chi tiết + bỏ khoảng trống phải

1. Grid gốc bỏ `RowDefinitions="*,Auto"` (trở lại 1 tầng): còn Grid 2 cột `340,*` (danh sách | chi tiết). Cột danh sách cao hết màn trở lại.
2. Cột chi tiết (Grid.Column=1) thành Grid `RowDefinitions="*,220"`:
   - Hàng 0: placeholder + ScrollViewer form như hiện tại.
   - Hàng 1: panel log đen (GIỮ NGUYÊN style nền `#0F1113`, chữ `#EDEDED`, `ListBoxItem` sát dòng, nút Xóa `logClear`, LogPath — chỉ DI CHUYỂN vị trí):
     - `ItemsSource` đổi → `{Binding FilteredLogEntries}`; **GIỮ `x:Name="LogList"`** (code-behind auto-scroll).
     - Tiêu đề: "Nhật ký — {tên tài khoản}" khi có chọn (bind `SelectedRow.Email`, ví dụ TextBlock phụ cạnh tiêu đề, `IsVisible` theo SelectedRow != null qua `ObjectConverters.IsNotNull`); chưa chọn → "Nhật ký hoạt động" + khung trống.
     - Tooltip nút Xóa cập nhật: "Xóa các dòng đang hiển thị của tài khoản này (không xóa file log trên đĩa)".
3. Bỏ khoảng trống phải: `StackPanel` form bỏ `Width="720"` → `MaxWidth="920"` + `HorizontalAlignment="Stretch"` (field giãn theo cửa sổ tới trần 920 — vẫn ổn định giữa các tài khoản vì không phụ thuộc nội dung; comment cũ về Width cố định cập nhật theo). Panel log hàng 1 stretch full bề ngang cột chi tiết.

### D. Hàng nút form → icon (`AccountsView.axaml`)

1. 6 nút giữ NGUYÊN Command/IsEnabled/Classes giọng màu, đổi Content text → glyph text-only (tránh emoji màu — bài học đợt trước) + `ToolTip.Tip` = nguyên văn chữ cũ (kèm mô tả dài sẵn có nếu nút đã có tooltip):
   - Lưu thay đổi: `✔` (Classes="accent" — nền cam chữ trắng như cũ)
   - Hủy: `↶`
   - Kiểm tra: `⟳`
   - Dừng: `■`
   - Xử lý đơn: `►` (accentOutline — đồng ngôn ngữ ► = chạy)
   - Mở trang bán hàng: `↗` (accentOutline)
   - Kích thước nút vuông gọn (~36x32, FontSize 14-15, Padding 0) — thêm class cục bộ (có thể tái dùng/pha `iconAction` sẵn có nếu hợp; nút accent nền cam thì style riêng). Executor thử glyph render Windows, được phép thay glyph tương đương text-only, ghi rõ bộ chọn cuối.
2. Layout hàng nút giữ nhóm trái (Lưu/Hủy) — nhóm phải (Kiểm tra/Dừng/Xử lý đơn/Mở trang) như bố cục cũ.

### E. Kiểm chứng

1. `dotnet build` 0 lỗi 0 warning; `dotnet test` pass toàn bộ (400 + test mới của B.2).
2. Grep: `FilteredLogEntries` là ItemsSource của LogList; `x:Name="LogList"` còn; không còn binding `LogEntries` cũ trong view; đủ Command binding các nút.
3. Smoke thật (người dùng): chọn shop A → khung log chỉ hiện dòng `[A]`; đổi sang shop B → log đổi theo; shop đang chạy nền vẫn ghi đủ vào file; nút Xóa chỉ xóa dòng shop đang chọn; hàng nút form là icon có tooltip; không còn vùng trống chết bên phải. Executor ghi rõ chờ người dùng.

## 4. Tiêu chí nghiệm thu

- [ ] Build sạch + test pass (400 + mới; WDAC chặn đồng loạt → ghi rõ).
- [ ] Đọc code: filtered rebuild đồng bộ UI-thread, không await xen giữa (bài học ViewModel); Add-path chỉ append khi khớp source; Clear(source) chỉ xóa hiển thị đúng nguồn, không đụng file; panel log nằm trong cột chi tiết, danh sách cao full; form MaxWidth stretch; 6 nút icon đủ Command + tooltip.
- [ ] Smoke thị giác + hành vi: chờ người dùng.

## 5. Rủi ro & lưu ý

- `Entries.CollectionChanged` nổ trên UI thread (ActivityLog uiPost) — KHÔNG thêm lock/await trong handler; rebuild là vòng for thuần.
- KHÔNG giữ tham chiếu `SelectedRow` qua await trong bất kỳ method mới nào (memory `viewmodel-mutable-field-after-await`).
- Instance `AccountRowViewModel` trong ObservableCollection có thể bị thay khi RefreshList — bộ lọc chỉ dùng `Email` (string chụp tại thời điểm rebuild), không giữ tham chiếu row.
- Đừng quên trường hợp `Email` trùng giữa 2 tài khoản (hiếm): filter theo Email là chấp nhận được (log Source cũng chính là email).
- Hai phiên Claude có thể cùng mở repo — executor `git status` trước khi sửa; cây phải sạch ở HEAD hiện tại (Fable ghi trong prompt); thấy thay đổi lạ → DỪNG báo.

---

## Báo cáo thực thi (Opus điền sau khi xong)

Opus làm đủ A/B/C/D (5 file + code-behind): FilteredLogEntries lọc theo SelectedRow.Email (Add append khớp / Remove gỡ per-item / còn lại rebuild — nhánh Remove per-item bổ sung sau khi panel nghiệm thu chốt 2 lỗi hiệu năng: cap-500 và Clear(source) gây rebuild toàn bộ mỗi event); ActivityLog.Clear(source) + 3 test mới (gồm test cap); panel log chuyển vào cột chi tiết (RowDefinitions "*,220"), tiêu đề động "Nhật ký — {Email}", auto-scroll code-behind đổi theo FilteredLogEntries; form MaxWidth=920 stretch (hết trống phải); 6 nút form icon ✓ ↶ ⟳ ■ ► ↗ (text-only, giữ Command/tooltip). Khác plan đã duyệt: sửa thêm AccountsView.axaml.cs (bắt buộc cho auto-scroll đúng collection); ✓ U+2713 thay ✔; rebuild trước guard _isRefreshing; giữ property LogEntries.

Nghiệm thu (Fable): tự build 0 warning + 403/403 test; panel đối kháng 2/3 phiếu chốt 2 lỗi hiệu năng (đã sửa + kiểm lại), bác 2 phát hiện khác. Smoke thị giác/hành vi: CHỜ NGƯỜI DÙNG (đổi shop → log đổi theo; glyph ↶ ⟳ ↗ hiển thị dạng ký tự; không còn trống phải). Ghi chú theo dõi: nếu người dùng thấy THIẾU vài dòng ĐẦU PHIÊN trong panel lọc (dòng ghi trước khi phiên gắn nhãn email), báo lại để mở rộng bộ lọc.
