# Plan: Nút "Kiểm tra" & "Sync" HÀNG LOẠT ở panel danh sách tài khoản (áp dụng cho các tài khoản đang tick)

- **Ngày:** 2026-07-17
- **Trạng thái:** chờ (làm SAU khi plan `2026-07-17-nut-sync-kiem-tra-tu-mo-phien.md` hoàn thành & merge — tái dùng helper của nó)
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)

## 1. Bối cảnh & mục tiêu

Panel "Danh sách tài khoản" hiện có hàng 4 nút icon: `✓` Chọn toàn bộ, `►` Chạy đã chọn, `■` Dừng đã chọn, `✕` Dừng tất cả (thao tác trên các dòng đang TICK — `AccountRowViewModel.IsSelected`). Người dùng yêu cầu thêm **2 nút hàng loạt**: **⟳ Kiểm tra đã chọn** và **⇊ Sync đã chọn** — tick nhiều shop rồi bấm một phát, app kiểm tra/sync lần lượt TỪNG shop đã tick.

Plan `2026-07-17-nut-sync-kiem-tra-tu-mo-phien.md` (điều kiện tiên quyết) đã tạo luồng per-account "chưa mở phiên thì tự mở → chờ sẵn sàng → chạy hành động" cho 2 nút đơn ở form chi tiết. Nút hàng loạt DÙNG CHUNG luồng đó cho từng tài khoản.

## 2. Phạm vi

- **Làm:** `AccountsViewModel.cs` (2 command hàng loạt) + `AccountsView.axaml` (2 nút icon thêm vào hàng nút panel danh sách).
- **Không làm:** không đổi 4 nút hiện có; không đổi hành vi 2 nút đơn ở form; không thêm cấu hình.

## 3. Các bước thực hiện

1. **Khảo sát sau merge:** đọc helper auto-start-rồi-chạy per-account mà plan tự-mở-phiên đã tạo trong `AccountsViewModel` (tên/chữ ký thực tế), và mẫu `RunSelectedCommand` hiện có (cách duyệt các row tick + gọi manager).
2. **2 command mới** `CheckSelectedCommand`, `SyncSelectedCommand`:
   - Chụp danh sách `(accountId, email)` của các row đang tick MỘT LẦN trước mọi await (bài học `viewmodel-mutable-field-after-await`); rỗng → StatusText/log "Chưa tick tài khoản nào." và thôi.
   - Với TỪNG tài khoản trong danh sách đã chụp: chạy luồng per-account (tự mở phiên nếu cần → chờ sẵn sàng → hành động) — **các tài khoản chạy SONG SONG** (mỗi phiên Brave/proxy độc lập, nhất quán với "Chạy đã chọn"; log per-account đã tách theo shop). Dùng `Task.WhenAll` trên các lượt per-account; MỖI lượt tự nuốt lỗi + log riêng cho shop đó (một shop lỗi không phá các shop khác).
   - Guard chống bấm đúp cấp batch: đang có lượt batch cùng loại chạy → bỏ qua nhẹ nhàng (tận dụng/nhân rộng guard bấm-đúp per-account của plan trước nếu khớp).
   - Tổng kết khi xong hết: log "Đã kiểm tra/sync xong {n} tài khoản đã chọn." (kết quả chi tiết từng shop nằm trong log per-account của shop đó).
3. **2 nút icon** thêm vào hàng nút panel danh sách (sau `✕`, cùng style `iconAction`): `⟳` ToolTip "Kiểm tra đã chọn — kiểm số đơn Chờ lấy hàng của các tài khoản đang tick (tự mở phiên nếu chưa mở)"; `⇊` ToolTip "Sync đã chọn — sync đơn hàng của các tài khoản đang tick (tự mở phiên nếu chưa mở)". Enable khi danh sách có ít nhất 1 dòng (điều kiện tick kiểm lúc bấm — như các nút batch hiện có; đối chiếu mẫu RunSelected).
4. Test: logic thuần tách được (ví dụ chọn danh sách từ rows tick) thì thêm; không ép.

## 4. Tiêu chí nghiệm thu

- [ ] Build 0 lỗi 0 warning; test pass toàn bộ.
- [ ] Đọc code: chụp danh sách trước await; per-account độc lập lỗi; song song WhenAll; guard bấm đúp; 4 nút cũ + 2 nút đơn không đổi.
- [ ] Smoke thật (người dùng): tick 2 shop (1 đang chạy, 1 chưa mở) → bấm ⇊ hàng loạt: shop đang chạy sync ngay, shop chưa mở tự mở rồi sync; log từng shop hiện trong panel khi chọn shop đó. Tương tự ⟳. Executor ghi rõ chờ người dùng.

## 5. Rủi ro & lưu ý

- Nhiều phiên tự mở đồng loạt → máy nặng; chấp nhận (nhất quán "Chạy đã chọn" hiện có — người dùng chủ động tick bao nhiêu tùy máy).
- Mỗi lượt per-account phải bọc try/catch riêng trong WhenAll — MỘT shop timeout/lỗi không hủy các shop khác; OCE khi app đóng vẫn thoát sạch.
- Không giữ tham chiếu row qua await — chỉ dùng (accountId, email) đã chụp.

---

## Báo cáo thực thi (Opus điền sau khi xong)

<Opus dán báo cáo cuối vào đây hoặc Fable tổng hợp lại sau nghiệm thu.>
