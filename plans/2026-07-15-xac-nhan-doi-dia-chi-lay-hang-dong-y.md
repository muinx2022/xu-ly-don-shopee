# Plan: Sau khi Lưu địa chỉ lấy hàng — bấm "Đồng ý" ở hộp xác nhận (nếu có)

- **Ngày:** 2026-07-15
- **Trạng thái:** đang làm
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)
- **Nghiệm thu:** (Fable điền sau)

## 1. Bối cảnh & yêu cầu

Luồng "Xử lý đơn" nay đã: điều hướng → tab Địa Chỉ → Sửa → tick "Đặt làm địa chỉ lấy hàng" → bấm **Lưu**.
Người dùng phát hiện: **sau khi bấm Lưu, Shopee CÓ THỂ bật thêm một hộp xác nhận** (modal thứ hai) — nội
dung đại ý *"Vui lòng xác nhận bạn muốn thay đổi địa chỉ lấy hàng. Lưu ý: Kênh vận chuyển Trong Ngày sẽ
bị tắt..."*, có 2 nút: **"Kiểm tra chi tiết"** và **"Đồng ý"** (nút primary). **Không phải lúc nào cũng
hiện.**

**Yêu cầu người dùng:** sau khi bấm Lưu → **kiểm tra**, nếu có hộp xác nhận mới bật lên thì **bấm "Đồng
ý"** để chốt. Nếu không hiện thì thôi (như hiện tại).

### Hiện trạng code (Fable khảo sát 15/7, sau commit `392aa6b`)

`SetPickupAddressAsync` trong `src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs` (class `LoginSession`),
bước 6–7 (trong khối try/finally):

```csharp
// 6) Bấm "Lưu"
(mx, my, clicked) = await TryHumanClickVisibleAsync(page, saveBtn, mx, my, rng, ct).ConfigureAwait(false);
if (!clicked) { result = SetPickupResult.SaveFailed; return result; }

// 7) Chờ modal đóng
result = await WaitEditAddressModalClosedAsync(page, 15000, ct).ConfigureAwait(false)
    ? SetPickupResult.Ok : SetPickupResult.SaveFailed;
return result;
```

Vấn đề: khi có hộp xác nhận, **modal "Sửa Địa chỉ" KHÔNG đóng cho tới khi bấm "Đồng ý"** → hiện tại sẽ
chờ hết 15s rồi trả `SaveFailed` (thực ra chỉ thiếu một cú xác nhận). Đã có sẵn:
`FindEditAddressModalAsync(page)`, `WaitEditAddressModalClosedAsync`, `FindButtonByTextAsync(scope, match)`,
`TryHumanClickVisibleAsync` (click kiểu người CÓ hit-test). Helper thuần khớp text ở
`src/XuLyDonShopee.Core/Services/ShopeeShippingNav.cs` (mẫu: `IsSaveButtonText`, `IsCancelButtonText`).

### DOM hộp xác nhận (người dùng cung cấp 15/7)

```html
<div class="eds-modal__box"><div class="eds-modal__content eds-modal__content--normal">
  <div class="eds-modal__body"><div>Vui lòng xác nhận bạn muốn thay đổi địa chỉ lấy hàng. Lưu ý:<br>
    - Kênh vận chuyển Trong Ngày sẽ bị tắt...<br>- Hình thức Thanh toán khi nhận hàng cũng sẽ bị tắt...</div></div>
  <div class="eds-modal__footer"><div class="eds-modal__footer-buttons">
    <button class="eds-button eds-button--normal"><span>Kiểm tra chi tiết</span></button>
    <button class="eds-button eds-button--primary eds-button--normal"><span>Đồng ý</span></button>
  </div></div>
</div></div>
```

Lưu ý: nút primary của hộp xác nhận là **"Đồng ý"** (khác nút primary "Lưu" của modal Sửa Địa chỉ) → khớp
theo text "đồng ý" là đủ phân biệt. Nút "Kiểm tra chi tiết" là dấu hiệu phụ để chắc chắn đúng hộp này.

## 2. Phạm vi

- **Làm:**
  - Helper thuần `IsConfirmButtonText` ("đồng ý") + `IsCheckDetailButtonText` ("kiểm tra chi tiết") trong
    `ShopeeShippingNav.cs` + unit test.
  - Helper `FindConfirmChangePickupButtonAsync(page)` trong `LoginSession`: dò nút "Đồng ý" của hộp xác
    nhận đổi địa chỉ lấy hàng (đang hiển thị).
  - Sửa bước 7: sau khi Lưu, **poll** — hộp xác nhận hiện → **bấm "Đồng ý" kiểu người (verified)**; rồi
    chờ modal Sửa Địa chỉ đóng → `Ok`. Không hộp xác nhận + modal đóng → `Ok`. Hết giờ → `SaveFailed`.
- **Không làm:**
  - KHÔNG đụng luồng điều hướng / login / các bước trước (tìm địa chỉ, mở modal, tick checkbox).
  - KHÔNG dùng `ClickAsync/FillAsync/Mouse.ClickAsync/native` — click "Đồng ý" qua
    `TryHumanClickVisibleAsync` (kiểu người, hit-test). KHÔNG bấm "Kiểm tra chi tiết".
  - KHÔNG tự commit (Fable commit sau nghiệm thu).

## 3. Các bước thực hiện

### Bước 1 — Helper thuần khớp text (Core, test được)

`src/XuLyDonShopee.Core/Services/ShopeeShippingNav.cs` — thêm, XML-doc tiếng Việt:

- `public static bool IsConfirmButtonText(string? s)` → `NormalizeUiText(s) == "đồng ý"`.
- `public static bool IsCheckDetailButtonText(string? s)` → `NormalizeUiText(s) == "kiểm tra chi tiết"`.

### Bước 2 — `FindConfirmChangePickupButtonAsync(IPage page)` trong `LoginSession`

XML-doc tiếng Việt: dò nút **"Đồng ý"** của **hộp xác nhận đổi địa chỉ lấy hàng** (chỉ nhận khi ĐANG hiển
thị — có bounding box). Cách làm (graceful, không thấy → `null`, không ném):

1. Duyệt các `.eds-modal__footer` (mọi modal đang mở). Với mỗi footer:
   - Tìm nút khớp `IsConfirmButtonText` qua `FindButtonByTextAsync(footer, ShopeeShippingNav.IsConfirmButtonText)`.
   - **Guard đúng hộp:** footer đó phải ĐỒNG THỜI có nút khớp `IsCheckDetailButtonText` (dấu hiệu riêng
     của hộp xác nhận này) → tránh bấm nhầm "Đồng ý" của hộp thoại khác. Không có "Kiểm tra chi tiết" →
     bỏ qua footer này.
   - Nút "Đồng ý" có `BoundingBoxAsync() != null` (đang hiển thị) → trả nút đó.
2. Không có → `null`. Bọc try/catch nuốt lỗi → `null`.

### Bước 3 — Sửa bước 7: chờ Lưu hoàn tất (xử lý hộp xác nhận tuỳ có/không)

Thay lời gọi `WaitEditAddressModalClosedAsync` ở bước 7 bằng helper mới
`WaitPickupSaveCompletedAsync(page, 15000, mx, my, rng, ct)` (trả `bool`), rồi:
`result = await WaitPickupSaveCompletedAsync(...) ? SetPickupResult.Ok : SetPickupResult.SaveFailed;`

`WaitPickupSaveCompletedAsync` — poll tới hết `timeoutMs`, mỗi vòng ~300ms:

```
var deadline = now + timeoutMs;
bool confirmDone = false;
do {
    ct.ThrowIfCancellationRequested();

    // 1) Hộp xác nhận đổi địa chỉ lấy hàng có thể hiện (KHÔNG chắc chắn) → bấm "Đồng ý" kiểu người.
    if (!confirmDone) {
        var confirmBtn = await FindConfirmChangePickupButtonAsync(page);
        if (confirmBtn is not null) {
            bool ok;
            (mx, my, ok) = await TryHumanClickVisibleAsync(page, confirmBtn, mx, my, rng, ct);
            if (ok) {
                confirmDone = true;
                await Task.Delay(rng.Next(300, 900), ct);   // "đọc" rồi tiếp
            }
            continue;   // kiểm lại vòng sau (hộp tan / modal đóng)
        }
    }

    // 2) Không (còn) hộp xác nhận + modal Sửa Địa chỉ đã đóng → Lưu xong.
    if (await FindEditAddressModalAsync(page) is null) return true;

    await Task.Delay(300, ct);
} while (now < deadline);
return false;
```

Ghi chú thứ tự QUAN TRỌNG: **ưu tiên tìm & bấm hộp xác nhận TRƯỚC** khi coi "modal đóng = xong" — tránh
trường hợp modal Sửa đóng nhưng hộp xác nhận vẫn treo mà ta trả `Ok` sớm (chưa thực sự chốt đổi địa chỉ).
`mx, my` được cập nhật trong helper (truyền vào, dùng nội bộ; không cần trả ra vì là bước cuối).

### Bước 4 — KHÔNG quên like-human

Click "Đồng ý" qua `TryHumanClickVisibleAsync` → `HumanMoveAndClickVerifiedAsync` (chuột cong, hit-test,
down→trễ→up). Có `Task.Delay` "đọc trang" giữa các nhịp. Không native, không bấm "Kiểm tra chi tiết".

### Bước 5 — Test + build

- Thêm test trong `src/XuLyDonShopee.Tests/ShopeeShippingNavTests.cs`: `IsConfirmButtonText` ("Đồng ý",
  "  đồng ý ", "ĐỒNG Ý\n" → true; "đồng", "huỷ", "lưu", null, "" → false); `IsCheckDetailButtonText`
  ("Kiểm tra chi tiết", biến thể hoa/thường/space → true; "kiểm tra", null → false).
- Baseline: `dotnet test` (nền hiện tại **279**). Sau sửa build 0/0, test toàn bộ pass (279 + case mới).
  WDAC chặn → build lại `-p:Deterministic=false` rồi `dotnet test --no-build`.
- Grep sau sửa: không có `.ClickAsync(`/`.FillAsync(`/`Mouse.ClickAsync(`/`.CheckAsync(` mới; click "Đồng
  ý" qua `TryHumanClickVisibleAsync`.

## 4. Tiêu chí nghiệm thu

- [ ] Sau khi bấm Lưu: nếu hộp xác nhận đổi địa chỉ lấy hàng hiện → **tự bấm "Đồng ý" kiểu người**; nếu
      không hiện → vẫn `Ok` khi modal Sửa đóng (không chờ vô ích tới timeout).
- [ ] Nhận đúng hộp xác nhận (footer có CẢ "Đồng ý" primary lẫn "Kiểm tra chi tiết") — KHÔNG bấm nhầm
      "Đồng ý" của hộp thoại khác; KHÔNG bao giờ bấm "Kiểm tra chi tiết".
- [ ] Ưu tiên bấm "Đồng ý" TRƯỚC khi kết luận "modal đóng = Ok" (không trả Ok sớm khi hộp xác nhận còn
      treo).
- [ ] Dừng nhanh khi hủy (ct cắt được mỗi vòng poll). Hết giờ → `SaveFailed` (StatusText đã có sẵn).
- [ ] Like-human giữ nguyên; không ClickAsync/native.
- [ ] Build 0/0; test 279 nền + case mới pass.
- [ ] Chỉ sửa: `ShopeeShippingNav.cs`, `ShopeeLoginService.cs`, `ShopeeShippingNavTests.cs`.

## 5. Rủi ro & lưu ý

- **Bấm "Đồng ý" sẽ TẮT kênh vận chuyển "Trong Ngày"** (nội dung hộp cảnh báo) — nhưng đây đúng là yêu
  cầu người dùng (chốt đổi địa chỉ lấy hàng). Ghi rõ trong XML-doc để người sau biết hệ quả.
- Hộp xác nhận là modal RIÊNG, có thể chồng lên hoặc thay modal Sửa → dò theo footer "Đồng ý"+"Kiểm tra
  chi tiết" trên TOÀN trang, không giả định quan hệ cha–con với modal Sửa.
- Shopee đổi DOM/chữ bất kỳ lúc nào → mọi selector graceful, hết giờ trả `SaveFailed`, KHÔNG ném xuyên phiên.
- Không smoke live được (môi trường Opus) → Opus KHÔNG claim đã chạy live; phủ bằng build + full test +
  đọc code đối chiếu DOM người dùng. Fable nhờ người dùng smoke sau merge.
- WDAC/ISG khi test như các plan trước.

---

## Báo cáo thực thi (Opus điền sau khi xong)

(để trống)
