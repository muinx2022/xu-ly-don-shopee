# Xử lý đơn Shopee

Dự án xử lý đơn hàng Shopee: app desktop Avalonia (.NET) quản lý tài khoản Shopee và proxy (KiotProxy), mở đăng nhập Shopee bằng Brave, thao tác trình duyệt qua Playwright/CDP.

**Quy trình làm việc chung** (2 tầng Fable → Opus, viết plan, nhiều việc song song bằng agent nền + worktree, quy ước commit) nằm ở `d:\Projects\CLAUDE.md` — Claude Code tự nạp file đó cùng file này. Làm đúng theo quy trình đó.

## Ghi chú riêng của dự án

- Solution: `XuLyDonShopee.sln`; chạy app bằng `chay-app.cmd`; test trong `src/XuLyDonShopee.Tests/`.
- Máy này có ISG/WDAC: DLL/exe mới build có thể bị chặn khi chạy hoặc chạy test (FileLoadException 0x800711C7) — đó là chính sách máy, không phải lỗi code. Cách xử lý đã ghi trong bộ nhớ phiên (memory).
- `plans/` giữ toàn bộ lịch sử plan của dự án — không xóa.
