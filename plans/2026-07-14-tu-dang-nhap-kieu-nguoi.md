# Plan: Tự đăng nhập Shopee kiểu người (human-like) — Plan 2/2

- **Ngày:** 2026-07-14
- **Trạng thái:** hoàn thành
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)
- **Ghi chú:** Xếp chồng lên Plan 1 `2026-07-14-cdp-brave-chong-nhan-dien-bot.md` (đã hoàn thành: Brave thật qua CDP, không banner, `navigator.webdriver=false`, không over-patch stealth).
- **Nghiệm thu:** Fable tự chạy build (0 error) + test (117/117) + panel rà soát đối kháng 2 góc (flow-safety + edge hàm thuần) → **0 lỗi xác nhận**. Smoke Opus trên trang đăng nhập Shopee thật: điền đúng user/pass, chuột 98 điểm cong (lệch tối đa 143px), fallback selector `button:has-text('Đăng nhập')` cứu (button[type=submit] không khớp). Hạn chế: selector Shopee dễ đổi (có fallback + graceful tự nhập tay); auto-click submit dễ bị soi hơn.

## 1. Bối cảnh & mục tiêu

Tiếp Plan 1. Mục tiêu: **tự đăng nhập kiểu người** để giảm bị Shopee nhận diện bot. Đảo lại quyết định cũ ("người dùng tự đăng nhập"). Chống bot thật đến từ **browser thật (Plan 1) + hành vi kiểu người (Plan 2)**, KHÔNG phải vá JS.

**Quyết định đã chốt với người dùng:**
1. **Tự điền + tự submit:** app tự gõ **user + password** (từ tài khoản đang mở) rồi **tự bấm nút đăng nhập**; chỉ **dừng ở captcha/OTP** (người dùng tự xử lý). (Người dùng chọn phương án này dù click tự động dễ bị soi hơn — đã cảnh báo.)
2. **Gõ kiểu người:** từng ký tự, delay ngẫu nhiên (~80–250ms, thỉnh thoảng dừng lâu hơn); KHÔNG `fill`/dán một phát.
3. **Di chuột kiểu người:** rê chuột vào ô theo **đường cong nhiều điểm, tốc độ biến thiên** (KHÔNG "kéo 1 phát theo đường thẳng"), rồi mới nhấn.
4. **Graceful:** đã đăng nhập sẵn (profile bền) / không thấy ô đăng nhập → bỏ qua tự điền, để người dùng thao tác tay (KHÔNG ném lỗi).

### Hiện trạng code (sau Plan 1)

- [ShopeeLoginService.cs](../src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs): `OpenAsync(userDataDir, proxy)` mở Brave qua CDP, trả `ILoginSession { CaptureCookiesJsonAsync(); Closed; IsClosed; OpenPageCount; }`. `LoginSession` giữ `IPlaywright/IBrowser/IBrowserContext/Process`, có `_context`/page. **Không còn init script** (Brave thật đã sạch).
- [AccountsViewModel.cs](../src/XuLyDonShopee.App/ViewModels/AccountsViewModel.cs) `OpenSellerAsync`: chụp `targetId`, mở session, vòng poll bắt cookie (gate `ShopeeLoginCookies.IsLoggedIn`) → `SaveCapturedCookie`, thoát khi `OpenPageCount==0` x2 / deadline. `Account` có `Email` (user) + `Password` (plaintext).
- App dùng `System.Random` bình thường (không vướng ràng buộc workflow).

## 2. Phạm vi

- **Làm:**
  - Core hàm thuần (test được): sinh **đường cong chuột** (`HumanMouse`) + **lịch delay gõ** (`HumanTyping`).
  - `ILoginSession`/`LoginSession`: `Task<bool> TryHumanLoginAsync(string user, string password, CancellationToken ct)` — dò form đăng nhập, di chuột cong + click + gõ kiểu người cho user & password, rồi bấm nút đăng nhập. Graceful (không ném; trả false nếu bỏ qua).
  - `OpenSellerAsync`: sau khi mở & điều hướng, gọi `TryHumanLoginAsync(acc.Email, acc.Password)` (bọc try/catch), rồi vào vòng poll như cũ.
  - Test hàm thuần; smoke test thật; cập nhật README.
- **Không làm:** giải captcha/OTP (người dùng); tự đăng nhập khi đã đăng nhập sẵn; đổi luồng cookie/proxy/CRUD; đổi hạ tầng CDP (Plan 1).

## 3. Các bước thực hiện

### Bước 1 — Core: đường cong chuột kiểu người (thuần, test được)

Tạo `src/XuLyDonShopee.Core/Services/HumanMouse.cs`:
```csharp
public static class HumanMouse
{
    /// <summary>Sinh chuỗi điểm (x,y) đi từ (x0,y0) tới (x1,y1) theo đường cong Bézier bậc 3 với 2 điểm
    /// điều khiển lệch ngẫu nhiên (đường cong, KHÔNG thẳng), <paramref name="steps"/> điểm, điểm cuối
    /// đúng bằng (x1,y1). Nhận <see cref="Random"/> để test tất định.</summary>
    public static IReadOnlyList<(double X, double Y)> GeneratePath(
        double x0, double y0, double x1, double y1, int steps, Random rng)
}
```
- 2 điểm điều khiển P1,P2 = điểm trên đoạn thẳng + lệch ngẫu nhiên vuông góc (biên độ tỉ lệ độ dài đoạn, vd ±(15–35%) khoảng cách). Lấy mẫu `t` theo ease (không đều → tốc độ biến thiên) + jitter nhỏ. Ép điểm cuối = (x1,y1). `steps>=2`; `steps<=1` hoặc start==end → trả về điểm cuối lặp lại (không NaN).
- **Test** (`HumanMouseTests`, `Random` có seed): số điểm == steps; điểm cuối == (x1,y1); điểm đầu ≈ (x0,y0); **không thẳng hàng** (tồn tại điểm có độ lệch vuông góc so với đường thẳng start→end > epsilon); start==end không ném.

### Bước 2 — Core: lịch delay gõ kiểu người (thuần, test được)

Tạo `src/XuLyDonShopee.Core/Services/HumanTyping.cs`:
```csharp
public static class HumanTyping
{
    /// <summary>Delay (ms) trước ký tự kế: ~80–220ms; ~12% cơ hội "ngập ngừng" 350–800ms. Random để test.</summary>
    public static int NextCharDelayMs(Random rng)
}
```
- **Test** (`HumanTypingTests`, seed): nhiều mẫu đều trong [80, 800]; đa số < 250; qua N mẫu có ít nhất vài mẫu > 300 (có ngập ngừng).

### Bước 3 — Interaction: `TryHumanLoginAsync` (trong LoginSession)

- `ILoginSession` thêm: `Task<bool> TryHumanLoginAsync(string user, string password, CancellationToken ct = default);`
- `LoginSession` hiện thực (dùng `_context.Pages[0]`/page hiện có + `page.Mouse`/`page.Keyboard`):
  1. **Bỏ qua nếu đã đăng nhập:** nếu `ShopeeLoginCookies.IsLoggedIn(await CaptureCookiesJsonAsync())` → return false.
  2. **Dò ô đăng nhập** (timeout ngắn ~5s, thử nhiều selector, không thấy → return false):
     - user: `input[name='loginKey']` (fallback: `input[type='text']` đầu tiên trong form đăng nhập).
     - password: `input[name='password']` (fallback: `input[type='password']`).
     - nút đăng nhập: `button[type='submit']` (fallback: button chứa chữ "Đăng nhập").
     - **Selector Shopee có thể đổi** — Opus xác minh trên trang thật lúc smoke, chỉnh cho khớp, và LUÔN có nhánh "không thấy → return false" (để người dùng tự nhập tay). Ghi selector thực tế vào báo cáo.
  3. **Điền kiểu người** cho user rồi password: lấy `BoundingBoxAsync()` của ô → tâm ô (+jitter nhỏ) → `HumanMouse.GeneratePath(mousePos → tâm)` → duyệt từng điểm `page.Mouse.MoveAsync(x,y)` + delay nhỏ ngẫu nhiên (5–25ms) (KHÔNG dùng `steps` lớn của MoveAsync — tự đi từng điểm cong) → `Mouse.DownAsync()`+delay+`Mouse.UpAsync()` (click) → gõ từng ký tự `page.Keyboard.TypeAsync(ch)` + `Task.Delay(HumanTyping.NextCharDelayMs(rng))`. Cập nhật `mousePos`.
  4. **Bấm đăng nhập:** di chuột cong tới nút → click. KHÔNG xử lý captcha/OTP.
  5. `Random` tạo nội bộ (`new Random()`); mọi bước bọc try/catch — bất kỳ lỗi nào → return false (không phá luồng). Theo dõi `mousePos` bắt đầu ở giữa viewport (đọc `page.ViewportSize`, null thì mặc định vd (640,360)).

### Bước 4 — Ghép vào ViewModel

Trong `OpenSellerAsync`, sau `session = await _loginService.OpenAsync(...)` và trước/đầu vòng poll:
```csharp
var acc = _services.Accounts.GetById(targetId);
if (acc is not null && !string.IsNullOrEmpty(acc.Password))
{
    BusyStatus = "Đang tự đăng nhập (kiểu người)...";
    try { await session.TryHumanLoginAsync(acc.Email, acc.Password); } catch { /* không phá luồng */ }
}
```
Rồi giữ nguyên BusyStatus "Hãy đăng nhập..." + vòng poll (vòng poll tự lưu cookie khi có cookie đăng nhập — dù đăng nhập do app điền hay người dùng tự làm captcha xong). Chụp `acc` theo `targetId` (không đọc lại form) để tránh race.

### Bước 5 — Test + Smoke + README

- **Test thuần:** `HumanMouseTests`, `HumanTypingTests` (seed). Giữ 104 test cũ xanh.
- **Smoke thật (Opus tự chạy):** mở Brave qua CDP tới trang đăng nhập Shopee (nếu profile chưa đăng nhập), gọi `TryHumanLoginAsync("test_user","test_pass")` → xác nhận: chuột di **cong** (log toạ độ vài điểm cho thấy không thẳng), ô user/password **được điền đúng giá trị** (đọc `input.value`), nút đăng nhập được click; KHÔNG ném khi không thấy ô (trang khác). Ghi selector thực tế + số liệu. Nếu môi trường không mở được cửa sổ → ghi rõ.
- **README:** bổ sung: app **tự đăng nhập kiểu người** (gõ + di chuột như người), dừng ở captcha/OTP; cảnh báo vẫn không đảm bảo 100% né anti-bot; selector Shopee có thể đổi (khi đó tự nhập tay).

## 4. Tiêu chí nghiệm thu

- [ ] Build 0 error/0 warning; `dotnet test` tất cả pass (gồm `HumanMouseTests`, `HumanTypingTests`); 104 test cũ xanh. (ISG: build lại `-p:Deterministic=false` nếu `0x800711C7`.)
- [ ] `HumanMouse.GeneratePath` cho đường **cong** (test độ lệch > 0), điểm cuối đúng đích; `HumanTyping.NextCharDelayMs` trong biên + có ngập ngừng.
- [ ] `TryHumanLoginAsync`: điền user+password kiểu người + click đăng nhập; **graceful** (đã đăng nhập / không thấy ô → return false, không ném). Smoke chứng minh ô được điền + chuột đi cong.
- [ ] `OpenSellerAsync` gọi tự đăng nhập rồi vẫn tự lưu cookie qua vòng poll; không đổi hành vi lưu cookie/proxy.

## 5. Rủi ro & lưu ý

- **Tự bấm đăng nhập dễ bị anti-bot soi hơn** click người thật — người dùng đã chọn; chỉnh dần nếu bị dính.
- **Selector Shopee dễ đổi** → bắt buộc có fallback "không thấy → tự nhập tay"; không hard-fail.
- **Không đảm bảo né 100% anti-bot** (như Plan 1) — best-effort.
- Mật khẩu đọc từ `Account.Password` (plaintext, như hiện tại).
- WDAC/ISG máy dev có thể chặn nạp DLL test (`0x800711C7`) — build lại `-p:Deterministic=false`.

---

## Báo cáo thực thi (Opus — 2026-07-14)

### Files đã tạo/sửa
- **Tạo** `src/XuLyDonShopee.Core/Services/HumanMouse.cs` — `GeneratePath(x0,y0,x1,y1,steps,rng)`: Bézier bậc 3, 2 điểm điều khiển lệch vuông góc **cùng một phía** (biên độ 15–35% khoảng cách → luôn cong, không thể triệt tiêu về thẳng bất kể seed), lấy mẫu t theo ease smoothstep + jitter, rung nhẹ (≤2px) ở điểm giữa, **ép** điểm đầu = (x0,y0) và điểm cuối = (x1,y1). Suy biến (steps≤1 / start≡end) → trả điểm cuối lặp lại, không NaN.
- **Tạo** `src/XuLyDonShopee.Core/Services/HumanTyping.cs` — `NextCharDelayMs(rng)`: 88% trả 80–220ms, 12% "ngập ngừng" 350–800ms; luôn trong [80,800].
- **Sửa** `src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs`:
  - `ILoginSession` thêm `Task<bool> TryHumanLoginAsync(string user, string password, CancellationToken ct = default)` (doc-comment nêu rõ graceful, không ném).
  - `LoginSession` hiện thực: (1) bỏ qua nếu đã đăng nhập (`ShopeeLoginCookies.IsLoggedIn`); (2) dò ô qua `FindFirstVisibleAsync` (poll selector, timeout 5s user / 3s pass / 3s submit; không thấy → return false); (3) `HumanFillAsync` = di chuột cong (`HumanMoveAndClickAsync`) + click người (down/trễ/up) + gõ **từng ký tự** `Keyboard.TypeAsync(ch)` + `Task.Delay(HumanTyping...)`; (4) bấm nút đăng nhập; toàn bộ bọc try/catch → return false. Chuột tự `Mouse.MoveAsync` **từng điểm** của path (steps mặc định =1, KHÔNG đi thẳng); `new Random()` nội bộ; con trỏ khởi đầu giữa viewport (đọc `ViewportSize`, null → 640×360).
- **Sửa** `src/XuLyDonShopee.App/ViewModels/AccountsViewModel.cs` — `OpenSellerAsync`: ngay sau `await using (session)`, đọc `acc = _services.Accounts.GetById(targetId)` (theo targetId, chống race), nếu có password thì đặt BusyStatus "Đang tự đăng nhập (kiểu người)..." rồi `try { await session.TryHumanLoginAsync(acc.Email, acc.Password); } catch { }`, sau đó vào vòng poll cũ (không đổi hành vi lưu cookie/proxy).
- **Tạo** `src/XuLyDonShopee.Tests/HumanMouseTests.cs` (7 hàm, 10 test-case gồm 1 Theory 4 seed) và `src/XuLyDonShopee.Tests/HumanTypingTests.cs` (3 test).
- **Sửa** `README.md` — đảo mô tả "đăng nhập thủ công" → "**tự đăng nhập kiểu người**" (gõ + di chuột như người, dừng ở captcha/OTP, không thấy ô/đã đăng nhập → tự nhập tay), thêm cảnh báo best-effort/không đảm bảo 100% + tự bấm dễ bị soi hơn.

### Kết quả kiểm chứng (số liệu thật)
- **Build:** `dotnet build XuLyDonShopee.sln -c Debug` → **Build succeeded, 0 Warning(s), 0 Error(s)**. (Đã kill app/brave trước; không gặp ISG `0x800711C7`, không cần `-p:Deterministic=false`.)
- **Test:** `dotnet test` → **Passed! Failed: 0, Passed: 117, Skipped: 0**. Trước: 104 (theo plan) → sau: 117 (thêm 13: 10 HumanMouse + 3 HumanTyping); 104 test cũ vẫn xanh.
- **Smoke THẬT** (harness console mở Brave qua `ShopeeLoginService.OpenAsync`, nối **CDP thứ hai** vào cùng Brave qua `DevToolsActivePort` để quan sát chính phương thức thật):
  - Trình duyệt: **Brave thật** (`C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe`).
  - Điều hướng thật tới `https://accounts.shopee.vn/seller/login?next=...`.
  - **Selector khớp trên trang thật:** user = `input[name='loginKey']` (primary), password = `input[name='password']` (primary), submit = **`button:has-text('Đăng nhập')`** — tức `button[type='submit']` KHÔNG khớp → **fallback text cứu**; chứng minh bắt buộc phải có fallback.
  - `TryHumanLoginAsync("test_user","test_pass")` → **return True**, ~7141ms, **không ném**.
  - `input.value` đọc lại: user = `'test_user'`, password = `'test_pass'` → **điền đúng** (dùng listener chặn submit navigation để giữ giá trị mà đọc).
  - Chuột: **98 điểm** mousemove ghi được; mẫu toạ độ `[[640,360],[839,530],[1287,668],[1739,616],[1848,592],[1841,659],[1846,731]]`; **độ lệch vuông góc tối đa = 143.4px** so với đường thẳng đầu→cuối → **đường CONG rõ rệt**, không thẳng. Nút đăng nhập được click (đếm 1 lần).
  - **Graceful:** điều hướng sang `https://example.com/` (không có form) rồi gọi lại → **return False**, ~5043ms (đúng bằng timeout dò), **không ném**.

### Đối chiếu tiêu chí nghiệm thu
- [x] Build 0/0; test 117 pass (gồm HumanMouseTests, HumanTypingTests); 104 test cũ xanh.
- [x] `GeneratePath` cho đường cong (test maxDev > 5 và > 1 với 4 seed; smoke 143.4px), điểm cuối đúng đích; `NextCharDelayMs` trong biên [80,800] + có ngập ngừng > 300ms.
- [x] `TryHumanLoginAsync` điền user+password kiểu người + click đăng nhập; graceful (đã đăng nhập / không thấy ô → false, không ném). Smoke chứng minh ô được điền đúng + chuột đi cong.
- [x] `OpenSellerAsync` gọi tự đăng nhập rồi vẫn giữ vòng poll lưu cookie như cũ; không đổi hành vi cookie/proxy.

### Quyết định phát sinh
- Hai điểm điều khiển Bézier lệch **cùng phía** (thay vì mỗi điểm một dấu ngẫu nhiên) để bảo đảm "không thẳng hàng" đúng với **mọi seed** (nếu ngược phía, một số seed có thể triệt tiêu độ lệch tại t=1/3 hoặc 2/3). Vẫn là một cung cong tự nhiên.
- `FindFirstVisibleAsync` poll **nhiều selector trong một hạn thời gian chung** (không nhân timeout theo từng selector), ưu tiên phần tử **đang hiển thị** (`IsVisibleAsync`) để tránh ô ẩn.
- README: sửa cả 2 chỗ nêu "đăng nhập thủ công" (mục tính năng + ghi chú) vì Plan 2 đảo quyết định đó; **không** đụng câu mô tả "init script stealth" cũ (thuộc phạm vi Plan 1) để tránh mở rộng — xem Hạn chế.

### Hạn chế / lưu ý
- Smoke chạy với tài khoản giả (`test_user`/`test_pass`) và **chặn submit navigation** bằng JS trong harness để đọc được `input.value`; không kiểm thử luồng đăng nhập thành công thật (cần tài khoản thật + tự làm captcha). Việc "dừng ở captcha/OTP" đảm bảo bằng thiết kế (chỉ click submit, không xử lý captcha) + đọc code, không unit-isolate.
- Selector submit thật hiện là nút chữ "Đăng nhập" (không phải `type=submit`) — nếu Shopee đổi text/ngôn ngữ, cần cập nhật `SubmitSelectors`; đã có nhánh không thấy → không click (người dùng tự bấm).
- README vẫn còn **một câu mô tả "init script stealth" tồn dư từ trước Plan 1** (Plan 1 đã bỏ init script) — nằm ngoài phạm vi Plan 2 nên chưa sửa; đề xuất Plan 1 dọn nốt.
- Harness smoke đặt tại scratchpad (`.../scratchpad/smoke`), không nằm trong solution, không ảnh hưởng build/test chính.
