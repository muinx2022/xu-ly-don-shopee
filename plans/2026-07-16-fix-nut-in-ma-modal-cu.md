# Plan: Fix nút "In phiếu giao" ma — modal cũ còn trong DOM làm đơn thứ 2 trở đi kẹt

- **Ngày:** 2026-07-16
- **Trạng thái:** hoàn thành (chờ người dùng smoke loạt ≥2 đơn)
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)

## 1. Bối cảnh & mục tiêu

**Hiện tượng (lặp lại cả ngày 16/7):** trong một lượt "Xử lý đơn" nhiều đơn, đơn ĐẦU luôn in phiếu ngon; **từ đơn THỨ 2 trở đi** bước bấm "In phiếu giao" kẹt (chờ hết 300s rồi PrintFailed), dù người dùng NHÌN THẤY modal "Thông Tin Chi Tiết" với nút "In phiếu giao" bình thường (đã đối chiếu DOM người dùng dán: nút có `data-testid="print-button"`, không `disabled`).

**Bằng chứng quyết định** — dòng chẩn đoán trong log 16:47:04 (đơn `260716SXF33JRF`, đơn thứ 2 của lượt 16:41):

```
Chẩn đoán nút In phiếu giao: via=testid, disabled=false, aria-disabled=null, text=[In phiếu giao], box=191x16, elementFromPoint=div.eds-modal__container
```

→ `document.querySelector` bắt được MỘT nút print-button nhưng **box bẹp 191x16** (nút thật cỡ ~190x40) và tâm nó bị `div.eds-modal__container` (vỏ của modal KHÁC) đè. Kết luận: **DOM đang chứa ≥2 modal chồng nhau** — Vue/eds-modal GIỮ LẠI modal "Thông Tin Chi Tiết" của ĐƠN TRƯỚC trong DOM (dạng ẩn/bẹp) sau khi đóng bằng nút X; modal thật của đơn hiện tại nằm SAU trong DOM. Mà `WaitPrintButtonClickableAsync` dò nút qua `FirstVisibleByBoxAsync` = `page.QuerySelectorAsync(...)` — **chỉ lấy PHẦN TỬ ĐẦU TIÊN** khớp selector → vớ đúng nút MA của modal cũ, hit-test fail vĩnh viễn, KHÔNG BAO GIỜ thử nút thật đứng sau.

**Vì sao chỉ đơn 2+:** mỗi vòng `ProcessFirstOrderAsync` "Về danh sách đơn" bằng `GoToAllOrdersAsync` — CLICK link menu (điều hướng SPA, KHÔNG tải lại trang) → DOM cũ (kèm xác modal) sống xuyên các đơn. Đơn đầu lượt chạy ngay sau login/mở trang (DOM sạch) nên luôn ngon.

Mục tiêu — 2 lớp fix:
- **A (gốc rễ):** dò nút In phiếu giao phải duyệt **TẤT CẢ** ứng viên và hit-test TỪNG ứng viên (ưu tiên duyệt từ CUỐI DOM lên — modal mới nhất được append cuối), thay vì tin phần tử đầu tiên.
- **B (vệ sinh trạng thái):** mỗi vòng xử lý đơn bắt đầu bằng trang danh sách TẢI LẠI SẠCH: nếu URL đã ở trang danh sách thì `GotoAsync` lại URL đó (tương đương người bấm F5/gõ URL — mẫu đã dùng sẵn trong codebase), để xác modal cũ không tích tụ (phòng luôn các chỗ tìm-phần-tử-đầu khác: `WaitModalByTitleAsync`, `CloseDetailModalAsync`).

## 2. Phạm vi

- **Làm:** 2 mục A/B trong `src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs` + cập nhật doc-comment liên quan.
- **Không làm:**
  - KHÔNG đổi thời gian chờ 5 phút (`PrintButtonWaitSeconds` giữ nguyên — vẫn cần cho ca Shopee tạo vận đơn chậm THẬT).
  - KHÔNG đổi logic bỏ-qua-đơn-lỗi/3-liên-tiếp (plan trước, đã chạy đúng).
  - KHÔNG sửa `FirstVisibleByBoxAsync` dùng chung (các chỗ gọi khác — menu, card — không có vấn đề trùng lặp; sửa cục bộ ở chỗ dò nút in).
  - KHÔNG đụng `SaveSlipAsync`, bước đặt địa chỉ, vòng theo dõi 30'.

## 3. Các bước thực hiện

### A. `WaitPrintButtonClickableAsync` duyệt TẤT CẢ ứng viên + hit-test từng cái (~dòng 3050–3110)

1. Viết lại thân vòng poll (giữ nguyên chữ ký hàm, nhịp poll ~400ms, deadline, `ct`, catch-stale):
   - Gom danh sách ứng viên MỖI LƯỢT POLL (re-query tươi, không giữ handle cũ):
     1. `page.QuerySelectorAllAsync("button[data-testid='print-button']")` — **duyệt NGƯỢC** (từ cuối mảng về đầu: modal mới nhất thường append cuối `body`).
     2. Nếu chưa có ứng viên pass: `page.QuerySelectorAllAsync("button")` lọc `textMatch(InnerText)` — cũng duyệt NGƯỢC.
   - Với TỪNG ứng viên: `BoundingBoxAsync()` ≠ null → tính tâm (cx, cy) → `IsPointOnElementAsync(cand, cx, cy)` → **PASS thì trả về NGAY ứng viên đó**. Fail/ném (handle stale) → thử ứng viên kế, không phá vòng (try/catch quanh từng ứng viên, `OperationCanceledException` vẫn ném).
   - Hết deadline không ứng viên nào pass → `null` (giữ nguyên hành vi cũ).
2. Cập nhật doc-comment của hàm: nêu rõ lý do duyệt tất cả + duyệt ngược (Vue giữ modal CŨ trong DOM sau khi đóng → nút MA đứng trước nút thật; bằng chứng log 16:47:04 `box=191x16, elementFromPoint=div.eds-modal__container`).

### B. Mỗi vòng đơn bắt đầu với trang danh sách TẢI LẠI SẠCH (`ProcessFirstOrderAsync`, bước 1, ~dòng 2116–2122)

1. Trước khi gọi `GoToAllOrdersAsync`: nếu `ShopeeShippingNav.IsAllOrdersHref(page.Url)` ĐÃ đúng → thay vì click menu SPA, **`page.GotoAsync(AllOrdersUrl, new PageGotoOptions { WaitUntil = DOMContentLoaded, Timeout = 30000 })`** (reload sạch DOM — tương đương người bấm F5 sau khi xong một đơn; mẫu Goto này đã dùng ở `CheckOrdersAsync`/fallback `GoToAllOrdersAsync`). Bọc try/catch nuốt lỗi điều hướng (`OperationCanceledException` ném); sau đó vẫn kiểm `IsAllOrdersHref(page.Url)` như cũ (fail → `OrdersPageNotOpened`).
2. URL KHÁC trang danh sách → giữ nguyên đường `GoToAllOrdersAsync` (click menu kiểu người).
3. Log giữ nguyên câu "Về danh sách đơn (Tất cả)..." (không đổi định dạng log).
4. Cập nhật comment bước 1 nêu lý do reload (xác modal cũ sống xuyên điều hướng SPA → nút ma; reload = khởi đầu sạch mỗi đơn).

### C. Doc/test

1. `dotnet build` + `dotnet test` — 392/392 pass (không có test mới bắt buộc: cả hai mục là hành vi Playwright runtime; logic thuần không đổi).
2. Grep xác nhận không còn chỗ nào khác trong luồng in phiếu tin "phần tử đầu tiên" của `print-button`.

## 4. Tiêu chí nghiệm thu

- [ ] `dotnet build XuLyDonShopee.sln -c Debug`: 0 lỗi, 0 warning mới.
- [ ] `dotnet test`: 392/392 pass (WDAC chặn đồng loạt → ghi rõ báo cáo).
- [ ] Đọc code: vòng poll dò nút duyệt TẤT CẢ ứng viên (testid rồi text), thứ tự NGƯỢC, hit-test TỪNG ứng viên, re-query tươi mỗi lượt; bước 1 reload trang danh sách khi URL đã đúng; OCE ném xuyên; không đổi `PrintButtonWaitSeconds`/logic vòng ngoài.
- [ ] Smoke thật (người dùng chạy): lượt "Xử lý đơn" có ≥2–3 đơn liên tiếp — **đơn thứ 2 trở đi phải bấm được "In phiếu giao" trong vài giây** (không đợi 5 phút), mỗi đơn có PNG + PDF gốc trong `D:\Phieu-giao-hang`. Executor ghi rõ mục này chờ người dùng.

## 5. Rủi ro & lưu ý

- **Duyệt ngược phải RE-QUERY mỗi lượt poll** — không cache danh sách qua các lượt (modal re-render → handle stale; đã có bài học `modal-async-vue-rerender-stale-handle`).
- **Hit-test từng ứng viên bọc try/catch riêng** — một ứng viên stale/ném không được làm hỏng cả lượt duyệt.
- Reload ở B chỉ khi URL ĐÃ là trang danh sách; đừng reload chồng lên đường `GoToAllOrdersAsync` (tránh double-navigation). Sau reload giữ khoảng dừng "đọc trang" ngẫu nhiên sẵn có.
- Không thêm click/hook nào mới trên trang — A chỉ ĐỌC (query + hit-test), B là Goto (hành vi người gõ URL/F5, đã dùng ở nơi khác).
- Hai phiên Claude có thể cùng mở repo — executor chạy `git status` trước khi sửa; cây phải sạch ở commit `8179e44`; thấy thay đổi lạ thì DỪNG báo lại.

---

## Báo cáo thực thi (Opus điền sau khi xong)

Opus thực thi đủ A/B trong 1 file `ShopeeLoginService.cs`: (A) `WaitPrintButtonClickableAsync` re-query tươi mỗi lượt, duyệt TẤT CẢ ứng viên testid rồi text theo thứ tự NGƯỢC, hit-test từng ứng viên qua helper mới `IsCandidateClickableAsync` (box + elementFromPoint tại tâm; lỗi → false), OCE ném xuyên; (B) `ProcessFirstOrderAsync` bước 1: URL đã ở trang danh sách → `GotoAsync(AllOrdersUrl)` reload sạch DOM (nuốt lỗi điều hướng, kiểm URL sau như cũ), URL khác → `GoToAllOrdersAsync` như cũ. Kèm cập nhật doc-comment (gồm comment đoạn diagJs — nói rõ chẩn đoán cố ý chụp nút ĐẦU làm bằng chứng, khác đường bấm thật). Khác plan (đã duyệt): tách helper `IsCandidateClickableAsync` thay vì inline.

Nghiệm thu (Fable): tự build 0 warning + 392/392 test xanh; panel rà soát đối kháng 2/3 phiếu — KHÔNG có lỗi mới do diff; 1 phát hiện mức thấp được giữ là lỗi CÓ SẴN trước diff (race hiếm: handle nút detach giữa lúc hit-test pass và lúc bấm → một cú click mù, vòng chờ tự thử lại; panel xác nhận diff không nới rộng) — ghi nợ, chưa sửa đợt này. Smoke thật: CHỜ NGƯỜI DÙNG chạy lượt ≥2–3 đơn, kỳ vọng đơn thứ 2+ bấm được "In phiếu giao" trong vài giây.
