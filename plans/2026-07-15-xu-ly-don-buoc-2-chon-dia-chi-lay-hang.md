# Plan: Xử lý đơn — bước 2: duyệt địa chỉ & đặt "địa chỉ lấy hàng" theo tỉnh mặc định

- **Ngày:** 2026-07-15
- **Trạng thái:** hoàn thành (code; smoke live chờ người dùng — cần phiên Shopee đăng nhập thật)
- **Nghiệm thu:** Fable đọc code `SetPickupAddressAsync` (mọi nhánh không chắc chắn đều bấm Hủy thay vì
  Lưu; chỉ Lưu sau khi checkbox xác nhận đã tick; chỉ click khi có BoundingBox; grep không còn
  ClickAsync/FillAsync/CheckAsync thẳng) + wiring `ProcessOrdersAsync` (tỉnh null → mặc định app,
  `_navigating` bao trùm 2 bước) + tự chạy test: **269/269 pass** (thêm 58 case helper).
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)
- **Phụ thuộc:** (1) plan `2026-07-15-nut-xu-ly-don-mo-cai-dat-van-chuyen.md` (ĐÃ commit — nút Xử lý đơn
  + `OpenShippingAddressSettingsAsync` + `ShopeeShippingNav`); (2) plan
  `2026-07-15-dia-chi-lay-hang-mac-dinh.md` (trường `Account.PickupAddress` + ComboBox 3 tỉnh, mặc định
  "Thanh Hóa") — **plan này chỉ được giao SAU khi (2) đã nghiệm thu + commit**.

## 1. Bối cảnh & mục tiêu

**Yêu cầu người dùng (tiếp nối bước 1):** khi Chờ lấy hàng > 0 và đã vào tab **Địa Chỉ** (bước 1 làm
xong), tiếp tục:

1. **Duyệt qua các địa chỉ** có trong trang (danh sách `.address-list`).
2. Với mỗi địa chỉ, **kiểm tra phần "Địa chỉ"** (ô `.detail`, nhiều dòng — dòng cuối là tỉnh/thành, vd
   `"xom thang , dong quang\nPhường Đông Sơn\nTỉnh Thanh Hóa"`).
3. **So với địa chỉ lấy hàng mặc định trong app của shop** (`Account.PickupAddress`: "Hà Nội" /
   "TP Hồ Chí Minh" / "Thanh Hóa"). Nếu khớp → **bấm nút "Sửa"** của địa chỉ đó → hiện modal
   **"Sửa Địa chỉ"**.
4. Trong modal, **click checkbox "Đặt làm địa chỉ lấy hàng"**.
5. **Bấm "Lưu"** trong footer modal để chốt. *(Quyết định của Fable: tin nhắn người dùng liệt kê tới
   bước tick checkbox; tick mà không Lưu thì thao tác vô nghĩa nên chốt bấm Lưu — nếu người dùng muốn
   khác sẽ chỉnh sau.)*

**RÀNG BUỘC XUYÊN SUỐT:** mọi thao tác trên trang bán hàng phải **giống người mức cao nhất** — mọi click
qua `HumanMoveAndClickAsync`, dừng "đọc trang" ngẫu nhiên 800–2500ms giữa các bước, KHÔNG
`ClickAsync()`/`FillAsync()` thẳng.

### Hiện trạng code (sau khi plan phụ thuộc (2) đã merge)

- `ShopeeLoginService.cs` / class `LoginSession`: đã có `OpenShippingAddressSettingsAsync(ct)` (mở CĐVC
  → tab Địa Chỉ, trả bool), `HumanMoveAndClickAsync(page, el, mx, my, rng, ct)`,
  `FirstVisibleByBoxAsync(page, selector, ct)` (chỉ nhận element có BoundingBox ≠ null).
- `ShopeeShippingNav.cs`: `NormalizeUiText` + các hàm Is*. `ShopeeShippingNavTests` có sẵn.
- `AccountSession.ProcessOrdersAsync()`: bật `_navigating` → gọi `OpenShippingAddressSettingsAsync` →
  StatusText kết quả → tắt cờ. **Bước 2 nối tiếp NGAY trong hàm này.**
- `Account.PickupAddress` (string?, null = coi như "Thanh Hóa") — từ plan (2);
  `AccountsViewModel.DefaultPickupAddress == "Thanh Hóa"` (App layer, `AccountSession` dùng được).
- Nền test sau plan (2): ~205+ pass (con số chính xác xem báo cáo plan (2); không được làm đỏ test cũ).

### DOM thật người dùng cung cấp (rút gọn phần cần)

Danh sách địa chỉ (tab Địa Chỉ):

```html
<div class="address-list">
  <div class="address-item-container">
    <p class="address-title">Address 1</p>
    <div class="personal-address-item">
      <div class="content-container"><div class="content">
        <div class="content-header old-content-header">
          <span class="label">Họ &amp; Tên</span><span class="name">Alina Store</span>
          <div class="eds-tag ... address-label">Default Address</div>
          <div class="eds-tag ... address-label">Địa chỉ lấy hàng</div>   <!-- tag CHỈ có ở địa chỉ đang là địa chỉ lấy hàng -->
        </div>
        <div class="grid"><span class="label">Số điện thoại</span><span class="detail">84364632552</span></div>
        <div class="grid"><span class="label">Địa chỉ</span>
          <div class="detail">Số 76, Ngõ 76 Đường Trung Văn, Phường Đại Mỗ, Thành phố Hà Nội, Việt Nam
Phường Đại Mỗ
Thành phố Hà Nội</div></div>
      </div>
      <div class="operations">
        ...<button type="button" class="eds-button eds-button--link eds-button--normal"><span>Sửa</span></button>...
        <!-- có thể kèm button "Xóa" tương tự; nút Sửa có popover cảnh báo với shop outlet -->
      </div></div>
    </div>
  </div>
  <div class="address-item-container"> ... Address 2: detail =
      "xom thang , dong quang\nPhường Đông Sơn\nTỉnh Thanh Hóa" (KHÔNG có tag) ... </div>
</div>
```

Modal "Sửa Địa chỉ" (mở sau khi bấm Sửa):

```html
<div class="eds-modal__box"><div class="eds-modal__content eds-modal__content--large">
  <div class="eds-modal__header"><div><div><div class="title">Sửa Địa chỉ</div></div></div></div>
  <div class="eds-modal__body"> ... form ...
    <div class="seller-address-common-check-box">
      <label class="eds-checkbox"><input type="checkbox" class="eds-checkbox__input" value="Đặt làm địa chỉ mặc đinh">...<span class="eds-checkbox__label">Đặt làm địa chỉ mặc đinh</span></label>
      <label class="eds-checkbox"><input type="checkbox" class="eds-checkbox__input" value="Đặt làm địa chỉ lấy hàng">...<span class="eds-checkbox__label">Đặt làm địa chỉ lấy hàng</span></label>
      <label class="eds-checkbox">... "Đặt làm địa chỉ trả hàng" ...</label>
    </div> ... (form còn có ô Tỉnh/Phường, Địa chỉ chi tiết, Google Maps — KHÔNG đụng tới)
  </div>
  <div class="eds-modal__footer"><div class="footer">
    <button class="eds-button eds-button--normal"><span>Hủy</span></button>
    <button class="eds-button eds-button--primary eds-button--normal"><span>Lưu</span></button>
  </div></div>
</div>...</div>
```

Lưu ý: nhãn checkbox trên Shopee viết **"Đặt làm địa chỉ mặc đinh"** (thiếu dấu, "đinh" thay vì "định")
— so khớp phải theo text THẬT này; phân biệt rõ với "Đặt làm địa chỉ lấy hàng" (cái cần click).

### Quyết định đã chốt

- So khớp tỉnh trên **DÒNG CUỐI không rỗng** của `.detail` (dòng tỉnh/thành). Chuẩn hóa rồi so
  **chứa tên lõi tỉnh**: "Hà Nội" → "hà nội"; "TP Hồ Chí Minh" → "hồ chí minh" (khớp cả "Thành phố Hồ
  Chí Minh"); "Thanh Hóa" → "thanh hóa" (khớp "Tỉnh Thanh Hóa"). Detail không có xuống dòng → so trên
  toàn chuỗi (fallback).
- Địa chỉ khớp tỉnh mà **đã có tag "Địa chỉ lấy hàng"** → coi như XONG (không bấm Sửa) — StatusText
  "Địa chỉ lấy hàng đã đúng (<tỉnh>)".
- Lấy **địa chỉ khớp ĐẦU TIÊN** theo thứ tự trên trang.
- Không có địa chỉ nào khớp → dừng graceful, StatusText "Không tìm thấy địa chỉ ở <tỉnh> — kiểm tra tay",
  trả false.
- Trong modal chỉ đụng: checkbox "Đặt làm địa chỉ lấy hàng" (nếu chưa tick) + nút "Lưu". KHÔNG sửa ô
  nhập nào khác. Checkbox đã tick sẵn → không click lại (click nữa là BỎ tick), chỉ bấm Lưu... thực ra
  đã tick sẵn mà chưa có tag thì hiếm — cứ: đã tick → bấm Hủy (không đổi gì) và coi như xong? KHÔNG —
  đơn giản & an toàn: đã tick sẵn → bấm "Hủy" đóng modal, trả true (trạng thái mong muốn đã có).
- Sau khi bấm Lưu: chờ modal biến mất (deadline ~15s). Modal không đóng (lỗi form/shop bị khóa sửa) →
  trả false + StatusText "Không lưu được địa chỉ lấy hàng — kiểm tra tay" (KHÔNG tự bấm gì thêm).

## 2. Phạm vi

- **Làm:** helper thuần so khớp địa chỉ/tỉnh + text nút/checkbox/modal; method
  `SetPickupAddressAsync(province, ct)` trên `ILoginSession`/`LoginSession`; nối bước 2 vào
  `AccountSession.ProcessOrdersAsync`; unit test helper.
- **Không làm:** các bước SAU khi đặt xong địa chỉ lấy hàng (quay lại xử lý đơn… — plan sau); không đổi
  bước 1; không sửa form/modal ngoài checkbox + Lưu/Hủy; KHÔNG tự commit.

## 3. Các bước thực hiện

### Bước 1 — Mở rộng helper thuần `ShopeeShippingNav` (Core)

Thêm vào `src/XuLyDonShopee.Core/Services/ShopeeShippingNav.cs`:

- `static string ProvinceCoreName(string? appProvince)`: normalize rồi bỏ tiền tố `"tp "`, `"tp."`,
  `"thành phố "`, `"tỉnh "` nếu có → tên lõi ("hà nội", "hồ chí minh", "thanh hóa").
- `static bool AddressDetailMatchesProvince(string? detailText, string? appProvince)`:
  appProvince rỗng → false; lấy **dòng cuối không rỗng** của detailText (split `\n`), normalize, so
  `Contains(ProvinceCoreName(appProvince))`; detail 1 dòng/không tách được → so trên toàn chuỗi normalize.
- `static bool IsPickupTagText(string? s)` → normalize == `"địa chỉ lấy hàng"`.
- `static bool IsEditButtonText(string? s)` → normalize == `"sửa"`.
- `static bool IsCancelButtonText(string? s)` → normalize == `"hủy"`.
- `static bool IsSaveButtonText(string? s)` → normalize == `"lưu"`.
- `static bool IsSetPickupCheckboxText(string? s)` → normalize == `"đặt làm địa chỉ lấy hàng"`
  (KHÔNG khớp "đặt làm địa chỉ mặc đinh" / "đặt làm địa chỉ trả hàng").
- `static bool IsEditAddressModalTitle(string? s)` → normalize == `"sửa địa chỉ"`.

### Bước 2 — `ILoginSession.SetPickupAddressAsync` (Core, kiểu người)

`src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs`:

- Interface: `Task<bool> SetPickupAddressAsync(string province, CancellationToken ct = default);` —
  XML-doc: chạy khi ĐANG ở tab Địa Chỉ của Cài đặt vận chuyển; tìm địa chỉ thuộc tỉnh
  <paramref name="province"/>, nếu chưa là địa chỉ lấy hàng thì bấm Sửa → tick "Đặt làm địa chỉ lấy
  hàng" → Lưu; graceful không ném, false khi không tìm thấy/không lưu được.
- `LoginSession` implement:
  1. `page = _context.Pages[0]` (null → false); `rng`, `(mx,my)` ngẫu nhiên; dừng 800–2500ms.
  2. **Chờ danh sách địa chỉ** xuất hiện: poll `.address-list .address-item-container` (deadline ~15s).
  3. **Duyệt từng `.address-item-container`**, với mỗi item:
     - Tìm `.detail` của hàng "Địa chỉ": duyệt các `div.grid` trong item, lấy grid có `span.label`
       InnerText normalize == `"địa chỉ"` → đọc InnerText phần tử `.detail` trong grid đó. (KHÔNG lấy
       nhầm `.detail` của hàng "Số điện thoại".)
     - `AddressDetailMatchesProvince(detail, province)` → item khớp đầu tiên thì dừng duyệt.
  4. Không có item khớp → false.
  5. Item khớp: kiểm tag — duyệt `.address-label` trong item, có cái nào
     `IsPickupTagText(InnerText)` → **true, XONG** (không bấm gì).
  6. Chưa có tag → tìm nút **Sửa** trong item: duyệt `button` trong `.operations` (fallback: mọi
     `button` trong item), khớp `IsEditButtonText(InnerText)`. `ScrollIntoViewIfNeededAsync()` trước;
     **CHỈ click khi `BoundingBoxAsync() != null`** (box null → scroll rồi lấy lại box; vẫn null →
     false) — vì `HumanMoveAndClickAsync` với box null sẽ nhấn tại vị trí chuột cũ (sai chỗ). Click
     kiểu người, cập nhật `(mx,my)`.
  7. **Chờ modal "Sửa Địa chỉ"**: poll `.eds-modal__box` có `.title` khớp `IsEditAddressModalTitle`
     (deadline ~10s). Không hiện (shop bị khóa sửa địa chỉ...) → false. Dừng 800–2500ms (đọc modal).
  8. **Checkbox**: trong modal, duyệt `label.eds-checkbox`, lấy label có `span.eds-checkbox__label`
     khớp `IsSetPickupCheckboxText`. Đọc trạng thái qua `input.eds-checkbox__input` bên trong:
     `IsCheckedAsync()`.
     - Đã tick sẵn → bấm nút **Hủy** (footer, `IsCancelButtonText`) kiểu người, chờ modal đóng → true.
     - Chưa tick → click kiểu người vào label/indicator; dừng 300–900ms; (best-effort kiểm lại
       `IsCheckedAsync()` — nếu vẫn chưa tick, thử click 1 lần nữa; vẫn không được → bấm Hủy, false).
  9. **Lưu**: tìm trong `.eds-modal__footer` nút `button.eds-button--primary` (fallback: button có span
     khớp `IsSaveButtonText`) → click kiểu người.
  10. **Chờ modal đóng** (selector `.eds-modal__box` + title "Sửa Địa chỉ" biến mất, deadline ~15s) →
      true. Không đóng → false.
  - Toàn bộ bọc try/catch trả false; mọi click qua `HumanMoveAndClickAsync`; KHÔNG ClickAsync thẳng.

### Bước 3 — Nối vào `AccountSession.ProcessOrdersAsync`

`src/XuLyDonShopee.App/Services/AccountSession.cs` — trong `try` sau khi
`OpenShippingAddressSettingsAsync` trả `true`:

- Đọc tỉnh: `var acc = _services.Accounts.GetById(_accountId);`
  `var province = string.IsNullOrWhiteSpace(acc?.PickupAddress) ? AccountsViewModel.DefaultPickupAddress : acc!.PickupAddress!;`
  — nếu tham chiếu `AccountsViewModel` từ Services gây vướng (App layer cùng assembly nên OK), dùng
  hằng cục bộ `"Thanh Hóa"` kèm comment trỏ về VM.
- `StatusText = $"Đang chọn địa chỉ lấy hàng ({province})...";`
- `var okPickup = await s.SetPickupAddressAsync(province, tok).ConfigureAwait(false);`
- StatusText cuối: thành công → `$"Đã đặt địa chỉ lấy hàng: {province}."`; thất bại →
  `$"Không đặt được địa chỉ lấy hàng ({province}) — kiểm tra tay trong cửa sổ Brave."`. Trả về
  `okPickup`.
- Giữ nguyên `_navigating` bao trùm cả bước 2 (finally tắt).

### Bước 4 — Test + build

- `src/XuLyDonShopee.Tests/ShopeeShippingNavTests.cs` mở rộng:
  - `ProvinceCoreName`: 3 option app; có/không tiền tố; null/rỗng.
  - `AddressDetailMatchesProvince`: detail 3 dòng Thanh Hóa (đúng ví dụ người dùng) khớp "Thanh Hóa",
    KHÔNG khớp "Hà Nội"; detail Hà Nội (dòng cuối "Thành phố Hà Nội") khớp "Hà Nội"; "Thành phố Hồ Chí
    Minh" khớp "TP Hồ Chí Minh"; detail 1 dòng chứa tỉnh → khớp (fallback); null/rỗng → false;
    **bẫy dòng đầu chứa tên tỉnh khác** (vd dòng 1 có "...Thành phố Hà Nội, Việt Nam" nhưng dòng cuối
    "Tỉnh Thanh Hóa" → chỉ khớp "Thanh Hóa", không khớp "Hà Nội").
  - `IsSetPickupCheckboxText`: đúng nhãn thật; KHÔNG khớp "Đặt làm địa chỉ mặc đinh" / "trả hàng".
  - `IsPickupTagText`/`IsEditButtonText`/`IsSaveButtonText`/`IsCancelButtonText`/`IsEditAddressModalTitle`.
- Build 0/0; `dotnet test` toàn bộ pass. WDAC → rebuild `--no-incremental -p:Deterministic=false` +
  `dotnet test --no-build`.

## 4. Tiêu chí nghiệm thu

- [ ] Bấm "Xử lý đơn": sau khi tab Địa Chỉ mở, app duyệt địa chỉ, chọn đúng địa chỉ có dòng tỉnh khớp
      `PickupAddress` của tài khoản; đã có tag "Địa chỉ lấy hàng" → không đụng gì; chưa có → Sửa →
      tick "Đặt làm địa chỉ lấy hàng" → Lưu → chờ modal đóng.
- [ ] Mọi click qua `HumanMoveAndClickAsync` + dừng ngẫu nhiên giữa bước; không ClickAsync thẳng;
      KHÔNG click khi BoundingBox null (soi code).
- [ ] Graceful mọi nhánh (không thấy địa chỉ khớp / modal không mở / checkbox không tick được / modal
      không đóng) → false + StatusText rõ ràng, phiên không chết.
- [ ] Test helper mới pass; toàn bộ test pass; build 0/0.
- [ ] Chỉ sửa: `ShopeeShippingNav.cs`, `ShopeeLoginService.cs`, `AccountSession.cs`,
      `ShopeeShippingNavTests.cs`.

## 5. Rủi ro & lưu ý

- **Bấm "Lưu" là thao tác GHI lên shop Shopee thật** — quyết định đã ghi ở mục 1; mọi nhánh không chắc
  chắn phải nghiêng về KHÔNG bấm gì thêm + báo StatusText.
- Nút Sửa của shop outlet có thể bị khóa ("cannot update address") → modal không mở → false graceful.
- `.detail` xuất hiện ở cả hàng "Số điện thoại" — PHẢI lọc theo `span.label == "Địa chỉ"`.
- Nhãn Shopee "Đặt làm địa chỉ mặc đinh" (chính tả thật) — đừng "sửa lỗi" khi so khớp; chỉ khớp đúng
  "đặt làm địa chỉ lấy hàng".
- Danh sách địa chỉ có thể phải cuộn — scroll trước, chỉ click khi có BoundingBox.
- WDAC như các plan trước. Smoke live cần tài khoản thật → không claim; người dùng smoke sau merge.

---

## Báo cáo thực thi (Opus điền sau khi xong)

**Ngày thực thi:** 2026-07-15 · **Kết quả:** Hoàn thành toàn bộ 4 bước. Build 0/0, test 269/269 pass
(baseline 211 + 58 case mới).

### Đã hoàn thành

- **Bước 1 — helper thuần** (`src/XuLyDonShopee.Core/Services/ShopeeShippingNav.cs`): thêm
  `ProvinceCoreName`, `AddressDetailMatchesProvince`, `IsPickupTagText`, `IsEditButtonText`,
  `IsCancelButtonText`, `IsSaveButtonText`, `IsSetPickupCheckboxText`, `IsEditAddressModalTitle`. Khớp
  tỉnh trên DÒNG CUỐI không rỗng của `.detail` (tách `\n`, chuẩn hóa, `Contains(ProvinceCoreName)`);
  detail 1 dòng → fallback toàn chuỗi.
- **Bước 2 — `SetPickupAddressAsync`** (`src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs`): thêm
  method vào `ILoginSession` (XML-doc) + implement trong `LoginSession`. Luồng đúng plan: chờ danh sách
  địa chỉ (poll 15s) → tìm địa chỉ khớp tỉnh đầu tiên → có tag "Địa chỉ lấy hàng" thì trả true (không
  đụng) → chưa có thì bấm Sửa → chờ modal "Sửa Địa chỉ" (10s) → checkbox "Đặt làm địa chỉ lấy hàng":
  đã tick → Hủy + true; chưa tick → click kiểu người (retry 1 lần) → bấm Lưu → chờ modal đóng (15s) →
  true. Mọi click qua helper mới `TryHumanClickVisibleAsync` (scroll + CHỈ click khi
  `BoundingBoxAsync() != null`, box null → không click) → gọi `HumanMoveAndClickAsync` sẵn có. Các nhánh
  thoát an toàn khỏi modal đều bấm Hủy (`HumanCancelModalAsync`) rồi false; sau khi đã bấm Lưu mà modal
  không đóng → false, KHÔNG bấm thêm. Toàn bộ bọc try/catch → false. Đọc `.detail` lọc đúng
  `span.label == "địa chỉ"` (không lấy nhầm hàng "Số điện thoại").
- **Bước 3 — nối vào `AccountSession.ProcessOrdersAsync`** (`src/XuLyDonShopee.App/Services/AccountSession.cs`):
  thêm `using XuLyDonShopee.App.ViewModels;`; sau khi `OpenShippingAddressSettingsAsync` trả true → đọc
  `acc.PickupAddress` (null/rỗng → `AccountsViewModel.DefaultPickupAddress`) → StatusText "Đang chọn địa
  chỉ lấy hàng ({province})..." → gọi `SetPickupAddressAsync` → StatusText cuối theo kết quả → trả
  `okPickup`. `_navigating` (finally tắt) bao trùm cả 2 bước.
- **Bước 4 — test** (`src/XuLyDonShopee.Tests/ShopeeShippingNavTests.cs`): thêm 58 case cho 8 helper mới,
  gồm ví dụ Thanh Hóa 3 dòng của người dùng, bẫy "dòng đầu chứa tỉnh khác", "Thành phố Hồ Chí Minh" ↔
  "TP Hồ Chí Minh", và phân biệt "đặt làm địa chỉ mặc đinh"/"trả hàng" khỏi "lấy hàng".

### Kết quả kiểm chứng (số liệu thật)

- `dotnet build XuLyDonShopee.sln -c Debug` → **Build succeeded, 0 Warning, 0 Error**.
- Rebuild test WDAC: `dotnet build src/XuLyDonShopee.Tests -c Debug --no-incremental -p:Deterministic=false`
  → 0/0.
- `dotnet test src/XuLyDonShopee.Tests --no-build` → **Failed: 0, Passed: 269, Skipped: 0** (~1s).
- `git status --short`: đúng 4 file trong phạm vi plan (ShopeeShippingNav.cs, ShopeeLoginService.cs,
  AccountSession.cs, ShopeeShippingNavTests.cs). Không commit.
- Grep xác nhận KHÔNG có `ClickAsync/FillAsync/CheckAsync` thẳng trong Core service (chỉ còn
  `Keyboard.TypeAsync` cũ của luồng login).

### Vướng mắc / bỏ dở

- Không có. Smoke live cần tài khoản Shopee thật → KHÔNG chạy được trong môi trường này, KHÔNG claim; cần
  người dùng smoke sau merge (bấm "Xử lý đơn" trên tài khoản có Chờ lấy hàng > 0).

### Điểm lệch / lưu ý cho kiến trúc sư

- **StatusText nhánh false gộp chung:** vì `SetPickupAddressAsync` trả `bool`, tầng App (plan Bước 3) set
  một StatusText chung "Không đặt được địa chỉ lấy hàng ({province}) — kiểm tra tay..." cho MỌI nhánh
  false (không tìm thấy địa chỉ / modal không mở / checkbox không tick / Lưu không đóng). Mục "Quyết định
  đã chốt" mô tả các câu StatusText riêng theo nhánh ("Không tìm thấy địa chỉ ở <tỉnh>...", "Không lưu
  được..."), nhưng Bước 3 (đặc tả cụ thể) chỉ định câu chung — đã theo Bước 3 để giữ tách tầng
  (Core không set StatusText của App). Nếu muốn thông báo chi tiết theo nhánh, cần đổi kiểu trả về thành
  enum/kết quả giàu hơn ở plan sau.
