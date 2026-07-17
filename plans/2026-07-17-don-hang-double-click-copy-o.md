# Plan: Màn Đơn hàng — double-click một ô để copy text ô đó vào clipboard

- **Ngày:** 2026-07-17
- **Trạng thái:** hoàn thành (đã merge; chờ người dùng smoke)

## Báo cáo nghiệm thu (Fable)

Opus (cây chính) dùng `DataGrid.CellPointerPressed` (xác minh API qua metadata DLL Avalonia 11.2.8) + `ClickCount==2` + chuột trái; `CellTextExtractor.ExtractCellText` (Button→null bỏ ô "Phiếu"; TextBlock đầu tiên DFS visual+logical) + 9 test; copy `TopLevel.Clipboard` null-safe không async void; toast bằng Popup khai báo neo cell + DispatcherTimer 1.2s reset. Fable đọc handler + build/test 564/564. Điểm khác plan hợp lý: Popup thay Flyout (tránh chrome theme), chốt chuột trái, nhận-Button thay so chuỗi. Smoke: CHỜ NGƯỜI DÙNG.
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)

## 1. Bối cảnh & mục tiêu

Màn "Đơn hàng" (`OrdersView.axaml` + `.axaml.cs`) là một `DataGrid` (đọc-chỉ) nhiều cột: Tài khoản, Mã đơn, Phiếu (nút link), Người mua, Sản phẩm, Tổng tiền, Ước tính, Thanh toán, Trạng thái (pill), Mô tả/Lý do hủy, ĐVVC, Mã vận đơn, Sync lúc. Người dùng thao tác nhiều với mã đơn / mã vận đơn / người mua → muốn **double-click vào một ô là copy text của ô đó vào clipboard** (nhanh, khỏi bôi đen).

## 2. Phạm vi

- **Làm:** bắt double-click ô trong DataGrid màn Đơn hàng → copy text ô → báo nhẹ "đã copy". File: `OrdersView.axaml` (+ `.axaml.cs`), có thể thêm 1 property/method nhỏ ở `OrdersViewModel` cho thông báo (tái dùng `StatusMessage` sẵn có).
- **Không làm:** KHÔNG đụng dữ liệu/lọc/CSV/converter màu; KHÔNG làm chọn-nhiều-ô/copy-cả-dòng (chỉ 1 ô/lần); không đổi cột.

## 3. Các bước thực hiện

1. **Khảo sát API DataGrid Avalonia 11** (bản dự án đang dùng): cách bắt double-click TRÊN MỘT Ô và lấy text ô. Hai hướng, executor chọn cách bền + đơn giản:
   - **A (ưu tiên nếu có):** `DataGrid.CellPointerPressed` cho `DataGridCellPointerPressedEventArgs` (có `Cell`/`Column`/`Row`) — kết hợp `e.PointerPressedEventArgs.ClickCount == 2` để nhận double-click, rồi lấy text từ `Cell.Content` (thường `TextBlock`).
   - **B (fallback):** handler `DoubleTapped` ở DataGrid, từ `e.Source`/hit-test tìm tổ tiên `DataGridCell`, đọc `Content`.
2. **Lấy text ô (bền với template):** `DataGridCell.Content` có thể là:
   - `TextBlock` (cột text) → lấy `.Text`.
   - Cột template (Trạng thái = pill Border→TextBlock; Ước tính/Tổng tiền = TextBlock) → tìm `TextBlock` con ĐẦU TIÊN (duyệt visual tree) → `.Text`.
   - Cột "Phiếu" (nút "In phiếu") → KHÔNG có text dữ liệu ý nghĩa → bỏ qua (không copy chuỗi "In phiếu"); nhận biết bằng: text rỗng/null hoặc ô chứa `Button` → thôi.
   - Helper thuần `ExtractCellText(Control cell) : string?` (duyệt tìm TextBlock đầu, trả text; null nếu không có) — tách để test được.
3. **Copy + phản hồi bằng TOOLTIP NHỎ (theo yêu cầu người dùng):** có text không rỗng → `TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(text)` (bọc try/catch nuốt lỗi). Rồi hiện **tooltip/flyout nhỏ ngay tại ô vừa double-click** với nội dung `"Đã copy text {rút gọn ô ≤ ~60 ký tự}"`, **tự ẩn sau ~1.2s**. Cách làm (executor chọn bền nhất):
   - **Ưu tiên `Flyout`/`Popup` nhẹ** neo vào chính `DataGridCell` (`PlacementTarget = cell`, `Placement = Top`), một `Border` bo góc nền tối mờ + `TextBlock` chữ trắng nhỏ; mở rồi hẹn `DispatcherTimer` (~1200ms) đóng. Tránh dùng ToolTip mặc định (chỉ hiện khi hover).
   - KHÔNG dùng `StatusMessage` cho việc này (người dùng muốn tooltip tại chỗ, không phải dòng dưới thanh). Nếu vẫn muốn giữ StatusMessage như phụ trợ thì tùy — nhưng tooltip là chính.
4. **UX:** double-click ô trống / ô nút Phiếu → không làm gì (im lặng, không hiện tooltip). Giữ hành vi DataGrid mặc định (double-click không sửa vì grid read-only). Tooltip cũ (nếu đang hiện) → double-click ô khác thì thay bằng tooltip mới tại ô đó.

## 4. Tiêu chí nghiệm thu

- [ ] Build 0 warning; test pass toàn bộ (+ test cho `ExtractCellText` nếu tách được thuần — dựng TextBlock/Border→TextBlock/Button giả).
- [ ] Đọc code: bắt đúng double-click MỘT ô; lấy text bền với cột template (pill/nút); copy clipboard bọc lỗi; hiện tooltip nhỏ "Đã copy text {…}" tại ô + tự ẩn ~1.2s; ô nút Phiếu/ô trống → bỏ qua.
- [ ] Smoke thật (người dùng): double-click ô Mã đơn / Mã vận đơn / Người mua → dán ra chỗ khác thấy đúng text; hiện tooltip nhỏ "Đã copy text …" tại ô rồi tự tắt; double-click ô nút In phiếu không copy nhầm. Executor ghi rõ chờ người dùng.

## 5. Rủi ro & lưu ý

- API DataGrid Avalonia lấy "cell text" không có sẵn 1 hàm — phải duyệt visual tree tìm TextBlock; viết phòng thủ (null-safe), không ném khi ô lạ.
- Clipboard qua `TopLevel.Clipboard` có thể null (chưa gắn cây) → bọc null/try-catch.
- Đây là cây chính (không agent song song lúc giao) — nếu Fable báo có agent khác thì chuyển worktree.
- Không phá double-click mặc định (grid read-only nên vô hại).

---

## Báo cáo thực thi (Opus điền sau khi xong)

<Opus dán báo cáo cuối vào đây hoặc Fable tổng hợp lại sau nghiệm thu.>
