# Handoff: Thiết kế lại UI "Xử lý đơn Shopee" (Windows 11 Fluent, nền sáng)

## Overview
Ứng dụng desktop quản lý tài khoản bán hàng Shopee. Đây là bản thiết kế lại giao diện WinForm cũ (nền tối) sang phong cách **Windows 11 Fluent, nền sáng, sạch sẽ**, với màu nhấn **cam Shopee `#EE4D2D`**. Gói này bao gồm 2 màn hình: **Tài khoản** (master–detail) và **Cài đặt**.

## About the Design Files
Các file trong gói này là **tài liệu tham chiếu thiết kế viết bằng HTML** — prototype thể hiện diện mạo và hành vi mong muốn, **không phải code production để copy trực tiếp**. Nhiệm vụ là **dựng lại các thiết kế HTML này trong môi trường sẵn có của dự án** tại `D:\Projects\Xu-ly-don-shopee` (WinForms/WPF, WinUI, Electron/React, v.v.), sử dụng pattern và thư viện đã có của dự án. Nếu dự án là **WinForms/WPF .NET**, hãy tái tạo layout bằng control gốc + style tương ứng; nếu là **Electron/React**, tái tạo bằng component + CSS.

File `.dc.html` được viết bằng một framework component nội bộ (thẻ `<sc-for>`, `<sc-if>`, style inline). **Không port cú pháp này** — chỉ đọc để lấy layout, màu, spacing, copy chính xác.

## Fidelity
**High-fidelity (hifi).** Màu sắc, typography, spacing, bo góc, đổ bóng đều là giá trị cuối. Hãy tái tạo pixel-perfect bằng thư viện/pattern sẵn có của codebase.

## Screens / Views

### 1. Khung cửa sổ (chung cho mọi màn)
- **Kích thước tham chiếu**: 1400 × 902 px, bo góc ngoài 8px, đổ bóng lớn.
- **Nền cửa sổ**: gradient dọc `#f7f8fa → #f0f2f5`.
- **Title bar** (cao 40px): nền `rgba(255,255,255,0.55)` + backdrop-blur 20px, viền dưới `rgba(0,0,0,0.06)`.
  - Trái: icon app 20×20 bo 5px, gradient `#ff6b3d → #ee4d2d`, bên trong vòng tròn viền trắng 2px. Kế bên: chữ "Xử lý đơn Shopee" 12.5px/600, màu `#3a3a3a`.
  - Phải: 3 nút cửa sổ (minimize / maximize / close), mỗi nút rộng 46px. Nút close hover: nền `#e81123`, chữ trắng. Hai nút kia hover: `rgba(0,0,0,0.05)`.

### 2. Sidebar (cao toàn bộ, rộng 236px)
- Nền `rgba(255,255,255,0.35)`, viền phải `rgba(0,0,0,0.05)`, padding 8px.
- **Header**: "Xử lý đơn Shopee" 15px/700 màu `#1c1c1c`; phụ đề "Quản lý tài khoản bán hàng" 11.5px màu `#8a8a8a`.
- **Nav items** (cao 40px, bo 6px, gap icon–label 12px): Tài khoản (icon ◵), Proxy (icon ⇄), Cài đặt (icon ⚙).
  - Item **active**: nền `rgba(238,77,45,0.10)`, thanh pill dọc bên trái 3×18px bo 2px màu `#ee4d2d`, icon màu `#ee4d2d`, label 13.5px/600 màu `#c0341c`.
  - Item **thường**: nền trong suốt, icon `#6a6a6a`, label 13.5px/500 màu `#3a3a3a`.
- **Footer card** (dưới cùng): avatar tròn 30px gradient cam, "Shop Manager" 12px/600, "1 tài khoản" 10.5px `#8a8a8a`. Nền `rgba(255,255,255,0.6)`, viền `rgba(0,0,0,0.05)`, bo 6px.

### 3. Màn Tài khoản — cột danh sách (rộng 340px, viền phải `rgba(0,0,0,0.06)`, padding 24/20px)
- Tiêu đề "Danh sách tài khoản" 19px/700 màu `#1c1c1c`, letter-spacing -0.3px.
- **Ô tìm kiếm** (cao 36px, bo 6px): nền `#fff`, viền `rgba(0,0,0,0.10)`, viền dưới 2px `rgba(0,0,0,0.22)` (kiểu Fluent underline). Icon ⌕ tuyệt đối bên trái (left 12px), placeholder "Tìm theo email / ghi chú…" 13px màu `#9a9a9a`.
- **Thẻ tài khoản** (padding 11/12px, bo 8px, gap 11px):
  - Avatar vuông 34px bo 8px, gradient `#ff8a5c → #ee4d2d`, chữ cái đầu trắng 14px/700.
  - Email 13px/600 `#232323` (ellipsis 1 dòng). Dưới: chấm trạng thái 6px màu `#f5a623` + text trạng thái 11px `#7a7a7a`.
  - Thẻ **được chọn**: nền `#fff`, viền `rgba(238,77,45,0.35)`, shadow `0 1px 4px rgba(238,77,45,0.12)`.
- **Nút dưới cùng** (gap 8px): "+ Thêm tài khoản" (flex-1, cao 38px, nền `#ee4d2d`, chữ trắng 13px/600, bo 6px; hover `#e0431f`) + nút xóa 🗑 vuông 38px (nền trắng, viền `rgba(0,0,0,0.12)`, icon `#c0392b`; hover nền `#fdecea`).

### 4. Màn Tài khoản — cột chi tiết (flex-1, cuộn dọc, nội dung max-width 720px, padding 24/32px)
- **Header hàng**: trái "Chi tiết tài khoản" 19px/700 + phụ đề "Thông tin đăng nhập và trạng thái" 12px `#8a8a8a`. Phải: **badge trạng thái** pill (padding 6/12px, bo 20px, nền `#fff4e5`, viền `#ffd9a8`): chấm 7px `#f5a623` + "Chưa kiểm tra" 12px/600 màu `#b8720a`.
- **3 card** (mỗi card: nền `#fff`, viền `rgba(0,0,0,0.07)`, bo 10px, shadow `0 1px 3px rgba(0,0,0,0.04)`, padding 22/24px, gap dọc 16px). Mỗi card mở đầu bằng **tiêu đề section** 11px/700, uppercase, letter-spacing 0.6px, màu `#ee4d2d`.
  - **Card 1 — Thông tin đăng nhập**:
    - Label field: 12.5px/600 màu `#4a4a4a`, margin-bottom 6px.
    - Input (cao 38px, bo 6px, nền `#fbfbfb`, viền `rgba(0,0,0,0.10)`, chữ 13.5px `#1e1e1e`): field **focus/active** dùng viền dưới 2px `#ee4d2d`; field thường viền dưới 2px `rgba(0,0,0,0.22)`.
    - "Email (dùng để đăng nhập)" = `witkoxatlinpbgld@hotmail.com` (đang focus → underline cam).
    - Hàng 2 (gap 14px): "Mật khẩu" (có nút con mắt 👁/🙈 28px hover `rgba(0,0,0,0.05)`, toggle ẩn/hiện) + "Số điện thoại" (placeholder "Tùy chọn" màu `#a5a5a5`).
  - **Card 2 — Cookie đăng nhập**: header có nhãn phải "JSON · 1.2 KB" (11px monospace `#9a9a9a`). Khối code nền `#1e2430`, bo 8px, padding 14/16px, font monospace 12px, line-height 1.7, `white-space:pre`, max-height 150px cuộn. Syntax highlight: dấu ngoặc `#7dd3fc`, key `#a5b4fc`, string `#86efac`. Nội dung JSON cookie mẫu (Name SPC_CDS, Value GUID, Domain banhang.shopee.vn, Path /).
  - **Card 3 — Ghi chú + Trạng thái**: hàng gap 14px: "Ghi chú" (placeholder "Tùy chọn") + "Trạng thái" (dropdown "Chưa kiểm tra" + mũi tên ▾). Dưới cùng: meta 12px `#9a9a9a` — "Ngày tạo: 13/07/2026 21:43" và "Ngày sửa: 14/07/2026 09:59" (giá trị đậm `#5a5a5a`/500), gap 28px.
- **Hàng nút hành động** (gap 10px, cao 40px, bo 6px):
  - "Lưu thay đổi": nền `#ee4d2d`, chữ trắng 13.5px/600; hover `#e0431f`.
  - "Hủy": nền `#fff`, viền `rgba(0,0,0,0.14)`, chữ `#4a4a4a`; hover `#f5f5f5`.
  - (đẩy phải) "Mở trang bán hàng ↗": nền `#fff`, viền `#ffd0c4`, chữ `#ee4d2d`; hover `#fff4f1`.

### 5. Màn Cài đặt (flex-1, cuộn, max-width 760px, padding 24/32px)
- Tiêu đề "Cài đặt" 19px/700 + phụ đề "Tùy chỉnh hành vi xử lý đơn và ứng dụng" 12px `#8a8a8a`.
- Ba nhóm, mỗi nhóm có tiêu đề section cam (11px/700 uppercase) + card trắng (viền/shadow như trên):
  - **Xử lý đơn hàng** — 3 hàng toggle, mỗi hàng padding 16/22px, gap 14px, phân cách viền dưới `rgba(0,0,0,0.05)`:
    - icon vuông 34px bo 8px nền `#fff2ee`; tiêu đề 13.5px/600 `#252525`; mô tả 12px `#8a8a8a`.
    - **Toggle**: track 42×24px bo 12px, knob tròn 20px trắng shadow. BẬT → track `#ee4d2d`, knob phải; TẮT → track `#d4d4d4`, knob trái.
    - Nội dung: "Tự động in hóa đơn / In ngay khi đơn được xác nhận" (BẬT); "Thông báo đơn mới / Hiện thông báo desktop khi có đơn" (BẬT); "Tự động xác nhận đơn / Xác nhận đơn đủ điều kiện không cần thao tác" (TẮT).
  - **Tự động hóa** — card padding 22/24px: slider "Chu kỳ quét đơn mới" (track 6px bo 3px, phần đã đi 42% màu `#ee4d2d`, knob 18px trắng) + ô giá trị "30 giây" (78×36px). Field "Thư mục lưu hóa đơn": input monospace `D:\Shopee\HoaDon\2026` + nút "Chọn…".
  - **Giao diện** — card padding 22/24px: label "Chủ đề" + 3 ô chọn (mỗi ô cao 74px, bo 8px): "Sáng" (được chọn → viền 2px `#ee4d2d`), "Tối", "Theo hệ thống", mỗi ô có mini-preview + nhãn 12px.

## Interactions & Behavior
- **Nav sidebar**: click item → chuyển màn (Tài khoản/Proxy dùng chung màn danh sách; Cài đặt → màn settings). Item active cập nhật pill + màu.
- **Toggle mật khẩu**: click nút 👁 → hiện mật khẩu thật; 🙈 → che bằng dấu chấm.
- **Toggle cài đặt**: click → đảo trạng thái track/knob.
- **Hover states**: đã ghi cụ thể cho từng nút/nav ở trên.
- Không có animation phức tạp; chỉ transition nhẹ cho pill (height .15s).

## State Management
- `activeTab`: "accounts" | "proxy" | "settings" — điều khiển màn hiển thị.
- `showPassword`: boolean — ẩn/hiện mật khẩu.
- `selectedAccountId`: id tài khoản đang chọn (danh sách master–detail).
- Trạng thái các toggle cài đặt (in hóa đơn, thông báo, tự động xác nhận), chu kỳ quét, thư mục lưu, chủ đề.
- Dữ liệu thật cần fetch/lưu: danh sách tài khoản (email, mật khẩu, SĐT, cookie JSON, ghi chú, trạng thái, ngày tạo/sửa).

## Design Tokens
**Màu**
- Accent (Shopee): `#EE4D2D`; accent hover: `#E0431F`; accent đậm text: `#C0341C`.
- Accent nhạt nền: `#FFF2EE`, `#FFF4F1`, `#FFF4E5`; viền nhạt cam: `#FFD0C4`, `#FFD9A8`.
- Nền cửa sổ: gradient `#F7F8FA → #F0F2F5`. Bề mặt card: `#FFFFFF`. Nền input: `#FBFBFB`.
- Chữ: chính `#1C1C1C` / `#232323` / `#252525`; phụ `#4A4A4A` / `#5A5A5A`; nhạt `#8A8A8A` / `#9A9A9A`; placeholder `#A5A5A5`.
- Viền: `rgba(0,0,0,0.05–0.14)`; underline field: `rgba(0,0,0,0.22)`.
- Trạng thái: vàng cảnh báo `#F5A623` / text `#B8720A`; đỏ close `#E81123` / xóa `#C0392B`.
- Code block: nền `#1E2430`, chữ `#CBD5E1`, cyan `#7DD3FC`, tím `#A5B4FC`, xanh lá `#86EFAC`.

**Typography**
- Font: **Segoe UI Variable / Segoe UI** (fallback system). Code: monospace (JetBrains Mono / Consolas).
- Cỡ chữ: tiêu đề màn 19px/700; header sidebar 15px/700; section label 11px/700 uppercase; label field 12.5px/600; body/input 13–13.5px; phụ 11–12px.

**Spacing** (thang dùng): 6, 8, 10, 12, 14, 16, 22, 24, 32px.
**Bo góc**: 5 (icon nhỏ), 6 (input/nút), 8 (thẻ/avatar/code), 10 (card lớn), 12 (toggle track), 20 (pill badge).
**Shadow**: card `0 1px 3px rgba(0,0,0,0.04)`; thẻ chọn `0 1px 4px rgba(238,77,45,0.12)`; nút accent `0 1px 2px rgba(238,77,45,0.4)`; cửa sổ `0 30px 80px rgba(0,0,0,0.35)`.

## Assets
- Không dùng ảnh ngoài. Icon dùng ký tự (◵ ⇄ ⚙ ⌕ 👁 🙈 🖨 🔔 📦 🗑 ↗ ▾). Khi triển khai nên thay bằng bộ icon của dự án (ví dụ **Fluent System Icons** / **Segoe Fluent Icons** cho .NET, hoặc SVG icon set cho web) để nét đồng bộ.
- Logo app: hiện là hình placeholder (gradient cam + vòng tròn). Thay bằng logo thật nếu có.

## Screenshots
- `screenshots/01-tai-khoan.png` — màn Tài khoản.
- `screenshots/02-cai-dat.png` — màn Cài đặt.
(Ảnh chụp ở khung xem hẹp nên có thể bị cắt bên phải; xem `Xu ly don Shopee.dc.html` để thấy toàn bộ 1400px.)

## Files
- `Xu ly don Shopee.dc.html` — prototype hi-fi đầy đủ 2 màn (Tài khoản + Cài đặt), có tương tác chuyển tab và ẩn/hiện mật khẩu. Mở trực tiếp bằng trình duyệt để xem.
