# Plan: Card "Địa chỉ lấy hàng mặc định" trong form chi tiết tài khoản

- **Ngày:** 2026-07-15
- **Trạng thái:** đang làm
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`, trên cây làm việc chính)

## 1. Bối cảnh & mục tiêu

**Yêu cầu người dùng:** trong form chi tiết tài khoản, thêm **một group (card) MỚI nằm GIỮA card
"PROXY" và card "Ghi chú"**, bên trong có phần **"Địa chỉ lấy hàng mặc định"** — chọn cố định 1 trong
**3 giá trị: "Hà Nội", "TP Hồ Chí Minh", "Thanh Hóa"**; **mặc định chọn "Thanh Hóa"**.

Giá trị này lưu **theo từng tài khoản** trong DB (sau này luồng "Xử lý đơn" sẽ dùng nó để chọn địa chỉ
lấy hàng trên trang Cài đặt vận chuyển của Shopee — NGOÀI phạm vi plan này).

### Hiện trạng code (đã khảo sát tại commit `86fa388` — chính là HEAD mà worktree này tách ra)

- Form chi tiết trong `src/XuLyDonShopee.App/Views/AccountsView.axaml`, thứ tự card: Card 1 "THÔNG TIN
  ĐĂNG NHẬP" → card "COOKIE ĐĂNG NHẬP" → card `<!-- Card: Proxy riêng theo tài khoản (API key KiotProxy) -->`
  (section "PROXY") → `<!-- Card 3: Ghi chú + Trạng thái + Ngày -->`. Card mới chèn giữa 2 card cuối.
- Trong Card 3 đã có mẫu **ComboBox** dùng được ngay:
  `<ComboBox Classes="field" HorizontalAlignment="Stretch" ItemsSource="{x:Static vm:AccountsViewModel.StatusOptions}" SelectedItem="{Binding EditStatus}">`.
- `Account` (`src/XuLyDonShopee.Core/Models/Account.cs`): đã có `ProxyKey` (string?) — mẫu để thêm trường mới.
- `Database.cs` có sẵn hạ tầng migration: `CREATE TABLE IF NOT EXISTS` + `EnsureColumn(conn, "accounts",
  "ProxyKey", "TEXT")` (PRAGMA table_info + ALTER TABLE, idempotent).
- `AccountRepository.cs`: SELECT liệt kê cột `Id, Email, Password, Phone, Cookie, Note, ProxyKey,
  Status, CreatedAt, UpdatedAt` (GetAll/GetById) + INSERT/UPDATE/`BindWritableFields`/`Map` — lần thêm
  ProxyKey đã dời index Map; làm đúng mẫu đó.
- `AccountsViewModel.cs`: `EditProxyKey` là mẫu cho field form (`[ObservableProperty]`, `LoadIntoForm`,
  `ClearForm`, `Save` cả nhánh Insert lẫn Update).
- Test mẫu: `AccountRepositoryTests` (round-trip ProxyKey), `DatabaseMigrationTests` (schema cũ →
  migration), `AccountsViewModelTests` (`TempDatabase` + `AppServices`).

### Quyết định đã chốt

- Lưu DB dạng **TEXT** cột `PickupAddress`, giá trị là đúng chuỗi hiển thị ("Hà Nội" / "TP Hồ Chí Minh"
  / "Thanh Hóa"). KHÔNG làm enum (danh sách sẽ còn mở rộng, tránh migration enum).
- Bản ghi cũ `PickupAddress` NULL (hoặc giá trị lạ ngoài 3 lựa chọn) → khi nạp form coi như
  **"Thanh Hóa"**; lần lưu kế tiếp sẽ ghi giá trị đang chọn.
- Tài khoản tạo mới: mặc định "Thanh Hóa".

## 2. Phạm vi

- **Làm:** cột DB + migration; model; repository; VM field + options; card UI mới; unit test.
- **Không làm:** KHÔNG đụng gì tới luồng phiên/trình duyệt/Xử lý đơn; KHÔNG sửa hàng nút hành động
  của form (một việc khác đang sửa vùng đó trên nhánh khác); KHÔNG tự commit.

## 3. Các bước thực hiện

(Chạy trên cây làm việc chính — trước khi giao việc, mọi việc trước đã commit; nút "Xử lý đơn" đã có ở
hàng nút hành động, KHÔNG đụng vào. Nền test hiện tại: **201** pass.)

### Bước 1 — Model + DB + Repository (đúng mẫu ProxyKey)

- `src/XuLyDonShopee.Core/Models/Account.cs`: thêm
  `public string? PickupAddress { get; set; }` — XML-doc: "Địa chỉ lấy hàng mặc định (tỉnh/thành, chọn
  từ danh sách cố định trên form). Null = chưa chọn → app coi như Thanh Hóa."
- `src/XuLyDonShopee.Core/Data/Database.cs`: thêm `PickupAddress TEXT` vào định nghĩa CREATE TABLE
  (DB mới) + gọi `EnsureColumn(conn, "accounts", "PickupAddress", "TEXT");` (DB cũ).
- `src/XuLyDonShopee.Core/Data/AccountRepository.cs`: thêm `PickupAddress` vào SELECT (GetAll/GetById,
  đặt ngay SAU `ProxyKey`), INSERT, UPDATE, `BindWritableFields` (`$pickupAddress` ←
  `(object?)a.PickupAddress ?? DBNull.Value`), `Map` (index sau ProxyKey; Status/CreatedAt/UpdatedAt
  dời tiếp +1). **Thứ tự cột SELECT ↔ Map phải khớp.**

### Bước 2 — ViewModel

`src/XuLyDonShopee.App/ViewModels/AccountsViewModel.cs`:

- Hằng + options (theo mẫu `StatusOptions`):
  ```csharp
  /// <summary>Giá trị mặc định của địa chỉ lấy hàng khi tài khoản chưa chọn.</summary>
  public const string DefaultPickupAddress = "Thanh Hóa";

  /// <summary>Danh sách cố định địa chỉ lấy hàng cho ComboBox trên form.</summary>
  public static string[] PickupAddressOptions { get; } = ["Hà Nội", "TP Hồ Chí Minh", "Thanh Hóa"];
  ```
- `[ObservableProperty] private string _editPickupAddress = DefaultPickupAddress;`
- `LoadIntoForm(Account a)`: `EditPickupAddress = PickupAddressOptions.Contains(a.PickupAddress ?? "")
  ? a.PickupAddress! : DefaultPickupAddress;` (giá trị lạ/null → default, tránh ComboBox trống).
- `ClearForm()`: `EditPickupAddress = DefaultPickupAddress;`
- `Save()`: cả nhánh tạo mới lẫn cập nhật ghi `PickupAddress = EditPickupAddress` (luôn có giá trị).

### Bước 3 — UI card mới

`src/XuLyDonShopee.App/Views/AccountsView.axaml` — chèn GIỮA card PROXY (kết thúc `</Border>` của nó)
và `<!-- Card 3: Ghi chú + Trạng thái + Ngày -->`, đúng style card hiện có:

```xml
<!-- Card: Địa chỉ lấy hàng mặc định (dùng cho xử lý đơn sau này) -->
<Border Classes="card" Padding="24,22" Margin="0,0,0,16">
    <StackPanel Spacing="12">
        <TextBlock Classes="section" Text="ĐỊA CHỈ LẤY HÀNG" />
        <StackPanel>
            <TextBlock Classes="fieldLabel" Text="Địa chỉ lấy hàng mặc định" />
            <ComboBox Classes="field" HorizontalAlignment="Stretch"
                      ItemsSource="{x:Static vm:AccountsViewModel.PickupAddressOptions}"
                      SelectedItem="{Binding EditPickupAddress}" />
        </StackPanel>
    </StackPanel>
</Border>
```

KHÔNG sửa bất kỳ phần nào khác của file (đặc biệt hàng nút hành động).

### Bước 4 — Test + build

- `src/XuLyDonShopee.Tests/AccountRepositoryTests.cs`: round-trip `PickupAddress` (insert "Hà Nội" →
  GetById đúng; null → null; update đổi giá trị).
- `src/XuLyDonShopee.Tests/DatabaseMigrationTests.cs`: DB schema cũ (thiếu cột) → khởi tạo → có cột
  `PickupAddress`, dữ liệu cũ nguyên vẹn (mở rộng test hiện có hoặc thêm case theo mẫu ProxyKey).
- `src/XuLyDonShopee.Tests/AccountsViewModelTests.cs`: (1) Add → Save không đụng gì → DB lưu
  "Thanh Hóa"; (2) account có "Hà Nội" → LoadIntoForm hiện "Hà Nội"; (3) account cũ PickupAddress null
  → form hiện "Thanh Hóa"; (4) đổi sang "TP Hồ Chí Minh" → Save → DB đúng.
- `dotnet build XuLyDonShopee.sln -c Debug` (0 error/0 warning); `dotnet test` toàn bộ pass (nền 166 +
  test mới — số nền tính tại worktree này, KHÔNG gồm test của việc song song khác).

## 4. Tiêu chí nghiệm thu

- [ ] Form chi tiết có card "ĐỊA CHỈ LẤY HÀNG" nằm GIỮA card PROXY và card Ghi chú; ComboBox 3 lựa chọn
      đúng thứ tự "Hà Nội", "TP Hồ Chí Minh", "Thanh Hóa"; tạo mới mặc định "Thanh Hóa".
- [ ] Lưu/nạp đúng theo tài khoản; bản ghi cũ (null) hiện "Thanh Hóa"; DB cũ migrate thêm cột không mất
      dữ liệu.
- [ ] Build 0 error/0 warning; `dotnet test` toàn bộ pass.
- [ ] Chỉ sửa: `Account.cs`, `Database.cs`, `AccountRepository.cs`, `AccountsViewModel.cs`,
      `AccountsView.axaml`, 3 file test trên.

## 5. Rủi ro & lưu ý

- **Thứ tự cột SELECT ↔ Map** trong repository — chỗ dễ sai nhất (đã từng phải dời index khi thêm
  ProxyKey). Test round-trip + migration phải bắt được nếu lệch.
- Migration `ALTER TABLE ADD COLUMN` idempotent, không phá dữ liệu (mẫu EnsureColumn có sẵn).
- WDAC/ISG khi test: `0x800711C7` → build lại `-p:Deterministic=false` rồi `dotnet test --no-build`;
  fail đồng loạt cùng lỗi này là policy máy, không phải code.
- Hàng nút hành động vừa được thêm nút "Xử lý đơn" (đã commit) — plan này CHỈ sửa vùng card/field,
  không đụng hàng nút.
- `dotnet build/test` nền hiện tại: 201 test pass. WDAC chặn hash mới → rebuild `--no-incremental
  -p:Deterministic=false` rồi `dotnet test --no-build` (có thể vài lần).

---

## Báo cáo thực thi (Opus điền sau khi xong)

<Opus dán báo cáo cuối vào đây.>
