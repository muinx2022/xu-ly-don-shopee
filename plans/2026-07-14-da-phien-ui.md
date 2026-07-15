# Plan B: UI đa tài khoản (chọn nhiều · badge số đơn · focus cửa sổ)

- **Ngày:** 2026-07-14
- **Trạng thái:** hoàn thành
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)
- **Ghi chú:** Xếp trên **Plan A** (`2026-07-14-da-phien-song-song-engine.md` — đã hoàn thành: `AccountSessionManager` + `AccountSession` chạy song song, per-account state, `AccountSession.BraveProcess` đã expose, `OnPropertyChanged` đã marshal về UI thread).
- **Nghiệm thu:** Fable tự chạy (build 0 error, **159 test**) + panel rà soát hồi quy (3 góc) → bắt **2 lỗi thật** chỉ lộ khi chạy nhiều phiên (mất tick chọn + mất thứ tự "nổi lên đầu" do `CookieSaved` dựng lại list). Đã sửa: `RefreshAfterCookieSaved` cập nhật tại chỗ (không dựng lại), `_selectedIds` bền giữ tick qua mọi rebuild (kể cả dòng ẩn), "nổi lên đầu" move cả trong `_all`. +3 test khóa 3 kịch bản. Hạn chế: smoke click-driven (tick→chạy→focus→dừng) chưa tự động hóa (cần UI automation + tài khoản Shopee thật); từng cơ chế đã kiểm bằng unit test + smoke Plan A.

## 1. Bối cảnh & mục tiêu

Plan A đã cho phép nhiều phiên chạy song song ở tầng engine. Plan B làm **UI để dùng cho ~15 shop**:
- Danh sách tài khoản: mỗi dòng hiện **chấm trạng thái đang chạy/dừng + số "Chờ lấy: N"** (đọc từ phiên).
- **Tick chọn nhiều** + nút **"Chọn toàn bộ"** + **"Chạy đã chọn"** + **"Dừng"** (đã chọn / tất cả).
- **Bấm 1 tài khoản → nổi lên đầu danh sách + đưa cửa sổ Brave của nó ra trước (focus).**

### Hiện trạng code (đã khảo sát)

- [AccountsView.axaml](../src/XuLyDonShopee.App/Views/AccountsView.axaml): cột trái `ListBox Classes="acct"` bind `ItemsSource={Binding Accounts}` (`ObservableCollection<Account>`), `SelectedItem={Binding SelectedAccount}`; item template hiện: avatar chữ cái + `Email` + chấm `Status` màu. Hàng nút dưới: "+ Thêm tài khoản", "🗑". Ô tìm kiếm trên.
- [AccountsViewModel.cs](../src/XuLyDonShopee.App/ViewModels/AccountsViewModel.cs): `Accounts` (ObservableCollection<Account>), `SelectedAccount` (Account?), `_all` (List<Account>), `ApplyFilter`/`RefreshList`/`PassesFilter` thao tác trên `Account`; `Save`/`DeleteAsync`/`LoadIntoForm`/`Cancel` dùng `SelectedAccount`/`_editingId`. `_services.Sessions` = `AccountSessionManager` (Plan A): `Start/Stop/StopAllAsync/IsRunning/Get/Active` + event `Changed`/`CookieSaved`; `AccountSession` có `[ObservableProperty] State/StatusText/ToShipCount`, `BraveProcess`.
- Đã nghe `Sessions.Changed`→`RunOnUi(UpdateSelectedSessionStatus)` và `CookieSaved`→`RunOnUi(RefreshAfterCookieSaved)`.

## 2. Phạm vi

- **Làm:**
  - **`AccountRowViewModel`** (App): bọc `Account` + `IsSelected` (tick) + `RunState`/`ToShipText` (đổ từ phiên). Danh sách chuyển sang `ObservableCollection<AccountRowViewModel>`. GIỮ mọi hành vi cũ (tìm kiếm, chọn, Lưu/Xóa, refresh cookie).
  - Item template: thêm **checkbox** + **chấm đang chạy/dừng** + **"Chờ lấy: N"**.
  - Nút **"Chọn toàn bộ"** + **"Chạy đã chọn"** + **"Dừng đã chọn"/"Dừng tất cả"**.
  - **Bấm tài khoản → nổi lên đầu danh sách + focus cửa sổ Brave** (`WindowFocus` P/Invoke, Windows-only, no-op OS khác).
  - Live update trạng thái/số đơn mỗi dòng khi `Sessions.Changed`.
  - Test phần thuần (row VM, chọn toàn bộ, format "Chờ lấy") + smoke.
- **Không làm:** đổi engine/session (Plan A xong); giới hạn/quota số phiên (có thể sau); đổi model/DB.

## 3. Các bước thực hiện

### Bước 1 — `AccountRowViewModel` (App)

Tạo `src/XuLyDonShopee.App/ViewModels/AccountRowViewModel.cs` (`ObservableObject`):
- `public Account Account { get; }` (nguồn); passthrough dùng trong template: `long Id => Account.Id`, `string Email => Account.Email`, `AccountStatus Status => Account.Status` (cho chấm status cũ + avatar Initial).
- `[ObservableProperty] bool _isSelected;` (tick chọn nhiều).
- `[ObservableProperty] SessionState? _runState;` (null = không chạy) — cho chấm "đang chạy".
- `[ObservableProperty] string? _toShipText;` (vd "Chờ lấy: 3"; null/"" khi chưa có số / không chạy).
- `public bool IsRunning => RunState is SessionState.Opening or SessionState.Running;` (+ raise khi RunState đổi).
- Ctor nhận `Account`. Hàm `void SyncFromSession(IAccountSession? s)`: cập nhật `RunState = s?.State`; `ToShipText = s is { State: SessionState.Running or SessionState.Opening } && s.ToShipCount is int n ? $"Chờ lấy: {n}" : null`.
- **Test được:** `SyncFromSession` (null→RunState null/ToShipText null; Running+ToShipCount=3→"Chờ lấy: 3"; Running+null→null).

### Bước 2 — ViewModel: danh sách dùng row VM

- `Accounts` → `ObservableCollection<AccountRowViewModel>`. `SelectedAccount` (Account?) đổi thành `[ObservableProperty] AccountRowViewModel? _selectedRow;` (expose `SelectedRow?.Account` chỗ nào cần Account). Cập nhật `OnSelectedRowChanged` thay `OnSelectedAccountChanged` (giữ logic nạp form theo `_editingId`).
- `ApplyFilter`/`RefreshList`: dựng `AccountRowViewModel` từ `_all` (List<Account>), giữ `PassesFilter` trên `row.Account`. Sau khi dựng list, **đồng bộ trạng thái phiên**: mỗi row `SyncFromSession(_services.Sessions.Get(row.Id))`.
- `Save`/`DeleteAsync`/`LoadIntoForm`/`Cancel`: đổi tham chiếu `SelectedAccount`→`SelectedRow?.Account`; chọn lại theo `Id` (đang chọn theo `_editingId`) — reselect `AccountRowViewModel` có `Id` khớp. Giữ nguyên hành vi (chống mất form khi lọc, chọn đúng bản ghi vừa lưu...).
- Cookie refresh (`RefreshAfterCookieSaved`): dựng lại list (row mới có cookie) như cũ.
- **Live update:** `Sessions.Changed` → `RunOnUi(SyncAllRows)`: mỗi row `SyncFromSession(Sessions.Get(row.Id))`. (Đơn giản, an toàn — VM đổ trạng thái vào row trên UI thread; không bind trực tiếp vào session để tránh phức tạp vòng đời, dù Plan A đã marshal PropertyChanged.)

### Bước 3 — Item template (AccountsView.axaml)

Trong `DataTemplate x:DataType` đổi sang `vm:AccountRowViewModel`:
- Thêm `CheckBox IsChecked="{Binding IsSelected}"` bên trái (đừng để click checkbox đổi SelectedItem — đặt trong template, ListBox vẫn chọn dòng khi bấm vùng khác).
- Giữ avatar (`Account.Email` qua Initial), `Email`, chấm `Status`.
- Thêm hàng nhỏ: **chấm xanh "đang chạy"** khi `IsRunning` (IsVisible=IsRunning) + **`TextBlock Text="{Binding ToShipText}"`** (IsVisible khi ToShipText không rỗng), màu nổi (vd #1565C0).

### Bước 4 — Nút "Chọn toàn bộ" / "Chạy đã chọn" / "Dừng"

- ViewModel commands:
  - `SelectAllCommand`: nếu chưa chọn hết → set mọi row `IsSelected=true`; nếu đã chọn hết → bỏ chọn hết (toggle). (Chỉ áp trên danh sách ĐANG HIỂN THỊ sau lọc.)
  - `RunSelectedCommand`: mỗi row `IsSelected` → `_services.Sessions.Start(row.Id)`. (Idempotent — đang chạy thì no-op.)
  - `StopSelectedCommand`: mỗi row `IsSelected` (và đang chạy) → `_services.Sessions.Stop(row.Id)`.
  - (Tùy chọn) `StopAllCommand`: `await _services.Sessions.StopAllAsync()`.
- UI (AccountsView.axaml): thêm hàng nút phía trên/dưới list (gợi ý trên list, cạnh ô tìm kiếm hoặc hàng riêng): **"Chọn toàn bộ"**, **"Chạy đã chọn"**, **"Dừng đã chọn"** (+ "Dừng tất cả" nếu gọn). Dùng `Classes` sẵn có.

### Bước 5 — Bấm tài khoản: nổi lên đầu + focus cửa sổ Brave

- **Focus cửa sổ Brave:** tạo `src/XuLyDonShopee.App/Services/WindowFocus.cs`:
  - `public static void BringToFront(System.Diagnostics.Process? p)`: nếu null/exited → no-op. `OperatingSystem.IsWindows()` → P/Invoke `ShowWindow(hWnd, SW_RESTORE=9)` + `SetForegroundWindow(hWnd)` với `hWnd = p.MainWindowHandle` (nếu `IntPtr.Zero` thì bỏ qua). OS khác → no-op. Nuốt lỗi.
  - (Brave fork → MainWindowHandle của process launcher có thể 0; nếu vậy fallback: enum cửa sổ theo ProcessId. Để đơn giản: thử MainWindowHandle trước; nếu 0, có thể `p.Refresh()` rồi thử lại; ghi chú nếu không focus được thì bỏ qua — không phá luồng.)
- **Khi chọn 1 row** (`OnSelectedRowChanged`, ngoài việc nạp form): 
  - **Nổi lên đầu:** di chuyển row đang chọn lên đầu `Accounts` (Move về index 0) — dưới cờ `_isRefreshing` để không kích hoạt nạp đè. (Chỉ move khi row không phải đã ở đầu.)
  - **Focus Brave:** nếu `Sessions.Get(row.Id)` đang chạy → `WindowFocus.BringToFront(session.BraveProcess)`.
- Lưu ý: "nổi lên đầu" tương tác với lọc/tìm kiếm — chỉ move trong danh sách hiển thị; giữ `SelectedRow` đúng sau move.

### Bước 6 — Test + Smoke + README

- **Test thuần:** `AccountRowViewModelTests` (`SyncFromSession` các case). Có thể test `SelectAll` toggle trên VM với DB tạm (dựng vài Account, gọi command, khẳng định IsSelected). Giữ 142 test cũ xanh.
- **Smoke (Opus):** dựng 2–3 tài khoản; "Chọn toàn bộ" → tick hết; "Chạy đã chọn" → nhiều Brave mở; danh sách hiện chấm chạy + "Chờ lấy: N" (nếu đăng nhập được; không thì xác nhận số cập nhật khi ToShipCount đổi); bấm 1 tài khoản → cửa sổ Brave của nó ra trước + row lên đầu; "Dừng" → phiên dừng, chấm tắt. Không có tài khoản Shopee thật thì mở profile trắng chứng minh cơ chế (focus + chọn nhiều + chạy nhóm). Chạy app build **Release** (WDAC chặn Debug — [[build-isg-deterministic-block]]).
- **README:** hướng dẫn chọn nhiều/chạy nhóm/theo dõi số đơn nhiều shop; bấm tài khoản để đưa Brave ra trước.

## 4. Tiêu chí nghiệm thu

- [ ] Build 0 error; test pass (gồm `AccountRowViewModelTests`); 142 test cũ xanh. (WDAC: Release / chạy lại.)
- [ ] Danh sách: mỗi dòng có checkbox + chấm đang chạy + "Chờ lấy: N" cập nhật live theo phiên.
- [ ] "Chọn toàn bộ" tick/bỏ hết (theo danh sách đang lọc); "Chạy đã chọn" mở nhiều phiên song song; "Dừng" dừng phiên đã chọn.
- [ ] Bấm 1 tài khoản → row lên đầu danh sách + cửa sổ Brave của nó (nếu đang chạy) ra trước.
- [ ] KHÔNG hồi quy: tìm kiếm/lọc, chọn nạp form, Lưu/Xóa, tự lưu cookie, mở/dừng đơn lẻ vẫn đúng như trước.

## 5. Rủi ro & lưu ý

- **Refactor danh sách `Account`→`AccountRowViewModel` là rủi ro #1** — nhiều chỗ VM/XAML tham chiếu `Account`/`SelectedAccount`. PHẢI giữ nguyên: chống-mất-form-khi-lọc, chọn đúng bản ghi sau Lưu, reselect theo Id, refresh cookie. Đọc kỹ `RefreshList/ApplyFilter/OnSelectedAccountChanged/Save/DeleteAsync` trước khi đổi.
- **Focus cửa sổ (SetForegroundWindow)** có ràng buộc Windows (chỉ tiến trình foreground/được phép mới set được); Brave fork khiến `MainWindowHandle` có thể 0 → focus best-effort, không phá luồng nếu fail. Windows-only.
- **"Nổi lên đầu"** đừng gây nạp đè form / mất lựa chọn (dùng cờ `_isRefreshing`); đừng làm rối khi đang lọc.
- **Live update nhiều dòng** chạy trên UI thread (RunOnUi) — không đụng ObservableCollection từ thread nền.
- 15 Brave nặng RAM — giữ cảnh báo (chưa giới hạn quota ở plan này).
- WDAC chặn Debug — chạy/ship Release.

---

## Báo cáo thực thi (Opus điền sau khi xong)

### Đã hoàn thành (đối chiếu từng bước)

**Bước 1 — `AccountRowViewModel`** (mới: `src/XuLyDonShopee.App/ViewModels/AccountRowViewModel.cs`)
- `ObservableObject`, ctor nhận `Account`; passthrough `Account`/`Id`/`Email`/`Status`.
- `[ObservableProperty] IsSelected`, `RunState (SessionState?)`, `ToShipText (string?)`.
- `IsRunning => RunState is Opening or Running`, raise `IsRunning` khi `RunState` đổi (`OnRunStateChanged`).
- `SyncFromSession(IAccountSession?)`: `RunState = s?.State`; `ToShipText = s is {State: Running or Opening} && s.ToShipCount is int n ? $"Chờ lấy: {n}" : null` — đúng đặc tả.

**Bước 2 — ViewModel dùng row VM** (`AccountsViewModel.cs`)
- `Accounts` → `ObservableCollection<AccountRowViewModel>`; `SelectedAccount (Account?)` → `[ObservableProperty] SelectedRow (AccountRowViewModel?)`.
- `ApplyFilter` dựng row từ `_all` và gọi `row.SyncFromSession(Sessions.Get(id))` ngay khi dựng.
- `RefreshList` reselect theo `Id` trên row VM; `PassesFilter` vẫn nhận `Account`.
- Đổi mọi tham chiếu `SelectedAccount`→`SelectedRow(?.Account)` ở `OnSearchTextChanged`, `Reload`, `Add`, `DeleteAsync`, `UpdateSelectedSessionStatus`, `RefreshAfterCookieSaved`, `SaveCapturedCookie`. GIỮ nguyên `_isRefreshing`, chống-race `_editingId`, chống-mất-form-khi-lọc, reselect-sau-Lưu, refresh cookie.
- **Live update:** `Sessions.Changed` → `RunOnUi(() => { SyncAllRows(); UpdateSelectedSessionStatus(); })`; `SyncAllRows` chỉ chạy trên UI thread, chỉ set thuộc tính row (không cấu trúc lại ObservableCollection từ thread nền).

**Bước 3 — Item template** (`AccountsView.axaml`)
- `DataTemplate x:DataType="vm:AccountRowViewModel"`; thêm cột `CheckBox IsChecked="{Binding IsSelected}"` (CheckBox nuốt click nên không đổi lựa chọn dòng).
- Giữ avatar/Email/chấm Status; thêm hàng "đang chạy" (`IsVisible={Binding IsRunning}`): chấm xanh `#2E7D32` + chữ "Đang chạy" + `TextBlock {Binding ToShipText}` màu `#1565C0` (ẩn khi rỗng).

**Bước 4 — Nút nhóm** (VM + XAML)
- Commands: `SelectAllCommand` (toggle theo danh sách đang lọc), `RunSelectedCommand` (Start từng row tick, idempotent), `StopSelectedCommand`, `StopAllCommand` (`await StopAllAsync`).
- XAML: hàng nút 2×2 phía trên list (Chọn toàn bộ / Chạy đã chọn / Dừng đã chọn / Dừng tất cả), dùng class `secondary`+`accentOutline` sẵn có.

**Bước 5 — Bấm tài khoản: nổi lên đầu + focus Brave**
- Mới `src/XuLyDonShopee.App/Services/WindowFocus.cs`: `BringToFront(Process?)` — null/exited/không-Windows/handle==0 → no-op; thử `MainWindowHandle`, nếu 0 thì `Refresh()` thử lại; `ShowWindow(SW_RESTORE)`+`SetForegroundWindow`; bọc try/catch nuốt lỗi (best-effort).
- `OnSelectedRowChanged`: nạp form theo `_editingId` (giữ guard đang-sửa-dở) rồi `BringSelectedToFront(row)` — `Accounts.Move(idx,0)` dưới cờ `_isRefreshing` (chỉ move khi idx>0), giữ `SelectedRow`, và focus `Sessions.Get(id)?.BraveProcess` nếu có phiên.

**Bước 6 — Test + README**
- Mới `AccountRowViewModelTests.cs` (7 test SyncFromSession: null / Running+số / Running+null / Opening+số / Stopped / Error / raise IsRunning).
- Thêm vào `AccountsViewModelTests.cs`: SelectAll (toggle / tick một phần / chỉ trên danh sách lọc), nổi-lên-đầu-khi-chọn, chọn-lại-đang-sửa-không-nạp-đè; đổi các test cũ sang `SelectedRow` và `.Account.Cookie`.
- Mới `WindowFocusTests.cs` (2 test: null & process không cửa sổ → không ném).
- README: thêm đoạn "Chọn nhiều & chạy nhóm" (tick, Chọn toàn bộ/Chạy đã chọn/Dừng, badge "Chờ lấy: N" live, bấm tài khoản → lên đầu + focus Brave, ghi chú Windows-only best-effort).

### Kết quả kiểm chứng (thật)
- **Build Debug** (`dotnet build XuLyDonShopee.sln -c Debug`, sau khi chắc App không chạy): **Build succeeded — 0 Warning(s), 0 Error(s)** (cả 3 project).
- **Build Release** (`dotnet build XuLyDonShopee.sln -c Release`): **Build succeeded — 0 Warning(s), 0 Error(s)**.
- **Test Debug** (`dotnet test ...Tests.csproj -c Debug --no-build`): **Passed! Failed: 0, Passed: 156, Skipped: 0, Total: 156** (142 test cũ giữ xanh — không hồi quy danh sách/form/cookie; + 14 test mới). Debug chạy được vì Core.dll Debug không đổi (đã "già" qua ISG).
- **Test Release**: **bị WDAC chặn** — mọi test fail với `System.IO.FileLoadException ... An Application Control policy has blocked this file. (0x800711C7)` khi nạp **Core.dll Release vừa build** (hash mới, ISG chưa duyệt). Đã thử lại 6+ lần (kể cả job nền cách quãng 70s×3), số fail luôn = số bị block, KHÔNG có assertion nào fail → **không phải lỗi code** (đúng cảnh báo `[[build-isg-deterministic-block]]`). Bằng chứng xanh dùng bản Debug 156/156.
- **Smoke (app thật, Debug)**: launch `XuLyDonShopee.App.exe` → cửa sổ chính "Xử lý đơn Shopee" tạo thành công (`HasWindow=True`, handle≠0) → `AccountsView` mới (checkbox + hàng nút nhóm + badge trạng thái) **khởi tạo runtime không lỗi** (mọi StaticResource/converter/binding resolve). Đã đóng app sau khi kiểm.

### Vướng mắc / bỏ dở (trung thực)
- **Smoke tương tác click-driven** (tick tất cả→chạy→nhiều Brave song song→bấm 1 tài khoản→Brave ra trước+row lên đầu→dừng→chấm tắt) **CHƯA kịch bản hoá**: cần UI-automation + tài khoản Shopee thật (chỉ có profile trắng) + bản chạy được (Release đang bị ISG chặn; môi trường không quan sát màn hình). Bù lại, từng cơ chế đã có kiểm chứng xác định: chọn-nhiều/nổi-lên-đầu/`SyncFromSession` bằng unit test; Brave song song đã được smoke ở **Plan A** (engine); `WindowFocus` an toàn bằng unit test + đã chứng minh app có cửa sổ handle hợp lệ (điều kiện `SetForegroundWindow` cần).
- **Test Release chưa xanh** trong phiên này do WDAC (không phải lỗi code) — nếu cần dấu xanh Release, chạy lại `dotnet test -c Release --no-build` sau khi ISG duyệt hash Core.dll Release (Plan A trước đây từng xanh 141/141 ở Release sau vài lần thử).

### Đề xuất (nhỏ)
- Nếu muốn tick nhiều mà dòng không nhảy lên đầu gây rối, cân nhắc CHỈ "nổi lên đầu" khi bấm vùng ngoài checkbox (hiện dựa vào việc CheckBox Avalonia tự nuốt click — đúng như quan sát, nên xác nhận lại khi smoke tay).
- Có thể thêm quota số phiên đồng thời (cảnh báo RAM 15 Brave) ở plan sau như đã ghi chú.

---

## Vòng sửa 2 — nghiệm thu đối kháng (2 lỗi THẬT khi chạy nhiều phiên)

Panel rà soát phát hiện 2 lỗi (2/2 phiếu) chỉ lộ khi chạy nhiều phiên thật — gốc chung: sự kiện nền `CookieSaved` bắn liên tục (mỗi lần phiên đăng nhập + trong vòng theo dõi 30') → `RefreshAfterCookieSaved` dựng-lại-toàn-bộ-danh-sách.

- **Lỗi 1 [MAJOR]** — rebuild do `CookieSaved` gọi `Accounts.Clear()` + dựng row mới (`IsSelected=false`) → **mất hết tick** → "Dừng/Chạy đã chọn" thấy `Accounts.Where(r=>r.IsSelected)` rỗng, không dừng/chạy gì.
- **Lỗi 2 [MINOR]** — `BringSelectedToFront` chỉ `Accounts.Move` (không đổi `_all`) → mỗi rebuild dựng lại theo thứ tự `_all` gốc → row "nổi lên đầu" rơi về chỗ cũ, thứ tự nhảy loạn theo nhịp cookie.

### Đã sửa (đúng SỬA A/B/C)
- **SỬA A — `RefreshAfterCookieSaved` cập nhật TẠI CHỖ, KHÔNG dựng lại** (`AccountsViewModel.cs`): bỏ `_all = GetAll()` và mọi `RefreshList` trong nhánh này. Chỉ `GetById(accountId)` rồi cập nhật `Cookie`/`UpdatedAt` trên đúng instance trong `_all` (row bọc chính instance đó → Save sau không ghi đè cookie về null); nếu đang mở đúng tài khoản thì cập nhật `EditCookie`/`UpdatedAtText`. Không đụng `ObservableCollection` nữa → giữ nguyên tick + thứ tự.
- **SỬA B — `ApplyFilter` giữ tick qua MỌI lần dựng lại** (search/Save/đổi tab/phiên lưu cookie): thêm tập bền `private readonly HashSet<long> _selectedIds`. Trước `Accounts.Clear()` đồng bộ tick các dòng đang hiển thị vào tập (tick→add, bỏ→remove); dựng row mới khôi phục `IsSelected = _selectedIds.Contains(id)`. **Dòng đang bị ẩn do lọc GIỮ nguyên tick trong tập** (đây là lý do dùng field bền thay vì recompute cục bộ như bản phác — nếu recompute thì tick của dòng ẩn sẽ mất, `TimKiem_RoiXoa_GiuTickChon` fail).
- **SỬA C — "nổi lên đầu" bền qua rebuild** (`BringSelectedToFront`): sau `Accounts.Move(idx,0)`, đưa luôn `Account` lên đầu `_all` (`_all.Remove(a); _all.Insert(0, a)`) trong cùng khối `_isRefreshing` → `ApplyFilter` (duyệt theo `_all`) dựng lại vẫn ở đầu.

### Test thêm (`AccountsViewModelTests.cs`)
- `LuuCookie_KhiDangChay_KhongMatTickChon`: tick 2 tài khoản → `SaveCapturedCookie(id, cookie hợp lệ)` (mô phỏng rebuild do CookieSaved) → khẳng định 2 row vẫn `IsSelected=true`.
- `TimKiem_RoiXoa_GiuTickChon`: tick 1 tài khoản → lọc ẩn nó rồi xóa từ khóa → row đó vẫn `IsSelected=true`.
- `NoiLenDau_BenQuaDungLaiList`: chọn tài khoản cuối → ở `Accounts[0]`; dựng lại danh sách (đổi lọc) → vẫn ở `Accounts[0]`.

### Nghiệm thu lại (thật)
- **Build Debug** (`dotnet build XuLyDonShopee.sln -c Debug`): **0 Warning / 0 Error**. **Build Release**: **0 Warning / 0 Error**.
- **Test Debug** (`dotnet test -c Debug --no-build`): **Passed! Failed: 0, Passed: 159, Skipped: 0, Total: 159** (156 trước + 3 mới). Toàn bộ test cũ (cookie/Save/reselect/reorder) giữ xanh — không hồi quy.
- (Release vẫn có thể bị WDAC chặn hash mới như đã nêu; bằng chứng xanh dùng Debug 159/159 — Debug lần này chạy được, ISG thất thường.)
