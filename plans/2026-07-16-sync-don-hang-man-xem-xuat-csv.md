# Plan: Sync Đơn hàng (phần 2) — màn "Đơn hàng" xem trong app + xuất CSV/Excel

- **Ngày:** 2026-07-16
- **Trạng thái:** chờ (làm SAU khi phần 1 `2026-07-16-sync-don-hang-thu-thap-luu-db.md` hoàn thành & merge)
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)

## 1. Bối cảnh & mục tiêu

Phần 1 đã có bảng `orders` + `OrdersRepository` + nút Sync. Phần này cho người dùng XEM dữ liệu đã sync trong app và XUẤT file CSV (mở bằng Excel).

## 2. Phạm vi

- **Làm:** màn "Đơn hàng" (mục mới trong sidebar trái MainWindow, cạnh Tài khoản/Proxy/Cài đặt): bảng DataGrid + bộ lọc + nút "Xuất CSV". Repository bổ sung method truy vấn.
- **Không làm:** không sửa/xóa đơn từ app (chỉ xem); không đồng bộ tự động theo lịch (bấm tay từ màn Tài khoản như phần 1).

## 3. Các bước thực hiện

1. **Repository**: `Query(accountId?, statusFilter?, searchText?)` → List (lọc SQL: account, status LIKE, search theo order_sn/buyer/item_summary); `AllStatuses(accountId?)` cho ComboBox lọc.
2. **`OrdersViewModel` + `OrdersView.axaml`** (theo pattern ProxiesView — dự án đã dùng DataGrid ở đó):
   - Bộ lọc trên đầu: ComboBox Tài khoản (tất cả/số dư từng account — nạp từ Accounts), ComboBox Trạng thái, ô tìm kiếm (mã đơn/người mua/sản phẩm), nút "Làm mới", nút "Xuất CSV".
   - DataGrid cột: Tài khoản, Mã đơn, Người mua, Sản phẩm (item_summary + "(+n)" nếu item_count>1), Tổng tiền (định dạng ₫), Thanh toán, Trạng thái, Mô tả/Lý do hủy, ĐVVC, Mã vận đơn, Sync lúc. Đơn giản, đọc-chỉ.
   - Dòng tổng số đơn đang hiển thị.
3. **MainWindow**: thêm mục nav "Đơn hàng" (icon text-only, theo pattern mục Proxy) → OrdersView.
4. **Xuất CSV**: nút "Xuất CSV" mở SaveFileDialog (mặc định `don-hang-{account|tatca}-{yyyyMMdd-HHmm}.csv`); ghi UTF-8 **có BOM** (Excel mở tiếng Việt đúng), escape RFC4180 (quote khi có `,`/`"`/xuống dòng), xuất đúng các dòng ĐANG lọc; log/StatusText "Đã xuất N đơn → <đường dẫn>". Hàm dựng nội dung CSV tách static thuần + unit test (escape, BOM, header).
5. Build + test; smoke người dùng: sync xong mở màn Đơn hàng thấy dữ liệu, lọc/tìm chạy, xuất CSV mở Excel không vỡ tiếng Việt.

## 4. Tiêu chí nghiệm thu

- [ ] Build 0 warning; test pass (thêm test CSV thuần).
- [ ] Màn Đơn hàng hiển thị đúng dữ liệu bảng orders, lọc theo tài khoản/trạng thái/tìm kiếm hoạt động (đọc code + smoke).
- [ ] File CSV xuất ra: UTF-8 BOM, escape chuẩn, mở Excel tiếng Việt không vỡ — chờ người dùng smoke.

## 5. Rủi ro & lưu ý

- DataGrid Avalonia cần theme/namespace đúng như ProxiesView đang dùng — copy pattern, đừng chế mới.
- SaveFileDialog: dùng StorageProvider API như dự án đang dùng (nếu có chỗ nào đã dùng — khảo sát; chưa có thì TopLevel.StorageProvider chuẩn Avalonia 11).
- items_json chỉ để dành — màn này dùng item_summary, KHÔNG parse json từng dòng khi render (hiệu năng).

---

## Báo cáo thực thi (Opus điền sau khi xong)

<Opus dán báo cáo cuối vào đây hoặc Fable tổng hợp lại sau nghiệm thu.>
