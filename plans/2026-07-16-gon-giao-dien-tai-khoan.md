# Plan: Gọn giao diện màn Tài khoản — bỏ trường thừa, log xuống dưới nền đen, danh sách dạng lưới + nút icon

- **Ngày:** 2026-07-16
- **Trạng thái:** hoàn thành (đã merge về master, chờ người dùng xem bằng mắt)
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`, worktree)

## 1. Bối cảnh & mục tiêu

Người dùng muốn màn "Tài khoản" gọn lại (kèm 2 ảnh chụp + mock). File chính: `src/XuLyDonShopee.App/Views/AccountsView.axaml` (+ code-behind `AccountsView.axaml.cs` có auto-scroll dùng `FindControl<ListBox>("LogList")` — dòng ~55).

Cấu trúc hiện tại (đã khảo sát):
- Grid gốc 3 cột: cột 0 = panel "Danh sách tài khoản" (search + 4 nút text 2x2 + ListBox card tài khoản + nút "+ Thêm tài khoản"/thùng rác), cột 1 = form chi tiết (ScrollViewer, các card), cột 2 = panel "Nhật ký hoạt động" (dòng ~356–394).
- Form: Card 1 "THÔNG TIN ĐĂNG NHẬP" (~205–259): Tên đăng nhập (1 hàng riêng) rồi Grid 2 cột Mật khẩu | Số điện thoại. Card COOKIE (~263), Card PROXY (~276), Card ĐỊA CHỈ LẤY HÀNG (~295), Card 3 "Ghi chú + Trạng thái + Ngày tạo/Ngày sửa" (~307–351).
- ListBox tài khoản ItemTemplate: card bo góc có avatar chữ cái + 2–3 dòng (tên, trạng thái, "Đang chạy Chờ lấy: N").

Yêu cầu (nguyên văn người dùng, 2 tin nhắn):
1. "Bỏ những trường không cần thiết trong ô đỏ" = bỏ **Số điện thoại** và bỏ **nguyên Card 3** (Ghi chú, Trạng thái, Ngày tạo/Ngày sửa).
2. "Cho u/p lên cùng 1 dòng" = Tên đăng nhập + Mật khẩu chung 1 hàng 2 cột.
3. "Kéo các field khác trong form lên" = form dồn lên gọn (giảm margin/spacing vừa phải).
4. "Cho cửa sổ log xuống dưới, bỏ log bên phải. Cửa sổ log là nền đen, chữ trắng, không cách dòng như hiện tại."
5. "Phần danh sách, các nút nhấn dùng icon, không dùng text nữa. List shop đưa vào lưới, không dùng card như hiện tại nữa. Mock: `[ ] Tên shop` mỗi dòng."

## 2. Phạm vi

- **Làm:** chỉ `AccountsView.axaml` (+ nếu bắt buộc: chỉnh nhỏ code-behind, KHÔNG đổi logic). KHÔNG đụng ViewModel/Model/DB — các property `EditPhone`, `EditNote`, `EditStatus`, `CreatedAtText`... GIỮ NGUYÊN trong VM (chỉ bỏ control binding tới chúng trong view; dữ liệu cũ của account không mất vì VM vẫn nạp/lưu đủ trường).
- **Không làm:** không đổi hành vi nút (Command/IsEnabled binding giữ nguyên); không đổi ActivityLog/LogEntry; không đổi màn Proxy/Cài đặt; không xóa nút "+ Thêm tài khoản", thùng rác, ô tìm kiếm.

## 3. Các bước thực hiện

### A. Layout gốc: log xuống DƯỚI toàn màn, bỏ cột phải

1. Grid gốc hiện 3 cột → bọc thành Grid `RowDefinitions="*,Auto"`:
   - Hàng 0: Grid 2 cột như cũ (danh sách + form chi tiết) — XÓA cột 2 (panel log phải, dòng ~356–394) và định nghĩa cột của nó.
   - Hàng 1: **panel log mới** trải toàn bề ngang, Height cố định 200 (Border):
     - Nền đen `#0F1113` (hoặc `#111` — chọn 1, thống nhất), không bo góc hoặc bo nhẹ 0, BorderThickness="0,1,0,0" màu `#2A2A2A`.
     - Bên trong DockPanel Margin="12,8,12,8":
       - Top: hàng header nhỏ — TextBlock "Nhật ký hoạt động" FontSize 12 SemiBold Foreground `#E8E8E8` + nút "Xóa" (giữ `Command="{Binding ClearLogCommand}"`, style secondary hiện có có thể lạc tông trên nền đen → dùng Button nhỏ nền `#2A2A2A` chữ trắng hoặc Classes hiện có nếu nhìn ổn — executor tự cân, ghi rõ).
       - Bottom: TextBlock `{Binding LogPath}` FontSize 10 Foreground `#8A8A8A`, 1 dòng, TextTrimming.
       - Fill: **ListBox `x:Name="LogList"`** (BẮT BUỘC giữ đúng x:Name — code-behind auto-scroll FindControl "LogList") `ItemsSource="{Binding LogEntries}"`, Background="Transparent":
         - **Không cách dòng:** thêm `<ListBox.Styles><Style Selector="ListBoxItem">` với `Padding="0"`, `MinHeight="0"`, `Margin="0"` (và `CornerRadius="0"` nếu theme có) — đây là chỗ tạo khoảng thưa hiện tại.
         - ItemTemplate: TextBlock `{Binding Display}`, FontFamily="Consolas, Menlo, monospace", FontSize="11", Foreground="#EDEDED", TextWrapping="Wrap", Margin="0", Padding không có.
2. Kiểm code-behind `AccountsView.axaml.cs`: auto-scroll vẫn tìm thấy "LogList" (không đổi tên); nếu code-behind có tham chiếu nào tới panel cũ thì cập nhật tương ứng (đọc file trước khi sửa).

### B. Form chi tiết: bỏ trường thừa, u/p cùng dòng, dồn lên

1. Card 1 "THÔNG TIN ĐĂNG NHẬP": thay 2 khối hiện tại bằng MỘT Grid `ColumnDefinitions="*,14,*"`: cột 0 = **Tên đăng nhập** (giữ nguyên TextBox binding `EditEmail` + watermark), cột 2 = **Mật khẩu** (giữ nguyên PasswordChar/RevealPassword + nút 👁). **XÓA khối Số điện thoại** (binding `EditPhone`) khỏi view.
2. **XÓA nguyên Card 3** (Ghi chú/Trạng thái/Ngày tạo/Ngày sửa, ~307–351). Badge pill trạng thái ở header form GIỮ (vẫn cho biết trạng thái tài khoản).
3. Dồn form lên: các Card Margin dưới 16 → 12; Card 1 `StackPanel Spacing` 16 → 12; Padding card 24,22 → 20,16 (đồng bộ các card còn lại). StackPanel gốc form Margin "24,24,32,32" → "24,16,32,20". KHÔNG thay đổi nào khác về control/binding.

### C. Panel danh sách tài khoản: nút icon + lưới phẳng

1. **4 nút thao tác** (Chọn toàn bộ / Chạy đã chọn / Dừng đã chọn / Dừng tất cả): chuyển thành 1 HÀNG 4 nút **icon-only** (bỏ lưới 2x2 text): mỗi nút vuông ~34x30, `Content` là glyph đơn sắc, **ToolTip.Tip = nguyên văn chữ cũ** (bắt buộc, để không mất nghĩa):
   - Chọn toàn bộ: `☑` · Chạy đã chọn: `▶` · Dừng đã chọn: `⏹` · Dừng tất cả: `✕` (hoặc bộ glyph tương đương hiển thị tốt trên Windows — executor thử render, ghi rõ bộ đã chọn; giữ phân biệt màu: "Chạy đã chọn" giữ giọng accent như nút cũ).
   - GIỮ NGUYÊN từng `Command`/`IsEnabled` binding của 4 nút.
2. **ListBox tài khoản → lưới phẳng** theo mock `[ ] Tên shop`:
   - ItemTemplate mới: Grid `ColumnDefinitions="Auto,*,Auto"` Padding gọn (~"8,5"): CheckBox (giữ binding chọn hiện có) | TextBlock tên shop (FontSize 13, 1 dòng, `TextTrimming="CharacterEllipsis"`) | khối phải: chấm tròn 6px màu theo `Status` (converter StatusColor sẵn có) và, khi `IsRunning`, thêm chấm xanh + `{Binding ToShipText}` FontSize 10–11 (giữ thông tin "đang chạy/Chờ lấy: N" — người dùng vận hành nhiều shop cần thấy; đây là DIỄN GIẢI của Fable từ mock tối giản, nếu người dùng muốn bỏ nốt sẽ bỏ sau).
   - BỎ: Border card bo góc/viền/đổ bóng từng item, avatar chữ cái, các dòng phụ nhiều tầng. Hàng cao ~30–34px, hover/selected đổi nền nhẹ (dùng style ListBoxItem sẵn có của theme nếu ổn).
   - Search box, nút "+ Thêm tài khoản", thùng rác: GIỮ NGUYÊN.

### D. Kiểm chứng

1. `dotnet build XuLyDonShopee.sln -c Debug` — 0 lỗi, 0 warning mới (AXAML compile bắt lỗi XAML).
2. `dotnet test --no-build` — toàn bộ pass (test không cover UI nhưng không được vỡ; WDAC chặn đồng loạt 0x800711C7 → ghi rõ báo cáo).
3. Rà lại: không còn binding `EditPhone`/`EditNote`/`EditStatus`(ComboBox)/`CreatedAtText`/`UpdatedAtText` trong AccountsView.axaml; `x:Name="LogList"` còn nguyên; mọi Command binding cũ còn đủ (grep từng Command).

## 4. Tiêu chí nghiệm thu

- [ ] Build sạch + test pass.
- [ ] Grep xác nhận: form không còn Số điện thoại/Ghi chú/Trạng thái(ComboBox)/Ngày tạo/Ngày sửa; LogList giữ tên; đủ Command binding (SaveCommand, CancelCommand, CheckOrdersCommand, StopCommand, ProcessOrdersCommand, OpenSellerCommand, ClearLogCommand, 4 command của 4 nút icon, search/add/delete).
- [ ] Smoke thật (người dùng mở app): form 1 hàng u/p, không còn trường thừa; log nằm DƯỚI toàn màn — nền đen chữ trắng, các dòng SÁT nhau; danh sách shop là các hàng phẳng `[ ] tên` + chấm trạng thái, 4 nút icon có tooltip. Executor ghi rõ mục này chờ người dùng (executor không chạy được app do WDAC).

## 5. Rủi ro & lưu ý

- **Làm trong WORKTREE** — mọi đường dẫn quy về thư mục làm việc của agent; TUYỆT ĐỐI không đọc/ghi file của cây làm việc chính.
- AXAML Avalonia: đổi layout Grid gốc dễ vỡ binding/`Grid.Column` các panel — sau khi sửa PHẢI build (XAML compiler bắt), và rà từng panel còn hiển thị đúng cột/hàng.
- KHÔNG đổi tên/binding nào của VM; KHÔNG xóa property VM (ngoài phạm vi).
- Glyph icon phải render được bằng font hệ thống Windows mặc định (app đã dùng 👁/🙈 nên emoji/glyph cơ bản ổn).
- Style Button/ListBoxItem của theme app nằm ở App.axaml hoặc file style chung — nếu cần style mới cho nền đen, thêm CỤC BỘ trong AccountsView.axaml (Styles trong UserControl), đừng sửa style chung ảnh hưởng màn khác.

---

## Báo cáo thực thi (Opus điền sau khi xong)

Opus (worktree) làm đủ A/B/C trong 1 file `AccountsView.axaml`: layout Grid 2 hàng — log xuống đáy toàn ngang (nền `#0F1113`, chữ `#EDEDED` Consolas 11, `ListBoxItem` Padding/Margin/MinHeight=0 để dòng sát nhau, giữ `x:Name="LogList"` cho auto-scroll, nút Xóa style tối `logClear`, LogPath 1 dòng); form bỏ Số điện thoại + nguyên Card Ghi chú/Trạng thái/Ngày (VM giữ nguyên property), Tên đăng nhập + Mật khẩu cùng hàng, spacing gọn, badge pill trạng thái giữ; danh sách 4 nút icon `✓ ► ■ ✕` (glyph text-only tránh emoji màu) + lưới phẳng `[ ] tên` + chấm trạng thái/ToShipText. Bổ sung theo yêu cầu thêm: tài khoản đang mở cửa sổ có mũi tên `►` xanh trước tên + tên `#2E7D32` SemiBold qua `Classes.running` (màu đặt trong style để override được). Style đều CỤC BỘ trong UserControl — không đụng style chung.

Nghiệm thu (Fable): đọc toàn bộ file mới + tự build/test trong worktree (392/392) và sau merge trên cây chính (400/400, 0 warning); grep đủ 15 binding Command/SearchText, không còn binding trường đã bỏ. Panel đối kháng BỎ QUA có chủ đích cho việc này: thay đổi thuần khai báo UI đã được XAML compiler + compiled bindings kiểm, phần thẩm mỹ chỉ người dùng quyết được. Merge `worktree-agent-acfe7fca2ffa79950` (2 commit) → master `eae85d4`. Smoke thị giác: CHỜ NGƯỜI DÙNG mở app.
