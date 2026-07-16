# Plan: Bước lưu phiếu chỉ giữ file PDF — bỏ ảnh chụp màn hình PNG

- **Ngày:** 2026-07-16
- **Trạng thái:** đang làm
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`, worktree)

## 1. Bối cảnh & mục tiêu

`SaveSlipAsync` (`src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs`) hiện lưu MỖI ĐƠN 2 file vào `D:\Phieu-giao-hang`:
- Bước d: **PNG chụp màn hình cả trang** (`<mã đơn>.png`, FullPage + retry viewport, biến `pngSaved`) — từng là artifact chính khi PDF render còn trắng.
- Bước e: **PDF** — e0 fetch blob PDF GỐC của Shopee (từ iframe) → e1 GET src http → e2 `printToPDF` + ngưỡng `MinRealSlipPdfBytes` → e3 GET page-URL.

Từ khi có e0 (các lượt chạy thật 16:41 / 18:12 / 22:25 ngày 16/7), **PDF gốc từ blob ra ổn định (~110KB, đúng khổ phiếu, chỉ chứa phiếu)** — ảnh PNG thành thừa. Người dùng yêu cầu: **bỏ ảnh chụp, chỉ giữ file PDF**.

## 2. Phạm vi

- **Làm:** chỉ `src/XuLyDonShopee.Core/Services/ShopeeLoginService.cs` (xóa khối chụp PNG + cập nhật doc/log liên quan).
- **Không làm:** KHÔNG đụng bước b chờ-nội-dung (vẫn cần cho blob/printToPDF chín), log chẩn đoán DOM, chuỗi e0→e3, ngưỡng chống PDF trắng, đóng tab, vòng xử lý đơn; KHÔNG xóa các file `.png` cũ trên đĩa; KHÔNG đụng các file đang được việc khác sửa (`AccountsView.axaml`, `AccountsViewModel.cs`, `ActivityLog.cs`).

## 3. Các bước thực hiện

1. **Xóa bước d** trong `SaveSlipAsync`: toàn bộ khối chụp PNG (ScreenshotAsync FullPage=true + retry FullPage=false, log "Đã chụp phiếu (PNG)...", "Chụp PNG FullPage lỗi...", "Cảnh báo: chụp PNG cả hai lần đều lỗi...") và biến `pngSaved`.
2. **Bước f** (tổng kết): điều kiện "đã lưu phiếu" giờ CHỈ là `pdfReal`; câu cảnh báo đổi thành `"Cảnh báo: CHƯA lưu được phiếu PDF — kiểm tra tay trong Brave."`.
3. **Cập nhật doc-comment/comment**:
   - Doc `SaveSlipAsync`: bỏ mô tả PNG/artifact chính-phụ — giờ mô tả: chờ nội dung → lấy PDF (e0 blob GỐC ưu tiên → e1 → e2 ngưỡng → e3) → đóng tab.
   - Doc `ProcessFirstOrderAsync` trên interface (~dòng 123–124, đoạn "CHỤP màn hình phiếu (PNG) + lưu PDF thật về downloadDir"): sửa thành "lưu phiếu PDF (ưu tiên bản GỐC từ blob) về downloadDir".
   - Grep cả file những chỗ nhắc "PNG"/"chụp" thuộc luồng lưu phiếu để cập nhật cho khớp (KHÔNG đụng chỗ nhắc PNG ở file khác nếu có).
4. Nếu sau xóa còn helper/`using` nào mồ côi chỉ phục vụ chụp PNG → xóa theo (kiểm bằng grep/build).

## 4. Tiêu chí nghiệm thu

- [ ] `dotnet build XuLyDonShopee.sln -c Debug`: 0 lỗi, 0 warning mới.
- [ ] `dotnet test`: toàn bộ pass (400 ở nền worktree này; WDAC chặn đồng loạt → ghi rõ).
- [ ] Grep `ScreenshotAsync|pngSaved|\.png` trong `ShopeeLoginService.cs` → không còn (trừ khi xuất hiện ở ngữ cảnh không thuộc luồng lưu phiếu — ghi rõ nếu có).
- [ ] Đọc code: bước b/chẩn đoán DOM/e0→e3/đóng tab giữ nguyên; chỉ mất nhánh PNG.
- [ ] Smoke thật (người dùng): xử lý 1 đơn → `D:\Phieu-giao-hang` chỉ sinh `<mã đơn>.pdf` (không còn `.png` mới), PDF mở ra đúng phiếu. Executor ghi rõ chờ người dùng.

## 5. Rủi ro & lưu ý

- **Làm trong WORKTREE** — mọi đường dẫn quy về thư mục làm việc của agent; TUYỆT ĐỐI không đọc/ghi file của cây làm việc chính.
- PDF giờ là artifact DUY NHẤT: giữ nguyên mọi lớp dự phòng e0→e3 và cảnh báo khi cả 4 fail — không được nuốt im.
- `OperationCanceledException` ném xuyên như hiện tại ở mọi nhánh còn lại.

---

## Báo cáo thực thi (Opus điền sau khi xong)

<Opus dán báo cáo cuối vào đây hoặc Fable tổng hợp lại sau nghiệm thu.>
