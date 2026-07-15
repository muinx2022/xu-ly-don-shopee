# Plan: Rải đều KiotProxy key cho các tài khoản khi bấm Lưu

- **Ngày:** 2026-07-15
- **Trạng thái:** hoàn thành
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)
- **Nghiệm thu:** Fable đọc code (Distribute + SaveKeys khớp plan, đúng round-robin, ô trống không đụng
  ProxyKey) + tự chạy `dotnet test --no-build`: **166/166 pass** (158 nền + 8 test mới).

## 1. Bối cảnh & mục tiêu

Màn **Proxy** có ô nhập danh sách API key KiotProxy (mỗi dòng một key) với nút **Lưu**
(`ProxiesViewModel.SaveKeys`, file `src/XuLyDonShopee.App/ViewModels/ProxiesViewModel.cs`), hiện chỉ
chuẩn hóa key (`KiotProxyKeyParser.Parse`) và lưu xuống settings (`SetKiotProxyKeys`).

Mỗi tài khoản đã có trường riêng `Account.ProxyKey` (API key KiotProxy riêng — plan
`2026-07-14-proxy-kiotproxy-moi-tai-khoan.md` đã làm xong: cột DB, repository, ô nhập trong form chi
tiết, và `OpenSellerAsync` ưu tiên key riêng khi mở trang bán hàng).

**Yêu cầu mới:** khi bấm **Lưu** ở ô key KiotProxy, ngoài việc lưu settings như cũ, **rải đều** các key
cho toàn bộ tài khoản theo kiểu vòng tròn (round-robin). Ví dụ của người dùng: có 10 tài khoản, 4 key →
4 tài khoản đầu nhận key 1–4, 4 tài khoản tiếp theo nhận lại key 1–4, 2 tài khoản cuối nhận key 1–2.
Tức: tài khoản thứ `i` (0-based, theo thứ tự danh sách) nhận `keys[i % keys.Count]`.

### Hiện trạng code (đã khảo sát — đúng tại thời điểm viết plan)

- `ProxiesViewModel.SaveKeys()` (dòng ~60): `Parse` → `SetKiotProxyKeys` → `Keys = Join(...)` →
  `SavedKeysMessage = "Đã lưu N key."` hoặc `"Đã lưu (chưa có key — sẽ dùng IP máy)."`.
- `AccountRepository.GetAll()` trả danh sách `ORDER BY Id` — chính là thứ tự hiển thị trong danh sách
  tài khoản → "4 acc đầu" = 4 tài khoản Id nhỏ nhất. `AccountRepository.Update(Account)` đã có.
- `AppServices` có `Accounts` (AccountRepository) — `ProxiesViewModel` đã giữ `_services`.
- Chuyển tab về màn Tài khoản, `MainViewModel` gọi `_accountsVm.Reload()` đọc lại DB → ô ProxyKey trong
  form chi tiết tự hiện giá trị mới, KHÔNG cần thông báo chéo giữa hai ViewModel.
- Test ViewModel hiện có mẫu: `AccountsViewModelTests` dùng `TempDatabase` + `new AppServices(temp.Path)`
  (xunit thuần, không cần Avalonia runtime).

### Quyết định đã chốt

- **Ghi đè** `ProxyKey` của **tất cả** tài khoản khi rải (kể cả tài khoản đã có key riêng) — đúng nghĩa
  "rải đều cho các tài khoản".
- **Danh sách key rỗng** (bấm Lưu khi ô trống): chỉ lưu settings như cũ, **KHÔNG** đụng `ProxyKey` của
  tài khoản nào (không xóa key đã gán — tránh phá dữ liệu ngoài ý muốn).
- Thứ tự tài khoản khi rải: theo `GetAll()` (`ORDER BY Id`).

## 2. Phạm vi

- **Làm:**
  - Hàm thuần rải key round-robin (Core, test được): `src/XuLyDonShopee.Core/Services/ProxyKeyDistributor.cs` (tạo mới).
  - Gọi hàm đó trong `ProxiesViewModel.SaveKeys` + lưu từng tài khoản qua repository + cập nhật thông báo.
  - Unit test cho hàm rải + test ViewModel mức DB tạm.
- **Không làm:**
  - KHÔNG đổi `KiotProxyKeyParser`, `SetKiotProxyKeys`, luồng chọn proxy khi mở trang bán hàng
    (`OpenSellerAsync`), form chi tiết tài khoản, màn Cài đặt.
  - KHÔNG đụng `src/XuLyDonShopee.App/Views/AccountsView.axaml` (Fable vừa sửa nhãn "Tên đăng nhập" trực
    tiếp trên cây chính — file đang có thay đổi chưa commit, tuyệt đối không sửa/revert).
  - KHÔNG tự commit (Fable commit sau nghiệm thu).

## 3. Các bước thực hiện

### Bước 1 — Hàm rải key (Core)

Tạo `src/XuLyDonShopee.Core/Services/ProxyKeyDistributor.cs`:

```csharp
/// <summary>Rải đều API key KiotProxy cho danh sách tài khoản theo vòng tròn (round-robin):
/// tài khoản thứ i nhận keys[i % keys.Count]. Hàm thuần: chỉ GÁN vào object, không tự lưu DB.</summary>
public static class ProxyKeyDistributor
{
    /// <summary>Gán key round-robin cho từng tài khoản (ghi đè ProxyKey cũ).
    /// keys rỗng → không đổi gì, trả 0. Trả về số tài khoản đã gán.</summary>
    public static int Distribute(IReadOnlyList<string> keys, IReadOnlyList<Account> accounts)
}
```

- `keys.Count == 0` hoặc `accounts.Count == 0` → trả 0, không đổi gì.
- Ngược lại: `accounts[i].ProxyKey = keys[i % keys.Count]` cho mọi `i`; trả `accounts.Count`.

### Bước 2 — Gọi trong `ProxiesViewModel.SaveKeys`

Sửa `SaveKeys()` trong `src/XuLyDonShopee.App/ViewModels/ProxiesViewModel.cs`:

- Sau `SetKiotProxyKeys` + `Keys = Join(...)` như cũ:
  - `keys.Count == 0` → giữ nguyên thông báo cũ `"Đã lưu (chưa có key — sẽ dùng IP máy)."`, không rải.
  - `keys.Count > 0` → `var accounts = _services.Accounts.GetAll();` →
    `ProxyKeyDistributor.Distribute(keys, accounts)` → lặp `_services.Accounts.Update(acc)` cho từng
    tài khoản → thông báo:
    - có tài khoản: `$"Đã lưu {keys.Count} key, đã rải cho {accounts.Count} tài khoản."`
    - chưa có tài khoản nào: `$"Đã lưu {keys.Count} key (chưa có tài khoản nào để rải)."`

### Bước 3 — Test

- Tạo `src/XuLyDonShopee.Tests/ProxyKeyDistributorTests.cs`:
  - Đúng ví dụ người dùng: 10 tài khoản, 4 key → acc1–4 nhận key1–4, acc5–8 nhận key1–4, acc9–10 nhận
    key1–2; trả 10.
  - 1 key, nhiều tài khoản → tất cả nhận key đó.
  - Nhiều key hơn tài khoản (vd 5 key, 3 acc) → acc nhận key1–3, key4–5 không dùng.
  - keys rỗng → trả 0, `ProxyKey` giữ nguyên (kể cả đang có giá trị).
  - accounts rỗng → trả 0, không ném.
- Tạo `src/XuLyDonShopee.Tests/ProxiesViewModelTests.cs` (theo mẫu `AccountsViewModelTests`:
  `TempDatabase` + `new AppServices(temp.Path)`):
  - Seed 10 tài khoản (Email khác nhau, Password bất kỳ; một vài tài khoản có sẵn `ProxyKey` cũ để kiểm
    ghi đè), set `vm.Keys` = 4 key (mỗi dòng một key), `SaveKeysCommand.Execute(null)` → đọc lại
    `services.Accounts.GetAll()`: `ProxyKey` từng tài khoản đúng round-robin theo Id; `SavedKeysMessage`
    chứa "rải cho 10 tài khoản"; settings lưu đúng 4 key.
  - `vm.Keys = ""` rồi Lưu → `ProxyKey` các tài khoản KHÔNG đổi; thông báo là câu "chưa có key" cũ.

### Bước 4 — Build + test toàn bộ

- `dotnet build XuLyDonShopee.sln -c Debug` (0 error/0 warning) và `dotnet test` (toàn bộ pass,
  hiện có 123 test — không được làm đỏ test cũ).

## 4. Tiêu chí nghiệm thu

- [ ] Bấm Lưu với N key, M tài khoản → tài khoản thứ i (theo Id tăng dần) có `ProxyKey = keys[i % N]`
      trong DB; đúng ví dụ 10 tài khoản/4 key của người dùng (kiểm bằng unit test).
- [ ] Ô trống bấm Lưu → settings lưu rỗng, `ProxyKey` mọi tài khoản giữ nguyên.
- [ ] Thông báo sau Lưu nêu số key + số tài khoản đã rải.
- [ ] Build 0 error/0 warning; `dotnet test` toàn bộ pass (123 cũ + test mới).
- [ ] Không sửa file ngoài danh sách: `ProxyKeyDistributor.cs` (mới), `ProxiesViewModel.cs`,
      `ProxyKeyDistributorTests.cs` (mới), `ProxiesViewModelTests.cs` (mới).

## 5. Rủi ro & lưu ý

- **Cây làm việc chính đang có nhiều thay đổi chưa commit của các việc trước** — tuyệt đối không
  `git add`/`git checkout`/revert bất cứ file nào; chỉ sửa đúng các file trong phạm vi.
- WDAC/ISG máy này có thể chặn DLL mới build khi chạy test (`FileLoadException 0x800711C7`) — đó là
  chính sách máy, không phải lỗi code: build lại (có thể vài lần), thử `-p:Deterministic=false`; nếu
  vẫn kẹt thì ghi rõ trong báo cáo thay vì kết luận test fail do code.
- `Update(Account)` tự set `UpdatedAt` — chấp nhận (rải key là một lần sửa tài khoản thật).
- Không cần cơ chế thông báo chéo ViewModel: màn Tài khoản tự `Reload()` khi chuyển tab.

---

## Báo cáo thực thi (Opus điền sau khi xong)

**Trạng thái:** Hoàn thành toàn bộ plan. Build 0 error/0 warning, test 166/166 pass.

### Đã hoàn thành

- **Bước 1 — Hàm rải key (Core):** Tạo mới `src/XuLyDonShopee.Core/Services/ProxyKeyDistributor.cs`.
  `ProxyKeyDistributor.Distribute(IReadOnlyList<string> keys, IReadOnlyList<Account> accounts)`:
  `keys.Count == 0 || accounts.Count == 0` → trả 0, không đổi gì; ngược lại gán
  `accounts[i].ProxyKey = keys[i % keys.Count]` cho mọi i, trả `accounts.Count`. Hàm thuần, không đụng DB.
- **Bước 2 — Gọi trong ViewModel:** Sửa `SaveKeys()` trong
  `src/XuLyDonShopee.App/ViewModels/ProxiesViewModel.cs`. Sau `SetKiotProxyKeys` + `Keys = Join(...)`:
  - `keys.Count == 0` → giữ nguyên thông báo cũ `"Đã lưu (chưa có key — sẽ dùng IP máy)."`, `return` sớm,
    không đụng `ProxyKey` tài khoản nào.
  - `keys.Count > 0` → `GetAll()` → `ProxyKeyDistributor.Distribute` → lặp `Update(acc)` từng tài khoản →
    thông báo `$"Đã lưu {keys.Count} key, đã rải cho {accounts.Count} tài khoản."` (hoặc
    `$"Đã lưu {keys.Count} key (chưa có tài khoản nào để rải)."` khi `accounts.Count == 0`).
- **Bước 3 — Test:**
  - Tạo mới `src/XuLyDonShopee.Tests/ProxyKeyDistributorTests.cs` (5 test): ví dụ 10 tài khoản/4 key;
    1 key nhiều tài khoản; 5 key/3 tài khoản (key thừa không dùng); keys rỗng giữ nguyên ProxyKey cũ;
    accounts rỗng trả 0 không ném.
  - Tạo mới `src/XuLyDonShopee.Tests/ProxiesViewModelTests.cs` (3 test): rải round-robin cho 10 tài khoản
    (có seed vài ProxyKey cũ để kiểm ghi đè) + kiểm settings lưu 4 key + thông báo "rải cho 10 tài khoản";
    ô trống bấm Lưu không đổi ProxyKey + thông báo cũ; có key nhưng chưa tài khoản → thông báo
    "chưa có tài khoản nào để rải".

### Kết quả kiểm chứng

- `dotnet build XuLyDonShopee.sln -c Debug` → **Build succeeded, 0 Warning, 0 Error.**
- `dotnet test XuLyDonShopee.sln -c Debug` (lần đầu) → 158 fail/8 pass, **tất cả fail cùng một lỗi WDAC**
  `FileLoadException ... An Application Control policy has blocked this file. (0x800711C7)` trên
  `XuLyDonShopee.Core.dll` — đúng chính sách máy nêu trong mục Rủi ro, không phải lỗi code.
- Build lại `-p:Deterministic=false` rồi `dotnet test --no-build` → **Passed! Failed: 0, Passed: 166,
  Total: 166.** (Baseline hiện tại là 158 test cũ trên cây chính — đã gồm test của các việc chưa commit
  khác, nhiều hơn con số 123 ghi trong plan — cộng 8 test mới của việc này = 166.)
- Chạy lọc riêng 8 test mới (`ProxyKeyDistributorTests` + `ProxiesViewModelTests`) → **Passed 8/8.**

### Vướng mắc / bỏ dở

- Không có hạng mục nào bỏ dở. Lưu ý duy nhất: lần test đầu bị WDAC chặn DLL mới build; xử lý bằng build
  lại `-p:Deterministic=false` (đổi hash → ISG cho phép), sau đó pass sạch — đúng như plan dự liệu.

### Đề xuất

- Không. Plan đủ chi tiết, thực thi khớp hoàn toàn; chỉ lưu ý số test baseline trên cây chính giờ là 158
  (không phải 123) do các việc song song chưa commit đã thêm test.
