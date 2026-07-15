# Plan: Theo dõi đơn "Chờ Lấy Hàng" trong phiên mở trang bán hàng

- **Ngày:** 2026-07-14
- **Trạng thái:** hoàn thành (báo cáo + tiêu chí đã tick ở cuối file; header này cập nhật muộn ngày 15/7)
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)
- **Ghi chú:** Bước đầu của tính năng "xử lý đơn". Plan này CHỈ làm phần **theo dõi + đọc số đơn Chờ Lấy Hàng**; nhánh **xử lý đơn thực sự** (khi >0) làm ở plan sau.

## 1. Bối cảnh & mục tiêu

Sau khi "Mở trang bán hàng" và đăng nhập, người dùng muốn **một process theo dõi trang đó**: cứ **30 phút** đọc số **"Chờ Lấy Hàng"** trong to-do box của Seller Centre; nếu **= 0** thì bỏ qua, 30' sau **reload** và check lại; nếu **> 0** thì (bước này) **chỉ ghi trạng thái** và **vẫn tiếp tục check** mỗi 30' (nhánh xử lý đơn để sau).

**HTML thật người dùng cung cấp** (to-do box, trang `banhang.shopee.vn`):
```html
<div class="to-do-box-item"> <!-- thực chất là thẻ <a> -->
  <a href="/portal/shipment?type=toship&source=to_process" class="to-do-box-item">
    <p class="item-title">0</p>
    <p class="item-desc">Chờ Lấy Hàng</p>
  </a>
</div>
```
→ Ô cần đọc: phần tử có `class="to-do-box-item"` mà `p.item-desc` = **"Chờ Lấy Hàng"**, đọc `p.item-title` (số). (href tương ứng `/portal/shipment?type=toship&source=to_process`.)

**Quyết định đã chốt với người dùng:**
- **Theo dõi TRONG phiên "Mở trang bán hàng"** (không phải chế độ nền riêng): giữ cửa sổ Brave mở, cứ 30' reload + check; dừng khi người dùng đóng cửa sổ.
- Khi **> 0**: **chỉ ghi trạng thái** (số đơn), **vẫn check tiếp** mỗi 30'. (Không mở link, không dừng vòng.)

### Hiện trạng code (đã khảo sát)

- [ShopeeLoginService.cs](../src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs): `LoginSession` giữ `IBrowserContext _context` + có page (`_context.Pages[0]`). Đã có `CaptureCookiesJsonAsync`, `OpenPageCount`, `Closed/IsClosed`, `TryHumanLoginAsync`. `ShopeeLoginCookies.IsLoggedIn(json)` phân biệt đã đăng nhập.
- [AccountsViewModel.cs](../src/XuLyDonShopee.App/ViewModels/AccountsViewModel.cs) `OpenSellerAsync`: sau khi mở + `TryHumanLoginAsync`, có **vòng poll 1s** bắt cookie (gate `IsLoggedIn`) → `SaveCapturedCookie`; thoát khi `OpenPageCount==0` ở 2 vòng liên tiếp **hoặc** `deadline = UtcNow + 15 phút` (chốt chặn cứng). Có `[ObservableProperty] BusyStatus`.
  - ⚠️ **15 phút là quá ngắn cho theo dõi 30'** — plan này đổi chốt chặn thành cap dài (12h) nhưng GIỮ tín hiệu kết thúc chính `OpenPageCount==0 x2` (Plan 1) để không hang.

## 2. Phạm vi

- **Làm:**
  - Core: hàm thuần `ShopeeDashboard.ParseToShipCount(string?)` (đọc số từ text `item-title`, test được).
  - `ILoginSession.ReadToShipCountAsync(bool reload, ct)` — (gate đã đăng nhập) reload nếu cần → tìm ô "Chờ Lấy Hàng" → đọc số. Graceful → null nếu chưa đăng nhập/không thấy.
  - `OpenSellerAsync`: đổi chốt chặn 15'→12h; thêm nhịp check đơn 30' (reload + đọc) trong cùng vòng poll; cập nhật trạng thái đơn. Khi >0 chỉ ghi trạng thái, vẫn check tiếp.
  - `[ObservableProperty] OrderStatus` + hiển thị UI.
  - Test hàm thuần; smoke thật; README.
- **Không làm:** nhánh xử lý đơn thực sự khi >0 (plan sau); chế độ nền độc lập; theo dõi nhiều tài khoản cùng lúc.

## 3. Các bước thực hiện

### Bước 1 — Core: đọc số từ item-title (thuần, test được)

Tạo `src/XuLyDonShopee.Core/Services/ShopeeDashboard.cs`:
```csharp
public static class ShopeeDashboard
{
    /// <summary>Đọc số đơn từ text ô item-title (vd "0", "12", "1.234", "99+"). Lấy các chữ số → int.
    /// Không parse được / rỗng → null.</summary>
    public static int? ParseToShipCount(string? itemTitleText)
}
```
- Bỏ mọi ký tự không phải chữ số (dấu chấm/phẩy ngăn nghìn, khoảng trắng, dấu "+", ...), parse phần số. Rỗng/không có chữ số → null. ("99+" → 99).
- **Test** (`ShopeeDashboardTests`): "0"→0; "12"→12; "1.234"→1234; "99+"→99; ""/null/"abc"→null.

### Bước 2 — `ReadToShipCountAsync` (trong LoginSession)

- `ILoginSession` thêm: `Task<int?> ReadToShipCountAsync(bool reload, CancellationToken ct = default);`
- `LoginSession` hiện thực (page = `_context.Pages[0]`), bọc try/catch toàn bộ → null khi lỗi:
  1. **Gate:** nếu `!ShopeeLoginCookies.IsLoggedIn(await CaptureCookiesJsonAsync())` → return null (chưa đăng nhập, to-do box chưa có).
  2. Nếu `reload` → `await page.ReloadAsync(new(){ WaitUntil=DOMContentLoaded, Timeout=30000 })` (nuốt lỗi điều hướng).
  3. Chờ ô xuất hiện (timeout ngắn ~8s): tìm phần tử "Chờ Lấy Hàng". **Cách tìm** (thử theo thứ tự, có fallback, ghi selector thật vào báo cáo):
     - Locator theo desc: các `.to-do-box-item`, duyệt tìm cái có `.item-desc` text == "Chờ Lấy Hàng" → đọc `.item-title` text.
     - Fallback theo href: `a[href*='type=toship'][href*='to_process'] .item-title`.
     - **Selector Shopee có thể đổi** → không thấy thì return null (không ném).
  4. `return ShopeeDashboard.ParseToShipCount(titleText);`

### Bước 3 — Ghép theo dõi vào `OpenSellerAsync`

Trong khối `await using (session)` (giữ nguyên phần bắt cookie), sửa vòng poll:
- Thêm `[ObservableProperty] private string? _orderStatus;` (VM). Đầu vòng: `OrderStatus = null;` cuối cùng (finally) `OrderStatus = null;`.
- Đổi `var deadline = UtcNow.AddMinutes(15);` → `var hardCap = DateTime.UtcNow.AddHours(12);` (cap an toàn dài; KHÔNG hang nhờ `OpenPageCount==0 x2` vẫn là tín hiệu chính).
- Thêm biến: `var nextOrderCheck = DateTime.UtcNow;` `bool firstOrderCheck = true;` Hằng: `const int OrderIntervalMin = 30; const int OrderRetrySec = 30;`.
- Trong vòng `while (!session.IsClosed && DateTime.UtcNow < hardCap)` (giữ nguyên phần `WhenAny`, đếm `zeroPageStreak`, bắt cookie), thêm SAU phần bắt cookie:
  ```csharp
  if (DateTime.UtcNow >= nextOrderCheck)
  {
      var count = await session.ReadToShipCountAsync(reload: !firstOrderCheck);
      if (count is int n)
      {
          firstOrderCheck = false;
          OrderStatus = n > 0
              ? $"Chờ Lấy Hàng: {n} đơn — vẫn theo dõi mỗi {OrderIntervalMin}'."
              : $"Chờ Lấy Hàng: 0 — kiểm lại sau {OrderIntervalMin}'.";
          nextOrderCheck = DateTime.UtcNow.AddMinutes(OrderIntervalMin); // đã đăng nhập → 30'
      }
      else
      {
          // Chưa đăng nhập / chưa đọc được → thử lại sớm (đợi người dùng xong đăng nhập/captcha), KHÔNG reload.
          nextOrderCheck = DateTime.UtcNow.AddSeconds(OrderRetrySec);
      }
  }
  ```
  (Lưu ý: khi >0, **chỉ cập nhật `OrderStatus`**, KHÔNG dừng, KHÔNG mở link — theo lựa chọn người dùng. `nextOrderCheck` vẫn +30' → tiếp tục reload+check.)
- Cập nhật `BusyStatus` khởi đầu: đại ý "Đã mở trình duyệt. Đăng nhập xong app sẽ tự theo dõi đơn mỗi 30'; đóng cửa sổ để dừng."
- Giữ nguyên "lần bắt cookie chốt" + thông báo kết quả cookie khi thoát vòng.

### Bước 4 — UI hiển thị OrderStatus

`AccountsView.axaml`: thêm 1 `TextBlock` ngay dưới dòng `BusyStatus` (phần thông báo đã ở DƯỚI hàng nút — KHÔNG di chuyển lại):
```xml
<TextBlock Text="{Binding OrderStatus}" Foreground="#1565C0" TextWrapping="Wrap" Margin="0,8,0,0"
           IsVisible="{Binding OrderStatus, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />
```

### Bước 5 — Test + Smoke + README

- **Test:** `ShopeeDashboardTests` (Bước 1). Giữ 123 test cũ xanh.
- **Smoke thật (Opus):** cần một phiên **đã đăng nhập** Shopee (dùng profile đã đăng nhập nếu có; nếu không có tài khoản thật thì mô phỏng: nạp một trang HTML tĩnh chứa đúng cấu trúc to-do box rồi gọi `ReadToShipCountAsync(reload:false)` → đọc đúng số). Để smoke nhanh, **tạm** đặt `OrderIntervalMin` nhỏ (vd 1 phút) chứng minh vòng lặp reload+đọc chạy lại, **rồi trả về 30**. Ghi selector thật + số liệu. Nếu không mở được cửa sổ/không có phiên đăng nhập → ghi rõ, phủ phần đọc bằng test HTML tĩnh + `ParseToShipCount`.
- **README:** thêm mục: sau khi mở & đăng nhập, app **tự theo dõi số "Chờ Lấy Hàng" mỗi 30'** (reload lại trang); hiện số trên form; >0 thì báo số (xử lý đơn tự động ở bước sau); đóng cửa sổ để dừng.

## 4. Tiêu chí nghiệm thu

- [ ] Build 0 error/0 warning; `dotnet test` tất cả pass (gồm `ShopeeDashboardTests`); 123 test cũ xanh. (ISG: `-p:Deterministic=false` nếu `0x800711C7`.)
- [ ] `ParseToShipCount` đúng các case ("0"/số/có dấu nghìn/"99+"/rác).
- [ ] `ReadToShipCountAsync`: gate đã-đăng-nhập; đọc đúng ô "Chờ Lấy Hàng"; graceful (chưa đăng nhập/không thấy → null, không ném). Smoke chứng minh đọc đúng số (trang thật hoặc HTML tĩnh mô phỏng).
- [ ] `OpenSellerAsync`: chốt chặn đổi 15'→cap dài, tín hiệu kết thúc vẫn `OpenPageCount==0 x2`; check đơn mỗi 30' (reload); >0 chỉ ghi `OrderStatus`, vẫn check tiếp; không đổi hành vi bắt cookie/proxy/human-login.
- [ ] Đóng cửa sổ → vòng thoát → không Brave mồ côi (Plan 1 giữ nguyên).

## 5. Rủi ro & lưu ý

- **Selector Shopee dashboard dễ đổi** → bắt buộc fallback + graceful null (không ném, không phá phiên).
- **Reload khi đang giải captcha/đăng nhập:** đã gate bằng `IsLoggedIn` — chưa đăng nhập thì KHÔNG reload (tránh phá thao tác người dùng); chỉ reload khi đã đăng nhập.
- **Phiên chạy dài (giờ):** cap 12h thay 15'. Tín hiệu kết thúc chính vẫn `OpenPageCount==0 x2` (đóng cửa sổ) — giữ nguyên chống-hang của Plan 1. `DisposeAsync` vẫn kill cây tiến trình.
- **30' là chu kỳ dài để smoke** — Opus dùng interval nhỏ tạm thời để chứng minh, rồi ĐẶT LẠI 30.
- **Chưa xử lý đơn khi >0** (đúng phạm vi) — chỉ báo số; nhánh xử lý ở plan sau.
- WDAC/ISG máy dev: `0x800711C7` → build lại `-p:Deterministic=false`, có thể vài lần.
- Đừng đụng: dòng thông báo đã đặt dưới hàng nút; nút "Mở trang bán hàng" đã bỏ ↗; ô proxy vừa thêm.

---

## Báo cáo thực thi (Opus điền sau khi xong)

**Ngày thực thi:** 2026-07-14 · **Người thực thi:** Opus (`opus-executor`)

### Files đã tạo/sửa
- **Tạo** `src/XuLyDonShopee.Core/Services/ShopeeDashboard.cs` — hàm thuần `ParseToShipCount(string?)`.
- **Sửa** `src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs` — thêm `ReadToShipCountAsync(bool, ct)` vào `ILoginSession` + hiện thực trong `LoginSession` (gate `IsLoggedIn` → reload có điều kiện → `FindToShipTitleAsync` (selector chính + fallback href, poll ~8s) → `ParseToShipCount`; bọc try/catch toàn bộ → null, không ném).
- **Sửa** `src/XuLyDonShopee.App/ViewModels/AccountsViewModel.cs` — thêm `[ObservableProperty] OrderStatus`; đổi chốt chặn `deadline 15'` → `hardCap 12h`; giữ nguyên `OpenPageCount==0 x2` làm tín hiệu kết thúc chính; thêm nhịp check đơn 30' (retry 30s khi chưa đọc được), khi >0 chỉ ghi `OrderStatus` vẫn check tiếp; cập nhật `BusyStatus` khởi đầu; `OrderStatus=null` ở finally.
- **Sửa** `src/XuLyDonShopee.App/Views/AccountsView.axaml` — thêm `TextBlock` (Foreground `#1565C0`) bind `OrderStatus` NGAY DƯỚI dòng `BusyStatus` (không di chuyển hàng nút / dòng thông báo / ô proxy).
- **Tạo** `src/XuLyDonShopee.Tests/ShopeeDashboardTests.cs` — 13 test cho `ParseToShipCount`.
- **Sửa** `README.md` — thêm mục "Theo dõi đơn Chờ Lấy Hàng".

### Selector thật đã dùng (đã xác minh trên DOM Chromium thật)
- **Chính:** `document.querySelectorAll(".to-do-box-item")` → với mỗi phần tử tìm `.item-desc`, chuẩn hóa khoảng trắng + so khớp (ignore-case) == **"Chờ Lấy Hàng"** → đọc `.item-title` (InnerText).
- **Fallback theo href:** `a[href*='type=toship'][href*='to_process'] .item-title`.
- Không thấy → `null` (không ném). Trùng cấu trúc HTML người dùng cung cấp.

### Kết quả build/test THẬT
- `dotnet build XuLyDonShopee.sln -c Debug`: **Build succeeded, 0 Warning, 0 Error**.
- `dotnet test` lần đầu: 11 fail do **WDAC/ISG chặn `XuLyDonShopee.App.dll` (`FileLoadException 0x800711C7`)** — không phải lỗi code (125 pass). Rebuild `-p:Deterministic=false` rồi test lại: **Passed! Failed: 0, Passed: 136, Total: 136** (123 test cũ + 13 test mới `ShopeeDashboardTests`).

### Smoke THẬT (đã chạy)
Không có tài khoản Shopee thật, và `ReadToShipCountAsync` bị chặn bởi gate `IsLoggedIn` + ctor `LoginSession` là private → không gọi trực tiếp được. Đã smoke bằng cách nạp **HTML tĩnh đúng cấu trúc to-do box** vào **Chromium thật (Playwright headless)** và chạy **đúng chuỗi selector** của `FindToShipTitleAsync`, rồi qua `ShopeeDashboard.ParseToShipCount`. Số liệu thật (từ file kết quả):
- Case1 (nhiều mục, "Chờ Lấy Hàng"=3, có cả "Chờ Xác Nhận"=10): raw `'3'` → **3** (chọn đúng mục, không nhầm).
- Case2 (=0): raw `'0'` → **0**.
- Case3 (desc đổi thành "Awaiting Pickup" → ép dùng fallback href, title=7): raw `'7'` → **7**.
- Case4 (không có to-do box): raw `<null>` → **null** (graceful).
- Case5 ("1.234"): raw `'1.234'` → **1234**.

File smoke tạm (`_SmokeToShipReadTests.cs`) đã **xóa** sau khi chạy; test suite trở lại 136.

### Đối chiếu tiêu chí nghiệm thu
- [x] Build 0 error/0 warning; `dotnet test` 136 pass (gồm `ShopeeDashboardTests`; 123 cũ xanh). ISG cần `-p:Deterministic=false`.
- [x] `ParseToShipCount` đúng các case ("0"/số/dấu nghìn/"99+"/rác/null/tràn int→null).
- [x] `ReadToShipCountAsync`: gate đã-đăng-nhập; đọc đúng ô "Chờ Lấy Hàng"; graceful null. Smoke chứng minh đọc đúng số bằng HTML tĩnh trên trình duyệt thật.
- [x] `OpenSellerAsync`: 15'→cap 12h; tín hiệu kết thúc vẫn `OpenPageCount==0 x2`; check đơn mỗi 30' (reload lần 2 trở đi); >0 chỉ ghi `OrderStatus`, vẫn check tiếp; không đổi bắt cookie/proxy/human-login.
- [~] Đóng cửa sổ → vòng thoát → không Brave mồ côi: **chỉ kiểm bằng đọc code** (giữ nguyên `OpenPageCount==0 x2` + `DisposeAsync` kill cây tiến trình của Plan 1, không sửa) — chưa chạy live vì không có tài khoản đăng nhập thật.

### Hạn chế / phần CHƯA chạy live (trung thực)
- **Không driver được vòng lặp 30'→1' trong app chạy thật** (đề xuất "đặt interval nhỏ để chứng minh reload+đọc lặp lại"): cần một phiên **đã đăng nhập Shopee thật** + UI Avalonia đang chạy, mà môi trường không có tài khoản thật và không có harness tự động hóa UI. Vì vậy `OrderIntervalMin` **giữ nguyên 30** trong code (không tạm đổi rồi quên revert). Việc vòng lặp gọi lại `ReadToShipCountAsync` mỗi chu kỳ (advance `nextOrderCheck` theo 30'/30s) **chỉ kiểm bằng đọc code**, không chạy live.
- Smoke đọc số chạy trên **HTML tĩnh mô phỏng** đúng cấu trúc, KHÔNG phải trang `banhang.shopee.vn` thật; selector khớp HTML người dùng cung cấp nhưng chưa xác minh trên trang production live.

### Quyết định phát sinh
- `IsToShipDesc` chuẩn hóa khoảng trắng + so khớp ignore-case (thay vì `==` thô) cho bền với whitespace/hoa-thường của label — vẫn đúng tinh thần plan.
- `ParseToShipCount` với chuỗi số tràn `int` → trả `null` (không ném), có test riêng.
- Fallback href kiểm được nhờ case desc đổi label trong smoke.
