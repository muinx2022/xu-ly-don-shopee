# Xử lý đơn Shopee

Dự án xử lý đơn hàng Shopee. Người dùng giao tiếp bằng **tiếng Việt** — mọi báo cáo, giải thích cho người dùng phải viết bằng tiếng Việt.

## Quy trình làm việc (BẮT BUỘC)

Dự án này dùng mô hình 2 tầng: **Fable lập kế hoạch → Opus thực thi**.

### Vai trò

| Vai trò | Model | Nhiệm vụ |
|---|---|---|
| Kiến trúc sư (phiên chính) | Fable | Đọc yêu cầu, khảo sát mã nguồn, tổng hợp thông tin, viết plan, nghiệm thu kết quả |
| Thực thi (subagent `opus-executor`) | Opus | Đọc plan và triển khai đúng theo plan, báo cáo lại |

### Các bước

1. **Tiếp nhận & tổng hợp** — Fable đọc kỹ yêu cầu của người dùng, khảo sát mã nguồn hiện có (Glob/Grep/Read hoặc agent Explore), làm rõ ràng buộc. Nếu yêu cầu mơ hồ ở điểm quyết định, hỏi lại người dùng bằng AskUserQuestion **trước khi** viết plan.
2. **Viết plan** — Lưu tại `plans/YYYY-MM-DD-<ten-viec>.md` theo mẫu [plans/TEMPLATE.md](plans/TEMPLATE.md). Plan phải đủ chi tiết để Opus thực thi được mà không cần đoán: liệt kê file cần tạo/sửa, hành vi mong muốn, tiêu chí nghiệm thu.
3. **Giao việc cho Opus** — Gọi Agent tool với `subagent_type: "opus-executor"`, trong prompt ghi rõ đường dẫn tuyệt đối của file plan và yêu cầu thực thi toàn bộ plan đó. Không tự thực thi plan bằng phiên chính.
4. **Nghiệm thu** — Fable đọc báo cáo của Opus, đối chiếu với tiêu chí nghiệm thu trong plan (đọc lại code/chạy thử nếu cần). Nếu chưa đạt, dùng SendMessage gửi phản hồi cho chính agent đó để sửa tiếp (giữ nguyên ngữ cảnh). Cuối cùng tổng kết cho người dùng bằng tiếng Việt: đã làm gì, kết quả kiểm tra, còn gì chưa xong.
5. **Cập nhật trạng thái plan** — Sau khi nghiệm thu, cập nhật mục `Trạng thái` ở đầu file plan (`đang làm` → `hoàn thành` / `dừng`).

### Nhiều việc song song trong một phiên

Người dùng giao nhiều việc liên tiếp trong **cùng một cửa sổ chat**, không cần chờ việc trước xong. Fable là tổng điều phối:

1. **Giao việc chạy nền** — Mỗi việc vẫn đi qua plan → `opus-executor`, nhưng agent chạy nền (mặc định của Agent tool). Fable báo ngay cho người dùng "đã giao việc X" rồi tiếp tục nhận việc mới; khi agent xong sẽ có thông báo để nghiệm thu.
2. **Kiểm tra tranh chấp trước khi giao** — Trước khi giao việc mới, đối chiếu danh sách file sẽ sửa (ghi trong plan) với các việc đang chạy:
   - **Không đụng file nhau** → giao thẳng, các agent làm song song trên working tree chính.
   - **Có đụng file** → giao việc mới với `isolation: "worktree"` để agent làm trên bản sao riêng, không phá việc đang chạy. KHÔNG trì hoãn việc, cũng KHÔNG để hai agent sửa cùng file trên cùng working tree.
3. **Gộp kết quả** — Khi các việc tranh chấp đều xong và đã nghiệm thu: commit phần trên working tree chính, rồi gộp thay đổi từ worktree về (merge nhánh worktree). Conflict nhỏ thì Fable tự xử lý; conflict lớn/nhiều file thì giao một agent merge riêng, xong vẫn phải nghiệm thu lại (build + test).
4. **Commit sau mỗi việc hoàn thành** — Sau khi nghiệm thu đạt, commit ngay với message tiếng Việt không dấu mô tả việc đó. Điều này bắt buộc để cơ chế worktree/merge hoạt động và để quay lui được khi agent làm hỏng.

### Khi nào KHÔNG cần plan

Làm trực tiếp bằng phiên chính (không giao Opus) khi:
- Câu hỏi thuần túy, giải thích, đọc hiểu code — không sửa file.
- Sửa lặt vặt 1–2 dòng, đổi tên, sửa chính tả.

Mọi việc còn lại (tính năng mới, sửa lỗi cần điều tra, thay đổi nhiều file) đều đi qua quy trình plan → Opus.

## Quy ước

- Plan viết bằng tiếng Việt, tên file dùng chữ không dấu (vd: `2026-07-13-doc-file-excel-don-hang.md`).
- Mỗi plan là một đơn vị bàn giao trọn vẹn; việc lớn thì tách thành nhiều plan nhỏ, giao lần lượt.
- Không xóa plan cũ — giữ lại làm lịch sử quyết định của dự án.
