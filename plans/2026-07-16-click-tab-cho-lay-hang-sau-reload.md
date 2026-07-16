# Plan: Sau khi vào trang danh sách đơn, đảm bảo đang ở tab "Chờ lấy hàng" (thêm 1 click)

- **Ngày:** 2026-07-16
- **Trạng thái:** hoàn thành (chờ người dùng smoke)
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)

## 1. Bối cảnh & mục tiêu

Sau plan `2026-07-16-fix-nut-in-ma-modal-cu.md` (commit `3c8383b`), mỗi vòng đơn mở đầu bằng **reload** trang danh sách (`GotoAsync /portal/sale/order`). Người dùng phát hiện: **reload làm Shopee về tab mặc định "Tất cả"** (trước đây điều hướng SPA giữ nguyên tab đang chọn), trong khi đơn cần "Chuẩn bị hàng" nằm ở tab **"Chờ lấy hàng"**. Log 18:12 xác nhận: sau reload quét "Thấy 18 đơn" (mọi trạng thái) thay vì ~10 đơn của tab Chờ lấy hàng. Lần này chưa sót đơn (18 đơn vừa trang đầu), nhưng khi shop đông đơn, đơn cần xử lý có thể bị đẩy khỏi trang 1 của tab "Tất cả" → app báo nhầm `NoOrder` ("hết đơn") dù còn.

Người dùng yêu cầu: **thêm 1 lần click** — sau khi vào trang danh sách, chuyển sang tab "Chờ lấy hàng" rồi mới quét. DOM thanh tab người dùng cung cấp (nguyên văn, đáng tin):

```html
<div class="eds-tabs__nav-tabs">
  <div class="eds-tabs__nav-tab"><div data-testid="l1-tab-all" class="tab-label"><span>Tất cả</span></div></div>
  <div class="eds-tabs__nav-tab"><div data-testid="l1-tab-unpaid" class="tab-label"><span>Chờ xác nhận</span></div></div>
  <div class="eds-tabs__nav-tab active"><div data-testid="l1-tab-toship" class="tab-label"><span>Chờ lấy hàng</span><div class="tab-badge"><span class="badge">(10)</span></div></div></div>
  <div class="eds-tabs__nav-tab"><div data-testid="l1-tab-shipping" class="tab-label"><span>Đang giao</span></div></div>
  ...
</div>
```

Ghi nhận: tab active đánh dấu bằng class `active` trên `.eds-tabs__nav-tab` (cha của `.tab-label`); nhãn tab có thể kèm badge số `(10)` nên khớp text phải dùng **StartsWith** sau chuẩn hóa.

## 2. Phạm vi

- **Làm:** thêm bước "đảm bảo tab Chờ lấy hàng" vào đầu vòng đơn; helper text thuần + test. File: `src/XuLyDonShopee.Core/Services/ShopeeShippingNav.cs`, `src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs`, `src/XuLyDonShopee.Tests/ShopeeShippingNavTests.cs`.
- **Không làm:** KHÔNG đổi logic reload/duyệt-ứng-viên/vòng ngoài của các plan trước; KHÔNG đổi `FindFirstProcessableOrderAsync` (card pane ẩn không có bounding box nên nút "Chuẩn bị hàng" của chúng đã bị lọc sẵn — dòng "Thấy N đơn" có thể đếm cả card pane ẩn, chấp nhận, không sửa đợt này); KHÔNG đụng SaveSlipAsync/bước địa chỉ/vòng theo dõi.

## 3. Các bước thực hiện

### A. `ShopeeShippingNav.cs` — helper text thuần

Thêm cạnh các helper text hiện có:

```csharp
/// <summary>True nếu text (đã chuẩn hóa) BẮT ĐẦU bằng "chờ lấy hàng" — nhãn tab "Chờ lấy hàng" trên
/// trang danh sách đơn (nhãn có thể kèm badge số: "Chờ lấy hàng (10)"). Dùng StartsWith vì badge.</summary>
public static bool IsToShipTabText(string? s)
    => NormalizeUiText(s).StartsWith("chờ lấy hàng", StringComparison.Ordinal);
```

(Đối chiếu chữ ký `NormalizeUiText` hiện có — nếu trả string thường thì viết tương ứng; KHÔNG đổi helper khác.)

### B. `ShopeeLoginService.cs` — helper `EnsureToShipTabAsync` + gọi ở bước 1

1. Helper mới (đặt gần `GoToAllOrdersAsync`):

```
private static async Task<(double X, double Y)> EnsureToShipTabAsync(
    IPage page, double mx, double my, Random rng, Action<string> L, CancellationToken ct)
```

Hành vi (best-effort — mọi nhánh fail chỉ log rồi đi tiếp, KHÔNG ném; OCE ném xuyên):
   - **Tìm phần tử tab** (poll ≤ 10s, bước ~400ms, RE-QUERY tươi mỗi lượt — không giữ handle qua lượt): ưu tiên `[data-testid='l1-tab-toship']`; fallback duyệt các `.tab-label` (hoặc `div` trong `.eds-tabs__nav-tab`) có `InnerText` khớp `ShopeeShippingNav.IsToShipTabText` — CHỈ nhận phần tử có bounding box. Hết 10s không thấy → `L("Không thấy tab Chờ lấy hàng — quét tab hiện tại.")` → return (mx, my).
   - **Đọc trạng thái active** (evaluate CHỈ-ĐỌC trên handle): `el => { const t = el.closest('.eds-tabs__nav-tab'); return !!t && t.classList.contains('active'); }`. Đã active → return (mx, my) ngay (không log, không click — trường hợp thường gặp khi Shopee nhớ tab).
   - **Chưa active → click kiểu người**: `TryHumanClickVisibleAsync(page, tabEl, mx, my, rng, ct)`; click được → `L("Chuyển sang tab Chờ lấy hàng.")`; rồi **chờ active** ≤ 5s (poll ~300ms, mỗi lượt re-query phần tử tươi + đọc lại class như trên) + sau khi active chờ settle ngẫu nhiên 800–1500ms (danh sách vẽ lại). Click không được / hết 5s chưa active → `L("Chưa chuyển được sang tab Chờ lấy hàng — quét tab hiện tại.")` → vẫn đi tiếp.
   - Trả (mx, my) mới từ lần click (nếu có).
2. **Gọi ở `ProcessFirstOrderAsync` bước 1**: ngay SAU khối kiểm `IsAllOrdersHref` (điểm hội tụ của cả nhánh reload lẫn nhánh `GoToAllOrdersAsync`), TRƯỚC khoảng dừng "đọc trang + chờ danh sách render":

```csharp
(mx, my) = await EnsureToShipTabAsync(page, mx, my, rng, L, ct).ConfigureAwait(false);
```

3. **Cập nhật comment**: comment bước 1 (thêm ý: reload về tab mặc định "Tất cả" → phải click sang "Chờ lấy hàng"); sửa luôn câu comment cũ sai thực tế "(/portal/sale/order — trang tự vào tab 'Chờ xử lý')" nếu còn.

### C. Tests — `ShopeeShippingNavTests.cs`

Nhóm Theory cho `IsToShipTabText`: khớp `"Chờ lấy hàng"`, `"Chờ lấy hàng (10)"`, `"  chờ  lấy  hàng \n(3)"` (tùy hành vi NormalizeUiText với khoảng trắng — đối chiếu helper hiện có rồi viết ca cho đúng), KHÔNG khớp `"Tất cả"`, `"Đang giao"`, `"Chờ xác nhận"`, `""`, `null`.

## 4. Tiêu chí nghiệm thu

- [ ] `dotnet build XuLyDonShopee.sln -c Debug`: 0 lỗi, 0 warning mới.
- [ ] `dotnet test`: toàn bộ pass (392 + tests mới; WDAC chặn đồng loạt → ghi rõ báo cáo).
- [ ] Đọc code: `EnsureToShipTabAsync` best-effort (không nhánh nào ném ra ngoài trừ OCE), evaluate chỉ đọc, click qua `TryHumanClickVisibleAsync` (hit-test), re-query tươi mỗi lượt poll; được gọi đúng 1 chỗ ở bước 1 (cả 2 nhánh điều hướng đều đi qua); không đổi hành vi nào khác.
- [ ] Smoke thật (người dùng chạy): sau mỗi lần reload giữa các đơn, log có "Chuyển sang tab Chờ lấy hàng." (hoặc không log gì nếu đã đúng tab) và số đơn quét được khớp tab Chờ lấy hàng (~badge số); các đơn vẫn xử lý liền mạch như lượt 18:12. Executor ghi rõ mục này chờ người dùng.

## 5. Rủi ro & lưu ý

- Nhãn/badge tab do Shopee render — **testid `l1-tab-toship` là khóa chính** (người dùng cung cấp từ DOM thật), text chỉ là fallback.
- `.tab-label` là phần tử nhận click (theo DOM); nếu click label không ăn thì `TryHumanClickVisibleAsync` tự trả false → nhánh cảnh báo, KHÔNG thử click phần tử khác (giữ đơn giản; log sẽ cho biết nếu cần chỉnh).
- Chờ-active phải **re-query tươi** (tab bar có thể re-render khi chuyển tab — bài học stale handle).
- Sau chuyển tab, `FindFirstProcessableOrderAsync` giữ nguyên: card pane ẩn không có box → không bị bấm nhầm; "Thấy N đơn" có thể đếm cả card ẩn (nhiễu nhẹ, chấp nhận — KHÔNG mở rộng phạm vi sửa).
- Hai phiên Claude có thể cùng mở repo — executor `git status` trước khi sửa; cây phải sạch ở `3c8383b`; thấy thay đổi lạ → DỪNG báo.

---

## Báo cáo thực thi (Opus điền sau khi xong)

Opus thực thi đủ A/B/C: `IsToShipTabText` (StartsWith "chờ lấy hàng" sau chuẩn hóa, chịu badge) + Theory 8 ca; `EnsureToShipTabAsync` (+`FindToShipTabAsync` ưu tiên testid `l1-tab-toship` fallback `.tab-label` khớp text, +`IsToShipTabActiveAsync` đọc class `active` trên `.eds-tabs__nav-tab`) — poll tìm ≤10s, đã active thì thôi, chưa thì click kiểu người + chờ active ≤5s + settle, mọi nhánh fail chỉ log (best-effort), OCE ném xuyên; gọi đúng 1 chỗ ở bước 1 `ProcessFirstOrderAsync` (điểm hội tụ cả nhánh reload lẫn click menu). Khác plan (đã duyệt): fallback chỉ dùng `.tab-label` để đồng nhất closest khi đọc active.

Nghiệm thu (Fable): tự build 0 warning + 400/400 test xanh; panel rà soát đối kháng 2/3 phiếu — 0 finding được giữ (2 phát hiện thô đều bị bác). Smoke thật: CHỜ NGƯỜI DÙNG — kỳ vọng sau mỗi reload log có "Chuyển sang tab Chờ lấy hàng." (hoặc im lặng nếu đã đúng tab), số đơn quét khớp tab.
