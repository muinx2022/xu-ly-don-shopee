# Plan: Xử lý đơn — bỏ qua (không vào Cài đặt vận chuyển) khi KHÔNG có đơn "Chờ lấy hàng"

- **Ngày:** 2026-07-17
- **Trạng thái:** hoàn thành (đã push; chờ người dùng smoke)
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)

## 1. Bối cảnh & mục tiêu

`AccountSession.ProcessOrdersAsync` (dùng bởi CẢ nút "Xử lý đơn" thủ công LẪN "Chạy tự động" — `AutoRunService` gọi `session.ProcessOrdersAsync()`) hiện chạy tuần tự: **bước 1** mở Cài Đặt Vận Chuyển → tab Địa Chỉ (`OpenShippingAddressSettingsAsync`) NGAY khi bắt đầu (dòng ~232-237), **bước 2** đặt địa chỉ lấy hàng, **bước 3** vòng quét/arrange đơn tới hết, **bước 4** đặt lại địa chỉ khác. KHÔNG kiểm có đơn hay không TRƯỚC.

Hệ quả (người dùng báo): khi **không có đơn** cần xử lý, app VẪN vào Cài đặt vận chuyển → đặt địa chỉ → ra list (không thấy đơn) → đặt lại địa chỉ — thao tác thừa, tốn thời gian, và ở "Chạy tự động" thì mỗi lô đều làm vậy dù shop không có đơn.

Yêu cầu: **nếu không có "Chờ lấy hàng" thì KHÔNG chuyển vào Cài đặt vận chuyển** (bỏ qua toàn bộ bước 1/2/3/4). Áp cho CẢ manual và chạy tự động.

## 2. Phạm vi

- **Làm:** thêm KIỂM TRA số "Chờ lấy hàng" ở ĐẦU `AccountSession.ProcessOrdersAsync`; == 0 (đọc được chắc chắn) → return sớm (không vào Cài đặt vận chuyển). File: `AccountSession.cs`. (Vì cả manual lẫn AutoRun đều gọi `ProcessOrdersAsync`, sửa 1 chỗ phủ cả hai.)
- **Không làm:** KHÔNG đổi các bước 1–4 khi CÓ đơn; KHÔNG đụng `SyncOrdersAsync`/`CheckOrdersAsync`/`RunAsync` nhịp theo dõi/`AutoRunService`; KHÔNG đụng màn Đơn hàng.

## 3. Các bước thực hiện

1. **Khảo sát:** ý nghĩa `ToShipCount` (số "Chờ Lấy Hàng" đọc từ to-do box trang chủ Seller qua `ReadToShipCountAsync`), cách `RunAsync` cập nhật nó, và `ReadToShipCountAsync(reload, ct)` (đọc mới có reload). Đối chiếu `CanProcessOrders` (nút manual bật khi `ToShipCount > 0`).
2. **Kiểm tra đầu `ProcessOrdersAsync`** — SAU guard `_navigating`/State (dòng ~227-232), TRƯỚC bước 1 (mở Cài đặt vận chuyển):
   - Xác định số "Chờ lấy hàng" HIỆN TẠI. Ưu tiên **đọc TƯƠI** để phản ánh đúng lúc bấm/lúc lô chạy: `var count = await s.ReadToShipCountAsync(reload: true, tok)` (bọc trong `_navigating` đang bật — an toàn vì đang giữ luồng). Nếu dự án có sẵn giá trị vừa đọc gần đây và muốn tránh reload thừa thì executor cân, nhưng ưu tiên đúng hiện trạng.
   - **Quy tắc skip AN TOÀN:** CHỈ bỏ qua khi **đọc ĐƯỢC số VÀ số == 0**. Đọc KHÔNG được (`null` — chưa đăng nhập/đọc lỗi) → **KHÔNG skip** (làm tiếp như cũ, tránh bỏ sót đơn thật).
   - Skip: cập nhật `ToShipCount = 0` (đồng bộ UI), `StatusText = "Không có đơn Chờ lấy hàng — bỏ qua xử lý."`, ghi ActivityLog cùng nội dung, rồi `return true` (hoặc giá trị phù hợp — coi là "xong, không có việc", KHÔNG phải lỗi). Không vào Cài đặt vận chuyển.
   - `finally` reset `_navigating` vẫn chạy (giữ nguyên cấu trúc try/finally hiện có — đặt kiểm tra BÊN TRONG try để finally nhả cờ).
3. **Cập nhật doc-comment** `ProcessOrdersAsync` (thêm: đầu luồng kiểm "Chờ lấy hàng"; == 0 → bỏ qua, không vào Cài đặt vận chuyển; đọc-không-được thì vẫn làm).
4. **Kiểm "cả manual và chạy tự động":** xác nhận `AutoRunService.RunAccountAsync` gọi `session.ProcessOrdersAsync()` (khi `DoProcess`) → tự hưởng skip (không cần sửa AutoRunService). Nút manual: `CanProcessOrders` đã yêu cầu `ToShipCount > 0` nhưng số có thể cũ → kiểm-đọc-tươi ở bước 2 bắt ca số cũ/đã hết đơn giữa chừng. Ghi rõ trong báo cáo là đã phủ cả hai đường.

## 4. Tiêu chí nghiệm thu

- [ ] Build 0 warning; test pass toàn bộ (+ test thuần nếu tách được hàm quyết định skip: đọc-được-0 → skip; đọc-null → không skip; >0 → không skip).
- [ ] Đọc code: kiểm tra ở ĐẦU ProcessOrdersAsync trước khi mở Cài đặt vận chuyển; skip CHỈ khi đọc được số == 0 (null → làm tiếp); `_navigating` finally vẫn nhả; không đổi bước 1–4 khi có đơn; AutoRun tự hưởng.
- [ ] Smoke thật (người dùng): tài khoản KHÔNG có đơn Chờ lấy hàng → bấm "Xử lý đơn" (hoặc để "Chạy tự động" chạy tới nó) → app KHÔNG vào Cài đặt vận chuyển, báo "Không có đơn Chờ lấy hàng — bỏ qua"; tài khoản CÓ đơn → xử lý bình thường (vào Cài đặt vận chuyển → đặt địa chỉ → arrange → đặt lại). Executor ghi rõ chờ người dùng.

## 5. Rủi ro & lưu ý

- **Đọc số THẤT BẠI không được coi là 0** — nếu đọc lỗi mà skip thì bỏ sót đơn thật. Chỉ skip khi số đọc được = 0.
- Đọc tươi (reload trang chủ) tốn ~vài giây nhưng NHẸ hơn nhiều so với vào Cài đặt vận chuyển + đặt địa chỉ + đặt lại; và tránh dùng số cũ (bấm Xử lý sau khi đơn đã hết).
- "Chờ Lấy Hàng" là tín hiệu người dùng thấy trên UI ("Chờ Lấy Hàng: N") — dùng đúng số đó để quyết, khớp kỳ vọng người dùng.
- Giữ nguyên hành vi khi CÓ đơn (bước 1–4) — chỉ THÊM cửa skip ở đầu.
- Cùng đụng `AccountSession.cs` với cụm "màn Đơn hàng đợt 3" (plan `2026-07-17-don-hang-refresh-in-nhieu-an-in-huy.md`, đụng `SyncOrdersAsync`) → hai việc này giao TUẦN TỰ (không song song trên `AccountSession.cs`).
- Cây chính (không agent song song lúc giao); `git status` sạch trước khi sửa.

---

## Báo cáo thực thi (Opus điền sau khi xong)

<Opus dán báo cáo cuối vào đây hoặc Fable tổng hợp lại sau nghiệm thu.>
