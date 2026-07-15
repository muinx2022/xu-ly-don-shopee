# Plan: Cửa sổ log hoạt động (panel trong app + ghi file)

- **Ngày:** 2026-07-15
- **Trạng thái:** đang làm
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)
- **Nghiệm thu:** (Fable điền sau)

## 1. Bối cảnh & yêu cầu

Người dùng muốn **một cửa sổ log để biết app đang làm gì** (nhất là khi luồng xử lý đơn sắp thêm nhiều
bước tự động). Đã chốt: **panel ngay trong app** (vùng trống bên phải màn Tài khoản) **+ ghi ra file**
trên đĩa để xem lại.

### Hiện trạng (đã khảo sát 15/7)

- App KHÔNG có logging (grep: không ILogger/Trace/AppendAllText). Trạng thái mỗi phiên hiện chỉ hiển thị
  qua `StatusText` của tài khoản đang chọn.
- `AccountSession` (App/Services): `SetStatus(SessionState, string)` (đổi State+StatusText) và `SetError`
  là 2 chỗ tập trung mọi thông báo trạng thái; có sẵn cơ chế post về UI thread (field dispatcher `ui`,
  dùng ở `OnPropertyChanged`). `_accountId` biết tài khoản; `_services.Accounts.GetById` đọc được email.
- `AppServices` gom Database + repository + `AccountSessionManager`; truyền vào ViewModel (không DI).
- `AccountsView.axaml`: `Grid ColumnDefinitions="340,*"` — cột trái danh sách (340), cột phải "*" chứa
  form chi tiết (form KHÔNG lấp hết bề ngang → phần trống bên phải chính là chỗ đặt panel log).
- `AccountsViewModel` là DataContext của AccountsView.

## 2. Phạm vi

- **Làm:** service `ActivityLog` (thu thập dòng log, cap ring-buffer, ghi file) trong `AppServices`;
  nạp log từ `SetStatus`/`SetError` của mọi phiên (global, gắn nhãn tài khoản); panel log ở cột phải
  AccountsView (cuộn theo thời gian, tự cuộn xuống cuối) + nút "Xóa"; ghi file log xoay theo ngày; unit
  test phần thuần (định dạng dòng + cap).
- **Không làm:** chưa nạp log cho các bước xử lý đơn mới (plan sau sẽ gọi `Log.Append` ở từng bước);
  không đổi luồng phiên/proxy; không đụng màn Proxy/Cài đặt; KHÔNG tự commit.

## 3. Các bước thực hiện

### Bước 1 — Service `ActivityLog` (App/Services, testable)

Tạo `src/XuLyDonShopee.App/Services/ActivityLog.cs`, XML-doc tiếng Việt:

- `public record LogEntry(DateTime Time, string Source, string Message)` + `public string Display =>
  $"{Time:HH:mm:ss} [{Source}] {Message}";` (một dòng hiển thị/ghi file). Tách hàm thuần
  `public static string FormatLine(LogEntry e)` để test.
- `public sealed class ActivityLog`:
  - Ctor: `ActivityLog(string logDir, Action<Action>? uiPost = null, int maxEntries = 500)`.
    `uiPost` null → dùng `Avalonia.Threading.Dispatcher.UIThread.Post`; **test truyền `a => a()`** (đồng
    bộ, không cần dispatcher). `logDir` được `AppServices` tạo sẵn.
  - `public ObservableCollection<LogEntry> Entries { get; } = new();` — chỉ đọc/ghi trên UI thread.
  - `public void Append(string source, string message)`:
    1. Tạo `entry` (Time = `DateTime.Now`).
    2. **Ghi file NGAY** (đồng bộ, dưới `lock`): append `FormatLine(entry) + Environment.NewLine` vào
       `Path.Combine(logDir, $"hoatdong-{DateTime.Now:yyyyMMdd}.log")` (UTF-8, nuốt lỗi IO → không phá app).
    3. **Đổ vào Entries qua uiPost**: thêm vào cuối; nếu `Entries.Count > maxEntries` thì `RemoveAt(0)`
       cho tới khi bằng cap (ring buffer, tránh phình bộ nhớ khi chạy lâu).
  - `public void Clear()`: qua uiPost → `Entries.Clear()` (KHÔNG xóa file).
  - `public string CurrentLogPath` (đường dẫn file hôm nay) để UI hiển thị.
- Lưu ý: `Append` gọi được từ nhiều thread (các phiên) → phần file có `lock`; phần `Entries` marshal qua
  `uiPost` nên luôn chạy trên UI thread (ObservableCollection an toàn).

### Bước 2 — Gắn vào `AppServices` + nạp log từ `AccountSession`

- `AppServices.cs`: thêm `public ActivityLog Log { get; }`. Trong ctor: `var logDir =
  Path.Combine(Path.GetDirectoryName(Database.Path) ?? ".", "logs"); Directory.CreateDirectory(logDir);
  Log = new ActivityLog(logDir);` (đặt TRƯỚC `Sessions` vì phiên sẽ dùng Log).
- `AccountSession.cs`:
  - Cache nhãn tài khoản 1 lần (đầu `RunAsync` hoặc lúc tạo): `_logLabel = acc?.Email ?? $"TK {_accountId}"`.
    Nếu lấy ở RunAsync, khởi tạo field mặc định `$"TK {_accountId}"` để log trước khi có acc vẫn có nhãn.
  - Trong `SetStatus(state, text)`: sau khi đặt StatusText/State, gọi `_services.Log.Append(_logLabel, text)`.
  - Trong `SetError(msg)`: gọi `_services.Log.Append(_logLabel, "LỖI: " + msg)`.
  - (Mọi thông báo trạng thái sẵn có → tự vào log. Các bước xử lý đơn mới ở plan sau sẽ gọi
    `_services.Log.Append(_logLabel, ...)` trực tiếp cho chi tiết hơn.)

### Bước 3 — ViewModel + UI panel

- `AccountsViewModel.cs`:
  - `public System.Collections.ObjectModel.ObservableCollection<LogEntry> LogEntries => _services.Log.Entries;`
  - `public string LogPath => _services.Log.CurrentLogPath;`
  - `[RelayCommand] private void ClearLog() => _services.Log.Clear();`
- `AccountsView.axaml`:
  - Đổi `Grid ColumnDefinitions="340,*"` → `"340,*,360"` (thêm cột log rộng 360 bên phải). Giữ nguyên
    cột 0 (danh sách) và cột 1 (form chi tiết hiện tại — kiểm: form chi tiết đang ở `Grid.Column="1"`?
    Nếu form ở cột 1 dạng `*`, GIỮ nguyên; chỉ thêm cột 2 cho log).
  - Panel log ở `Grid.Column="2"`, `Border` viền trái, `DockPanel`:
    - Header (Dock Top): TextBlock "Nhật ký hoạt động" + nút nhỏ "Xóa" (`Command="{Binding ClearLogCommand}"`).
    - (Dock Bottom) TextBlock nhỏ hiển thị `LogPath` (mờ) để biết file log ở đâu.
    - Giữa: `ListBox` (hoặc `ItemsControl` trong `ScrollViewer`) `ItemsSource="{Binding LogEntries}"`,
      item hiển thị `{Binding Display}` (font mono nhỏ, wrap). **Tự cuộn xuống cuối** khi có dòng mới
      (xem Bước 4).
- Nếu form chi tiết hiện dùng toàn bộ cột "*" và có `ScrollViewer` riêng, đảm bảo panel log là cột SỐNG
  SONG (không lồng vào form) để luôn thấy dù cuộn form.

### Bước 4 — Tự cuộn xuống cuối (code-behind AccountsView)

`src/XuLyDonShopee.App/Views/AccountsView.axaml.cs`:
- Đặt tên cho ListBox log (`x:Name="LogList"`). Trong code-behind, sau khi `InitializeComponent`, đăng ký:
  khi `LogEntries.CollectionChanged` (Add) → `LogList.ScrollIntoView(last item)` (marshal UI nếu cần).
  Lấy `LogEntries` qua DataContext khi `DataContextChanged`. Nuốt lỗi an toàn (panel có thể chưa gắn).
- Nếu Avalonia ListBox có sẵn cách auto-scroll đơn giản hơn (vd `AutoScrollToSelectedItem` + set
  SelectedIndex cuối), Opus chọn cách gọn, miễn là dòng mới nhất luôn hiện.

### Bước 5 — Test + build

- Tạo `src/XuLyDonShopee.Tests/ActivityLogTests.cs`:
  - `FormatLine` đúng định dạng `HH:mm:ss [Source] Message` (dùng thời điểm cố định truyền vào LogEntry).
  - Cap ring-buffer: tạo `ActivityLog(tempDir, uiPost: a => a(), maxEntries: 3)`, Append 5 dòng →
    `Entries.Count == 3` và giữ 3 dòng MỚI NHẤT (dòng cũ bị loại).
  - Ghi file: Append vài dòng → file `hoatdong-*.log` trong tempDir tồn tại và chứa các dòng (đọc lại kiểm).
    (Dùng thư mục tạm; dọn sau — theo mẫu test repo hiện có nếu có helper.)
- `dotnet build XuLyDonShopee.sln -c Debug` 0/0; `dotnet test` toàn bộ pass (nền hiện tại **303** + ca mới).
  WDAC chặn → build lại `-p:Deterministic=false` rồi `dotnet test --no-build`.

## 4. Tiêu chí nghiệm thu

- [ ] Màn Tài khoản có panel "Nhật ký hoạt động" ở cột phải; mọi thay đổi trạng thái của MỌI phiên (mở
      Brave, đọc đơn, đổi proxy, lỗi...) hiện thành dòng `HH:mm:ss [tài khoản] nội dung`, tự cuộn xuống cuối.
- [ ] Đồng thời ghi ra file `logs/hoatdong-YYYYMMDD.log` cạnh database; nút "Xóa" chỉ xóa hiển thị, KHÔNG
      xóa file.
- [ ] Log an toàn đa luồng (nhiều phiên chạy song song không lỗi collection); lỗi ghi file bị nuốt, KHÔNG
      phá app.
- [ ] Ring-buffer cap (500) không phình bộ nhớ khi chạy lâu.
- [ ] Build 0/0; test 303 nền + ca ActivityLog mới pass.
- [ ] Chỉ tạo/sửa: `ActivityLog.cs` (mới), `AppServices.cs`, `AccountSession.cs`, `AccountsViewModel.cs`,
      `AccountsView.axaml`, `AccountsView.axaml.cs`, `ActivityLogTests.cs` (mới).

## 5. Rủi ro & lưu ý

- **UI thread:** ObservableCollection chỉ được đụng trên UI thread → mọi mutate Entries qua `uiPost`
  (Dispatcher.UIThread.Post). Test truyền uiPost đồng bộ để không cần dispatcher.
- **Ghi file mỗi dòng** có thể chậm nếu log dồn dập → chấp nhận (append + lock nhẹ); nếu sau thấy nặng,
  tối ưu bằng buffer/flush định kỳ (không làm bây giờ).
- Panel log là global (mọi tài khoản) — đúng ý "biết app đang làm gì"; nhãn `[tài khoản]` phân biệt nguồn.
- Không đổi hành vi phiên/proxy; chỉ THÊM lời gọi `Log.Append` ở `SetStatus`/`SetError`.
- WDAC/ISG khi test như các plan trước.

---

## Báo cáo thực thi (Opus điền sau khi xong)

(để trống)
