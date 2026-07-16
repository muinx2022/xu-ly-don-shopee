# Plan: Sau khi xử lý hết đơn, đặt địa chỉ lấy hàng về MỘT ĐỊA CHỈ KHÁC

- **Ngày:** 2026-07-16
- **Trạng thái:** đang làm
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)

## 1. Bối cảnh & mục tiêu

Luồng "Xử lý đơn" hiện tại (`AccountSession.ProcessOrdersAsync`): (1) mở Cài Đặt Vận Chuyển → tab Địa Chỉ (`OpenShippingAddressSettingsAsync`), (2) đặt địa chỉ lấy hàng theo tỉnh cấu hình của tài khoản (`SetPickupAddressAsync(province)` — best-effort, Shopee có thể khóa đổi khi có đơn "Chờ lấy hàng"), (3) vòng xử lý mọi đơn.

**Yêu cầu mới (người dùng, đã chốt qua hỏi-đáp):** sau khi xử lý **hết** đơn, quay lại Cài Đặt Vận Chuyển và đặt địa chỉ lấy hàng về **một địa chỉ KHÁC bất kỳ** — app tự chọn **địa chỉ ĐẦU TIÊN trong danh sách KHÔNG mang tag "Địa chỉ lấy hàng"** — không để nguyên địa chỉ mà app đã đặt cho việc xử lý.

Hiện trạng code liên quan (đã khảo sát):
- `SetPickupAddressAsync(province)` (`ShopeeLoginService.cs` ~1404–1553): tìm item theo tỉnh (`FindMatchingAddressItemAsync`, selector `.address-list .address-item-container`) → nếu item đã có tag (`ItemHasPickupTagAsync`, `.address-label` khớp `IsPickupTagText`) → Ok; ngược lại mở "Sửa" (`FindEditButtonAsync`) → chờ modal (`WaitEditAddressModalAsync`) → tick "Đặt làm địa chỉ lấy hàng" (khối 5a–5c, re-query tươi, DOM sống) → "Lưu" + chờ hoàn tất (`WaitPickupSaveCompletedAsync`) → `finally` Hủy-nếu-còn-modal. Toàn bộ khối từ "mở Sửa" trở đi KHÔNG phụ thuộc tỉnh — tái dùng được.
- `OpenShippingAddressSettingsAsync` (ILoginSession dòng 92, hiện thực ~1005): điều hướng kiểu người tới trang Địa Chỉ, có fallback Goto — tái dùng nguyên cho bước quay lại.
- Tests KHÔNG có fake `ILoginSession` (đã grep) — thêm method vào interface không vỡ test nào.

## 2. Phạm vi

- **Làm:** method mới `SetPickupAddressToOtherAsync` (interface + hiện thực, refactor tái dùng khối sửa-tick-lưu); bước 4 mới trong `ProcessOrdersAsync`; test cho helper thuần nếu tách được. File: `src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs`, `src/XuLyDonShopee.App/Services/AccountSession.cs`.
- **Không làm:**
  - KHÔNG đổi bước 2 hiện tại (vẫn đặt địa chỉ theo tỉnh trước khi xử lý).
  - KHÔNG thêm cấu hình/UI/cột DB mới (phương án "địa chỉ cố định tự chọn" đã bị loại khi hỏi người dùng).
  - KHÔNG đổi kết quả trả về của `ProcessOrdersAsync` theo kết quả bước 4 (bước 4 là best-effort thuần).
  - KHÔNG đụng vòng xử lý đơn / tab Chờ lấy hàng / SaveSlipAsync.

## 3. Các bước thực hiện

### A. `ShopeeLoginService.cs` — refactor + method mới

1. **Refactor tái dùng (di chuyển nguyên văn, KHÔNG đổi logic):** tách phần thân `SetPickupAddressAsync` từ bước 3 ("Tìm & bấm nút Sửa của địa chỉ đó") đến hết bước 7 **kể cả `try/finally` Hủy-modal** thành hàm private mới:
   ```csharp
   private async Task<SetPickupResult> OpenEditAndSetItemAsPickupAsync(
       IPage page, IElementHandle item, double mx, double my, Random rng, CancellationToken ct)
   ```
   `SetPickupAddressAsync` sau refactor: tìm item theo tỉnh → check tag (Ok nếu đã là pickup) → gọi hàm trên. Hành vi/return/thông điệp GIỮ NGUYÊN 100%.
2. **Method mới** — thêm vào interface `ILoginSession` (cạnh `SetPickupAddressAsync`, doc-comment tiếng Việt tương tự) và hiện thực:
   ```csharp
   Task<SetPickupResult> SetPickupAddressToOtherAsync(CancellationToken ct = default);
   ```
   Hành vi (trang Địa Chỉ đã được mở sẵn bởi caller — giống hợp đồng của `SetPickupAddressAsync`):
   - Mẫu mở đầu như `SetPickupAddressAsync`: page = `_context.Pages[0]` (null → Failed), rng/mx/my ngẫu nhiên, delay "đọc trang" 800–2500ms.
   - **Tìm item đích** (poll ≤ 15s, ~400ms/lượt, re-query tươi): lấy `.address-list .address-item-container`; trong danh sách, chọn **item ĐẦU TIÊN có `ItemHasPickupTagAsync(item) == false`**. Danh sách rỗng hoặc mọi item đều mang tag (shop chỉ có 1 địa chỉ) → `SetPickupResult.AddressNotFound`.
   - Gọi `OpenEditAndSetItemAsPickupAsync(page, item, mx, my, rng, ct)` và trả kết quả.
   - Bọc `try/catch` ngoài như `SetPickupAddressAsync` (OCE ném xuyên, exception khác → `Failed`).
3. Doc-comment nêu rõ: "địa chỉ KHÁC bất kỳ = item đầu tiên không mang tag Địa chỉ lấy hàng; nếu tag không đọc được (DOM đổi) có thể chọn trúng địa chỉ đang là pickup — vô hại (tick lại chính nó, Shopee giữ nguyên)".

### B. `AccountSession.cs` — bước 4 trong `ProcessOrdersAsync`

1. Sau khối tổng kết hiện tại (`StatusText = summary; log(summary);`), **trước** `return`:
   - **Điều kiện chạy:** chỉ khi vòng kết thúc TỰ NHIÊN — `last == NoOrder` (hết đơn) hoặc `last == Ok` (chạm chốt chặn). Khi dừng vì 3 lỗi liên tiếp → KHÔNG đổi địa chỉ (việc còn dở, người dùng sẽ chạy lại), chỉ `log("Giữ nguyên địa chỉ lấy hàng (vòng dừng giữa chừng).")`.
   - Thân bước 4 (best-effort, bọc try/catch nuốt exception thường — OCE ném xuyên để dừng sạch):
     ```
     StatusText = "Đang đặt lại địa chỉ lấy hàng (địa chỉ khác)...";
     log("Quay lại Cài đặt vận chuyển để đặt địa chỉ lấy hàng về địa chỉ khác...");
     var nav2 = await s.OpenShippingAddressSettingsAsync(tok);
     nếu nav2 != Ok → log + StatusText "Không mở lại được Cài đặt vận chuyển — giữ nguyên địa chỉ lấy hàng."; xong bước 4.
     var res = await s.SetPickupAddressToOtherAsync(tok);
     ```
   - Map kết quả (ghi CẢ log LẪN StatusText — StatusText ghép sau summary cho khỏi mất tổng kết, ví dụ `StatusText = summary + " Đã đặt địa chỉ lấy hàng về địa chỉ khác."`):
     - `Ok` → "Đã đặt địa chỉ lấy hàng về địa chỉ khác."
     - `AddressNotFound` → "Shop không có địa chỉ nào khác — giữ nguyên địa chỉ lấy hàng."
     - Các kết quả khác (`EditModalNotOpened`/`CheckboxNotFound`/`CheckboxClickFailed`/`SaveFailed`/`Failed`) → "Chưa đặt lại được địa chỉ lấy hàng ({bước lỗi}) — Shopee có thể khóa đổi địa chỉ khi có đơn Chờ lấy hàng; kiểm tra tay nếu cần." (tận dụng các câu mô tả bước lỗi sẵn có ở bước 2 nếu tiện, không bắt buộc y hệt).
   - **KHÔNG** đổi giá trị `return` của `ProcessOrdersAsync` theo kết quả bước 4.
2. Cập nhật doc-comment `ProcessOrdersAsync` (giờ 4 bước; bước 4 best-effort đặt địa chỉ khác khi xử lý hết đơn).

### C. Build/test

- `dotnet build` + `dotnet test`: 400/400 pass (không bắt buộc test mới — logic mới là Playwright runtime; nếu executor thấy tách được helper thuần đáng test thì thêm, ghi rõ).

## 4. Tiêu chí nghiệm thu

- [ ] Build 0 lỗi, 0 warning mới; test 400/400 (WDAC chặn đồng loạt → ghi rõ).
- [ ] Đọc code: `SetPickupAddressAsync` sau refactor hành vi GIỮ NGUYÊN (đối chiếu từng bước với bản cũ bằng git diff); method mới chọn đúng "item đầu tiên KHÔNG tag"; bước 4 chỉ chạy khi NoOrder/cap; mọi kết quả bước 4 chỉ log/StatusText, không đổi return; OCE ném xuyên.
- [ ] Smoke thật (người dùng): chạy "Xử lý đơn" tới hết đơn → log có "Quay lại Cài đặt vận chuyển..." và kết cục ("Đã đặt địa chỉ lấy hàng về địa chỉ khác." / câu lỗi rõ ràng); vào Shopee kiểm tra địa chỉ lấy hàng đã đổi sang địa chỉ khác. LƯU Ý khi smoke: ngay sau khi arrange loạt đơn, shop CÓ đơn "Chờ lấy hàng" nên Shopee rất có thể KHÓA đổi địa chỉ → gặp câu "Chưa đặt lại được..." là hành vi ĐÚNG của app (giới hạn phía Shopee). Executor ghi rõ mục này chờ người dùng.

## 5. Rủi ro & lưu ý

- **Refactor phải là DI CHUYỂN nguyên văn** khối sửa-tick-lưu (kể cả `finally` Hủy-modal, các delay, thông điệp) — không "tiện tay" sửa gì trong đó; mọi khác biệt phải nêu trong báo cáo.
- Modal "Sửa Địa chỉ" có Google Map load ngầm → giữ nguyên các cơ chế re-query tươi/DOM sống sẵn có (bài học `modal-async-vue-rerender-stale-handle`).
- Poll tìm item đích phải re-query tươi mỗi lượt; `ItemHasPickupTagAsync` ném/false → coi như không-tag một cách THẬN TRỌNG: chỉ chọn item khi đọc tag KHÔNG ném (tránh chọn bừa lúc DOM chưa render — executor tự cân nhắc, ghi rõ cách xử lý trong báo cáo).
- Shopee khóa đổi địa chỉ khi có đơn Chờ lấy hàng (đã biết từ commit 22ba62c) — bước 4 rất dễ trả `SaveFailed`/`EditModalNotOpened`; đây là best-effort, KHÔNG retry, KHÔNG phá kết quả vòng xử lý.
- Hai phiên Claude có thể cùng mở repo — executor `git status` trước khi sửa; cây phải sạch (Fable sẽ ghi HEAD mong đợi trong prompt); thấy thay đổi lạ → DỪNG báo.

---

## Báo cáo thực thi (Opus điền sau khi xong)

<Opus dán báo cáo cuối vào đây hoặc Fable tổng hợp lại sau nghiệm thu.>
