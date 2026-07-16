# Plan: Mục "Chạy tự động" — chạy theo lô tự động (config riêng), lặp liên tục

- **Ngày:** 2026-07-17
- **Trạng thái:** hoàn thành (chờ người dùng smoke — đặt N=1 M=1 bật Sync tắt Xử lý để thử nhanh)

## Báo cáo nghiệm thu (Fable)

Opus làm: `AutoRunService` độc lập (API công khai Sessions.Start/Stop/IsRunning + poll `ReadyForActions`, KHÔNG đụng VM), vòng lặp lô N song song → Process/Sync (theo cờ) → Check (luôn) → đóng phiên do-mình-mở → nghỉ M → lặp; config 4 khóa vào bảng `settings` sẵn có (`AutoRunSettings.Normalize` kẹp [1,20]/[1,1440]); mục nav "Chạy tự động" index 2 (dời Proxy→3/Cài đặt→4); `AutoRunBatcher`/`AutoRunPlan` thuần + test. Panel đối kháng giữ 1 lỗi CAO (Fable tự đọc xác nhận): Dừng treo vô thời hạn khi autorun chạy hành động dài trên phiên NGƯỜI DÙNG tự mở (không đóng được) → Opus vá: (1a) `StopAsync` chờ loop có timeout 8s; (1b) autorun BỎ QUA tài khoản đang có phiên tay (chỉ thao tác phiên do mình mở → CloseAllOpenedByMe đóng được hết → không kẹt) + (2) kẹp MAX N/M chống tràn Task.Delay/mở quá nhiều Brave. Build Debug+Release 0 warning + 518/518 test. Smoke: CHỜ NGƯỜI DÙNG.
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)

## 1. Bối cảnh & mục tiêu

Người dùng muốn một **mục nav riêng bên trái tên "Chạy tự động"** (cạnh Tài khoản / Đơn hàng / Proxy / Cài đặt) chứa **cấu hình + điều khiển** một bộ chạy tự động theo LÔ. Quyết định đã chốt qua hỏi-đáp:

- **Phạm vi:** TẤT CẢ tài khoản đã lưu tham gia vòng tự động.
- **Mô hình lô:** mỗi lô mở **N tài khoản** (song song) → làm hành động → **ĐÓNG hết phiên của lô** → nghỉ **M phút** → lô kế tiếp N tài khoản khác trong danh sách. (Tối đa N Brave mở cùng lúc → nhẹ máy.)
- **Lặp:** hết lô cuối → nghỉ M phút → **quay lại lô đầu, lặp liên tục** tới khi bấm Dừng.
- **Hành động mỗi tài khoản:**
  - **Kiểm tra đơn: LUÔN chạy** (đọc số "Chờ Lấy Hàng") — không cần bật.
  - **Sync đơn hàng: CHỈ khi config bật.**
  - **Xử lý đơn: CHỈ khi config bật.**
- **Config (người dùng đặt trong mục này):** N (số tài khoản/lô), M (phút nghỉ giữa các lô), bật/tắt Sync, bật/tắt Xử lý đơn.

## 2. Phạm vi

- **Làm:** bảng/lưu config (settings), `AutoRunViewModel` + `AutoRunView` (mục nav mới), scheduler chạy nền (`AutoRunService` hoặc trong VM — quyết sau khảo sát), nút Bắt đầu/Dừng + trạng thái tiến trình. Tái dùng luồng per-account của plan tự-mở-phiên.
- **Không làm:** không đổi luồng Xử lý đơn/Sync/Kiểm tra lõi (chỉ ĐIỀU PHỐI gọi chúng); không đụng màn Đơn hàng; không lịch cron hệ điều hành (chạy trong app, bấm Dừng là dừng).

## 3. Các bước thực hiện

### A. Lưu config (khảo sát pattern SettingsViewModel/bảng settings hiện có TRƯỚC)

1. Nếu dự án đã có cơ chế settings key-value/bảng config → thêm các khóa: `autorun_batch_size` (N, mặc định 3, min 1), `autorun_gap_minutes` (M, mặc định 15, min 1), `autorun_do_sync` (bool, mặc định false), `autorun_do_process` (bool, mặc định false). Nếu chưa có → tạo bảng `app_settings(key TEXT PRIMARY KEY, value TEXT)` + repository nhỏ (theo pattern repo hiện có). Không lưu trạng thái "đang chạy" vào DB (trạng thái runtime, không bền).

### B. Scheduler (nền, hủy được)

1. `AutoRunService` (App/Services) hoặc logic trong `AutoRunViewModel` — chọn theo pattern (ưu tiên service testable, VM chỉ bind). Vòng chạy dưới `Task.Run` + `CancellationTokenSource` (Bắt đầu tạo, Dừng cancel + chờ thoát sạch như `AccountSession.StopAsync`).
2. Vòng:
   - Lấy snapshot danh sách tất cả tài khoản đã lưu (id + email) tại đầu MỖI lượt (người dùng có thể thêm/xóa tài khoản giữa chừng — đọc lại mỗi vòng ngoài).
   - Chia thành các lô N. Với mỗi lô: chạy SONG SONG per-account cho các tài khoản trong lô — mỗi tài khoản: **mở phiên nếu chưa mở → chờ sẵn sàng** (tái dùng helper plan tự-mở-phiên) → **Xử lý đơn (nếu bật)** → **Sync (nếu bật)** → **Kiểm tra (luôn)**; mỗi tài khoản bọc try/catch riêng + log per-account (1 tài khoản lỗi không phá lô). Thứ tự Xử lý→Sync→Kiểm tra: arrange đơn trước, rồi lưu snapshot sau xử lý, rồi đọc số cuối.
   - Lô xong (WhenAll) → **ĐÓNG phiên các tài khoản của lô** (qua manager StopAsync) → chờ đóng sạch.
   - Nghỉ M phút (`Task.Delay` truyền ct — Dừng cắt ngay).
   - Hết các lô của lượt → nghỉ M phút → lặp lại từ snapshot mới.
3. Cờ trạng thái: `IsRunning`, `CurrentPhase` (ví dụ "Lô 2/5 — đang xử lý", "Nghỉ 15' trước lô kế"), số liệu tối thiểu để người dùng biết đang ở đâu.

### C. UI — mục nav "Chạy tự động"

1. `MainViewModel`: thêm `NavItem("Chạy tự động", "▶")` (hoặc glyph text-only phù hợp — executor chọn) vào `NavItems` (vị trí hợp lý, ví dụ sau "Đơn hàng"); cập nhật switch index điều hướng (cẩn thận dời index các mục sau — đối chiếu code hiện tại sau khi màn Đơn hàng đã merge).
2. `AutoRunView.axaml` + `AutoRunViewModel`: 
   - Ô cấu hình: NumericUpDown/TextBox N, M; 2 CheckBox "Tự Sync đơn hàng", "Tự Xử lý đơn"; nút "Lưu cấu hình" (hoặc auto-lưu khi đổi). Ghi chú rõ "Kiểm tra đơn luôn tự chạy".
   - Nút "Bắt đầu chạy tự động" / "Dừng" (toggle theo IsRunning).
   - Vùng trạng thái: CurrentPhase + có thể danh sách tài khoản lô hiện tại. (Log chi tiết vẫn ở panel log per-account màn Tài khoản.)
3. Enable/disable hợp lý: đang chạy → khóa ô config (tránh đổi giữa chừng), nút chuyển thành Dừng.

### D. Kiểm chứng

1. Build 0 warning; test pass (thêm test thuần cho: chia lô từ danh sách (N, số dư), đọc/ghi config, quyết định thứ tự hành động theo cờ). Phần scheduler phụ thuộc session/thời gian ghi rõ phủ bằng đọc code.
2. Smoke thật (người dùng): đặt N=1, M=1, bật Sync, tắt Xử lý → Bắt đầu → app lần lượt mở từng tài khoản, kiểm tra + sync, đóng, nghỉ 1', sang tài khoản kế; hết thì lặp lại; bấm Dừng → dừng sạch (đóng phiên đang mở). Executor ghi rõ chờ người dùng.

## 4. Tiêu chí nghiệm thu

- [ ] Build 0 lỗi 0 warning; test pass (+ test mới chia lô/config/thứ tự hành động).
- [ ] Đọc code: check LUÔN chạy, sync/xử lý theo cờ config; đóng phiên sau mỗi lô; lặp liên tục; Dừng hủy sạch (ct xuyên mọi Delay/vòng); per-account lỗi độc lập; không đọc trạng thái mutable sau await; tái dùng đúng helper per-account (không copy luồng mở-phiên).
- [ ] Smoke: chờ người dùng.

## 5. Rủi ro & lưu ý

- **Đóng phiên sau lô có thể đóng shop người dùng đang xem thủ công** — đây là chế độ máy tự chạy, chấp nhận; nhưng cân nhắc: nếu một tài khoản của lô ĐÃ được người dùng mở thủ công từ trước, scheduler có nên đóng nó khi hết lô không? → An toàn nhất: scheduler CHỈ đóng những phiên do CHÍNH nó mở (đánh dấu phiên mở-bởi-autorun); phiên người dùng tự mở thì để nguyên. Executor cân nhắc, ghi rõ cách chọn.
- **Xử lý đơn GHI lên Shopee** (arrange + in phiếu) — chỉ chạy khi người dùng chủ động bật cờ; mặc định TẮT.
- Mở N Brave song song mỗi lô + proxy watchdog mỗi phiên → tải máy; N do người dùng đặt, cảnh báo nhẹ trong UI nếu N lớn (tùy chọn).
- Dừng phải cắt được ở MỌI điểm chờ (đang mở phiên, đang chờ sẵn sàng 5', đang nghỉ M phút) — ct truyền xuyên.
- Tương tác với watchdog/nhịp 30' của từng phiên: phiên do autorun mở vẫn có vòng RunAsync riêng; hành động autorun đi qua `_navigating` guard như nút thủ công.
- Hai phiên Claude cùng repo — executor `git status` trước khi sửa; thấy lạ → DỪNG báo.

---

## Báo cáo thực thi (Opus điền sau khi xong)

<Opus dán báo cáo cuối vào đây hoặc Fable tổng hợp lại sau nghiệm thu.>
