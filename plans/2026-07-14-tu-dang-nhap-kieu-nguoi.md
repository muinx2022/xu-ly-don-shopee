# Plan: Tự đăng nhập Shopee kiểu người (human-like) — Plan 2/2

- **Ngày:** 2026-07-14
- **Trạng thái:** chưa làm (chờ Plan 1 `2026-07-14-cdp-brave-chong-nhan-dien-bot.md` nghiệm thu xong)
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)

> Đây là bản ghi yêu cầu để chưa quên. Fable sẽ viết chi tiết đầy đủ (chữ ký hàm, selector, test) SAU khi Plan 1 xong, vì Plan 2 xếp chồng lên hạ tầng CDP của Plan 1.

## Bối cảnh & mục tiêu

Xếp chồng lên Plan 1 (Brave qua CDP + stealth). Mục tiêu: **tự đăng nhập kiểu người** để giảm bị Shopee nhận diện bot. Đảo lại quyết định cũ ("người dùng tự đăng nhập").

## Yêu cầu đã chốt với người dùng

1. **Tự điền + tự submit:** app tự gõ **user + password** (từ `Account.Email` + `Account.Password`) rồi **tự bấm nút đăng nhập**. Chỉ **dừng ở captcha/OTP** (người dùng tự xử lý). (Người dùng chọn phương án này dù thao tác click tự động dễ bị soi hơn — đã cảnh báo.)
2. **Gõ kiểu người:** gõ từng ký tự, delay ngẫu nhiên (~80–250ms), thỉnh thoảng dừng lâu hơn; không dán/`fill` một phát.
3. **Di chuột kiểu người:** rê chuột vào ô text theo **đường cong, nhiều bước nhỏ, tốc độ biến thiên** (không "kéo 1 phát theo đường thẳng"), rồi mới click ô.
4. **Graceful:** nếu đã đăng nhập sẵn (profile bền) hoặc không tìm thấy ô đăng nhập → bỏ qua tự điền, để người dùng thao tác tay (không ném lỗi).

## Hướng kỹ thuật (phác thảo, chi tiết hóa sau)

- `ILoginSession`/`LoginSession` (Plan 1) mở rộng: expose page hoặc thêm `Task<bool> TryHumanLoginAsync(string user, string password, ct)`.
- Selector đăng nhập Shopee (kiểm chứng lại lúc làm): username `input[name='loginKey']`, password `input[name='password']`, nút đăng nhập `button[type='submit']` (hoặc theo text). Có fallback nếu đổi.
- **Gõ kiểu người:** loop từng ký tự `page.Keyboard.PressAsync/TypeAsync(ch)` + delay ngẫu nhiên; helper `HumanTypeAsync(page, locator, text)`.
- **Di chuột kiểu người:** helper `HumanMoveAsync(page, x, y)` — sinh đường cong (Bézier/nhiều điểm) + `page.Mouse.MoveAsync(x,y,{steps})` theo nhiều chặng, tốc độ biến thiên, rồi `Mouse.DownAsync/UpAsync` (click). KHÔNG dùng `locator.Click()` teleport.
- `OpenSellerAsync` (ViewModel): sau khi mở & điều hướng, nếu chưa đăng nhập → gọi tự đăng nhập; truyền `Account.Email`/`Password` (chụp cùng `targetId` để tránh race). Vòng poll cookie giữ nguyên.
- **Test thuần:** thuật toán sinh đường cong chuột (số điểm > 2, không thẳng, trong biên) + lịch delay gõ (biến thiên, trong khoảng). Phần điều khiển browser → smoke test.

## Rủi ro

- Thao tác **tự bấm đăng nhập** dễ bị anti-bot soi hơn click người thật — người dùng đã chọn; chỉnh dần nếu bị dính.
- Selector Shopee dễ đổi → phải có fallback về nhập tay.
- Không đảm bảo né 100% anti-bot (giống Plan 1).

---

## Báo cáo thực thi (Opus điền sau khi xong)

<Chưa bắt đầu.>
