# Xử lý đơn Shopee

Ứng dụng desktop (đa nền tảng Windows/Linux) để quản lý tài khoản Shopee và danh sách proxy,
là bước đầu của bộ công cụ xử lý đơn hàng Shopee. Viết bằng **C# + Avalonia (.NET 8)**, lưu dữ liệu
bằng **SQLite** cục bộ (không cần server).

## Tính năng hiện có

- **Quản lý tài khoản** (giao diện 2 panel: danh sách + chi tiết): thêm/sửa/xóa/lưu, tìm kiếm,
  chấp nhận **user dạng email hoặc tên đăng nhập bất kỳ** (không trùng), mật khẩu bắt buộc.
- **Mở trang bán hàng & lấy cookie**: nút *"Mở trang bán hàng"* trong form tài khoản mở
  [banhang.shopee.vn](https://banhang.shopee.vn) bằng một **hồ sơ (profile) riêng cho từng tài khoản**,
  định tuyến qua proxy nếu có. App **tự khởi chạy Brave thật** (không có Brave thì dùng Chromium đóng
  gói của Playwright) rồi **nối vào qua CDP** (`--remote-debugging-port`), thay vì để Playwright tự
  launch. Nhờ vậy **không hiện thanh "controlled by automated test software"** và giữ
  `navigator.webdriver = false` một cách **tự nhiên** (Brave thật vốn đã sạch — plugins/WebGL/chrome
  thật) nên **đỡ bị Shopee nhận diện là bot**. App **không** chồng thêm init script vá JS (vá lại tự
  tạo dấu hiệu lộ bot); chống nhận diện dựa vào **trình duyệt thật + hành vi kiểu người** (bên dưới).
  App **tự đăng nhập kiểu người**: khi trang hiện form đăng nhập Shopee, app **tự gõ user + mật khẩu**
  (gõ **từng ký tự** với nhịp trễ ngẫu nhiên, thỉnh thoảng ngập ngừng) và **di chuột theo đường cong**
  tới ô/nút rồi bấm đăng nhập — chỉ **dừng ở captcha/OTP** để bạn tự xử lý. Nếu **đã đăng nhập sẵn**
  hoặc **không tìm thấy ô đăng nhập** (Shopee đổi giao diện/selector) thì app **bỏ qua để bạn tự nhập
  tay** (không báo lỗi). Sau đó app **tự động bắt & lưu cookie khi bạn đóng cửa sổ** (không hỏi nữa) vào
  trường **Cookie** của đúng tài khoản đó — dù đăng nhập do app điền hay do bạn tự làm captcha xong. Vì
  mở bằng profile riêng lưu tại `%APPDATA%\XuLyDonShopee\profiles\<id>` nên **lần sau mở lại vẫn còn
  đăng nhập**.

  > **Mở nhiều tài khoản song song:** mỗi tài khoản chạy **một phiên độc lập** (Brave + profile + proxy +
  > theo dõi đơn riêng, CDP port riêng) nên có thể **mở/theo dõi nhiều shop cùng lúc**. Mở tài khoản này
  > **không khóa** nút "Mở trang bán hàng" của tài khoản khác; **đóng cửa sổ** một tài khoản **không ảnh
  > hưởng** tài khoản khác; **thoát app** sẽ đóng sạch mọi cửa sổ Brave (không để tiến trình mồ côi). Có
  > thêm nút **"Dừng"** để dừng phiên của tài khoản đang chọn.
  >
  > **Chọn nhiều & chạy nhóm (dành cho nhiều shop):** mỗi dòng trong danh sách tài khoản có **ô tick**;
  > phía trên danh sách có các nút **"Chọn toàn bộ"** (tick/bỏ tick hết theo danh sách đang lọc),
  > **"Chạy đã chọn"** (mở phiên cho mọi tài khoản đang tick — nhiều Brave song song), **"Dừng đã chọn"**
  > và **"Dừng tất cả"**. Mỗi dòng còn hiển thị **live** một **chấm xanh "đang chạy"** và số **"Chờ lấy:
  > N"** đọc từ phiên tương ứng, để theo dõi nhiều shop cùng lúc mà không cần mở từng form. **Bấm vào một
  > tài khoản** sẽ **đưa dòng đó lên đầu danh sách** và **đưa cửa sổ Brave** của nó (nếu đang chạy)
  > **ra trước** (focus) — tiện nhảy nhanh giữa nhiều cửa sổ shop. (Focus cửa sổ chỉ hỗ trợ Windows và là
  > best-effort — nếu không đưa ra trước được thì bỏ qua, không ảnh hưởng luồng.)
  >
  > ⚠️ **Nhiều Brave rất tốn RAM/CPU:** mỗi phiên là một trình duyệt thật — mở ~15 shop cùng lúc là **rất
  > nặng**. Hãy cân nhắc số lượng phiên phù hợp với cấu hình máy.
- **Theo dõi đơn "Chờ Lấy Hàng"**: sau khi mở & đăng nhập, trong lúc cửa sổ trình duyệt còn mở, app
  **tự theo dõi số "Chờ Lấy Hàng"** trong to-do box của Seller Centre: cứ **30 phút** **reload lại
  trang** rồi đọc số, hiển thị ngay trên form (dòng "Chờ Lấy Hàng: …"). Nếu **= 0** thì kiểm lại sau
  30'; nếu **> 0** thì (bước này) **chỉ báo số đơn** và **vẫn tiếp tục theo dõi mỗi 30'** — phần **xử
  lý đơn tự động** sẽ làm ở bước sau. **Đóng cửa sổ để dừng** theo dõi. (Trước khi đăng nhập xong, app
  chưa reload — chờ bạn đăng nhập/qua captcha rồi mới bắt đầu đọc.)

  > **Lưu ý anti-bot:** cơ chế trên là **best-effort**, KHÔNG đảm bảo 100% né được hệ thống chống bot
  > của Shopee (họ vẫn có thể dò CDP, fingerprint, hành vi gõ/di chuột, và IP). Proxy được đặt qua
  > `--proxy-server`; proxy có user:pass thì xác thực **qua CDP** (không hiện hộp thoại đăng nhập proxy).
  > Với KiotProxy, app **giữ IP ổn định**: luôn ưu tiên proxy hiện tại (`/current`), chỉ xin IP mới
  > (`/new`) khi chưa có — tránh xoay IP liên tục.
- **Proxy riêng cho từng tài khoản**: trong form chi tiết tài khoản có ô **"Proxy — API key KiotProxy"**.
  Dán **API key KiotProxy riêng** vào đây thì khi mở trang bán hàng, tài khoản đó dùng **đúng key này**
  lấy proxy sticky (`/current`) → **mỗi tài khoản một IP ổn định riêng**. Thứ tự ưu tiên chọn proxy khi
  mở: (1) **key riêng của tài khoản** → (2) danh sách proxy thủ công (round-robin) → (3) danh sách API
  key KiotProxy **chung** trong Cài đặt → (4) IP máy. Để trống ô này = dùng cấu hình chung / IP máy.
- **Quản lý proxy**: dán danh sách proxy (`host:port` hoặc `host:port:user:pass`), hiển thị bảng,
  xóa dòng/xóa tất cả.
- **Cài đặt**: nhập/lưu **danh sách** API key KiotProxy (mỗi dòng một key).
- **Service xoay vòng proxy** (`ProxyRotator`, logic thuần đã có test): xoay vòng round-robin qua
  danh sách proxy; nếu danh sách trống thì dùng KiotProxy (khi có API key) hoặc IP máy.

> Bước này **chưa** làm tính năng "chạy tài khoản" (tái dùng cookie để tự thao tác đơn). Khi mở trang
> bán hàng, nếu danh sách proxy thủ công trống, app **kiểm tra proxy KiotProxy còn sống trước khi mở**
> (gọi API `/current` → `/new` có kiểm `expirationAt`, kết hợp thử kết nối thật qua proxy); proxy chết
> hoặc không có thì **tự dùng IP máy**. Client KiotProxy đã được hiệu chỉnh **theo tài liệu API chính
> thức** (đọc `data.http`, xử lý phản hồi `FAIL`, gửi `region=random`, xoay vòng nhiều key); cần API
> key thật để chạy thực tế.

> **Chọn trình duyệt khi bấm "Mở trang bán hàng":**
> - Nếu máy **đã cài Brave**, app dùng luôn Brave — **không tải** Chromium (đỡ ~150MB).
>   (Tìm Brave ở các vị trí cài đặt mặc định trên Windows/Linux/macOS.)
> - Nếu **không có Brave**, lần đầu app sẽ tự tải trình duyệt Chromium của Playwright
>   (~150MB, chỉ một lần — lưu tại `%LOCALAPPDATA%\ms-playwright`). Các lần sau mở ngay.
> - Dù là Brave hay Chromium đóng gói, app đều **tự khởi chạy tiến trình rồi nối vào qua CDP**
>   (không để Playwright tự launch) để không hiện thanh automation và giữ `navigator.webdriver=false`.
>
> Mỗi tài khoản mở bằng một **hồ sơ (profile) persistent riêng** (thư mục
> `%APPDATA%\XuLyDonShopee\profiles\<id>`, không dùng hồ sơ Brave thật của bạn) nên cookie/phiên đăng
> nhập giữa các tài khoản không lẫn nhau, và **lần sau mở lại vẫn còn đăng nhập**.
> Nút chỉ bật khi đang mở một tài khoản **đã lưu** (cần Id để biết ghi cookie vào đâu); tài khoản
> mới phải Lưu trước. App **tự đăng nhập kiểu người** (gõ + di chuột như người) bằng user/mật khẩu đã
> lưu, dừng ở captcha/OTP; nếu Shopee đổi selector khiến app không thấy ô thì **bạn tự nhập tay**. App
> tự bắt cookie trong lúc cửa sổ mở và lưu khi bạn đóng cửa sổ (không còn bấm *"Đồng ý"* để lưu như trước).
>
> **Lưu ý:** tự bấm đăng nhập có thể **dễ bị anti-bot soi hơn** thao tác tay, và cơ chế kiểu người
> **không đảm bảo 100%** né được hệ thống chống bot của Shopee — đây là best-effort.

## Yêu cầu

- **.NET SDK 8.0** trở lên (đã kiểm thử với 8.0.422).

## Chạy ứng dụng

Windows (PowerShell) hoặc Linux (bash), tại thư mục gốc repo:

```
dotnet run --project src/XuLyDonShopee.App
```

## Chạy test

```
dotnet test
```

## Build

```
dotnet build XuLyDonShopee.sln
```

## Cấu trúc dự án

| Project | Vai trò |
|---|---|
| `src/XuLyDonShopee.Core` | Model, tầng dữ liệu SQLite, service (parser, xoay vòng proxy, KiotProxy client) |
| `src/XuLyDonShopee.App`  | Giao diện Avalonia (MVVM) |
| `src/XuLyDonShopee.Tests`| Unit test (xUnit) cho logic trong Core |

## Vị trí file dữ liệu

Database SQLite được tạo tự động tại thư mục dữ liệu ứng dụng của người dùng:

- **Windows:** `%APPDATA%\XuLyDonShopee\app.db`
- **Linux:** `~/.config/XuLyDonShopee/app.db`

## Ghi chú bảo mật

- **Mật khẩu tài khoản hiện được lưu dạng thường (chưa mã hóa)** trong file SQLite cục bộ. Đây là
  chấp nhận có chủ đích ở bước đầu; sẽ xem xét mã hóa ở giai đoạn sau.
- **Cookie đăng nhập** (bắt được từ trang bán hàng) cũng là thông tin nhạy cảm, hiện lưu dạng thường
  trong cùng file SQLite — chấp nhận ở bước này giống như mật khẩu, chưa thêm mã hóa.
- **Hồ sơ (profile) trình duyệt** của từng tài khoản (`profiles/<id>`) chứa **session đăng nhập lưu
  trên đĩa** (không mã hóa) để lần sau vẫn đăng nhập — cũng là dữ liệu nhạy cảm giống cookie/mật khẩu.
- Các API key KiotProxy được lưu trong bảng `settings` của cùng file DB (không hard-code trong mã nguồn).
- File `*.db` đã được đưa vào `.gitignore` để không commit dữ liệu người dùng.
