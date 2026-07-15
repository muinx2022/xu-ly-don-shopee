# Plan: Sửa lỗi "Xử lý đơn" click nhầm link khác — hit-test elementFromPoint trước khi nhả click

- **Ngày:** 2026-07-15
- **Trạng thái:** đang làm
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)
- **Nghiệm thu:** (Fable điền sau)

## 1. Bối cảnh & lỗi thực tế

Người dùng bấm nút **"Xử lý đơn"** → app báo *"Không mở được Cài đặt vận chuyển — thao tác tay trong
cửa sổ Brave."*. Quan sát thực tế trong Brave: **chuột có chạy và click, nhưng click trúng MỘT LINK
SHOPEE KHÁC** (không phải "Cài Đặt Vận Chuyển"), trang chuyển sang chỗ khác → điều hướng thất bại.
Người dùng nhận định: khi nhóm "Quản Lý Đơn Hàng" được bung ra thì khả năng click sẽ trúng.

### Nguyên nhân gốc (Fable đã đối chiếu code 15/7)

Luồng hiện tại (`src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs`, class lồng `LoginSession`):

1. `FindShippingLinkAsync` tìm link ĐÚNG bằng href/test-id/text — phần tìm không sai.
2. Điều kiện "đang hiển thị" chỉ là `BoundingBoxAsync() != null` (cả ở `FirstVisibleByBoxAsync` lẫn
   `TryHumanClickVisibleAsync`). **Không đủ**: phần tử trong submenu bị CỤP (accordion kiểu
   `max-height:0; overflow:hidden`) hoặc bị panel/popover đè lên **vẫn có bounding box**.
3. `HumanMoveAndClickAsync` đưa chuột tới tâm bounding box rồi `Mouse.Down/Up` **mù** — không kiểm tra
   phần tử thật sự nhận click tại điểm đó. Nếu tọa độ ấy đang thuộc về link khác (submenu cụp nên hàng
   dưới trồi lên chỗ đó; hoặc flyout `with-sidebar-panel`/popover hover đè lên) → **click trúng link
   khác** → trang đi sai chỗ.
4. Hệ quả phụ: `WaitShippingPageAsync` nhận điều kiện `page.Url` chứa `/portal/all-settings/shipping`
   **HOẶC** có `.eds-tabs__nav-tab` — trang SAI mở ra mà cũng có eds-tabs thì bị nhận nhầm là "đã mở",
   rồi fail muộn ở bước tìm tab "Địa Chỉ".
5. Hệ quả phụ 2: `AccountSession.ProcessOrdersAsync` chỉ có MỘT câu báo lỗi cho MỌI bước hỏng
   ("Không mở được Cài đặt vận chuyển") → không chẩn đoán được lỗi ở bước nào.

### DOM thật (người dùng cung cấp 15/7, trích phần cần)

- Sidebar: `<div id="sidebar-container" class="sidebar-container with-sidebar-panel">` — chế độ có
  sidebar-panel (flyout).
- Link đích: `<a href="/portal/all-settings/shipping" class="sidebar-submenu-item-link"
  test-id="order shipping setting">…<span>Cài Đặt Vận Chuyển</span>…</a>` nằm trong
  `<li class="sidebar-menu-box ps_menu_order">` → `<ul class="sidebar-submenu with-sidebar-panel">`.
- Mỗi item submenu bọc `eds-popover` với popper `display:none` bật khi hover — popper của item KHÁC có
  thể đè lên link đích lúc chuột lướt qua; popper của CHÍNH item đích nằm BÊN TRONG thẻ `<a>` (nên
  hit-test bằng `node.contains(hit)` vẫn nhận đúng).

## 2. Phạm vi

- **Làm:**
  - Primitive click mới có **hit-test `document.elementFromPoint`** ngay trước khi nhả click
    (`HumanMoveAndClickVerifiedAsync`) — vẫn 100% kiểu người (chuột cong, down→trễ→up, chờ ngẫu nhiên).
  - Dùng primitive mới cho MỌI click nghiệp vụ trong `OpenShippingAddressSettingsAsync` và
    `SetPickupAddressAsync` (qua việc đổi ruột `TryHumanClickVisibleAsync`).
  - Khi hit-test fail trên link Cài Đặt Vận Chuyển → click (verified) mục cha "Quản Lý Đơn Hàng" để
    bung submenu rồi thử lại; đường thoát cuối vẫn là `GotoAsync` (đã có).
  - Siết `WaitShippingPageAsync` chỉ nhận theo URL; caller fallback `GotoAsync` một lần khi hết giờ.
  - Trả kết quả PHÂN BIỆT BƯỚC LỖI (enum mới `ShippingNavResult`) để `AccountSession` báo StatusText
    đúng bước hỏng.
- **Không làm:**
  - KHÔNG đụng luồng login (`TryHumanLoginAsync`) — `HumanMoveAndClickAsync` gốc giữ nguyên hành vi.
  - KHÔNG vá JS stealth, KHÔNG thêm `ClickAsync()`/`FillAsync()` thẳng (ràng buộc like-human số 1).
  - KHÔNG đổi `IAccountSession.ProcessOrdersAsync` (vẫn `Task<bool>`) — 2 stub test không phải sửa.
  - KHÔNG tự commit (Fable commit sau nghiệm thu).

## 3. Các bước thực hiện

### Bước 1 — Enum kết quả điều hướng (file mới)

Tạo `src/XuLyDonShopee.Core/Services/ShippingNavResult.cs` — XML-doc tiếng Việt:

```csharp
/// <summary>Kết quả điều hướng "Cài Đặt Vận Chuyển" → tab "Địa Chỉ" — phân biệt bước hỏng để app báo đúng.</summary>
public enum ShippingNavResult
{
    /// <summary>Đã mở trang cài đặt vận chuyển và tab "Địa Chỉ" đang active.</summary>
    Ok,
    /// <summary>Không có trang/phiên (Pages rỗng) hoặc lỗi bất ngờ.</summary>
    Failed,
    /// <summary>Không đưa được trình duyệt tới trang cài đặt vận chuyển (click không ăn / URL không đổi, kể cả sau fallback Goto).</summary>
    PageNotOpened,
    /// <summary>Trang cài đặt vận chuyển ĐÃ mở nhưng không tìm thấy / không bấm được tab "Địa Chỉ" (Shopee đổi giao diện?).</summary>
    AddressTabNotFound,
}
```

### Bước 2 — Primitive click có hit-test (`ShopeeLoginService.cs`, class `LoginSession`)

1. **Tách phần di chuột** từ `HumanMoveAndClickAsync` thành helper private
   `HumanMoveToAsync(page, el, mx, my, rng, ct)` → trả `(double X, double Y, bool HasBox)`:
   nguyên logic hiện có (BoundingBox → tâm + jitter; box null → `ScrollIntoViewIfNeededAsync` + giữ vị
   trí chuột, HasBox=false; đường cong `HumanMouse.GeneratePath`, 5–25ms/điểm). `HumanMoveAndClickAsync`
   gốc gọi lại helper này + Down/trễ/Up — **hành vi không đổi** (login flow giữ nguyên).
2. **Hit-test helper**:
   ```csharp
   /// <summary>True nếu tại điểm (x,y) của viewport, phần tử nhận sự kiện chính là el / con của el /
   /// tổ tiên của el (elementFromPoint trả node TRÊN CÙNG — bị phần tử khác đè thì trả phần tử đè).</summary>
   private static async Task<bool> IsPointOnElementAsync(IElementHandle el, double x, double y)
   {
       try
       {
           return await el.EvaluateAsync<bool>(
               "(node, pt) => { const hit = document.elementFromPoint(pt.x, pt.y);" +
               " return !!hit && (node === hit || node.contains(hit) || hit.contains(node)); }",
               new { x, y }).ConfigureAwait(false);
       }
       catch { return false; }
   }
   ```
   Lưu ý: chấp nhận cả `hit.contains(node)` (hit là tổ tiên) để không từ chối oan các case label/input;
   hai kịch bản lỗi thật (submenu cụp → hit thuộc nhóm khác; flyout đè → hit thuộc cây popper khác)
   đều KHÔNG phải quan hệ tổ tiên/hậu duệ nên vẫn bị chặn đúng.
3. **Primitive mới**:
   ```csharp
   private static async Task<(double X, double Y, bool Clicked)> HumanMoveAndClickVerifiedAsync(
       IPage page, IElementHandle el, double mx, double my, Random rng, CancellationToken ct)
   ```
   - `HumanMoveToAsync` tới phần tử. Nếu `HasBox == false` → thử `ScrollIntoViewIfNeededAsync` + move
     lại MỘT lần; vẫn không có box → `(mx, my, false)` (không click).
   - **Poll hit-test** `IsPointOnElementAsync(el, tx, ty)` tối đa ~2s (lặp mỗi 150–300ms ngẫu nhiên,
     chuột ĐỨNG YÊN tại đích — giống người dừng nhìn rồi mới bấm; popover hover của item khác sẽ tự tắt
     trong lúc này vì chuột không còn trên item đó).
   - Poll fail cả ~2s → **KHÔNG Down/Up**, trả `(tx, ty, false)`.
   - Poll pass → `Mouse.Down` + trễ 40–120ms + `Mouse.Up` (nguyên mẫu cũ) → `(tx, ty, true)`.
4. **Đổi ruột `TryHumanClickVisibleAsync`** (giữ nguyên chữ ký `(X, Y, Clicked)`): sau bước scroll-vào-
   tầm-nhìn hiện có, gọi `HumanMoveAndClickVerifiedAsync` thay vì `HumanMoveAndClickAsync`; `Clicked`
   lấy từ kết quả verified. → mọi call site trong `SetPickupAddressAsync` (nút Sửa, label checkbox, nút
   Lưu, kể cả `HumanCancelModalAsync` nếu nó cũng dùng — kiểm tra và đổi tương tự nếu đang gọi thẳng
   `HumanMoveAndClickAsync`) tự hưởng hit-test, không đổi chữ ký.

### Bước 3 — Luồng `OpenShippingAddressSettingsAsync` (đổi chữ ký + chống click nhầm)

- Interface `ILoginSession`: `Task<bool> OpenShippingAddressSettingsAsync(...)` →
  `Task<ShippingNavResult> OpenShippingAddressSettingsAsync(CancellationToken ct = default)`;
  cập nhật XML-doc. (Grep toàn repo: KHÔNG có stub ILoginSession nào trong test — chỉ `LoginSession`
  implement; call site duy nhất là `AccountSession.ProcessOrdersAsync` — sửa ở Bước 5.)
- Trong `LoginSession.OpenShippingAddressSettingsAsync`:
  1. `page` null → `Failed`. Giữ nguyên khởi tạo rng/mx/my + dừng đọc trang 800–2500ms.
  2. Tìm link (`FindShippingLinkAsync` giữ nguyên). Có link → `HumanMoveAndClickVerifiedAsync`.
  3. **`Clicked == false`** (điểm đang bị cụp/che — chính là lỗi thực tế):
     - Tìm mục cha `FindOrderMenuParentAsync` → click **verified**; chờ 500–1500ms; `FindShippingLinkAsync`
       lại (deadline 10s) → click verified lần nữa.
     - Vẫn không click được (hoặc không thấy link) → **fallback `GotoAsync`** (nguyên khối cũ).
  4. Link không thấy ngay từ đầu → giữ nhánh cũ (mở mục cha → tìm lại → không được thì `GotoAsync`),
     nhưng mọi click đổi sang verified.
  5. **`WaitShippingPageAsync` siết lại:** CHỈ còn điều kiện `ShopeeShippingNav.IsShippingSettingHref(page.Url)`
     — XÓA điều kiện `.eds-tabs__nav-tab` (trang khác cũng có eds-tabs → dương tính giả; `page.Url` của
     Playwright phản ánh cả đổi route SPA qua history API nên không cần điều kiện phụ). Cập nhật XML-doc.
  6. Sau click (mọi nhánh): `WaitShippingPageAsync(page, 20000, ct)`. Hết giờ mà chưa tới trang → nếu
     CHƯA từng Goto trong lượt này thì `GotoAsync` fallback MỘT lần + `WaitShippingPageAsync` lại;
     vẫn không được → `PageNotOpened`.
  7. Bước tab "Địa Chỉ": giữ tìm kiếm cũ (`FindAddressTabAsync`); tab null → `AddressTabNotFound`;
     tab đã `active` → `Ok`; chưa active → click **verified** — `Clicked == false` →
     `AddressTabNotFound`; click được → `Ok`.
  8. `catch` ngoài cùng → `Failed` (giữ nguyên tinh thần không ném).

### Bước 4 — KHÔNG quên like-human

Mọi thay đổi giữ nguyên chuỗi hành vi người: chuột cong từng điểm, dừng ngẫu nhiên giữa bước, down→trễ→up.
Hit-test poll là "người dừng nhìn rồi mới bấm" — KHÔNG thêm thao tác máy nào lộ liễu. Cấm tuyệt đối
`ElementHandle.ClickAsync`/`Mouse.ClickAsync`/`FillAsync` cho nghiệp vụ (grep xác nhận sau khi sửa).

### Bước 5 — `AccountSession.ProcessOrdersAsync` báo đúng bước lỗi

`src/XuLyDonShopee.App/Services/AccountSession.cs` (giữ chữ ký `Task<bool>`):

```csharp
var nav = await s.OpenShippingAddressSettingsAsync(tok).ConfigureAwait(false);
if (nav != ShippingNavResult.Ok)
{
    StatusText = nav switch
    {
        ShippingNavResult.PageNotOpened =>
            "Không mở được trang Cài đặt vận chuyển (click không ăn / trang không chuyển) — thao tác tay trong cửa sổ Brave.",
        ShippingNavResult.AddressTabNotFound =>
            "Đã mở Cài đặt vận chuyển nhưng không thấy tab \"Địa Chỉ\" — Shopee có thể đã đổi giao diện, thao tác tay trong Brave.",
        _ => "Không mở được Cài đặt vận chuyển — thao tác tay trong cửa sổ Brave.",
    };
    return false;
}
```

(Thêm `using XuLyDonShopee.Core.Services;` nếu chưa có.) Phần bước 2 (SetPickupAddress) giữ nguyên.

### Bước 6 — Test + build

- **Baseline TRƯỚC khi sửa:** chạy `dotnet test` ghi lại tổng số pass hiện tại (con số nền mới nhất —
  KHÔNG tin số 201 của plan cũ vì sau đó đã có thêm việc).
- Test mới (`src/XuLyDonShopee.Tests/`): enum `ShippingNavResult` không có logic thuần đáng test riêng;
  KHÔNG cần test Playwright (không chạy được headless ở đây). Nếu Opus tách được logic thuần nào
  (vd: hàm quyết định retry) thì thêm test; không thì thôi — KHÔNG viết test hình thức.
- `dotnet build XuLyDonShopee.sln -c Debug` → 0 error/0 warning; `dotnet test` → toàn bộ pass, không đỏ
  test cũ. WDAC chặn (`0x800711C7`) → build lại `-p:Deterministic=false` rồi `dotnet test --no-build`;
  fail đồng loạt cùng mã lỗi này là policy máy, không phải code.
- Grep sau sửa: không còn call site nghiệp vụ nào gọi thẳng `HumanMoveAndClickAsync` trong
  `OpenShippingAddressSettingsAsync`/`SetPickupAddressAsync`/`HumanCancelModalAsync` (login flow thì
  ĐƯỢC giữ); không có `.ClickAsync(`/`.FillAsync(`/`Mouse.ClickAsync(` mới.

## 4. Tiêu chí nghiệm thu

- [ ] Click nghiệp vụ CHỈ nhả chuột khi `document.elementFromPoint` tại điểm click trả về đúng phần tử
      đích / con / tổ tiên của nó; bị che/cụp → không click, có đường xử lý tiếp (bung mục cha → thử
      lại → Goto fallback) — KHÔNG bao giờ click mù vào tọa độ nữa.
- [ ] `WaitShippingPageAsync` chỉ nhận theo URL `/portal/all-settings/shipping`; hết giờ có fallback
      Goto một lần rồi mới chịu thua.
- [ ] `ProcessOrdersAsync` báo StatusText PHÂN BIỆT: không mở được trang ≠ không thấy tab "Địa Chỉ".
- [ ] Like-human giữ nguyên: chuột cong, down→trễ→up, dừng ngẫu nhiên; không ClickAsync/FillAsync thẳng.
- [ ] `HumanMoveAndClickAsync` gốc (login flow) hành vi không đổi.
- [ ] Build 0 error/0 warning; toàn bộ test cũ pass (baseline đo trước khi sửa).
- [ ] Chỉ sửa: `ShopeeLoginService.cs`, `AccountSession.cs`, `ShippingNavResult.cs` (mới),
      (tùy chọn) test mới trong `src/XuLyDonShopee.Tests/`.

## 5. Rủi ro & lưu ý

- **elementFromPoint chỉ nhìn main frame** — sidebar nằm ở main frame nên đủ; KHÔNG chọc iframe trong
  plan này.
- Tọa độ `BoundingBoxAsync` và `elementFromPoint` cùng hệ quy chiếu viewport của main frame — dùng đúng
  cặp (tx, ty) đã tính cho click, đừng tính lại box lần nữa giữa chừng.
- Popper của CHÍNH item đích nằm TRONG thẻ `<a>` → `node.contains(hit)` nhận đúng; đừng "làm chặt" hơn
  mức đặc tả (bằng tuyệt đối node === hit) kẻo từ chối oan.
- `EvaluateAsync` với arg `new { x, y }` — Playwright .NET serialize thành object `{x, y}` cho JS; nếu
  gặp vấn đề kiểu số, ép `(float)`/`Math.Round` trước khi truyền.
- Smoke live cần phiên Shopee đăng nhập thật — môi trường Opus KHÔNG có ⇒ Opus KHÔNG claim đã chạy live
  (bài học cũ); phủ bằng build + full test + đọc code đối chiếu. Fable nhờ người dùng smoke sau merge.
- WDAC/ISG như ghi ở Bước 6.

---

## Báo cáo thực thi (Opus điền sau khi xong)

(để trống)
