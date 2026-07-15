# Plan A: Engine đa phiên song song (mở/theo dõi nhiều tài khoản cùng lúc)

- **Ngày:** 2026-07-14
- **Trạng thái:** hoàn thành
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)
- **Ghi chú:** Đây là **Plan A/B**. Plan B (`2026-07-14-da-phien-ui.md`) làm UI (chọn nhiều/badge số đơn/focus cửa sổ). Plan A CHỈ làm **engine**: nhiều phiên chạy song song, mỗi tài khoản 1 phiên độc lập, bỏ khóa toàn cục.
- **Nghiệm thu:** Fable tự chạy + panel rà soát đối kháng concurrency (3 góc) → bắt **3 lỗi thật** (gỡ-phiên-theo-key làm mồ côi Brave [blocker]; race Dừng→Mở-lại khóa hồ sơ [major]; PropertyChanged bắn từ thread nền [major]). Đã sửa hết (gỡ theo value; Stop giữ IsRunning tới khi Brave chết; override OnPropertyChanged marshal UI thread) + test hồi quy `StoppedTre_...`. Build 0 error, **142 test** (Release), smoke xác nhận. Hạn chế: marshal UI kiểm bằng đọc-code + smoke (không UI test headless); WDAC chặn DLL Debug mới build ([[build-isg-deterministic-block]]).

## 1. Bối cảnh & mục tiêu

Người dùng cần **mở/theo dõi ~15 shop cùng lúc** (nếu 1 shop thì không cần app). Hiện `AccountsViewModel.OpenSellerAsync` chỉ chạy **1 phiên/lúc**: cờ `IsBusy` **dùng chung** khóa nút "Mở trang bán hàng" cho MỌI tài khoản khi 1 phiên đang chạy; tính năng theo dõi đơn 30' lại giữ phiên mở lâu → không mở được tài khoản thứ 2. Cần đổi sang **N phiên song song, mỗi tài khoản một phiên độc lập**.

**Đã chốt với người dùng:** đúng là đang có 1 phiên mở (nút xám là đúng, không phải bug); nhưng phải mở được nhiều shop cùng lúc (như `D:\Projects\shopee-suite` chạy nhiều Brave — mỗi profile/CDP port/proxy riêng).

### Hiện trạng code (đã khảo sát)

- [AccountsViewModel.cs](../src/XuLyDonShopee.App/ViewModels/AccountsViewModel.cs) `OpenSellerAsync` (`[RelayCommand]`): chụp `targetId=_editingId.Value`, `IsBusy=true` (try/finally), đọc `acc=GetById(targetId)`, tính `userDataDir=BrowserProfilePaths.ForAccount`, **chọn proxy** (ưu tiên `acc.ProxyKey`→`KiotProxyClient(key)`→`ProxySelector`; else `NextManualProxy`; else global keys; else IP máy), `EnsureBrowserInstalled` (Task.Run), `_loginService.OpenAsync(userDataDir, proxy)`, `TryHumanLoginAsync(acc.Email, acc.Password)`, **vòng poll 1s** (bắt cookie gate `IsLoggedIn`→`SaveCapturedCookie` + **theo dõi đơn 30'** `ReadToShipCountAsync`→`OrderStatus`), thoát khi `OpenPageCount==0 x2` / cap 12h, bắt-cookie-chốt, `DialogService.InfoAsync` (MODAL), finally reset `IsBusy/BusyStatus/OrderStatus`.
  - Trạng thái hiện là **singleton VM**: `IsBusy`, `BusyStatus`, `OrderStatus`, `CanOpenSeller` (= `IsEditing && !IsNew && _editingId is not null && !IsBusy`).
  - `SaveCapturedCookie` mutate `_all`/`Accounts` (ObservableCollection) + `RefreshList` → **phải chạy trên UI thread**.
- [ShopeeLoginService.cs](../src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs): `OpenAsync(userDataDir, proxy)` tự launch Brave (port=0 riêng mỗi profile) + CDP; `ILoginSession { CaptureCookiesJsonAsync; Closed; IsClosed; OpenPageCount; TryHumanLoginAsync; ReadToShipCountAsync; DisposeAsync (kill process tree) }`.
- [AppServices.cs](../src/XuLyDonShopee.App/Services/AppServices.cs): `Database/Accounts/Proxies/Settings`. Nơi hợp lý để giữ `AccountSessionManager` (App shutdown truy cập được).
- [App.axaml.cs](../src/XuLyDonShopee.App/App.axaml.cs): lifetime desktop — nơi gọi `StopAll()` khi thoát app.

## 2. Phạm vi

- **Làm (engine):**
  - Tách luồng-1-phiên trong `OpenSellerAsync` ra **`AccountSession`** (App): 1 tài khoản = 1 phiên chạy nền độc lập (proxy→Brave→login→cookie+theo dõi đơn), có trạng thái quan sát được (đang mở/đang chạy/dừng/lỗi, số đơn Chờ Lấy Hàng, dòng trạng thái, lỗi).
  - **`AccountSessionManager`** (App): `ConcurrentDictionary<long, AccountSession>` — Start/Stop/StopAll/IsRunning/Get + event đổi trạng thái. Đặt trong `AppServices`.
  - `AccountsViewModel`: `OpenSellerCommand`→`manager.StartAccount(id)` (KHÔNG khóa tài khoản khác); thêm `StopCommand`; `CanOpenSeller` kiểm **theo tài khoản** (`!manager.IsRunning(id)`) thay cờ `IsBusy` toàn cục; `BusyStatus`/`OrderStatus` hiển thị theo **tài khoản đang chọn** (đọc từ session của nó).
  - **Bỏ mọi `DialogService.InfoAsync` trong luồng phiên** (15 phiên = 15 modal → không được) → thay bằng trạng thái/log per-account.
  - **Thread-safety:** phần đụng `Accounts`/`RefreshList`/`ObservableCollection` marshal về **UI thread** (`Dispatcher.UIThread`).
  - App shutdown → `StopAll()` (kill hết Brave, không mồ côi).
  - Test cho `AccountSessionManager` (logic start/stop/idempotent) bằng session-factory stub.
- **Không làm (để Plan B):** UI chọn nhiều tài khoản (tick), badge số đơn trong danh sách, nút "Chạy nhóm đã chọn", focus cửa sổ Brave khi bấm, "nổi lên đầu danh sách". Plan A giữ nút "Mở trang bán hàng" đơn lẻ (nhưng nay KHÔNG khóa tài khoản khác).
- **Không làm:** đổi cơ chế login/cookie/proxy/monitor/CDP (tái dùng nguyên); đổi model/DB.

## 3. Các bước thực hiện

### Bước 1 — `AccountSession` (App): 1 phiên độc lập

Tạo `src/XuLyDonShopee.App/Services/AccountSession.cs` (kế thừa `ObservableObject` để UI bind được):
- Ctor nhận: `long accountId`, `AppServices services`, `ShopeeLoginService loginService`, `IProxyHealthChecker healthChecker`, `Action<Action> uiInvoke` (marshal UI thread — truyền `Dispatcher.UIThread.Post` từ VM), và callback `Func<long,string,Core.Models.Account?, ...>`? → đơn giản: nhận `AccountsViewModel`? KHÔNG (vòng phụ thuộc). Thay vào đó session tự làm việc DB + phát event; VM nghe event để cập nhật UI list.
- Trạng thái `[ObservableProperty]`: `SessionState State` (enum `Stopped/Opening/Running/Error`), `string? StatusText`, `int? ToShipCount` (null=chưa đọc), `string? LastError`.
- `Process? BraveProcess` (để Plan B focus cửa sổ) — lấy từ `ILoginSession` (Bước 4 mở rộng interface expose process/handle).
- `Task StartAsync()`: nếu đang chạy → return. Tạo `_cts`, chạy `_runTask = Task.Run(RunAsync)`:
  - `RunAsync` = **bê nguyên luồng `OpenSellerAsync`** (proxy→EnsureBrowserInstalled→OpenAsync→TryHumanLogin→vòng poll cookie+đơn→bắt-cookie-chốt) NHƯNG:
    - Đọc `acc` theo `accountId` (không đọc form).
    - Cập nhật `State`/`StatusText`/`ToShipCount` thay cho `BusyStatus`/`OrderStatus` singleton.
    - **Bỏ `DialogService.InfoAsync`** → set `StatusText`/`LastError` (vd lỗi mở browser → `State=Error, LastError=ex.Message`; kết thúc → `StatusText="Đã lưu cookie"`/"Chưa lưu...").
    - `SaveCapturedCookie` (ghi DB + cập nhật list): phần DB chạy trên thread nền OK; phần **cập nhật `Accounts`/RefreshList marshal về UI thread** qua `uiInvoke`. → Tách `SaveCapturedCookie` thành: (a) ghi DB (thread-safe, nền), (b) refresh list UI (uiInvoke). HOẶC phát event `CookieSaved(accountId)` để VM tự refresh list trên UI thread. **Chọn: session ghi DB + phát event `CookieSaved`; VM nghe → refresh list trên UI thread.**
  - Kết thúc (đóng cửa sổ/lỗi/cancel) → `State=Stopped`; `await using` dispose session (kill Brave).
- `Task StopAsync()`: `_cts.Cancel()`; chờ `_runTask` (timeout ~8s); đảm bảo session disposed (kill Brave). `State=Stopped`.
- Event: `event Action<AccountSession>? Changed` (raise khi State/ToShipCount/StatusText đổi) + `event Action<long>? CookieSaved`.

### Bước 2 — `AccountSessionManager` (App)

Tạo `src/XuLyDonShopee.App/Services/AccountSessionManager.cs`:
- `ConcurrentDictionary<long, AccountSession> _sessions`.
- Factory session tách được để test: ctor nhận `Func<long, AccountSession> sessionFactory` (mặc định tạo `AccountSession` thật; test truyền stub).
- `AccountSession Start(long id)`: `_sessions.GetOrAdd(id, factory)`; nếu State==Stopped → `StartAsync()`; return session. (Idempotent: đang chạy → return session cũ, không mở trùng.)
- `void Stop(long id)`, `Task StopAllAsync()`, `bool IsRunning(long id)` (session tồn tại && State != Stopped), `AccountSession? Get(long id)`, `IReadOnlyCollection<AccountSession> Active`.
- `event Action? Changed` (gộp từ session.Changed) — VM/UI nghe để cập nhật.
- Dọn session Stopped khỏi dictionary (khi stop xong).
- Đặt instance trong `AppServices` (`public AccountSessionManager Sessions { get; }`) để App shutdown gọi `StopAllAsync()`.

### Bước 3 — `AccountsViewModel` dùng manager

- Bỏ dùng cờ `IsBusy` **toàn cục** để khóa nút. `CanOpenSeller` = `IsEditing && !IsNew && _editingId is not null && !_services.Sessions.IsRunning(_editingId ?? -1)`. (Raise `CanOpenSeller` khi manager.Changed hoặc SelectedAccount đổi.)
- `OpenSellerCommand`: `_services.Sessions.Start(targetId)` (đồng bộ, nhanh — session tự chạy nền). KHÔNG await vòng đời phiên trong command.
- Thêm `StopCommand`: `_services.Sessions.Stop(_editingId.Value)`.
- **Hiển thị trạng thái theo tài khoản ĐANG CHỌN:** khi `SelectedAccount`/`manager.Changed`: đọc `session = manager.Get(selectedId)`; set `BusyStatus = session?.StatusText`, `OrderStatus = session?.ToShipCount` (format). Session không chạy → null.
- Nghe `CookieSaved(accountId)` → refresh list trên UI thread (giữ hành vi cookie hiện tại: instance trong `Accounts` có cookie mới).
- Bỏ `IsBusy`/`BusyStatus`/`OrderStatus` như biến-điều-khiển-luồng; giữ `BusyStatus`/`OrderStatus` như **ô hiển thị** (đổ từ session đang chọn).

### Bước 4 — Expose process cho focus (chuẩn bị Plan B) + shutdown

- `ILoginSession` thêm `Process? BraveProcess { get; }` (LoginSession trả `_process`). (Plan B dùng để focus cửa sổ.)
- `App.axaml.cs`: khi `IClassicDesktopStyleApplicationLifetime.ShutdownRequested`/`Exit` → `await services.Sessions.StopAllAsync()` (kill hết Brave).

### Bước 5 — Test + Smoke + README

- **Test** `AccountSessionManagerTests` (stub `AccountSession` factory — hoặc tách `IAccountSession` interface nhỏ để stub): Start 2 lần cùng id → 1 session (không trùng); Stop gỡ khỏi Active; StopAll rỗng; IsRunning đúng. (Không test luồng Brave — như các phần browser khác.)
- Giữ toàn bộ 136 test cũ xanh.
- **Smoke thật (Opus):** mở **2 tài khoản** đồng thời qua manager → 2 cửa sổ Brave riêng (2 profile/port khác nhau) cùng chạy; đóng 1 cửa sổ → phiên đó Stopped, phiên kia vẫn chạy; nút "Mở trang bán hàng" của tài khoản KHÁC không bị khóa khi 1 phiên đang chạy; StopAll đóng sạch. Nếu không có 2 tài khoản Shopee thật → mở 2 profile trắng cùng lúc chứng minh song song + không khóa. Ghi số liệu.
- **README:** app hỗ trợ **mở/theo dõi nhiều tài khoản song song** (mỗi tài khoản 1 Brave + proxy + theo dõi riêng); cảnh báo **nhiều Brave tốn RAM** (15 shop = nặng).

### Nghiệm thu bắt buộc
- `dotnet build -c Debug` 0 error; `dotnet test` pass (**dùng `-c Release` hoặc chạy lại nếu WDAC chặn Debug — xem [[build-isg-deterministic-block]]**). Chạy/ship app bằng **Release** ([[build-isg-deterministic-block]]).

## 4. Tiêu chí nghiệm thu

- [ ] Build 0 error; test pass (gồm `AccountSessionManagerTests`); 136 test cũ xanh.
- [ ] Mở tài khoản A KHÔNG khóa nút của tài khoản B (`CanOpenSeller` theo từng tài khoản). 2+ phiên chạy song song, mỗi phiên Brave/profile/proxy riêng.
- [ ] KHÔNG còn `DialogService.InfoAsync` trong luồng phiên (thay bằng trạng thái per-account). Trạng thái/số đơn hiển thị theo tài khoản đang chọn.
- [ ] Cookie vẫn tự lưu đúng tài khoản; cập nhật `Accounts` chạy trên UI thread (không crash ObservableCollection khi nhiều phiên).
- [ ] Đóng 1 cửa sổ → phiên đó dừng, phiên khác không ảnh hưởng; thoát app → StopAll kill hết Brave (không mồ côi).

## 5. Rủi ro & lưu ý

- **Thread-safety UI** là rủi ro #1: nhiều phiên nền cùng đụng `Accounts`/`RefreshList` (ObservableCollection chỉ UI thread) → PHẢI marshal về `Dispatcher.UIThread` (dùng event `CookieSaved` cho VM tự refresh). Sai → crash/hỏng list.
- **15 Brave = nặng RAM/CPU**; Plan A không giới hạn số phiên (Plan B có thể thêm cap/warmup như shopee-suite). Ghi cảnh báo.
- **Khóa profile:** mỗi tài khoản 1 `userDataDir` riêng → không đụng nhau. Không mở CÙNG 1 tài khoản 2 lần (manager idempotent chặn).
- **DialogService bỏ khỏi luồng** nhưng vẫn dùng ở `DeleteAsync` (giữ). Xác nhận không còn Info/Confirm nào trong phiên.
- Giữ nguyên chống-hang (OpenPageCount==0 x2 + cap), kill process tree, bắt-cookie-chốt, human-login, proxy per-account — chỉ DI CHUYỂN vào `AccountSession`, không đổi logic.
- WDAC chặn Debug — build/chạy Release. Không dùng modal cho 15 phiên.

---

## Yêu cầu cho Plan B (ghi lại để không quên)
- Danh sách tài khoản: mỗi dòng hiện **chấm trạng thái đang chạy/dừng + số "Chờ lấy: N"** (đọc từ `AccountSession`).
- **Chọn nhiều** (tick/checkbox) + nút **"Chọn toàn bộ"** (trên/dưới list, tick hết tài khoản) + nút **"Chạy nhóm đã chọn"** + **"Dừng"** (chọn/tất cả).
- Bấm 1 tài khoản → **đưa cửa sổ Brave của nó ra trước (focus)** (Win32 SetForegroundWindow qua `AccountSession.BraveProcess`) + **nổi lên đầu danh sách** (sort tài khoản đang chạy/được chọn lên trên).

---

## Báo cáo thực thi (Opus — 2026-07-14)

### Files đã tạo/sửa

**Mới:**
- `src/XuLyDonShopee.App/Services/IAccountSession.cs` — enum `SessionState` (Stopped/Opening/Running/Error) + interface `IAccountSession` (nhỏ, để manager test được bằng stub; gồm State/StatusText/ToShipCount/LastError/BraveProcess + event Changed/CookieSaved + StartAsync/StopAsync).
- `src/XuLyDonShopee.App/Services/AccountSession.cs` — 1 phiên nền độc lập (kế thừa `ObservableObject`). Bê nguyên luồng `OpenSellerAsync` (proxy per-account → EnsureBrowserInstalled → OpenAsync → TryHumanLogin → vòng poll cookie+đơn 30' → bắt-cookie-chốt), **bỏ hết modal**. `StartAsync` idempotent (lock, chỉ chạy khi Stopped/Error), `StopAsync` (cancel + chờ ≤8s + kill dự phòng). Ghi DB trên thread nền + phát `CookieSaved`; cập nhật State/StatusText/ToShipCount qua `[ObservableProperty]`, partial `On*Changed` phát `Changed`.
- `src/XuLyDonShopee.App/Services/AccountSessionManager.cs` — `ConcurrentDictionary<long, IAccountSession>`, factory tách (`Func<long,IAccountSession>`; ctor thật dùng chung `ShopeeLoginService`+`ProxyHealthChecker`). `Start` (GetOrAdd + đăng ký event 1 lần dưới lock, idempotent), `Stop`, `StopAllAsync` (WhenAll), `IsRunning` (Opening/Running), `Get`, `Active`, event `Changed`/`CookieSaved`. Giữ round-robin proxy thủ công (`NextManualProxy`, thread-safe) dùng chung cho mọi phiên.
- `src/XuLyDonShopee.Tests/AccountSessionManagerTests.cs` — 5 test bằng stub `IAccountSession` (Start 2 lần = 1 session + factory gọi 1 lần; IsRunning theo từng tài khoản; Stop gỡ khỏi Active; StopAll rỗng; Get trả phiên/null).

**Sửa:**
- `src/XuLyDonShopee.App/Services/AppServices.cs` — thêm `public AccountSessionManager Sessions { get; }` (tạo sau các repository).
- `src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs` — `ILoginSession` thêm `Process? BraveProcess { get; }`; `LoginSession` trả `_process` (cho Plan B focus + kill dự phòng). KHÔNG đổi logic login/cookie/proxy/monitor/CDP.
- `src/XuLyDonShopee.App/App.axaml.cs` — `desktop.ShutdownRequested += … StopAllAsync().GetAwaiter().GetResult()` (đóng sạch Brave khi thoát).
- `src/XuLyDonShopee.App/ViewModels/AccountsViewModel.cs` — bỏ cờ `IsBusy` toàn cục + toàn bộ `OpenSellerAsync` (kèm mọi `DialogService.InfoAsync`). Thêm command `OpenSeller`→`Sessions.Start` (không khóa tài khoản khác) và `Stop`→`Sessions.Stop`. `CanOpenSeller`/`CanStopSeller` kiểm theo `Sessions.IsRunning(id)` (từng tài khoản). Nghe `Sessions.Changed`/`CookieSaved` và marshal về UI thread (`RunOnUi`→`Dispatcher.UIThread.Post`) trước khi đụng `Accounts`/`RefreshList`. `BusyStatus`/`OrderStatus` nay là ô hiển thị đổ từ phiên của tài khoản đang chọn. Giữ nguyên `SaveCapturedCookie`/`SaveCookieResult` (tests phụ thuộc); `NextManualProxy` uỷ quyền cho manager. `DeleteAsync` giữ nguyên (Confirm).
- `src/XuLyDonShopee.App/Views/AccountsView.axaml` — thêm nút "Dừng" (`StopCommand`/`CanStopSeller`).
- `README.md` — thêm ghi chú mở/theo dõi nhiều tài khoản song song + cảnh báo nhiều Brave tốn RAM.

### Kết quả kiểm chứng (thật)
- **Build Debug** (`dotnet build XuLyDonShopee.sln -c Debug`, sau khi kill App+brave): **Build succeeded — 0 Warning(s), 0 Error(s)**.
- **Test Release** (`dotnet test XuLyDonShopee.sln -c Release` — Debug bị WDAC chặn): **Passed! Failed: 0, Passed: 141, Skipped: 0** (136 test cũ + 5 `AccountSessionManagerTests` mới).
- **Smoke thật** (harness console lái trực tiếp `AccountSessionManager`, Brave thật, 2 profile trắng, password rỗng để bỏ qua auto-login), exit code 0:
  - 2 phiên cùng **Running**; PID Brave khác nhau (vd 27904/25380) và **CDP port khác nhau** (52041/52042) → mỗi phiên Brave/profile/port riêng.
  - `IsRunning(id1)=True, IsRunning(id2)=True, Active=2` → **mở A không khóa B**.
  - Kill cây tiến trình Brave phiên 1 (giả lập đóng cửa sổ) → `s1.State=Stopped, IsRunning(id1)=False`; **phiên 2 vẫn `Running`, IsRunning(id2)=True, Active=1** → đóng 1 không ảnh hưởng cái kia.
  - `StopAllAsync()` → `Active=0`, cả 2 PID không còn sống, tổng brave còn lại = 0 (bằng lúc đầu) → **đóng sạch, không mồ côi**.
- **Boot app Release**: khởi động, sống ≥6s không crash (xác nhận wiring `AppServices.Sessions` + VM subscribe event lúc dựng không lỗi), rồi tắt sạch.

### Đối chiếu tiêu chí nghiệm thu (mục 4)
1. Build 0 error; test pass gồm `AccountSessionManagerTests`; 136 test cũ xanh → **ĐẠT** (141 pass).
2. Mở A không khóa nút B; ≥2 phiên song song Brave/profile/proxy riêng → **ĐẠT** (smoke).
3. Không còn `DialogService.InfoAsync` trong luồng phiên; trạng thái/số đơn hiển thị theo tài khoản đang chọn → **ĐẠT** (grep xác nhận Info chỉ còn trong file plan cũ; VM chỉ còn Confirm ở Delete).
4. Cookie tự lưu đúng tài khoản; cập nhật `Accounts` trên UI thread → **ĐẠT ở tầng engine + thiết kế** (session ghi DB nền, refresh list marshal về `Dispatcher.UIThread`); xem hạn chế.
5. Đóng 1 cửa sổ → phiên đó dừng, phiên khác không ảnh hưởng; thoát app → StopAll kill hết → **ĐẠT** (smoke).

### Cách xử lý thread-safety UI (rủi ro #1)
- `Accounts` (ObservableCollection) + `RefreshList` **chỉ** được đụng trên UI thread. Phiên chạy nền tuyệt đối không chạm collection: nó chỉ (a) ghi DB SQLite (thread nền an toàn) và (b) **phát event** `CookieSaved(id)`/`Changed`. VM bắt event qua `RunOnUi` (chạy ngay nếu đã ở UI thread, ngược lại `Dispatcher.UIThread.Post`) rồi mới dựng lại danh sách/cập nhật form. Không có đường nào để thread nền chạm `Accounts`.
- Session cập nhật `[ObservableProperty]` trên thread nền, nhưng Plan A **không bind UI trực tiếp** vào session — VM đọc snapshot trong handler đã marshal. (Plan B nếu bind thẳng vào `AccountSession` sẽ cần marshal thêm — đã ghi chú.)

### Quyết định phát sinh (khác/ mở rộng nhẹ so với chữ trong plan)
- Tách **`IAccountSession`** (plan đã gợi ý) để manager/VM phụ thuộc interface, stub test được.
- **Bỏ tham số `uiInvoke`** khỏi `AccountSession`: phương án đã chọn trong plan là dùng **event** (`CookieSaved`) nên session không cần đụng UI; toàn bộ marshaling dồn về VM (sạch hơn, tránh tham số thừa). `Changed` để **parameterless** (manager closure giữ tham chiếu phiên) — khớp `event Action? Changed` của manager.
- **Round-robin proxy thủ công** chuyển chỉ số dùng chung vào manager (thread-safe, chia sẻ giữa các phiên để nhiều tài khoản trải đều proxy); VM.`NextManualProxy` uỷ quyền cho manager → một nguồn duy nhất, test cũ vẫn xanh.
- **Trạng thái Error KHÔNG tính là "đang chạy"**: `IsRunning`=Opening/Running. Phiên kết thúc bình thường → Stopped → manager gỡ khỏi dictionary (hiển thị về null như hành vi reset cũ). Phiên lỗi → giữ Error để còn hiển thị lỗi; bấm "Mở" lại chạy lại phiên đó.
- Thêm nút **"Dừng"** đơn lẻ cho tài khoản đang chọn (Plan A giữ thao tác đơn; multi-select để Plan B).

### Hạn chế / chưa kiểm được
- Phần **marshaling UI qua Dispatcher** mới kiểm bằng đọc-code + boot app Release (không crash), **chưa** kiểm bằng thao tác UI thật với nhiều phiên (khó tự động hoá headless). Smoke đã chứng minh engine (manager + AccountSession + Brave thật) đúng ở tầng dưới UI, gồm chạy nền đa thread.
- Smoke dùng **2 profile trắng** (không có 2 tài khoản Shopee thật): password rỗng để bỏ qua auto-login; vẫn mở 2 Brave thật + 2 CDP port riêng nên đủ chứng minh song song/không-khóa/đóng-độc-lập/StopAll.
- Harness smoke nằm ngoài repo (scratchpad), không thêm vào solution.

---

## Cập nhật vòng 2 — sửa 3 lỗi concurrency (panel rà soát đối kháng, 2026-07-14)

Panel phát hiện 3 lỗi thật (2/2 phiếu). Đã sửa cả 3 + thêm test cho Lỗi 1.

### Lỗi 1 [BLOCKER] — gỡ phiên theo KEY xóa nhầm phiên mới
`AccountSessionManager.OnSessionChanged` xóa theo key (`TryRemove(id)`). Race: id 5 = A (Running) → Dừng → Start lại 5 tạo B (dict[5]=B) → event `Stopped` TRỄ của A → `TryRemove(5)` xóa nhầm B → B mồ côi (Brave chạy tiếp, `IsRunning(5)=false`, Stop/StopAll không thấy).
**Sửa:** gỡ theo (key, VALUE) — `((ICollection<KeyValuePair<long, IAccountSession>>)_sessions).Remove(new(id, session))` → chỉ xóa khi `dict[id]` đúng instance vừa phát event; là B thì bỏ qua.

### Lỗi 2 [MAJOR] — race Dừng→Mở lại khóa hồ sơ
`Stop(id)` `TryRemove` NGAY → `IsRunning=false` tức thì → nút Mở bật lại trong khi `StopAsync` fire-and-forget (Brave cũ chưa chết) → user Mở lại → launch Brave trên CÙNG `profiles/<id>` đang khóa → Error "hồ sơ đang bị khóa".
**Sửa:** `Stop` KHÔNG gỡ dict, chỉ gọi `StopAsync`. Việc gỡ để `OnSessionChanged` làm khi `State→Stopped` (RunAsync finally đặt Stopped SAU `await using` dispose kill Brave). Nhờ đó `IsRunning` giữ true tới khi Brave chết thật → nút Mở còn khóa. Thêm UX: `StopAsync` đặt `StatusText="Đang dừng..."` (giữ State=Running).

### Lỗi 3 [MAJOR] — PropertyChanged bắn từ thread nền (Plan B sẽ crash)
`AccountSession` set State/StatusText/ToShipCount từ RunAsync (thread nền) → `PropertyChanged` bắn trên nền. Chưa crash vì Plan A không bind trực tiếp (VM marshal qua RunOnUi), nhưng Plan B bind `ItemsControl` vào `AccountSession` → Avalonia cập nhật binding trên nền → "Call from invalid thread".
**Sửa:** override `AccountSession.OnPropertyChanged` → marshal về UI thread (`Dispatcher.UIThread.CheckAccess` ? inline : `Post`). Event `Changed` vẫn bắn từ nền (manager ConcurrentDictionary + VM tự marshal — an toàn).

### Nghiệm thu lại (thật)
- **Build Debug**: `Build succeeded — 0 Warning(s), 0 Error(s)`.
- **Test Release** (`dotnet test -c Release`): **Passed! Failed: 0, Passed: 142** (thêm test `StoppedTre_CuaPhienCu_KhongXoaNhamPhienMoiCungId` cho Lỗi 1; test này FAIL với code gỡ-theo-key cũ, PASS với gỡ-theo-value).
- **Smoke lại** (Brave thật), exit 0:
  - Phase 1–4 vẫn đúng (2 phiên song song PID/port riêng; đóng 1 không ảnh hưởng cái kia; StopAll → 0 brave, kiểm lại sau 2s vẫn **0 orphan**).
  - Phase 5 (Lỗi 2): Mở id1 (PID 1752) → `Stop` → **ngay sau Stop `IsRunning(id1)=True`** (nút Mở còn khóa) → chờ Brave chết → `IsRunning=False`, PID cũ chết → **Mở lại Running (PID 9892 khác)**, KHÔNG lỗi khóa hồ sơ.
  - Cũng xác nhận override `OnPropertyChanged` (Lỗi 3) KHÔNG phá luồng nền trong console (phiên vẫn đạt Running).

### Files sửa thêm (vòng 2)
- `src/XuLyDonShopee.App/Services/AccountSessionManager.cs` — `OnSessionChanged` gỡ theo value (Lỗi 1); `Stop` không gỡ dict ngay (Lỗi 2).
- `src/XuLyDonShopee.App/Services/AccountSession.cs` — override `OnPropertyChanged` marshal UI thread (Lỗi 3); `StopAsync` đặt `StatusText="Đang dừng..."`.
- `src/XuLyDonShopee.Tests/AccountSessionManagerTests.cs` — thêm `StubSession.RaiseChanged()` + test Lỗi 1 (nay 6 test manager; tổng 142).
