# Plan: Sửa lỗi không tick được ô "Đặt làm địa chỉ lấy hàng" (handle stale + modal để mở)

- **Ngày:** 2026-07-15
- **Trạng thái:** hoàn thành (code; smoke live chờ người dùng — cần phiên Shopee đăng nhập thật)
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)
- **Nghiệm thu:** Fable tự chạy build (0/0, `-p:Deterministic=false`) + `dotnet test` (**279/279 pass**);
  đọc toàn bộ diff 3 file đối chiếu đặc tả; chạy panel rà soát đối kháng (3 góc nhìn × phản biện 2/3
  phiếu) → **0 lỗi thật** (1 nghi vấn mức thấp về cửa sổ hủy giữa "modal mở" và khối try bị bác 3/3 vì
  chỉ xảy ra khi phiên đang bị Dừng, modal treo tạm vô hại). Đạt.

## 1. Bối cảnh & lỗi thực tế

Sau khi bản trước sửa xong việc điều hướng, luồng "Xử lý đơn" đã **mở đúng Cài đặt vận chuyển → tab Địa
Chỉ → bấm Sửa → modal "Sửa Địa chỉ" hiện đúng**. Nhưng bước tiếp — tick ô **"Đặt làm địa chỉ lấy hàng"**
— thất bại: app báo *"Không đặt được địa chỉ lấy hàng (Thanh Hóa) — kiểm tra tay trong cửa sổ Brave."*.
Người dùng xác nhận: **"không click được mặc dù mở popup đúng"**, và **modal vẫn đang mở** (người dùng
dán được DOM modal).

### Nguyên nhân gốc (Fable đối chiếu code 15/7)

`SetPickupAddressAsync` trong `src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs` (class lồng
`LoginSession`), khối bước 5 (dòng ~1426–1461):

1. Bắt cặp handle `(label, input)` checkbox MỘT lần (dòng 1427) rồi **dùng lại qua nhiều `await`** (đọc
   `input.IsCheckedAsync()`, click `label`, đọc lại `IsCheckedAsync`, click lại...).
2. **Modal chứa Google Map load bất đồng bộ** (DOM người dùng có `#googlemaps`, iframe, canvas — map tự
   tải sau khi modal mở). Khi map tải xong / form re-layout, **Vue vẽ lại phần form → handle `label`/
   `input` cũ rời DOM (detached)**.
3. Thao tác trên handle detached → `BoundingBoxAsync()` / `IsCheckedAsync()` / `InnerTextAsync()` **ném
   exception**. `HumanMoveToAsync` và `TryHumanClickVisibleAsync` **không** bọc try quanh `BoundingBoxAsync`
   → exception xuyên thẳng lên **`catch` ngoài cùng** của `SetPickupAddressAsync` (dòng 1481) → `return
   false` **KHÔNG gọi `HumanCancelModalAsync`** → **modal để nguyên mở** (đúng hiện tượng) + StatusText
   báo lỗi chung chung.

Bằng chứng đối chiếu: mọi nhánh "return false" HIỀN (dòng 1431/1446/1458/1467/1474) đều Hủy modal TRƯỚC
khi thoát. Modal còn mở ⇒ đã đi vào `catch` ngoài cùng ⇒ có exception ⇒ khớp giả thuyết handle stale.

Phụ (khả năng thấp hơn nhưng cùng hướng fix): kể cả không stale, đọc trạng thái "đã tick chưa" qua handle
`input` bắt sẵn sau khi click (Vue re-render input) dễ sai; và click vào cả thẻ `<label>` có ô input
`opacity:0` phủ lên khiến hit-test kém rõ ràng.

### DOM thật (người dùng cung cấp 15/7, phần cần)

```html
<div class="seller-address-common-check-box">
  <label class="eds-checkbox">
    <input type="checkbox" class="eds-checkbox__input" value="Đặt làm địa chỉ mặc đinh"> …
    <span class="eds-checkbox__label">Đặt làm địa chỉ mặc đinh</span></label>
  <label class="eds-checkbox">
    <input type="checkbox" class="eds-checkbox__input" value="Đặt làm địa chỉ lấy hàng"> …
    <span class="eds-checkbox__label">Đặt làm địa chỉ lấy hàng</span></label>
  <label class="eds-checkbox">
    <input type="checkbox" class="eds-checkbox__input" value="Đặt làm địa chỉ trả hàng"> …
    <span class="eds-checkbox__label">Đặt làm địa chỉ trả hàng</span></label>
</div>
```

Footer modal: `<div class="eds-modal__footer"> … <button …primary>Lưu</button> <button>Hủy</button>`.
Lưu ý chính tả Shopee: ô mặc định ghi **"Đặt làm địa chỉ mặc đinh"** (thiếu dấu) — helper
`IsSetPickupCheckboxText` đã khớp đúng "đặt làm địa chỉ lấy hàng", KHÔNG đụng cái này.

## 2. Phạm vi

- **Làm:**
  - Chống handle stale: **re-query tươi** checkbox (label + input + text span) NGAY TRƯỚC mỗi lần dùng;
    đọc "đã tick chưa" bằng JS eval trên phần tử vừa query (không giữ handle qua re-render).
  - **Bọc phòng thủ**: mọi lỗi/không-làm-được ở bước checkbox → **luôn Hủy modal** (không để modal mở);
    tách bước checkbox có try riêng.
  - **Click vào text span** `span.eds-checkbox__label` (mục tiêu lớn, rõ, hit-test sạch) thay vì cả `<label>`.
  - **Chờ modal ổn định** trước khi thao tác (map load xong / form re-render xong).
  - **Kết quả phân biệt bước lỗi** (enum `SetPickupResult`) để `AccountSession` báo StatusText đúng bước
    (để lần chạy live sau chỉ ra chính xác chỗ hỏng nếu vẫn lỗi).
  - Làm `HumanCancelModalAsync` bền: re-find nút Hủy tươi, không dựa handle cũ.
- **Không làm:**
  - KHÔNG đụng luồng điều hướng (đã sửa ở commit `4132d36`), luồng login, đọc số đơn.
  - KHÔNG dùng `ElementHandle.ClickAsync`/`FillAsync`/`Mouse.ClickAsync` thẳng; KHÔNG native `input.check()`
    hay dispatch event máy — ràng buộc like-human số 1 (click vẫn qua `HumanMoveAndClickVerifiedAsync`).
  - KHÔNG vá JS stealth.
  - KHÔNG tự commit (Fable commit sau nghiệm thu).

## 3. Các bước thực hiện

### Bước 1 — Enum kết quả đặt địa chỉ lấy hàng (file mới)

Tạo `src/XuLyDonShopee.Core/Services/SetPickupResult.cs` — XML-doc tiếng Việt:

```csharp
/// <summary>Kết quả đặt "địa chỉ lấy hàng" theo tỉnh — phân biệt bước hỏng để app báo đúng.</summary>
public enum SetPickupResult
{
    /// <summary>Đã là địa chỉ lấy hàng sẵn, hoặc đã tick + Lưu thành công.</summary>
    Ok,
    /// <summary>Không có trang/phiên hoặc lỗi bất ngờ.</summary>
    Failed,
    /// <summary>Không thấy địa chỉ nào khớp tỉnh trong danh sách.</summary>
    AddressNotFound,
    /// <summary>Bấm "Sửa" nhưng modal "Sửa Địa chỉ" không mở (shop khóa sửa?).</summary>
    EditModalNotOpened,
    /// <summary>Modal mở nhưng không thấy ô "Đặt làm địa chỉ lấy hàng".</summary>
    CheckboxNotFound,
    /// <summary>Thấy ô nhưng click không tick được sau vài lần (bị che / hit-test loại / re-render).</summary>
    CheckboxClickFailed,
    /// <summary>Đã tick nhưng bấm "Lưu" không được / modal không đóng.</summary>
    SaveFailed,
}
```

### Bước 2 — `SetPickupAddressAsync` đổi trả về + chống stale + luôn-Hủy-khi-lỗi

`ShopeeLoginService.cs`:

- Interface `ILoginSession`: đổi `Task<bool> SetPickupAddressAsync(string province, CancellationToken ct
  = default)` → `Task<SetPickupResult> SetPickupAddressAsync(...)`; cập nhật XML-doc. (Grep xác nhận
  KHÔNG có stub `ILoginSession` trong Tests — chỉ `LoginSession` implement; call site duy nhất là
  `AccountSession.ProcessOrdersAsync`.)
- Trong `LoginSession.SetPickupAddressAsync` sửa các nhánh return theo enum:
  - `page` null / catch ngoài cùng → `Failed`.
  - Không thấy địa chỉ khớp tỉnh → `AddressNotFound`.
  - Đã có tag "địa chỉ lấy hàng" sẵn → `Ok`.
  - Bấm Sửa không được / modal không mở → `EditModalNotOpened`.
  - Không thấy checkbox → `CheckboxNotFound` (đã Hủy modal).
  - Click checkbox không tick được → `CheckboxClickFailed` (đã Hủy modal).
  - Lưu không được / modal không đóng → `SaveFailed`.
- **Khối checkbox (bước 5) viết lại theo hướng re-query tươi + luôn Hủy khi lỗi:**
  1. Thêm helper `FindPickupCheckboxLabelAsync(IElementHandle modal)` trả về **chỉ `label` (IElementHandle?)**
     của checkbox "Đặt làm địa chỉ lấy hàng" (duyệt `label.eds-checkbox` khớp text span, như hiện tại).
  2. Thêm helper `IsPickupCheckedAsync(IElementHandle modal)` → `bool?`: re-query label tươi rồi
     `label.EvaluateAsync<bool>("l => l.querySelector('input.eds-checkbox__input')?.checked === true")`.
     Nuốt lỗi (label detached) → `null` (không rõ). Đọc trực tiếp DOM sống nên không dính handle cũ.
  3. Thêm helper `FindPickupClickTargetAsync(IElementHandle modal)` → text span
     `label.eds-checkbox span.eds-checkbox__label` của đúng ô cần tick (mục tiêu click: lớn, rõ,
     hit-test sạch). Không thấy → null.
  4. Luồng:
     - Chờ ổn định: poll `FindPickupCheckboxLabelAsync` tới khi thấy label **hai lần liên tiếp** (cách
       ~400ms) hoặc hết deadline ~8s — để map/form re-render xong mới thao tác. Không thấy →
       `CheckboxNotFound` (Hủy modal).
     - Đọc `IsPickupCheckedAsync`: `true` → đã tick sẵn → Hủy modal, `Ok` (không đổi gì). (null → coi như
       chưa tick, tiếp tục.)
     - Vòng tick tối đa 3 lần: re-query text span tươi → `TryHumanClickVisibleAsync(page, span, …)` →
       chờ `rng.Next(300, 900)` → đọc lại `IsPickupCheckedAsync`; `true` → thoát vòng thành công. Hết 3
       lần vẫn chưa `true` → `CheckboxClickFailed` (Hủy modal).
     - Tick xong → bước 6 (Lưu) như cũ nhưng theo enum: nút Lưu không thấy/không click được, hoặc
       `WaitEditAddressModalClosedAsync` hết giờ → `SaveFailed` (Hủy modal nếu còn mở); đóng được → `Ok`.
  5. **Bọc toàn khối sau khi modal mở trong try/finally**: `finally` — nếu kết quả KHÔNG phải `Ok` và
     modal còn mở thì gọi `HumanCancelModalAsync` (best-effort, nuốt lỗi). Đảm bảo **không bao giờ để
     modal mở** khi thất bại, kể cả khi có exception (catch ngoài cùng → `Failed` cũng qua finally này).
     Ghi chú: các helper đọc/ click đều đã graceful, nhưng finally là chốt chặn cuối.
- `TryHumanClickVisibleAsync` / `HumanMoveToAsync`: **bọc `BoundingBoxAsync()` bằng try** (detached →
  coi như không có box → `Clicked=false`, KHÔNG ném). Đây là điểm khiến exception rò lên catch ngoài;
  sửa để lỗi handle luôn biến thành "không click được" graceful thay vì văng lên.

### Bước 3 — `AccountSession.ProcessOrdersAsync` báo đúng bước lỗi (bước 2 của luồng)

`src/XuLyDonShopee.App/Services/AccountSession.cs` — đoạn gọi `SetPickupAddressAsync` (dòng ~214):

```csharp
var pick = await s.SetPickupAddressAsync(province, tok).ConfigureAwait(false);
StatusText = pick switch
{
    SetPickupResult.Ok => $"Đã đặt địa chỉ lấy hàng: {province}.",
    SetPickupResult.AddressNotFound =>
        $"Không thấy địa chỉ ở {province} trong danh sách — kiểm tra tay trong cửa sổ Brave.",
    SetPickupResult.EditModalNotOpened =>
        $"Mở được danh sách nhưng không sửa được địa chỉ ({province}) — shop có thể bị khóa sửa, kiểm tra tay.",
    SetPickupResult.CheckboxNotFound =>
        $"Mở được ô Sửa địa chỉ nhưng không thấy mục \"Đặt làm địa chỉ lấy hàng\" — kiểm tra tay trong Brave.",
    SetPickupResult.CheckboxClickFailed =>
        $"Không tick được \"Đặt làm địa chỉ lấy hàng\" ({province}) — kiểm tra tay trong cửa sổ Brave.",
    SetPickupResult.SaveFailed =>
        $"Đã tick nhưng chưa Lưu được địa chỉ lấy hàng ({province}) — kiểm tra tay trong cửa sổ Brave.",
    _ => $"Không đặt được địa chỉ lấy hàng ({province}) — kiểm tra tay trong cửa sổ Brave.",
};
return pick == SetPickupResult.Ok;
```

(Thêm `using XuLyDonShopee.Core.Services;` nếu chưa có — file này đã dùng `ShippingNavResult` nên chắc đã có.)

### Bước 4 — KHÔNG quên like-human

Click checkbox/Lưu/Hủy vẫn qua `HumanMoveAndClickVerifiedAsync` (chuột cong, hit-test, down→trễ→up),
có dừng ngẫu nhiên giữa các bước. Re-query chỉ là tìm lại phần tử — không thêm thao tác máy lộ liễu.
Tuyệt đối không native check/dispatch.

### Bước 5 — Test + build

- **Baseline:** chạy `dotnet test` ghi số pass hiện tại (nền 279 sau commit `4132d36` — xác nhận lại).
- Enum `SetPickupResult` không có parser/logic thuần đáng test (khác `LinkReadiness` có parse chuỗi JS);
  KHÔNG viết test hình thức. Nếu Opus tách được logic thuần nào thì thêm; không thì thôi.
- `dotnet build XuLyDonShopee.sln -c Debug` 0/0; `dotnet test` toàn bộ pass (không đỏ 279 nền). WDAC chặn
  → build lại `-p:Deterministic=false` rồi `dotnet test --no-build`.
- Grep sau sửa: không có `.ClickAsync(`/`.FillAsync(`/`Mouse.ClickAsync(`/`.CheckAsync(` mới cho nghiệp
  vụ; click checkbox/Lưu/Hủy đều qua `TryHumanClickVisibleAsync` → verified.

## 4. Tiêu chí nghiệm thu

- [ ] Handle checkbox được **re-query tươi** trước mỗi lần dùng; đọc trạng thái tick bằng JS eval trên
      phần tử vừa query (không giữ handle qua re-render map/form).
- [ ] `BoundingBoxAsync` trong `HumanMoveToAsync`/`TryHumanClickVisibleAsync` được bọc try → handle
      detached biến thành "không click được" graceful, KHÔNG ném lên catch ngoài.
- [ ] **Mọi nhánh thất bại đều Hủy modal** (finally chốt chặn) — không bao giờ để modal "Sửa Địa chỉ"
      mở treo.
- [ ] Click vào text span "Đặt làm địa chỉ lấy hàng"; tick tối đa 3 lần, kiểm lại bằng DOM sống.
- [ ] `ProcessOrdersAsync` báo StatusText PHÂN BIỆT bước lỗi (địa chỉ không thấy / modal không mở /
      không thấy ô / không tick được / chưa Lưu được).
- [ ] Like-human giữ nguyên; không ClickAsync/FillAsync/native-check thẳng.
- [ ] Build 0/0; toàn bộ test cũ pass (baseline 279).
- [ ] Chỉ sửa: `ShopeeLoginService.cs`, `AccountSession.cs`, `SetPickupResult.cs` (mới).

## 5. Rủi ro & lưu ý

- **Giả thuyết stale-handle là suy luận từ "modal để mở + có exception", chưa smoke live** — nên fix
  theo hướng phòng thủ NHIỀU lớp (re-query + bọc box + finally-Hủy + status granular). Kể cả nếu nguyên
  nhân thật là hit-test hay vị trí bị footer che, các lớp này vẫn xử lý hoặc chỉ ra đúng bước để lần sau
  chẩn. Status granular chính là cách "nhìn thấy" bước hỏng ở lần chạy live kế.
- **Không smoke live được** (môi trường Opus không có phiên Shopee thật) → Opus KHÔNG claim đã chạy live;
  phủ bằng build + full test + đọc code. Fable nhờ người dùng smoke sau merge (bấm "Xử lý đơn", xem
  StatusText mới cho biết bước nào).
- Google Map trong modal có thể mất vài giây; poll "thấy label 2 lần liên tiếp" + deadline ~8s là để né
  đúng cửa sổ re-render; đừng đặt deadline quá ngắn.
- Shopee đổi DOM bất kỳ lúc nào → mọi selector graceful, trả enum tương ứng, KHÔNG ném xuyên phiên.
- WDAC/ISG khi test như các plan trước.

---

## Báo cáo thực thi (Opus điền — 2026-07-15)

### Baseline trước khi sửa
- `dotnet build XuLyDonShopee.sln -c Debug -p:Deterministic=false` → **Build succeeded, 0 Warning, 0 Error**.
- `dotnet test --no-build` → **Passed! Failed: 0, Passed: 279, Skipped: 0** (đúng nền 279).

### Đã hoàn thành (từng bước)

**Bước 1 — Enum kết quả (file mới)**
- Tạo `src/XuLyDonShopee.Core/Services/SetPickupResult.cs` với enum `SetPickupResult` gồm 7 giá trị
  (`Ok, Failed, AddressNotFound, EditModalNotOpened, CheckboxNotFound, CheckboxClickFailed, SaveFailed`) —
  đúng nguyên văn plan, XML-doc tiếng Việt.

**Bước 2 — `SetPickupAddressAsync` đổi trả về + chống stale + luôn-Hủy-khi-lỗi**
(`src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs`)
- Interface `ILoginSession.SetPickupAddressAsync`: đổi `Task<bool>` → `Task<SetPickupResult>`, cập nhật
  XML-doc liệt kê từng nhánh enum.
- Viết lại thân `LoginSession.SetPickupAddressAsync`:
  - Các nhánh sớm trả enum: page null/catch ngoài → `Failed`; không thấy địa chỉ → `AddressNotFound`; đã có
    tag lấy hàng → `Ok`; nút Sửa không thấy / click Sửa không ăn / modal không mở → `EditModalNotOpened`.
  - Khối checkbox (bước 5) bọc trong `try { … } finally { … }`: biến `result` (mặc định `Failed`); mọi nhánh
    set `result` rồi `return result`. `finally` — nếu `result != Ok` thì **re-find modal tươi** qua
    `FindEditAddressModalAsync(page)`, còn mở thì `HumanCancelModalAsync` (best-effort, nuốt lỗi) → chốt chặn
    "không bao giờ để modal treo", kể cả khi exception rơi lên catch ngoài (catch ngoài chạy SAU finally).
  - Luồng checkbox: (5a) `WaitPickupCheckboxStableAsync` chờ thấy label **2 lần liên tiếp** cách ~400ms,
    deadline 8s → không thấy = `CheckboxNotFound`; (5b) `IsPickupCheckedAsync == true` → Hủy inline + `Ok`;
    (5c) vòng tick tối đa 3 lần: re-query text span tươi → `TryHumanClickVisibleAsync` → chờ 300–900ms → đọc
    lại bằng DOM sống, hết 3 lần chưa tick = `CheckboxClickFailed`; (6) Lưu: nút không thấy / click không ăn /
    modal không đóng sau 15s = `SaveFailed`, đóng được = `Ok`.
  - Helper mới thay `FindPickupCheckboxAsync` (đã xóa vì thành dead code):
    `FindPickupCheckboxLabelAsync(modal)` (label tươi mỗi lần gọi), `IsPickupCheckedAsync(modal)` → `bool?`
    (re-query label tươi rồi `label.EvaluateAsync<bool>("l => l.querySelector('input.eds-checkbox__input')?.checked === true")`
    — đọc DOM sống, nuốt lỗi → null), `FindPickupClickTargetAsync(modal)` → `span.eds-checkbox__label` (mục
    tiêu click), `WaitPickupCheckboxStableAsync(modal, timeoutMs, ct)`.
  - Bọc `BoundingBoxAsync()` bằng try: trong `HumanMoveToAsync` (khai báo `ElementHandleBoundingBoxResult? box`
    trong try/catch → null khi detached) và trong `TryHumanClickVisibleAsync` (qua helper mới
    `HasBoundingBoxAsync` — try/catch → false khi detached). Đây là điểm khiến exception rò lên catch ngoài
    trước đây; nay lỗi handle biến thành "không click được" graceful.

**Bước 3 — `AccountSession.ProcessOrdersAsync` báo đúng bước lỗi**
(`src/XuLyDonShopee.App/Services/AccountSession.cs`)
- Thay `okPickup` bằng `pick` + `switch` trên `SetPickupResult` sinh StatusText phân biệt: địa chỉ không thấy /
  không sửa được / không thấy ô / không tick được / chưa Lưu được / mặc định. `return pick == SetPickupResult.Ok`.
  File đã sẵn `using XuLyDonShopee.Core.Services;`.

**Bước 4 — like-human**: click checkbox (text span), Lưu, Hủy, Sửa đều qua `TryHumanClickVisibleAsync` →
`HumanMoveAndClickVerifiedAsync` (chuột cong, hit-test, down→trễ→up). Không thêm thao tác máy.

**Bước 5 — test/build**: không viết test hình thức cho enum (không có logic thuần parse — như plan cho phép).

### Kết quả kiểm chứng (số liệu thật)
- `dotnet build XuLyDonShopee.sln -c Debug -p:Deterministic=false` → **Build succeeded, 0 Warning, 0 Error**.
- `dotnet test XuLyDonShopee.sln -c Debug --no-build` → **Passed! Failed: 0, Passed: 279, Skipped: 0**
  (không đỏ nền 279).
- Grep API máy bị cấm trong `ShopeeLoginService.cs` (`.ClickAsync(`, `.FillAsync(`, `Mouse.ClickAsync(`,
  `.CheckAsync(`, `.DispatchEventAsync(`, `input.check(`) → **No matches**.
- Grep xác nhận không còn `IsCheckedAsync` (đọc handle cũ) và không còn `FindPickupCheckboxAsync` cũ.
- `git status` → đúng 3 file: `ShopeeLoginService.cs` (M), `AccountSession.cs` (M), `SetPickupResult.cs` (mới).

### Đối chiếu tiêu chí nghiệm thu mục 4
- [x] Re-query tươi checkbox trước mỗi lần dùng; đọc tick bằng JS eval trên phần tử vừa query.
- [x] `BoundingBoxAsync` bọc try trong cả `HumanMoveToAsync` và `TryHumanClickVisibleAsync` → detached
      graceful, không ném lên catch ngoài.
- [x] Mọi nhánh thất bại đều Hủy modal qua `finally` (chốt chặn); nhánh "đã tick sẵn" Hủy inline.
- [x] Click vào text span "Đặt làm địa chỉ lấy hàng"; tick tối đa 3 lần, kiểm lại bằng DOM sống.
- [x] `ProcessOrdersAsync` báo StatusText phân biệt bước lỗi.
- [x] Like-human giữ nguyên; không ClickAsync/FillAsync/native-check thẳng.
- [x] Build 0/0; test 279 pass (không đỏ nền).
- [x] Chỉ sửa 3 file trong plan.

### Vướng mắc / lưu ý
- **Kiểu trả về của `IElementHandle.BoundingBoxAsync()` là `ElementHandleBoundingBoxResult?`** (KHÔNG phải
  `BoundingBox`) — plan không nêu tên kiểu; lần đầu khai báo nhầm `BoundingBox?` gây 7 lỗi CS0246/CS1061, đã
  sửa đúng tên. Code cũ dùng `var` nên không lộ tên kiểu này.
- **KHÔNG smoke live trên Shopee thật** (môi trường không có phiên đăng nhập) — chỉ build + test + đối chiếu
  code, đúng ràng buộc. Cần người dùng bấm "Xử lý đơn" sau merge để xác nhận StatusText mới chỉ đúng bước hỏng.
- Giả định `modal` (`.eds-modal__box`) là container ổn định (map re-render phần form bên trong, không thay
  container) — các helper checkbox neo vào `modal` để re-query label tươi. Nếu Shopee thay cả container modal
  thì các helper trả null → `CheckboxNotFound` + finally vẫn Hủy (graceful, không treo).
