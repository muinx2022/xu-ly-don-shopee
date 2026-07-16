# Plan: Nút "Sync Đơn hàng" & "Kiểm tra" luôn bấm được — chưa mở phiên thì TỰ MỞ rồi thực hiện

- **Ngày:** 2026-07-17
- **Trạng thái:** đang làm
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`, worktree)

## 1. Bối cảnh & mục tiêu

Hiện 2 nút ở hàng nút chi tiết tài khoản chỉ bật khi phiên của tài khoản đang chọn ĐANG CHẠY (`CanSyncOrders`/`CanCheckOrders` trong `AccountsViewModel`): `⇊` Sync Đơn hàng, `⟳` Kiểm tra. Người dùng yêu cầu: **luôn enable**; bấm vào → **nếu phiên CHƯA mở thì app tự "Mở trang bán hàng"** (mở Brave, tự đăng nhập) **rồi thực hiện hành động**; nếu phiên đã mở → thực hiện ngay như hiện tại.

Ràng buộc quan trọng: sau khi phiên mở, **luồng tự-đăng-nhập kiểu người còn đang chạy** trong `RunAsync` của `AccountSession` — nếu Sync/Kiểm tra điều hướng ngay sẽ giẫm chuột lên luồng đó. Phải CHỜ phiên "sẵn sàng thao tác" rồi mới chạy hành động.

Phạm vi nút "Xử lý đơn" (`►`) GIỮ NGUYÊN điều kiện hiện tại (người dùng không yêu cầu đổi).

## 2. Phạm vi

- **Làm:** `src/XuLyDonShopee.App/ViewModels/AccountsViewModel.cs` (điều kiện enable + luồng auto-start-rồi-chạy), có thể thêm helper nhỏ ở `AccountSession`/`IAccountSession` nếu cần tín hiệu "sẵn sàng" (executor quyết sau khảo sát). KHÔNG đụng `AccountsView.axaml` (binding `IsEnabled` giữ nguyên tên `CanSyncOrders`/`CanCheckOrders`), KHÔNG đụng màn Đơn hàng (việc khác đang làm ở cây chính), KHÔNG đổi hành vi nút Xử lý đơn/Mở trang bán hàng/Dừng.
- **Làm trong WORKTREE** — tuyệt đối không đọc/ghi file cây làm việc chính.

## 3. Các bước thực hiện

1. **Khảo sát hiện trạng** (bắt buộc trước khi viết): `CanSyncOrders`/`CanCheckOrders` + `SyncOrdersCommand`/`CheckOrdersCommand` hiện tại; đường `OpenSellerCommand` mở phiên qua manager (`_services.Sessions...`); `AccountSession.RunAsync` — tìm TÍN HIỆU "phiên sẵn sàng thao tác" tốt nhất: tiêu chí là **luồng tự-đăng-nhập + lượt đọc đơn đầu tiên đã xong** (gợi ý: `ToShipCount != null` sau khi vòng theo dõi đọc được số "Chờ Lấy Hàng" lần đầu — chứng tỏ đã đăng nhập xong và trang chủ ổn định; hoặc cờ/StatusText nội bộ nào rõ hơn — executor chọn, ghi rõ lý do; nếu cần expose thêm property/method chỉ-đọc trên `IAccountSession` thì thêm gọn).
2. **Điều kiện enable mới**: `CanSyncOrders`/`CanCheckOrders` = "đang chọn một tài khoản ĐÃ LƯU" (không phụ thuộc phiên chạy; giữ các notify chỗ cũ + thêm chỗ cần thiết như `OnSelectedRowChanged`). Tài khoản mới chưa lưu (IsNew) → vẫn disable.
3. **Luồng command mới** (dùng chung cho cả 2 nút — tách private helper, tham số là hành động):
   - Chụp `accountId` (và email cho log) TRƯỚC mọi await (bài học `viewmodel-mutable-field-after-await`); toàn bộ luồng sau đó bám theo **tài khoản lúc bấm** (session theo accountId), KHÔNG đọc lại `SelectedRow` sau await.
   - Session đã `Running` → chạy hành động như hiện tại (đường cũ giữ nguyên).
   - Session chưa chạy → StatusText/log `"Phiên chưa mở — đang mở trang bán hàng trước khi {tên hành động}..."` → mở phiên đúng đường `OpenSellerCommand` đang dùng (qua manager, chạy nền) → **chờ sẵn sàng**: poll (~1s/lượt) tới khi tín hiệu ở bước 1 đạt, timeout **5 phút**; phiên chuyển Stopped/Error giữa chừng → dừng chờ, báo `"Không mở được phiên — {StatusText/LastError}"`.
   - Sẵn sàng → chạy hành động (`session.SyncOrdersAsync()` / `session.CheckOrdersAsync()`), báo kết quả như hiện tại; timeout → `"Phiên chưa sẵn sàng sau 5 phút (có thể cần đăng nhập tay) — thử lại sau."` (KHÔNG chạy hành động).
   - Chống bấm đúp: trong lúc một lượt auto-start-rồi-chạy đang diễn ra cho tài khoản đó, lượt bấm mới cùng nút → bỏ qua nhẹ nhàng (cờ/guard trong VM; nêu cách trong báo cáo).
4. **Test**: logic thuần nào tách được (ví dụ hàm quyết định trạng thái enable, hay state-machine chờ-sẵn-sàng nếu tách thuần được) → unit test; phần phụ thuộc session/dispatcher ghi rõ phủ bằng đọc code + build. Không ép mock lớn.

## 4. Tiêu chí nghiệm thu

- [ ] Build 0 lỗi 0 warning; test pass toàn bộ (428 + mới nếu có; WDAC chặn đồng loạt → ghi rõ).
- [ ] Đọc code: 2 nút enable theo tài-khoản-đã-lưu; đường session-đang-chạy không đổi hành vi; đường auto-start chờ đúng tín hiệu sẵn sàng + timeout + guard bấm đúp; không đọc SelectedRow/_editingId sau await; AXAML không đổi.
- [ ] Smoke thật (người dùng): tài khoản CHƯA mở phiên → bấm ⇊ (hoặc ⟳) → app tự mở Brave, đăng nhập, rồi tự chạy sync/kiểm tra và báo kết quả; tài khoản ĐANG chạy → hành vi như trước. Executor ghi rõ chờ người dùng.

## 5. Rủi ro & lưu ý

- **Tín hiệu "sẵn sàng" phải chắc chắn nằm SAU khi luồng tự-đăng-nhập kiểu người xong** — chọn sai thì hai luồng chuột giẫm nhau (lỗi khó tái hiện). Ghi rõ căn cứ code trong báo cáo.
- Đăng nhập có thể cần thao tác tay (OTP/captcha) → timeout 5 phút phải báo rõ ràng, không treo nút mãi.
- `_navigating` của session vẫn là lớp loại trừ cuối (SyncOrdersAsync/CheckOrdersAsync đã tự guard) — luồng mới KHÔNG bỏ lớp nào hiện có.
- Worktree: đường dẫn plan tương đối `plans/…`; không đụng cây chính.

---

## Báo cáo thực thi (Opus điền sau khi xong)

<Opus dán báo cáo cuối vào đây hoặc Fable tổng hợp lại sau nghiệm thu.>
