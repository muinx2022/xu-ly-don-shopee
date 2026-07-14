# Plan: Ứng dụng desktop Avalonia — quản lý tài khoản Shopee + quản lý proxy (bước đầu)

- **Ngày:** 2026-07-13
- **Trạng thái:** hoàn thành
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)

## 1. Bối cảnh & mục tiêu

Người dùng cần một ứng dụng desktop chạy được cả **Windows và Linux** (họ gọi là "winform" nhưng đã chốt dùng **C# + Avalonia** để đa nền tảng). Đây là bước đầu của bộ công cụ xử lý đơn Shopee. Repo hiện **trống hoàn toàn** (chỉ có CLAUDE.md và plans/), nên plan này dựng toàn bộ khung dự án từ đầu.

Yêu cầu đã chốt với người dùng:

- Màn hình chính có **2 panel**: bên trái là **danh sách tài khoản**, bên phải là **chi tiết tài khoản**, kèm đầy đủ **CRUD** (thêm/sửa/xóa/lưu).
- Tài khoản gồm: **user dạng email** (bắt buộc, đúng định dạng email), **mật khẩu** (bắt buộc); kèm các trường mở rộng cho Shopee: số điện thoại, cookie đăng nhập, ghi chú, trạng thái.
- Có phần **quản lý proxy**: nhập (dán) một danh sách proxy. Về sau khi chạy 1 tài khoản, hệ thống sẽ **xoay vòng (round-robin)** qua danh sách proxy đó. Nếu **không có proxy** trong danh sách thì dùng **IP của máy**, và việc đổi IP máy sẽ thông qua dịch vụ **KiotProxy** (kiotproxy.com — dịch vụ proxy xoay của Việt Nam, dùng API key).
- Lưu trữ dữ liệu bằng **SQLite** (file .db cục bộ, không cần server).

Bước này **chưa** làm tính năng "chạy tài khoản" (đăng nhập/thao tác Shopee). Chỉ dựng: khung app, CRUD tài khoản, CRUD proxy, cài đặt KiotProxy API key, và **service xoay vòng proxy** (logic thuần, có test) để bước sau cắm vào phần chạy tài khoản.

Môi trường máy dev: Windows 11, đã cài **.NET SDK 8.0.422**. Target framework: **net8.0**.

## 2. Phạm vi

- **Làm:**
  - Solution .NET 8 + 3 project: `XuLyDonShopee.Core` (model, data, service), `XuLyDonShopee.App` (UI Avalonia), `XuLyDonShopee.Tests` (xunit).
  - CRUD tài khoản với giao diện 2 panel (list + detail), validate email, lưu SQLite.
  - Quản lý proxy: dán danh sách, hiển thị, xóa; lưu SQLite.
  - Màn hình Cài đặt: nhập/lưu KiotProxy API key.
  - Service `ProxyRotator` (round-robin + fallback KiotProxy/IP máy) + `KiotProxyClient` (gọi API lấy proxy mới) — chỉ logic + test, chưa dùng thật.
  - `.gitignore`, `README.md` ngắn (cách chạy trên Windows/Linux).
- **Không làm:**
  - Đăng nhập/tự động thao tác Shopee, kiểm tra proxy sống/chết (chỉ để sẵn cột trạng thái).
  - Mã hóa mật khẩu trong DB (bước đầu chấp nhận lưu thường, ghi rõ trong README).
  - Đóng gói installer/publish self-contained.
  - Không đụng vào thư mục `plans/`, `CLAUDE.md` ngoài việc điền báo cáo vào cuối file plan này.

## 3. Các bước thực hiện

Toàn bộ code đặt dưới `src/`, solution `XuLyDonShopee.sln` ở gốc repo `d:\Projects\Xu-ly-don-shopee`.

### Bước 1 — Khung solution

1. Tạo `XuLyDonShopee.sln` tại gốc repo.
2. `src/XuLyDonShopee.Core/` — classlib net8.0. Package: `Microsoft.Data.Sqlite` (bản 8.x).
3. `src/XuLyDonShopee.App/` — app Avalonia. Dùng template Avalonia (`dotnet new install Avalonia.Templates` nếu chưa có, rồi `dotnet new avalonia.mvvm`) hoặc tự dựng thủ công. Package: `Avalonia` 11.x (bản stable mới nhất), `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`, `CommunityToolkit.Mvvm` 8.x, `Avalonia.Controls.DataGrid`. Tham chiếu project Core.
4. `src/XuLyDonShopee.Tests/` — xunit, tham chiếu Core.
5. `.gitignore` chuẩn .NET (bin/, obj/, *.user, .vs/ …) tại gốc.

### Bước 2 — Tầng dữ liệu (Core)

1. **Models** (`Models/`):
   - `Account`: `Id` (long), `Email` (string, bắt buộc), `Password` (string, bắt buộc), `Phone` (string?), `Cookie` (string?), `Note` (string?), `Status` (enum `AccountStatus`: `ChuaKiemTra`, `HoatDong`, `BiKhoa` — lưu DB dạng TEXT), `CreatedAt`, `UpdatedAt` (ISO-8601 TEXT).
   - `ProxyEntry`: `Id` (long), `Host`, `Port` (int), `Username` (string?), `Password` (string?), `Type` (enum `ProxyType`: `Http`, `Socks5`; mặc định Http), `Status` (enum `ProxyStatus`: `ChuaKiemTra`, `Song`, `Chet`), `CreatedAt`.
2. **Database** (`Data/Database.cs`): mở/khởi tạo SQLite bằng `Microsoft.Data.Sqlite`. Đường dẫn mặc định: `Environment.SpecialFolder.ApplicationData` + `/XuLyDonShopee/app.db` (Windows: `%APPDATA%\XuLyDonShopee\app.db`; Linux: `~/.config/XuLyDonShopee/app.db`); constructor nhận đường dẫn tùy chọn để test dùng file tạm. Tự tạo thư mục + bảng nếu chưa có (`CREATE TABLE IF NOT EXISTS`): `accounts`, `proxies`, `settings` (key TEXT PRIMARY KEY, value TEXT).
3. **Repositories** (`Data/`): `AccountRepository`, `ProxyRepository`, `SettingsRepository` với CRUD đầy đủ (GetAll, GetById, Insert, Update, Delete; ProxyRepository thêm `InsertMany`, `DeleteAll`; SettingsRepository dạng Get/Set theo key, dùng key `kiotproxy_api_key`). SQL tham số hóa, không nối chuỗi.
4. **Parser** (`Services/ProxyParser.cs`): parse text nhiều dòng thành `List<ProxyEntry>`. Mỗi dòng chấp nhận `host:port` hoặc `host:port:user:pass` (trim khoảng trắng, bỏ dòng trống). Trả về cả danh sách hợp lệ lẫn danh sách dòng lỗi (số thứ tự dòng + nội dung) để UI báo cho người dùng.
5. **Validator**: hàm kiểm tra định dạng email (dùng `System.Net.Mail.MailAddress` try-parse hoặc regex đơn giản), dùng chung cho UI.

### Bước 3 — Service xoay vòng proxy (Core)

1. `Services/IKiotProxyClient.cs` + `Services/KiotProxyClient.cs`: gọi API KiotProxy lấy proxy mới bằng API key. Endpoint theo tài liệu công khai của kiotproxy.com — dạng `GET https://api.kiotproxy.com/api/v1/proxies/new?key={apiKey}`; **base URL phải là tham số cấu hình** (constructor), parse JSON trả về lấy host:port. Nếu không xác minh được chính xác schema API (không có mạng/không có key thật), cứ triển khai theo dạng trên, bọc kỹ try/catch, trả `null` khi lỗi — phần này sẽ được hiệu chỉnh ở plan sau khi có key thật. **Không hard-code API key.**
2. `Services/ProxyRotator.cs`: nhận danh sách `ProxyEntry` + `IKiotProxyClient?`. Hành vi:
   - Có proxy trong danh sách → `GetNextAsync()` trả proxy theo round-robin (thread-safe, quay vòng về đầu khi hết).
   - Danh sách trống + có KiotProxy API key → lấy proxy từ `IKiotProxyClient`.
   - Danh sách trống + không có key (hoặc KiotProxy lỗi) → trả `null` = dùng IP máy (kết nối trực tiếp).
   - Có phương thức `Reload(list)` để UI cập nhật danh sách mới.

### Bước 4 — Giao diện Avalonia (App)

Kiến trúc MVVM với `CommunityToolkit.Mvvm` (ObservableObject, RelayCommand). **Toàn bộ chữ trên UI bằng tiếng Việt.** Cửa sổ chính ~1100×700, tiêu đề "Xử lý đơn Shopee".

1. **MainWindow**: sidebar trái (ListBox 3 mục: "Tài khoản", "Proxy", "Cài đặt") + vùng nội dung bên phải đổi theo mục chọn (ContentControl + DataTemplate theo ViewModel). Mặc định mở "Tài khoản".
2. **AccountsView** — 2 panel như yêu cầu:
   - **Panel trái (danh sách):** ô tìm kiếm (lọc theo email/ghi chú, lọc ngay khi gõ), ListBox tài khoản (mỗi dòng: email + badge trạng thái), nút **"+ Thêm"** và **"Xóa"** (Xóa phải hỏi xác nhận qua dialog).
   - **Panel phải (chi tiết):** form các trường — Email (TextBox), Mật khẩu (TextBox có `PasswordChar='●'` + nút 👁 hiện/ẩn), Số điện thoại, Cookie (TextBox nhiều dòng, cao ~100px), Ghi chú, Trạng thái (ComboBox 3 giá trị tiếng Việt); hiển thị Ngày tạo/Ngày sửa (read-only); nút **"Lưu"** và **"Hủy"**.
   - Hành vi: chọn tài khoản trong list → form hiện dữ liệu; "+ Thêm" → form trống ở chế độ tạo mới; "Lưu" → validate (email đúng định dạng, email không trùng tài khoản khác, mật khẩu không rỗng — sai thì hiện thông báo lỗi đỏ ngay trong form, không lưu) rồi ghi DB và cập nhật list; "Hủy" → bỏ thay đổi, quay về dữ liệu đang chọn. Khi chưa chọn gì và không ở chế độ tạo mới → panel phải hiện chữ mờ "Chọn một tài khoản hoặc bấm + Thêm".
3. **ProxiesView**:
   - Thanh trên: nút **"Nhập danh sách"**, **"Xóa dòng chọn"**, **"Xóa tất cả"** (hỏi xác nhận), label tổng số proxy.
   - DataGrid các cột: Host, Port, User, Loại, Trạng thái, Ngày thêm (dùng `Avalonia.Controls.DataGrid`, nhớ thêm StyleInclude theme của DataGrid vào App.axaml; nếu DataGrid trục trặc theme thì được phép thay bằng ListBox nhiều cột bằng Grid — ghi rõ trong báo cáo).
   - "Nhập danh sách" mở dialog: TextBox nhiều dòng để dán, ghi chú định dạng "`host:port` hoặc `host:port:user:pass`, mỗi dòng một proxy", nút "Nhập"/"Hủy". Sau khi nhập: báo "Đã nhập N proxy, M dòng không hợp lệ" (liệt kê dòng lỗi nếu có).
   - Dòng chú thích cuối trang: "Khi chạy tài khoản, app xoay vòng qua danh sách này. Nếu danh sách trống, app dùng IP của máy (đổi IP qua KiotProxy nếu đã cài API key)."
4. **SettingsView**: ô nhập "KiotProxy API key" (PasswordChar + nút hiện/ẩn), nút "Lưu" (ghi vào bảng settings, báo "Đã lưu"). Chú thích ngắn về vai trò của key.
5. Đăng ký service đơn giản (khởi tạo `Database` + repositories một lần trong `App.axaml.cs` rồi truyền vào ViewModel qua constructor — không cần DI container).

### Bước 5 — Test (Tests)

Chỉ test logic thuần trong Core (không test UI):

1. `ProxyParserTests`: parse `host:port`, `host:port:user:pass`, dòng trống/thừa khoảng trắng, dòng sai định dạng (port không phải số, thiếu phần) → đúng số lượng hợp lệ + đúng danh sách dòng lỗi.
2. `ProxyRotatorTests`: round-robin 3 proxy gọi 7 lần → thứ tự 1,2,3,1,2,3,1; danh sách trống + mock KiotProxy trả proxy → dùng proxy đó; danh sách trống + không có client → trả null; `Reload` đổi danh sách giữa chừng.
3. `AccountRepositoryTests` + `ProxyRepositoryTests`: dùng SQLite file tạm (Path.GetTempFileName) — Insert → GetAll/GetById, Update, Delete, InsertMany, DeleteAll; kiểm tra dữ liệu giữ nguyên sau khi đóng và mở lại `Database` (persistence).
4. `EmailValidatorTests`: vài email hợp lệ/không hợp lệ.

### Bước 6 — README & hoàn thiện

`README.md` tại gốc: mô tả ngắn, yêu cầu .NET 8 SDK, lệnh chạy (`dotnet run --project src/XuLyDonShopee.App`) trên Windows/Linux, lệnh test, vị trí file DB, ghi chú "mật khẩu hiện lưu dạng thường trong SQLite cục bộ".

## 4. Tiêu chí nghiệm thu

- [ ] `dotnet build XuLyDonShopee.sln` thành công, **0 error** (warning chấp nhận được nhưng nên dọn).
- [ ] `dotnet test` — toàn bộ test pass.
- [ ] `dotnet run --project src/XuLyDonShopee.App` mở được cửa sổ app trên Windows, không crash (chạy thử ít nhất ~10 giây rồi tắt; nếu chạy được hãy chụp/mô tả những gì hiển thị trong báo cáo).
- [ ] CRUD tài khoản hoạt động qua tầng repository (được chứng minh bằng test persistence ở Bước 5 — thêm/sửa/xóa và dữ liệu còn nguyên sau khi mở lại DB).
- [ ] UI đủ 3 màn hình (Tài khoản 2 panel, Proxy, Cài đặt), chữ tiếng Việt, các nút lệnh gắn command đầy đủ (không có nút chết).
- [ ] Không hard-code API key, không commit file .db, `.gitignore` có bin/obj.

## 5. Rủi ro & lưu ý

- **API KiotProxy chưa xác minh được schema chính xác** (chưa có API key thật). Vì vậy phải cô lập toàn bộ trong `KiotProxyClient` với base URL cấu hình được, lỗi thì trả `null` chứ không throw ra UI. Sẽ hiệu chỉnh ở plan sau.
- **Avalonia DataGrid** hay gặp lỗi thiếu theme: phải thêm `<StyleInclude Source="avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml"/>` trong App.axaml. Nếu vẫn trục trặc, cho phép fallback sang ListBox (ghi rõ trong báo cáo).
- Phiên bản package: chọn bản **stable mới nhất** của Avalonia 11.x trên NuGet tại thời điểm chạy; nếu restore lỗi mạng, thử lại hoặc ghim bản 11.2.x.
- Mật khẩu lưu thường trong DB cục bộ — đã chấp nhận ở bước đầu, chỉ cần ghi chú trong README, **không** tự ý thêm mã hóa (ngoài phạm vi).
- Máy dev là Windows (PowerShell 5.1) — không dùng cú pháp bash khi chạy lệnh; đường dẫn tuyệt đối gốc repo: `d:\Projects\Xu-ly-don-shopee`.

---

## Báo cáo thực thi (Opus — 2026-07-13)

### Tổng quan
Đã dựng toàn bộ khung dự án và triển khai đầy đủ Bước 1 → Bước 6. Nghiệm thu đạt cả 6 tiêu chí.

### Kết quả kiểm chứng (số liệu thật)
- `dotnet build XuLyDonShopee.sln -c Debug` → **Build succeeded, 0 Warning, 0 Error**.
- `dotnet test XuLyDonShopee.sln` → **Passed! Failed: 0, Passed: 36, Skipped: 0, Total: 36** (~0.13–0.5s).
- `dotnet run --project src/XuLyDonShopee.App` → chạy nền ~14s không crash. Process `XuLyDonShopee.App`
  còn sống, `Responding=True`, `MainWindowTitle='Xử lý đơn Shopee'`, stderr rỗng (không exception).
  Đã tắt tiến trình sau khi kiểm tra.
- Không có file `*.db` trong repo; DB thật được tạo tại `%APPDATA%\XuLyDonShopee\app.db` (đúng vị trí).
- `.gitignore` có `bin/`, `obj/`, `*.db`. Không hard-code API key (key lưu trong bảng `settings`).

### Đã hoàn thành theo từng bước

**Bước 1 — Khung solution**
- `XuLyDonShopee.sln` (gốc repo) + 3 project dưới `src/`.
- App project dựng từ template `avalonia.mvvm` rồi chỉnh: net8.0, Avalonia **11.2.8** (bản 11.2.x mới
  nhất trên NuGet lúc chạy — Avalonia, Desktop, Themes.Fluent, Fonts.Inter, Controls.DataGrid),
  CommunityToolkit.Mvvm 8.4.2, tham chiếu Core.
- Core: `Microsoft.Data.Sqlite` 8.0.10. Tests: xunit + tham chiếu Core.
- `.gitignore` chuẩn .NET.

**Bước 2 — Tầng dữ liệu (Core)**
- `Models/`: `Account.cs`, `ProxyEntry.cs`, `Enums.cs` (AccountStatus/ProxyType/ProxyStatus).
- `Data/Database.cs` (đường dẫn mặc định ApplicationData, tự tạo thư mục + 3 bảng
  `accounts`/`proxies`/`settings`, constructor nhận path tùy chọn), `DbSerialization.cs`.
- `Data/AccountRepository.cs`, `ProxyRepository.cs` (có `InsertMany`/`DeleteAll`),
  `SettingsRepository.cs` (Get/Set, key `kiotproxy_api_key`). SQL tham số hóa toàn bộ.
- `Services/ProxyParser.cs` (trả `ProxyParseResult` gồm danh sách hợp lệ + danh sách dòng lỗi
  kèm số dòng, lý do). `Validation/EmailValidator.cs` (siết bằng MailAddress + kiểm tra thủ công).

**Bước 3 — Service xoay vòng proxy (Core)**
- `Services/IKiotProxyClient.cs` + `KiotProxyClient.cs` (base URL cấu hình được, `GET /api/v1/proxies/new?key=`,
  bọc try/catch trả `null` khi lỗi, parser JSON dò nhiều tên trường thường gặp — chưa xác minh schema thật).
- `Services/ProxyRotator.cs` (round-robin thread-safe, fallback KiotProxy → IP máy, `Reload`).

**Bước 4 — Giao diện Avalonia (App)**
- `Services/AppServices.cs` (khởi tạo Database + repositories một lần), `Services/DialogService.cs`.
- `Converters/`: `VietnameseEnumConverter`, `StatusColorConverter`, `DateTimeDisplayConverter`.
- ViewModels: `MainViewModel` (điều hướng sidebar), `AccountsViewModel` (CRUD + tìm kiếm + validate),
  `ProxiesViewModel`, `SettingsViewModel`.
- Views: `MainWindow` (sidebar 3 mục + ContentControl, tiêu đề "Xử lý đơn Shopee", 1100×700),
  `AccountsView` (2 panel: list + form đầy đủ trường, badge trạng thái, hiện/ẩn mật khẩu bằng
  `RevealPassword`), `ProxiesView` (**DataGrid** đủ 6 cột + StyleInclude theme DataGrid trong App.axaml),
  `SettingsView`, và 2 dialog `ConfirmDialog`/`ImportProxyDialog`.
- Toàn bộ chữ tiếng Việt; mọi nút gắn command (không có nút chết); nút Xóa hỏi xác nhận.

**Bước 5 — Test (Tests)**: `ProxyParserTests` (7), `ProxyRotatorTests` (5, có fake IKiotProxyClient),
`AccountRepositoryTests` (5, có test persistence mở lại DB), `ProxyRepositoryTests` (5), `EmailValidatorTests` (14 case).

**Bước 6 — README.md** tại gốc (mô tả, yêu cầu .NET 8, lệnh chạy/test/build, vị trí file DB, ghi chú mật khẩu lưu thường).

### Quyết định phát sinh
- **Phiên bản**: template mặc định sinh net10.0 + Avalonia 12.1.0 (không hợp SDK 8.0.422). Đã hạ về
  **net8.0 + Avalonia 11.2.8** đúng yêu cầu plan.
- **Cú pháp MVVM**: dùng `[ObservableProperty]` trên **field** (cú pháp cổ điển) thay vì `partial property`
  của template (partial property cần C# 13/net9).
- **DataGrid**: giữ nguyên `Avalonia.Controls.DataGrid` như plan (KHÔNG phải fallback ListBox). Thêm
  `StyleInclude` theme Fluent của DataGrid vào App.axaml; đặt `x:CompileBindings="False"` cho `ProxiesView`
  để binding cột DataGrid dùng reflection (tránh vướng compiled-binding), các view khác vẫn compiled binding.
- Bỏ `.WithDeveloperTools()`/gói diagnostics của template (không cần cho bước này).

### Chưa làm / ngoài phạm vi (đúng plan)
- Chưa có tính năng "chạy tài khoản", chưa kiểm tra proxy sống/chết, chưa gọi KiotProxy thật (chờ API key),
  chưa mã hóa mật khẩu, chưa đóng gói installer. Tất cả đều nằm trong mục "Không làm" của plan.

### Đề xuất cho plan sau
- Khi có KiotProxy API key thật: xác minh lại schema JSON và endpoint trong `KiotProxyClient.ParseResponse`
  (hiện dò theo nhiều tên trường phỏng đoán: `proxyHttp`, `http`, `data.proxyHttp`, ...).
- Cân nhắc mã hóa mật khẩu/cookie khi chuyển sang giai đoạn vận hành thật.

---

## Sửa sau nghiệm thu vòng 1 (Opus — 2026-07-13)

Vòng rà soát đa tác tử phát hiện 3 lỗi thật. Đã sửa cả 3 và bổ sung test hồi quy.

### Số liệu kiểm chứng sau sửa
- `dotnet build XuLyDonShopee.sln` → **Build succeeded, 0 Warning, 0 Error**.
- `dotnet test` → **Passed! Failed: 0, Passed: 41, Skipped: 0, Total: 41** (trước là 36; +5 test).
- Chạy thử app ~12s: process `XuLyDonShopee.App` sống, `Responding=True`, tiêu đề đúng, stderr rỗng.

### Lỗi 1 — Lưu tài khoản mới khi đang lọc → form kẹt trạng thái mâu thuẫn
- **File**: `src/XuLyDonShopee.App/ViewModels/AccountsViewModel.cs` (phương thức `Save`).
- **Sửa**: hợp nhất nhánh insert/update; sau khi ghi DB **gán `_editingId = account.Id` ngay**, đặt
  `IsNew = false`, nạp lại `_all`; **nếu bộ lọc hiện tại đang ẩn bản ghi vừa lưu thì tự xóa `SearchText`**
  để bản ghi luôn hiển thị và chọn được; sau đó `RefreshList(id)` + `LoadIntoForm(saved)`. Nhờ đó bấm Lưu
  lần 2 đi đúng nhánh update, không tạo bản ghi trùng và không báo nhầm "Email đã tồn tại".

### Lỗi 2 — Gõ ô tìm kiếm khi đang sửa dở → mất thay đổi chưa lưu
- **File**: cùng file trên (`OnSelectedAccountChanged`, `Reload`, thêm `RefreshList`).
- **Sửa**: trong `OnSelectedAccountChanged`, nếu tài khoản được chọn lại có **`Id == _editingId`** thì **không
  nạp đè form** (giữ nguyên dữ liệu đang sửa). Đồng thời gom việc dựng lại danh sách + reselect vào
  `RefreshList` (thực hiện dưới cờ `_isRefreshing`), nên thao tác reselect khi lọc/nạp lại không kích hoạt
  nạp đè form. Luồng "chọn sang tài khoản khác" vẫn nạp form như cũ (có test bảo vệ).

### Lỗi 3 — Thiếu test cho `ProxyRepository.Update`
- **File**: `src/XuLyDonShopee.Tests/ProxyRepositoryTests.cs`.
- **Sửa**: thêm `Update_SuaMoiTruong_VaKhongAnhHuongDongKhac` — sửa toàn bộ trường (host/port/user/pass/type/status)
  của 1 proxy, khẳng định `GetById` trả đúng giá trị mới **và** bản ghi khác không bị ảnh hưởng (bắt lỗi SQL sai cột/sai WHERE).

### Test hồi quy bổ sung (đã xác minh ngược)
- Thêm project reference `XuLyDonShopee.App` vào Tests và file `AccountsViewModelTests.cs` (4 test): Lỗi 1,
  Lỗi 2, "chọn tài khoản khác vẫn nạp form", "cập nhật ghi đúng DB". ViewModel test được bằng xunit thuần
  (chỉ cần `AppServices` trên DB tạm, không cần Avalonia runtime); đường dẫn Xóa dùng `DialogService`
  (Avalonia Window) nên không test.
- **Đã kiểm chứng ngược**: tạm khôi phục logic cũ → 2 test hồi quy (Lỗi 1, Lỗi 2) **FAIL** đúng như kỳ vọng;
  khôi phục bản sửa → **41/41 pass**. Chứng tỏ test thực sự bắt lỗi.
- **Ghi chú trung thực về Lỗi 2**: trigger đầu-cuối trong app (ListBox tự ghi `null` vào `SelectedItem` khi
  `ItemsSource` bị `Clear`) là hành vi của UI framework, không tái hiện được trong xunit thuần không có
  ListBox. Test hồi quy dùng `Reload()` để tái hiện **cùng khiếm khuyết gốc** (reselect làm nạp đè form) ở
  mức quan sát được headless, và nó FAIL trên code cũ.
