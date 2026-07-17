# Plan: Màu sắc phân biệt trạng thái ở màn Đơn hàng

- **Ngày:** 2026-07-17
- **Trạng thái:** hoàn thành (đã merge; chờ người dùng xem màu)

## Báo cáo nghiệm thu (Fable)

Opus (worktree) thêm `OrderStatusPillConverter` (text status → brush theo keyword, vai trò bg/border/text) đăng ký ở App.axaml; cột "Trạng thái" OrdersView thành pill (nền nhạt + viền + chữ đậm SemiBold), ẩn khi status rỗng; KHÔNG đụng sync/lưu/OrderRowViewModel. Map: đỏ (hủy/trả hàng/hoàn tiền/không thành công/thất bại), xanh lá (hoàn thành/đã giao/thành công/đã nhận), xanh dương (đang giao/vận chuyển), amber (chờ/chuẩn bị/xác nhận), xám (không rõ). Fable phát hiện "không thành công" dính keyword "thành công" → Opus vá thêm "không thành công"/"thất bại" vào nhánh Cancelled (kiểm trước Done) + thêm OrderStatusPillConverterTests (15 test khóa logic). Build 0 warning + 533/533. Cột rộng 120→140. Smoke thị giác: CHỜ NGƯỜI DÙNG.
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`, worktree)

## 1. Bối cảnh & mục tiêu

Màn "Đơn hàng" (`OrdersView.axaml`) có cột "Trạng thái" hiển thị text thô. Sync ĐÃ lưu **nguyên văn mọi trạng thái** Shopee (JS `card.querySelector('.status-info-col .status')` → `SyncedOrder.Status`, không giới hạn) — Chờ lấy hàng, Đã hủy, Thành công, Hoàn hàng, Đang giao, Chờ xác nhận... và ComboBox lọc đã liệt kê distinct trạng thái từ DB. **KHÔNG cần sửa phần lưu/lọc** (đã đúng). Chỉ THÊM **màu phân biệt** cho cột trạng thái để nhìn nhanh.

Yêu cầu người dùng: mỗi trạng thái một màu — ví dụ Đã hủy đỏ, Chờ lấy hàng xanh...

## 2. Phạm vi

- **Làm:** converter text-trạng-thái → màu + đổi cột "Trạng thái" trong `OrdersView.axaml` sang hiển thị có màu (pill/badge). File: converter mới (App/Converters hoặc chỗ converter hiện có), `OrdersView.axaml`, đăng ký converter (App.axaml resources nếu cần).
- **Không làm:** KHÔNG đụng luồng Sync/lưu status (đã đúng); KHÔNG đụng OrderRowViewModel (việc Cài đặt đang đụng SlipPath ở đó — tránh xung đột); KHÔNG đổi cột khác. **Làm trong WORKTREE.**

## 3. Các bước thực hiện

1. **Khảo sát:** cách dự án đăng ký converter (App.axaml `<Application.Resources>` hay `StaticResource` cục bộ), mẫu badge trạng thái tài khoản (`StatusPill`/`StatusColor` converter cho AccountStatus) để theo phong cách.
2. **Converter `OrderStatusBrush`** (IValueConverter, nhận `string?` status → `IBrush`): chuẩn hóa text (lower, bỏ dấu thừa/khoảng trắng — tái dùng cách chuẩn hóa nếu có) rồi map theo **CHỨA keyword** (bền với biến thể text Shopee):
   - chứa "hủy" → ĐỎ (`#C62828`)
   - chứa "chờ lấy" → XANH DƯƠNG (`#1565C0`)
   - chứa "thành công" / "đã giao" / "hoàn thành" / "đã nhận" → XANH LÁ (`#2E7D32`)
   - chứa "hoàn" / "trả hàng" / "hoàn tiền" → CAM (`#EF6C00`)
   - chứa "đang giao" / "vận chuyển" / "đang gửi" → TÍM (`#6A1B9A`)
   - chứa "chờ xác nhận" / "chờ thanh toán" → XÁM ĐẬM (`#616161`)
   - còn lại (không rõ) → XÁM (`#757575`)
   - Hỗ trợ `ConverterParameter`: `"bg"` trả nền NHẠT (cùng tông, alpha thấp ~#1A…), mặc định/`"fg"` trả màu ĐẬM cho chữ+viền. (Một converter, 2 chế độ — mẫu `StatusPill` account.)
3. **`OrdersView.axaml`:** đổi cột "Trạng thái" từ `DataGridTextColumn` → `DataGridTemplateColumn` chứa **pill**: `Border` `CornerRadius` bo tròn, `Background` = converter(`Status`, `bg`), `Padding` nhỏ, bên trong `TextBlock` `{Binding Status}` `Foreground` = converter(`Status`, `fg`), FontSize ~12 SemiBold, `TextTrimming`. Giữ Header "Trạng thái", Width hợp lý. Các cột khác giữ nguyên.
4. **Đăng ký converter** đúng cách dự án đang dùng (resource key) để `{Binding Status, Converter={StaticResource OrderStatusBrush}, ConverterParameter=bg}` hoạt động ở `x:CompileBindings="False"` DataGrid (đối chiếu OrdersView hiện có).

## 4. Tiêu chí nghiệm thu

- [ ] Build 0 warning; test pass toàn bộ (không có test UI, không được vỡ).
- [ ] Đọc code: converter map theo keyword (bền biến thể), có chế độ bg/fg; cột trạng thái là pill màu; KHÔNG đụng sync/lưu/OrderRowViewModel; các cột khác nguyên.
- [ ] Smoke thị giác (người dùng): mỗi trạng thái một màu rõ (Đã hủy đỏ, Chờ lấy hàng xanh dương, Thành công xanh lá, Hoàn hàng cam...). Executor ghi rõ chờ người dùng.

## 5. Rủi ro & lưu ý

- Text trạng thái Shopee có thể có biến thể ("Đã hủy" / "Đã huỷ" / "Đã hủy một phần") — dùng CHỨA keyword + chuẩn hóa dấu để bền; "hủy một phần" vẫn ra đỏ (chấp nhận, cùng nhóm hủy).
- Nếu converter trả nền quá đậm làm chữ khó đọc → dùng nền alpha thấp + chữ đậm (mẫu badge account).
- **Worktree:** không đụng cây chính; gộp không xung đột với việc Cài đặt (khác file: việc này OrdersView.axaml + converter; Cài đặt đụng OrderRowViewModel/Settings/AccountSession).

---

## Báo cáo thực thi (Opus điền sau khi xong)

<Opus dán báo cáo cuối vào đây hoặc Fable tổng hợp lại sau nghiệm thu.>
