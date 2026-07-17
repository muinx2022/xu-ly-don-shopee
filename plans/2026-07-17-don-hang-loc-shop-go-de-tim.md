# Plan: Màn Đơn hàng — ô lọc theo shop cho GÕ ĐỂ TÌM (searchable) thay ComboBox thường

- **Ngày:** 2026-07-17
- **Trạng thái:** hoàn thành (đã merge; chờ người dùng smoke gõ tìm shop)
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)

## 1. Bối cảnh & mục tiêu

Màn "Đơn hàng" (`OrdersView.axaml` + `OrdersViewModel.cs`) có bộ lọc theo tài khoản là **ComboBox thường**: `AccountOptions` (`ObservableCollection<AccountFilterOption>` — mục đầu `(null, "Tất cả tài khoản")` rồi từng shop theo Email) bind với `SelectedAccount`; `OnSelectedAccountChanged` → `ReloadStatuses` + `Apply` (lọc bảng). Khi có **~100 shop**, ComboBox phải cuộn tay tìm — bất tiện. Người dùng muốn **gõ tên shop để tìm nhanh** (lọc gợi ý theo chữ gõ) rồi chọn.

## 2. Phạm vi

- **Làm:** đổi ô lọc TÀI KHOẢN ở `OrdersView.axaml` từ `ComboBox` sang **`AutoCompleteBox`** (gõ → lọc gợi ý theo Email/label → chọn), giữ nguyên logic lọc/`SelectedAccount`/`AccountOptions` ở VM (chỉnh tối thiểu nếu cần cho AutoCompleteBox). File: `OrdersView.axaml`, có thể `OrdersViewModel.cs` (thuộc tính phụ trợ cho AutoCompleteBox nếu cần).
- **Không làm:** KHÔNG đổi ComboBox TRẠNG THÁI (ít mục, không cần search); KHÔNG đổi ô tìm kiếm text (mã đơn/người mua/sản phẩm) hiện có; KHÔNG đụng dữ liệu/CSV/cột/màu/double-click.

## 3. Các bước thực hiện

1. **Khảo sát `AutoCompleteBox` Avalonia 11.2** (bản dự án): `ItemsSource`, `FilterMode` (`Contains`, không phân biệt hoa/thường — `ContainsOrdinal`/`Contains`), `SelectedItem`, `Text`, `ValueMemberBinding`/`ItemTemplate` để hiển thị `Label`. Đối chiếu xem dự án đã dùng AutoCompleteBox chỗ nào chưa (mẫu). Nếu bản Avalonia không có AutoCompleteBox tiện thì fallback: ComboBox `IsTextSearchEnabled` + editable — nhưng ưu tiên AutoCompleteBox.
2. **`OrdersView.axaml`:** thay `ComboBox` lọc tài khoản bằng `AutoCompleteBox`:
   - `ItemsSource="{Binding AccountOptions}"`.
   - Hiển thị theo `Label`: `ValueMemberBinding` trỏ `Label` (hoặc `ItemTemplate` TextBlock `{Binding Label}` + `FilterMode`/`TextSelector` phù hợp) để gõ khớp theo Email.
   - `SelectedItem="{Binding SelectedAccount}"` (2 chiều) → chọn 1 gợi ý là lọc ngay như cũ.
   - `FilterMode="Contains"` (khớp CHỨA, không phân biệt hoa/thường) — gõ "alina" ra "alina99.store".
   - `Watermark="Gõ tên shop để tìm… (trống = tất cả)"`, bề rộng khớp ComboBox cũ.
3. **Hành vi "Tất cả" + xóa trống:** khi ô rỗng / người dùng xóa hết → `SelectedAccount` về mục `(null, "Tất cả tài khoản")` (hiện tất cả). Xử lý cách gọn nhất theo AutoCompleteBox (ví dụ: nếu `SelectedItem` null do gõ dở/không khớp → coi như "Tất cả"; hoặc theo dõi `Text` rỗng → set về option "Tất cả"). Đảm bảo KHÔNG kẹt trạng thái "gõ dở không khớp" làm bảng trống vô lý — ưu tiên: chỉ đổi lọc khi `SelectedItem` là một option hợp lệ; gõ dở chưa chọn thì giữ lọc hiện tại.
4. **Giữ đồng bộ:** `Reload()` dựng lại `AccountOptions` (thêm/xóa shop) — AutoCompleteBox tự cập nhật (ItemsSource là ObservableCollection). `SelectedAccount` sau reload vẫn giữ như logic hiện có.

## 4. Tiêu chí nghiệm thu

- [ ] Build 0 warning; test pass toàn bộ (không có test UI; logic Apply/AccountOptions ở VM giữ nguyên nên test cũ không vỡ).
- [ ] Đọc code: ô lọc tài khoản là AutoCompleteBox gõ-lọc theo Label, chọn → `SelectedAccount` → Apply như cũ; "Tất cả"/xóa trống xử lý đúng (không kẹt bảng trống); ComboBox trạng thái + ô tìm text không đổi.
- [ ] Smoke thật (người dùng): gõ vài chữ tên shop → danh sách gợi ý thu hẹp → chọn → bảng lọc đúng shop; xóa trống → về tất cả; thêm shop mới rồi Làm mới → gõ tìm thấy. Executor ghi rõ chờ người dùng.

## 5. Rủi ro & lưu ý

- `AutoCompleteBox` khác `ComboBox` ở chỗ có thể có `Text` KHÔNG khớp item nào (gõ dở) → phải tránh việc đó làm `SelectedAccount = null` rồi hiểu nhầm thành lọc rỗng; chỉ áp lọc khi chọn một option THẬT (hoặc null-về-"Tất cả" tường minh).
- `ValueMemberBinding`/hiển thị: đảm bảo gõ khớp theo Email (Label) chứ không theo `ToString()` mặc định của record.
- Giữ `AccountFilterOption`/`SelectedAccount`/`Apply` nguyên vẹn để không đụng logic lọc trạng thái/tìm kiếm.
- Cây chính (không agent song song lúc giao); `git status` sạch trước khi sửa.

---

## Báo cáo thực thi (Opus điền sau khi xong)

<Opus dán báo cáo cuối vào đây hoặc Fable tổng hợp lại sau nghiệm thu.>
