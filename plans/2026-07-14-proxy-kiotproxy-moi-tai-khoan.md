# Plan: Ô proxy KiotProxy riêng mỗi tài khoản + bỏ cờ cảnh báo Brave

- **Ngày:** 2026-07-14
- **Trạng thái:** hoàn thành (Bước B); Bước A: giữ nguyên theo quyết định người dùng
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)
- **Nghiệm thu:** Fable tự chạy build (0 error) + test (123/123). **Bước B** (proxy KiotProxy riêng mỗi tài khoản) ĐẠT — đọc code xác nhận migration DB an toàn (`EnsureColumn` guard PRAGMA + ALTER, idempotent, không phá dữ liệu), thứ tự cột SELECT↔Map khớp, proxy-wiring ưu tiên key riêng đúng. **Bước A** (bỏ cờ `--disable-blink-features=AutomationControlled`): Opus smoke chứng minh premise plan SAI — bỏ cờ → `navigator.webdriver=TRUE` (thủ phạm là kết nối CDP, không phải `--enable-automation`) → Opus revert đúng. Người dùng chốt: **GIỮ cờ, chấp nhận thanh vàng "unsupported flag"** (thanh này là UI trình duyệt, Shopee không đọc được → không ảnh hưởng chống bot). Không đổi thêm.

## 1. Bối cảnh & mục tiêu

Hai việc (gộp vì cùng đụng luồng mở Brave/proxy, cùng cần smoke):

**A. Bỏ cờ gây cảnh báo "unsupported command-line flag":** Brave hiện thanh vàng *"You are using an unsupported command-line flag: --disable-blink-features=AutomationControlled"*. Cờ này CHỈ cần khi trình duyệt ở chế độ automation (`--enable-automation`) để ép `navigator.webdriver=false`. Nhưng ta **tự launch Brave thật KHÔNG có `--enable-automation`** nên `navigator.webdriver` **vốn đã false** — cờ này **thừa** và gây cảnh báo. → **Bỏ cờ**, giữ webdriver=false (smoke xác nhận).

**B. Ô proxy KiotProxy riêng mỗi tài khoản:** người dùng muốn **mỗi tài khoản dùng 1 proxy riêng**, bằng cách **dán API key KiotProxy** vào ô proxy trong form chi tiết. Khi mở trang bán hàng cho tài khoản đó → dùng key riêng của nó lấy proxy sticky (`/current`) → mỗi tài khoản 1 IP ổn định (giữ IP lâu). (Đã chốt: ô chứa **API key KiotProxy**, KHÔNG phải chuỗi proxy trực tiếp.)

### Hiện trạng code (đã khảo sát)

- [Account.cs](../src/XuLyDonShopee.Core/Models/Account.cs): `Id, Email, Password, Phone?, Cookie?, Note?, Status, CreatedAt, UpdatedAt`. Chưa có trường proxy.
- [Database.cs](../src/XuLyDonShopee.Core/Data/Database.cs): `Initialize()` chạy `CREATE TABLE IF NOT EXISTS accounts (...)`. **DB người dùng đã tồn tại** (`%APPDATA%\XuLyDonShopee\app.db`) → thêm cột phải bằng **migration ALTER**, không chỉ sửa CREATE TABLE.
- [AccountRepository.cs](../src/XuLyDonShopee.Core/Data/AccountRepository.cs): `GetAll/GetById` SELECT liệt kê cột; `Insert/Update` + `BindWritableFields` + `Map`.
- [ShopeeLoginService.BraveLaunchArgs.cs](../src/XuLyDonShopee.Core/Services/BraveLaunchArgs.cs): `BuildBraveArgs` có `--disable-blink-features=AutomationControlled` và `--disable-features=Translate,AutomationControlled`.
- [AccountsViewModel.cs](../src/XuLyDonShopee.App/ViewModels/AccountsViewModel.cs) `OpenSellerAsync`: chọn proxy TRƯỚC khi mở Brave — hiện: `manual.Count>0 → NextManualProxy`; else global KiotProxy keys (`_services.Settings.GetKiotProxyKeys()`) → `ProxySelector.SelectKiotProxyAsync(kiot, _healthChecker)`; else IP máy. Có `_healthChecker`. Đọc `acc = GetById(targetId)` (hiện đọc ở trong `await using`, cho TryHumanLogin) — cần đọc **sớm hơn** để lấy `ProxyKey` trước khi launch.
- [ProxySelector.cs](../src/XuLyDonShopee.Core/Services/ProxySelector.cs): `SelectKiotProxyAsync(IKiotProxyClient?, IProxyHealthChecker)` = `/current ?? /new` + health check. [KiotProxyClient.cs](../src/XuLyDonShopee.Core/Services/KiotProxyClient.cs): `new KiotProxyClient(IEnumerable<string> keys)`.
- [AccountsView.axaml](../src/XuLyDonShopee.App/Views/AccountsView.axaml): form chi tiết dạng nhiều "card". (Vừa sửa: dòng thông báo đã xuống dưới nút; nút "Mở trang bán hàng" đã bỏ ký tự ↗.)

## 2. Phạm vi

- **Làm:**
  - A: `BuildBraveArgs` bỏ `--disable-blink-features=AutomationControlled`, đổi `--disable-features=Translate,AutomationControlled` → `--disable-features=Translate`. Sửa test.
  - B: thêm `Account.ProxyKey` + **migration** cột DB; repository đọc/ghi; ViewModel `EditProxyKey` + form UI; ưu tiên dùng proxy key riêng của tài khoản khi mở.
  - Test + smoke + README.
- **Không làm:** đổi màn Cài đặt (global KiotProxy keys vẫn giữ làm fallback); bỏ danh sách proxy thủ công; đổi cơ chế CDP/human-login (Plan 1/2).

## 3. Các bước thực hiện

### Bước A1 — Bỏ cờ cảnh báo (BraveLaunchArgs)

- Trong `BuildBraveArgs`: **xóa** dòng `"--disable-blink-features=AutomationControlled"`; đổi `"--disable-features=Translate,AutomationControlled"` → `"--disable-features=Translate"`.
- XML-doc: ghi rõ "KHÔNG dùng `--disable-blink-features=AutomationControlled` (gây thanh 'unsupported command-line flag'); vì không có `--enable-automation` nên `navigator.webdriver` vốn đã false."
- **Test** `BraveLaunchArgsTests`: sửa/thêm — args **KHÔNG** chứa `--disable-blink-features=AutomationControlled`; **KHÔNG** chứa chuỗi `AutomationControlled`; vẫn không có `--enable-automation`/`--headless`; vẫn có `--user-data-dir`/`--remote-debugging-port`/`--lang=vi-VN`.

### Bước B1 — Model + Migration DB

- `Account.cs`: thêm `public string? ProxyKey { get; set; }` với XML-doc: "API key KiotProxy riêng của tài khoản (tùy chọn). Có → mở trang bán hàng dùng proxy sticky theo key này."
- `Database.cs` `Initialize()`: giữ `CREATE TABLE IF NOT EXISTS` (thêm `ProxyKey TEXT` vào định nghĩa cho DB mới), **và** thêm bước migration cho DB cũ:
  ```csharp
  // Sau khi tạo bảng: thêm cột ProxyKey nếu DB cũ chưa có (không phá dữ liệu).
  EnsureColumn(conn, "accounts", "ProxyKey", "TEXT");
  ```
  `EnsureColumn`: đọc `PRAGMA table_info(accounts)`; nếu không có cột → `ALTER TABLE accounts ADD COLUMN ProxyKey TEXT`. (Hàm nhỏ, tái dùng được.)
- `AccountRepository.cs`: thêm `ProxyKey` vào danh sách cột `SELECT` (GetAll/GetById), `INSERT`, `UPDATE`, `BindWritableFields` (`$proxyKey` ← `(object?)a.ProxyKey ?? DBNull.Value`), và `Map` (`ProxyKey = r.IsDBNull(idx) ? null : r.GetString(idx)`). Cẩn thận **thứ tự cột** khớp giữa SELECT và Map.

### Bước B2 — ViewModel + UI

- `AccountsViewModel.cs`: thêm `[ObservableProperty] private string _editProxyKey = string.Empty;`. Nạp/ghi như các trường khác: `LoadIntoForm` (`EditProxyKey = a.ProxyKey ?? ""`), `ClearForm` (`= ""`), `Save` (Insert + Update: `ProxyKey = NullIfEmpty(EditProxyKey)`).
- `AccountsView.axaml`: thêm 1 ô nhập trong form chi tiết (gợi ý: 1 card mới "PROXY" hoặc thêm vào Card 3), theo đúng style `Classes="field"`/`fieldLabel` hiện có:
  - Label: "Proxy — API key KiotProxy (mỗi tài khoản 1 proxy)".
  - TextBox bind `EditProxyKey`, watermark "Dán API key KiotProxy cho tài khoản này (để trống = dùng cấu hình chung / IP máy)".

### Bước B3 — Ưu tiên proxy key riêng khi mở

- `OpenSellerAsync`: đọc `var acc = _services.Accounts.GetById(targetId);` **sớm** (ngay sau `targetId`, trước khi chọn proxy) và tái dùng `acc` cho cả chọn proxy lẫn TryHumanLogin (bỏ lần `GetById` trùng ở trong `await using`).
- Logic chọn proxy (thay khối hiện tại), theo thứ tự ưu tiên:
  1. **Proxy key riêng của tài khoản:** nếu `!string.IsNullOrWhiteSpace(acc?.ProxyKey)` → `IKiotProxyClient kiot = new KiotProxyClient(new[]{ acc.ProxyKey! });` → `proxy = await ProxySelector.SelectKiotProxyAsync(kiot, _healthChecker);` (current sticky → new → health → IP máy).
  2. Else nếu `manual.Count > 0` → `NextManualProxy(manual)`.
  3. Else global KiotProxy keys (như hiện tại).
  4. Else `null` (IP máy).
- Giữ nguyên phần còn lại (launch Brave, human-login, vòng poll cookie).

### Bước B4 — Test + Smoke + README

- **Test:**
  - `AccountRepositoryTests`: round-trip có `ProxyKey` (Insert giá trị → GetById trả đúng; null → null).
  - **Migration**: test tạo DB có bảng `accounts` **thiếu** cột ProxyKey (tạo tay bằng SQL cũ) rồi khởi tạo `new Database(path)` → `PRAGMA table_info` có `ProxyKey`; dữ liệu cũ còn nguyên. (Nếu khó dựng schema cũ, tối thiểu test `EnsureColumn` idempotent trên DB mới không ném.)
  - `BraveLaunchArgsTests` (Bước A1).
  - Giữ 117 test cũ xanh.
- **Smoke thật (Opus):**
  - A: mở Brave qua CDP → **KHÔNG còn thanh "unsupported command-line flag"**; `navigator.webdriver` **vẫn = false**, `hasOwnProperty('webdriver')` = false. (Nếu bỏ cờ mà webdriver thành true → BÁO NGAY, cân nhắc giữ cờ + chấp nhận cảnh báo; nhưng dự kiến vẫn false.)
  - B: nếu có key KiotProxy thật → tài khoản có ProxyKey mở ra đi đúng IP proxy của key đó; không có key thật thì ghi rõ, kiểm bằng đọc code + unit test.
- **README:** thêm: mỗi tài khoản có thể **dán API key KiotProxy riêng** (mỗi tài khoản 1 IP sticky); bỏ mô tả cờ automation cũ nếu có.

## 4. Tiêu chí nghiệm thu

- [ ] Build 0 error/0 warning; `dotnet test` tất cả pass (gồm test ProxyKey round-trip + migration + BraveLaunchArgs); 117 test cũ xanh. (ISG: build lại `-p:Deterministic=false` nếu `0x800711C7`.)
- [ ] A: `BuildBraveArgs` không còn `AutomationControlled`; smoke không còn thanh cảnh báo; `navigator.webdriver` vẫn false.
- [ ] B: form chi tiết có ô "Proxy — API key KiotProxy"; lưu/nạp đúng; DB cũ được migrate thêm cột `ProxyKey` không mất dữ liệu; khi mở tài khoản có ProxyKey, chọn proxy đi qua `KiotProxyClient(key)` + `/current` (đọc code xác nhận thứ tự ưu tiên).
- [ ] Không đổi hành vi CDP/human-login/cookie ngoài phần proxy.

## 5. Rủi ro & lưu ý

- **Migration DB là chỗ dễ hỏng dữ liệu người dùng** — dùng `ALTER TABLE ADD COLUMN` (không phá), guard bằng `PRAGMA table_info`, idempotent (chạy nhiều lần không lỗi). Test kỹ.
- **Thứ tự cột SELECT ↔ Map** phải khớp khi thêm `ProxyKey` — sai chỉ số `Map` sẽ đọc nhầm trường.
- Bỏ cờ `--disable-blink-features=AutomationControlled`: dự kiến webdriver vẫn false (không có `--enable-automation`), nhưng **phải smoke xác nhận**; nếu regress thì báo lại để cân nhắc.
- Proxy key riêng rỗng → rơi về cấu hình chung (giữ tương thích). KiotProxy `/current` cho IP sticky nhưng KiotProxy vẫn có thể xoay phía server theo `ttl` — ngoài tầm kiểm soát.
- WDAC/ISG máy dev: `FileLoadException 0x800711C7` khi test → build lại `-p:Deterministic=false`, có thể vài lần.
- Máy dev Windows; đã sửa sẵn `AccountsView.axaml` (thông báo xuống dưới nút, bỏ ↗) — Opus thêm ô proxy vào form, đừng đụng 2 chỗ đó.

---

## Báo cáo thực thi (Opus)

**Tóm tắt:** Bước B (proxy KiotProxy riêng mỗi tài khoản) hoàn thành đầy đủ. **Bước A (bỏ cờ) đã REVERT**
vì smoke thật chứng minh premise của plan sai: bỏ cờ làm `navigator.webdriver` thành **true** (regress
anti-bot). Chi tiết dưới.

### A. Bỏ cờ cảnh báo — DỪNG & REVERT (premise plan sai, đã smoke chứng minh)

Smoke đối chứng launch Brave THẬT (`C:\Program Files\BraveSoftware\...\brave.exe`) qua đúng
`BraveLaunchArgs.BuildBraveArgs` + nối CDP (`ConnectOverCDPAsync`) — y hệt luồng `ShopeeLoginService`,
đánh giá `navigator.webdriver` trên trang:

| Cấu hình | `navigator.webdriver` |
|---|---|
| **Bỏ cờ** (Bước A đề xuất) | **True** ← REGRESS |
| **Giữ cờ** (code cũ) | **False** |

Kết luận: cờ `--disable-blink-features=AutomationControlled` **KHÔNG thừa**. Vì app **luôn** nối CDP qua
`--remote-debugging-port`, chính kết nối CDP làm Blink bật cờ AutomationControlled → webdriver=true. Cờ
này ép webdriver về false. Premise plan ("không có `--enable-automation` nên webdriver vốn đã false") sai:
thủ phạm là CDP, không phải `--enable-automation`.

Hai tiêu chí nghiệm thu của Bước A ("không còn thanh cảnh báo" VÀ "webdriver vẫn false") **không thể đồng
thời đạt** chỉ bằng bỏ cờ. Theo quy tắc "plan sai thì dừng, không tự đoán/mở rộng phạm vi", tôi **revert
Bước A về nguyên trạng** (giữ cờ, giữ `--disable-features=Translate,AutomationControlled`) để KHÔNG ship
regress webdriver=true — vốn phá đúng mục đích chống-bot của app. Thanh vàng "unsupported command-line
flag" vẫn còn (đánh đổi có chủ đích). Đã cập nhật XML-doc `BraveLaunchArgs` + comment giải thích rõ lý do
BẮT BUỘC giữ cờ, để lần sau không ai bỏ nhầm.

**Cần Fable quyết định** (đề xuất ở cuối). Files A (đã revert về đúng hành vi cũ, kèm ghi chú mới):
`src/XuLyDonShopee.Core/Services/BraveLaunchArgs.cs`, `src/XuLyDonShopee.Tests/BraveLaunchArgsTests.cs`.

### B. Proxy KiotProxy riêng mỗi tài khoản — HOÀN THÀNH

- **B1 Model + Migration:**
  - `Account.cs`: thêm `public string? ProxyKey { get; set; }` (có XML-doc).
  - `Database.cs`: thêm `ProxyKey TEXT` vào `CREATE TABLE` (DB mới) + hàm `EnsureColumn(conn, table,
    column, type)` guard bằng `PRAGMA table_info` → `ALTER TABLE ADD COLUMN` chỉ khi thiếu; idempotent,
    KHÔNG phá dữ liệu. Gọi `EnsureColumn(conn, "accounts", "ProxyKey", "TEXT")` sau khi tạo bảng.
  - `AccountRepository.cs`: thêm `ProxyKey` vào SELECT (GetAll/GetById), INSERT, UPDATE,
    `BindWritableFields` (`$proxyKey`), `Map` (index 6; Status/CreatedAt/UpdatedAt dời sang 7/8/9). Thứ tự
    cột SELECT ↔ Map đã khớp (test migration + round-trip xác nhận).
- **B2 ViewModel + UI:**
  - `AccountsViewModel.cs`: `[ObservableProperty] _editProxyKey`; nạp ở `LoadIntoForm`, xóa ở `ClearForm`,
    ghi ở `Save` (cả Insert lẫn Update) qua `NullIfEmpty(EditProxyKey)`.
  - `AccountsView.axaml`: thêm card "PROXY" (giữa Card 2 Cookie và Card 3) — label "Proxy — API key
    KiotProxy (mỗi tài khoản 1 proxy)", TextBox bind `EditProxyKey`, watermark theo plan. **Không đụng** 2
    chỗ sửa tay (thông báo dưới hàng nút; nút "Mở trang bán hàng" không có ↗).
- **B3 Ưu tiên proxy riêng khi mở:** `OpenSellerAsync` đọc `acc = GetById(targetId)` **sớm** (1 lần, tái
  dùng cho cả chọn proxy lẫn human-login — đã bỏ lần `GetById` trùng trong `await using`). Thứ tự chọn
  proxy: (1) `acc.ProxyKey` → `new KiotProxyClient(new[]{key})` → `ProxySelector.SelectKiotProxyAsync`
  (/current sticky → /new → health → IP máy); (2) proxy thủ công round-robin; (3) key KiotProxy chung;
  (4) IP máy. Phần launch/human-login/poll cookie giữ nguyên.
- **B4 Test + README:**
  - `AccountRepositoryTests.cs`: +3 test ProxyKey (insert có key→đọc đúng; không key→null; update
    đổi/xóa key + kiểm không lệch chỉ số cột).
  - `DatabaseMigrationTests.cs` (mới): DB schema CŨ thiếu ProxyKey → khởi tạo `Database` → cột được thêm,
    dữ liệu cũ còn nguyên; ghi/đọc ProxyKey sau migration; khởi tạo nhiều lần idempotent không ném.
  - `README.md`: thêm bullet "Proxy riêng cho từng tài khoản" (dán API key KiotProxy riêng → mỗi tài
    khoản 1 IP sticky; nêu thứ tự ưu tiên chọn proxy).
- **Smoke B:** KHÔNG có API key KiotProxy sống để chạy proxy thật → phủ bằng đọc code (thứ tự ưu tiên +
  `KiotProxyClient(key)`→`/current`) và unit test có sẵn (`ProxySelectorTests` đã cover /current→/new,
  `KiotProxyKeyParserTests`/`KiotProxyClientTests` cover parse key). Không tự gọi mạng bằng key thật của
  người dùng.

### Kết quả kiểm chứng (số liệu thật)

- `dotnet build XuLyDonShopee.sln -c Debug`: **Build succeeded, 0 Warning, 0 Error** (đã dừng app+brave
  trước; không gặp `0x800711C7`).
- `dotnet test -c Debug`: **Passed 123 / Failed 0 / Skipped 0** (117 cũ + 6 mới: 3 ProxyKey repo + 3
  migration). Không dùng `-p:Deterministic=false` (không dính ISG lần này).
- Smoke A (đối chứng, đã revert): giữ cờ → `navigator.webdriver=False`; bỏ cờ → `True`. `hasOwnProperty
  ('webdriver')=False` ở cả hai. (Thanh vàng là chrome trình duyệt, không đọc được qua CDP/DOM — kết luận
  bằng phân tích: chuỗi cờ là thứ duy nhất trong args kích hoạt thanh đó.)

### Đối chiếu tiêu chí nghiệm thu

- [x] Build 0/0; test 123 pass (gồm ProxyKey round-trip + migration).
- [~] A: **KHÔNG đạt như plan** — không thể vừa bỏ cảnh báo vừa giữ webdriver=false. Đã revert giữ
  webdriver=false; thanh cảnh báo vẫn còn. Cần Fable quyết.
- [x] B: form có ô "Proxy — API key KiotProxy"; lưu/nạp đúng; DB cũ migrate thêm cột không mất dữ liệu;
  khi mở tài khoản có ProxyKey đi qua `KiotProxyClient(key)` + `/current` (đọc code + test xác nhận).
- [x] Không đổi hành vi CDP/human-login/cookie ngoài phần proxy.

### Đề xuất cho Bước A (Fable quyết)

1. **Giữ cờ, chấp nhận thanh vàng (đang là trạng thái sau revert)** — an toàn nhất cho anti-bot. Thanh
   "unsupported command-line flag" chỉ khó chịu về UX, không lộ webdriver.
2. **Thử `--test-type`** để ẩn thanh vàng mà (có thể) vẫn giữ webdriver=false — CẦN smoke kiểm chứng (cờ
   này có thể kéo theo hành vi test khác). Ngoài phạm vi plan hiện tại nên tôi chưa tự thêm.
3. Bỏ cờ + chấp nhận webdriver=true: **không khuyến nghị** (phá mục đích chống-bot).

### Files đã tạo/sửa

- Sửa: `src/XuLyDonShopee.Core/Models/Account.cs`, `src/XuLyDonShopee.Core/Data/Database.cs`,
  `src/XuLyDonShopee.Core/Data/AccountRepository.cs`,
  `src/XuLyDonShopee.App/ViewModels/AccountsViewModel.cs`, `src/XuLyDonShopee.App/Views/AccountsView.axaml`,
  `src/XuLyDonShopee.Tests/AccountRepositoryTests.cs`, `README.md`.
- Tạo: `src/XuLyDonShopee.Tests/DatabaseMigrationTests.cs`.
- Revert về nguyên trạng (kèm ghi chú mới): `src/XuLyDonShopee.Core/Services/BraveLaunchArgs.cs`,
  `src/XuLyDonShopee.Tests/BraveLaunchArgsTests.cs`.
