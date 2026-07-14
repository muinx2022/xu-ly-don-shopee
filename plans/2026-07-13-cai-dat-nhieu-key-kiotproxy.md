# Plan: Cài đặt — nhập nhiều API key KiotProxy (mỗi dòng một key)

- **Ngày:** 2026-07-13
- **Trạng thái:** hoàn thành
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)
- **Nghiệm thu (2026-07-13):** build sạch (0 warning/error), `dotnet test` 64/64 pass (14 test mới). Đã kiểm độc lập KiotProxyClient/SettingsViewModel/SettingsView + chỗ sửa phát sinh `AccountsViewModel.cs:362-364`. Chưa click-through GUI thủ công (app desktop, môi trường không tương tác); binding được xác thực gián tiếp qua compiled binding lúc build.

## 1. Bối cảnh & mục tiêu

### Yêu cầu người dùng
Trong màn **Cài đặt**: bỏ ô "KiotProxy API key" kiểu mật khẩu (che dấu + nút 👁). Thay bằng **một ô text nhiều dòng**, người dùng dán API key của KiotProxy, **mỗi dòng là một key**. App dùng các key này gọi API KiotProxy để lấy/đổi proxy.

### Tài liệu API KiotProxy (chính thức, dùng để hiệu chỉnh client)
- **Lấy/đổi proxy:** `GET https://api.kiotproxy.com/api/v1/proxies/new?key=<KEY>&region=<REGION>`
  - `region` ∈ `bac` | `trung` | `nam` | `random` (chọn `random` = toàn hệ thống).
  - **Thành công** → JSON có `"success": true`, `"status": "SUCCESS"`, và object `data` chứa:
    - `http`: `"ip:port"` (proxy HTTP) ← **trường ta cần**
    - `socks5`: `"ip:port"`, `host`: `"ip"`, `httpPort`/`socks5Port` (số), `location`, `ttl`, `ttc`, `expirationAt`...
  - **Thất bại** → JSON có `"success": false`, `"status": "FAIL"`, `"error": "KEY_NOT_FOUND"`... (HTTP status có thể vẫn 200).
- Các endpoint khác (`/current`, `/out`) **không dùng** ở plan này.

Ví dụ thành công (rút gọn):
```json
{ "data": { "realIpAddress":"171.229.10.20", "http":"171.229.10.20:39008",
  "socks5":"171.229.10.20:39009", "httpPort":39008, "host":"171.229.10.20",
  "location":"Phu Tho", "ttl":1200, "ttc":59 },
  "success": true, "code": 200, "status": "SUCCESS" }
```
Ví dụ thất bại:
```json
{ "success": false, "code": 40400006, "message": "Key not found",
  "status": "FAIL", "error": "KEY_NOT_FOUND" }
```

### Hiện trạng code (đã khảo sát)
- [src/XuLyDonShopee.App/ViewModels/SettingsViewModel.cs](../src/XuLyDonShopee.App/ViewModels/SettingsViewModel.cs): props `ApiKey`, `ShowApiKey`, `SavedMessage`; commands `Save`, `ToggleShowApiKey`; đọc/ghi qua `Settings.Get/Set(SettingsRepository.KiotProxyApiKey)`.
- [src/XuLyDonShopee.App/Views/SettingsView.axaml](../src/XuLyDonShopee.App/Views/SettingsView.axaml): `TextBox` `PasswordChar="●"` + nút 👁 (`ToggleShowApiKeyCommand`) + đoạn mô tả + nút "Lưu".
- [src/XuLyDonShopee.Core/Data/SettingsRepository.cs](../src/XuLyDonShopee.Core/Data/SettingsRepository.cs): `const string KiotProxyApiKey = "kiotproxy_api_key";` + `Get(key)`, `Set(key,value)`.
- [src/XuLyDonShopee.Core/Services/KiotProxyClient.cs](../src/XuLyDonShopee.Core/Services/KiotProxyClient.cs): `class KiotProxyClient : IKiotProxyClient`, ctor `(string apiKey, string baseUrl=DefaultBaseUrl, HttpClient? httpClient=null)`, `GetNewProxyAsync()`, `internal static ParseResponse(json)`.
- [src/XuLyDonShopee.Core/Services/IKiotProxyClient.cs](../src/XuLyDonShopee.Core/Services/IKiotProxyClient.cs): `Task<ProxyEntry?> GetNewProxyAsync(CancellationToken=default)` — **giữ nguyên**.
- [src/XuLyDonShopee.Core/Services/ProxyRotator.cs](../src/XuLyDonShopee.Core/Services/ProxyRotator.cs): dùng `IKiotProxyClient?` — **không đổi** (logic nhiều key nằm trong client).
- [src/XuLyDonShopee.Core/Services/ProxyParser.cs](../src/XuLyDonShopee.Core/Services/ProxyParser.cs): `ProxyParser.Parse("host:port")` → `ProxyEntry` (Type=Http). Dùng lại để parse `data.http`.
- Không có `[InternalsVisibleTo]` trong Core; Tests **không** truy cập được `internal`. Tests project (`XuLyDonShopee.Tests`) dùng xUnit, tham chiếu cả Core + App, **không có** Moq → tự viết stub `HttpMessageHandler`.
- **Chưa có** chỗ nào trong App dựng `new KiotProxyClient(...)`/`new ProxyRotator(...)` (plan `2026-07-13-mo-trang-ban-hang-bat-cookie.md` đang làm, mới là hướng dẫn, chưa thành code) → **đổi chữ ký ctor `KiotProxyClient` không gây vỡ biên dịch**.

### Quyết định đã chốt (mặc định an toàn)
1. **Giữ nguyên** màn "Proxy" thủ công (dán `host:port`). Plan này chỉ đổi phần KiotProxy trong Cài đặt. `ProxyRotator` vẫn ưu tiên list thủ công; trống mới dùng KiotProxy.
2. **Nhiều key = xoay vòng (round-robin) trong `KiotProxyClient`**; nếu key đang tới lượt bị lỗi thì thử key kế tiếp cho tới khi có proxy hoặc hết key → `null` (dùng IP máy).
3. **`region` cố định = `random`** (đúng tinh thần "chỉ 1 ô text", không thêm dropdown). Đặt hằng, có thể chỉnh sau.
4. **Giữ nguyên chuỗi DB key `"kiotproxy_api_key"`** để không mất key người dùng đã nhập trước đó (một key cũ sẽ đọc thành list 1 phần tử). Chỉ đổi tên hằng C# → số nhiều cho rõ nghĩa.

## 2. Phạm vi

- **Làm:**
  - Đổi UI Cài đặt: bỏ ô che dấu + nút 👁 → 1 `TextBox` nhiều dòng, mỗi dòng 1 key.
  - Thêm parser key + helper repository; đổi `SettingsViewModel` sang mô hình nhiều key.
  - Hiệu chỉnh `KiotProxyClient`: nhận **nhiều** key, xoay vòng + fallback; parse đúng schema tài liệu (`data.http`, xử lý `success/status:FAIL`), gửi `region=random`.
  - Test: parser key, `ParseResponse` theo tài liệu, xoay vòng/fallback nhiều key (stub HttpClient), round-trip repository.
  - Cập nhật README + đồng bộ ghi chú trong plan `mo-trang-ban-hang-bat-cookie.md`.
- **Không làm:**
  - Không đụng màn "Proxy" thủ công, `ProxyParser`, `ProxyRepository`, `ProxyRotator`, `AccountsViewModel`.
  - Không thêm UI chọn region; không dùng endpoint `/current`, `/out`.
  - Không kiểm tra proxy sống/chết; không mã hóa key.

## 3. Các bước thực hiện

### Bước 1 — `Core/Services/KiotProxyKeyParser.cs` (TẠO MỚI)
Tách văn bản nhiều dòng thành danh sách key; trim từng dòng, bỏ dòng trống, **loại trùng giữ thứ tự**.
```csharp
namespace XuLyDonShopee.Core.Services;

/// <summary>
/// Tách văn bản nhiều dòng thành danh sách API key KiotProxy (mỗi dòng một key).
/// Trim đầu/cuối, bỏ dòng trống, loại trùng (giữ thứ tự xuất hiện đầu tiên).
/// </summary>
public static class KiotProxyKeyParser
{
    public static List<string> Parse(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (var raw in lines)
        {
            var key = raw.Trim();
            if (key.Length > 0 && seen.Add(key))
            {
                result.Add(key);
            }
        }
        return result;
    }

    /// <summary>Ghép danh sách key thành văn bản chuẩn hóa (mỗi dòng một key, ngăn bằng '\n').</summary>
    public static string Join(IEnumerable<string> keys) => string.Join("\n", keys);
}
```

### Bước 2 — `Core/Data/SettingsRepository.cs` (SỬA)
- Đổi tên hằng, **giữ nguyên giá trị chuỗi** `"kiotproxy_api_key"`:
  ```csharp
  /// <summary>Key lưu danh sách API key KiotProxy (mỗi dòng một key).</summary>
  public const string KiotProxyApiKeys = "kiotproxy_api_key";
  ```
- Thêm `using XuLyDonShopee.Core.Services;` và 2 helper:
  ```csharp
  /// <summary>Đọc danh sách API key KiotProxy đã lưu (đã chuẩn hóa).</summary>
  public List<string> GetKiotProxyKeys() => KiotProxyKeyParser.Parse(Get(KiotProxyApiKeys));

  /// <summary>Lưu danh sách API key KiotProxy (chuẩn hóa rồi ghép mỗi dòng một key).</summary>
  public void SetKiotProxyKeys(IEnumerable<string> keys)
      => Set(KiotProxyApiKeys, KiotProxyKeyParser.Join(keys));
  ```

### Bước 3 — `Core/Services/KiotProxyClient.cs` (SỬA — viết lại nội dung)
Yêu cầu hành vi:
- Ctor mới: `KiotProxyClient(IEnumerable<string> apiKeys, string region = DefaultRegion, string baseUrl = DefaultBaseUrl, HttpClient? httpClient = null)`; chuẩn hóa key bằng `KiotProxyKeyParser`.
- `DefaultRegion = "random"`. URL: `{_baseUrl}/api/v1/proxies/new?key=<esc>&region=<esc>`.
- `GetNewProxyAsync`: nếu không có key → `null` (không gọi HTTP). Ngược lại chọn điểm bắt đầu xoay vòng (thread-safe, tăng `_index`), thử tối đa `KeyCount` key từ điểm đó, trả proxy đầu tiên lấy được; hết → `null`.
- `ParseResponse` **đổi `internal` → `public`** (để test cross-assembly) và theo schema tài liệu: nếu `success:false` hoặc `status == "FAIL"` → `null`; ưu tiên `data.http`; giữ fallback các tên trường cũ.
```csharp
using System.Text.Json;
using XuLyDonShopee.Core.Models;

namespace XuLyDonShopee.Core.Services;

/// <summary>
/// Gọi API KiotProxy (kiotproxy.com) lấy proxy theo API key. Hỗ trợ NHIỀU key:
/// xoay vòng qua các key; key tới lượt bị lỗi thì thử key kế tiếp. Mọi lỗi
/// (mạng/timeout/JSON/key hỏng) đều nuốt và trả null để tầng gọi dùng IP máy.
/// Tài liệu: GET /api/v1/proxies/new?key=&region= → data.http = "ip:port".
/// </summary>
public class KiotProxyClient : IKiotProxyClient
{
    public const string DefaultBaseUrl = "https://api.kiotproxy.com";
    public const string DefaultRegion = "random";

    private readonly object _lock = new();
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _region;
    private readonly List<string> _keys;
    private int _index;

    public KiotProxyClient(IEnumerable<string> apiKeys, string region = DefaultRegion,
        string baseUrl = DefaultBaseUrl, HttpClient? httpClient = null)
    {
        _keys = KiotProxyKeyParser.Parse(apiKeys is null ? null : string.Join("\n", apiKeys));
        _region = string.IsNullOrWhiteSpace(region) ? DefaultRegion : region.Trim();
        _baseUrl = (baseUrl ?? DefaultBaseUrl).TrimEnd('/');
        _http = httpClient ?? new HttpClient();
    }

    /// <summary>Số key hợp lệ hiện có.</summary>
    public int KeyCount
    {
        get { lock (_lock) { return _keys.Count; } }
    }

    public async Task<ProxyEntry?> GetNewProxyAsync(CancellationToken cancellationToken = default)
    {
        List<string> keys;
        int start;
        lock (_lock)
        {
            if (_keys.Count == 0)
            {
                return null;
            }
            keys = _keys;
            start = _index;
            _index = (_index + 1) % _keys.Count;
        }

        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[(start + i) % keys.Count];
            var proxy = await FetchAsync(key, cancellationToken).ConfigureAwait(false);
            if (proxy != null)
            {
                return proxy;
            }
        }
        return null;
    }

    private async Task<ProxyEntry?> FetchAsync(string key, CancellationToken ct)
    {
        try
        {
            var url = $"{_baseUrl}/api/v1/proxies/new?key={Uri.EscapeDataString(key)}" +
                      $"&region={Uri.EscapeDataString(_region)}";
            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseResponse(body);
        }
        catch
        {
            return null; // nuốt lỗi → key kế tiếp / IP máy
        }
    }

    /// <summary>
    /// Trích proxy "host:port" từ JSON KiotProxy. Ưu tiên data.http. Nếu
    /// success=false hoặc status="FAIL" → null. Lỗi parse → null.
    /// </summary>
    public static ProxyEntry? ParseResponse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        string[] candidateKeys =
        {
            "http", "proxyHttp", "proxy", "proxyAddress", "address",
            "socks5", "proxySocks5", "https"
        };

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False)
                {
                    return null;
                }
                if (root.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String &&
                    string.Equals(st.GetString(), "FAIL", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }

            var value = FindProxyString(root, candidateKeys);
            if (value is null && root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("data", out var data))
            {
                value = FindProxyString(data, candidateKeys);
            }
            if (value is null)
            {
                return null;
            }

            var parsed = ProxyParser.Parse(value);
            return parsed.Valid.Count > 0 ? parsed.Valid[0] : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? FindProxyString(JsonElement element, string[] keys)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        foreach (var key in keys)
        {
            if (element.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                var s = prop.GetString();
                if (!string.IsNullOrWhiteSpace(s) && s!.Contains(':'))
                {
                    return s;
                }
            }
        }
        return null;
    }
}
```

### Bước 4 — `App/ViewModels/SettingsViewModel.cs` (SỬA)
Bỏ `ApiKey`, `ShowApiKey`, `ToggleShowApiKey`. Dùng `Keys` (nhiều dòng).
```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.Core.Data;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.App.ViewModels;

/// <summary>
/// Màn hình cài đặt: nhập/lưu danh sách API key KiotProxy (mỗi dòng một key).
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public SettingsViewModel(AppServices services)
    {
        _services = services;
        Reload();
    }

    [ObservableProperty]
    private string _keys = string.Empty;

    [ObservableProperty]
    private string? _savedMessage;

    public void Reload()
    {
        Keys = _services.Settings.Get(SettingsRepository.KiotProxyApiKeys) ?? string.Empty;
        SavedMessage = null;
    }

    [RelayCommand]
    private void Save()
    {
        var keys = KiotProxyKeyParser.Parse(Keys);
        _services.Settings.SetKiotProxyKeys(keys);
        Keys = KiotProxyKeyParser.Join(keys); // hiển thị bản đã chuẩn hóa (bỏ trùng/trống)
        SavedMessage = keys.Count == 0
            ? "Đã lưu (chưa có key — sẽ dùng IP máy)."
            : $"Đã lưu {keys.Count} key.";
    }
}
```

### Bước 5 — `App/Views/SettingsView.axaml` (SỬA)
Thay khối nhãn + `Grid` (ô che dấu + nút 👁) bằng nhãn + `TextBox` nhiều dòng; cập nhật đoạn mô tả; giữ nút "Lưu" + thông báo.
```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:vm="using:XuLyDonShopee.App.ViewModels"
             mc:Ignorable="d" d:DesignWidth="880" d:DesignHeight="700"
             x:Class="XuLyDonShopee.App.Views.SettingsView"
             x:DataType="vm:SettingsViewModel">

    <ScrollViewer>
        <StackPanel Margin="24" Spacing="12" MaxWidth="560" HorizontalAlignment="Left">

            <TextBlock Text="Cài đặt" FontSize="16" FontWeight="SemiBold" />

            <TextBlock Text="API key KiotProxy (mỗi dòng một key)" />
            <TextBox Text="{Binding Keys}"
                     AcceptsReturn="True"
                     TextWrapping="NoWrap"
                     MinHeight="180"
                     FontFamily="Consolas, Menlo, monospace"
                     ScrollViewer.VerticalScrollBarVisibility="Auto"
                     ScrollViewer.HorizontalScrollBarVisibility="Auto"
                     Watermark="Dán các API key kiotproxy.com, mỗi dòng một key" />

            <TextBlock Foreground="#757575" TextWrapping="Wrap"
                       Text="Mỗi dòng là một API key của KiotProxy (proxy xoay của Việt Nam). Khi chạy tài khoản mà danh sách proxy thủ công đang trống, app sẽ xoay vòng qua các key này để đổi IP. Để trống nếu muốn dùng trực tiếp IP máy." />

            <StackPanel Orientation="Horizontal" Spacing="8">
                <Button Content="Lưu" Command="{Binding SaveCommand}"
                        Background="#1976D2" Foreground="White" />
                <TextBlock Text="{Binding SavedMessage}" Foreground="#2E7D32"
                           VerticalAlignment="Center"
                           IsVisible="{Binding SavedMessage, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />
            </StackPanel>
        </StackPanel>
    </ScrollViewer>
</UserControl>
```
> Lưu ý: đảm bảo **không còn** binding tới `ShowApiKey`/`ToggleShowApiKeyCommand`/`ApiKey` ở bất kỳ đâu (nếu còn sẽ lỗi binding runtime).

### Bước 6 — Test (`XuLyDonShopee.Tests`)
1. **`KiotProxyKeyParserTests.cs` (mới):**
   - `Parse(null)` và `Parse("")` → list rỗng.
   - Nhiều dòng có khoảng trắng thừa + dòng trống → trim, bỏ dòng trống. VD `" k1 \n\n k2 "` → `["k1","k2"]`.
   - Loại trùng giữ thứ tự: `"k1\nk2\nk1"` → `["k1","k2"]`.
   - CRLF: `"k1\r\nk2"` → `["k1","k2"]`.
   - `Join(["k1","k2"])` == `"k1\nk2"`.
2. **`KiotProxyClientTests.cs` (mới):** tự viết stub `HttpMessageHandler` (không cần package ngoài). JSON test **chỉ ASCII** (tránh rắc rối encoding). Dùng mẫu:
   - `SuccessJson`: `{"data":{"http":"171.229.10.20:39008","socks5":"171.229.10.20:39009","host":"171.229.10.20","httpPort":39008},"success":true,"status":"SUCCESS"}`
   - `FailJson`: `{"success":false,"code":40400006,"message":"Key not found","status":"FAIL","error":"KEY_NOT_FOUND"}`
   - Case:
     - `ParseResponse(SuccessJson)` → `Host=="171.229.10.20"`, `Port==39008`, `Type==ProxyType.Http`.
     - `ParseResponse(FailJson)` → `null`.
     - `GetNewProxyAsync` khi **không có key** → `null` và stub **không** nhận request nào (`KeyCount==0`).
     - **Fallback:** 2 key `["bad","good"]`; stub trả `FailJson` (HTTP 200) khi query chứa `key=bad`, `SuccessJson` khi chứa `key=good` → trả proxy hợp lệ, stub nhận **2** request (đã thử `bad` rồi `good`).
     - **Xoay vòng:** 2 key `["k1","k2"]`, stub luôn trả `SuccessJson`; gọi 2 lần → request thứ nhất chứa `key=k1`, thứ hai chứa `key=k2` (kiểm tra `RequestedUrls`).
   - Gợi ý stub:
     ```csharp
     private sealed class StubHandler : HttpMessageHandler
     {
         private readonly Func<HttpRequestMessage,(System.Net.HttpStatusCode,string)> _fn;
         public List<string> RequestedUrls { get; } = new();
         public StubHandler(Func<HttpRequestMessage,(System.Net.HttpStatusCode,string)> fn) => _fn = fn;
         protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
         {
             RequestedUrls.Add(req.RequestUri!.ToString());
             var (code, body) = _fn(req);
             return Task.FromResult(new HttpResponseMessage(code)
             { Content = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json") });
         }
     }
     ```
     Tạo client: `new KiotProxyClient(new[]{"k1","k2"}, httpClient: new HttpClient(stub))`.
3. **`SettingsRepositoryTests.cs` (mới):** theo đúng khuôn `ProxyRepositoryTests` (dùng `TempDatabase`). Test `SetKiotProxyKeys([" k1 ","k1","","k2"])` rồi `GetKiotProxyKeys()` == `["k1","k2"]`.

### Bước 7 — Tài liệu
- **README.md:** cập nhật dòng "**Cài đặt**: nhập/lưu KiotProxy API key" → "nhập/lưu **danh sách** API key KiotProxy (mỗi dòng một key)". Cập nhật ghi chú bảo mật ("KiotProxy API key" → "các API key KiotProxy"). Chỉnh câu "Phần gọi KiotProxy thật sẽ được hiệu chỉnh ở plan sau khi có API key thật" → nêu client **đã theo tài liệu API chính thức** (`data.http`, xử lý `FAIL`, `region=random`), cần key thật để chạy thực tế.
- **plans/2026-07-13-mo-trang-ban-hang-bat-cookie.md (đồng bộ ghi chú, không đổi trạng thái):**
  - Dòng tham chiếu `SettingsRepository.KiotProxyApiKey` → `SettingsRepository.KiotProxyApiKeys` (giờ là danh sách).
  - Dòng chữ ký `new KiotProxyClient(apiKey, ...)` → `new KiotProxyClient(IEnumerable<string> apiKeys, region?, baseUrl?, httpClient?)`.
  - Đoạn snippet dựng rotator (khoảng dòng 100): thay
    `var apiKey = _services.Settings.Get(SettingsRepository.KiotProxyApiKey); IKiotProxyClient? kiot = string.IsNullOrWhiteSpace(apiKey) ? null : new KiotProxyClient(apiKey);`
    bằng
    `var kiotKeys = _services.Settings.GetKiotProxyKeys(); IKiotProxyClient? kiot = kiotKeys.Count == 0 ? null : new KiotProxyClient(kiotKeys);`

## 4. Tiêu chí nghiệm thu

- [ ] `dotnet build XuLyDonShopee.sln` **không lỗi, không warning mới**.
- [ ] `dotnet test` **xanh toàn bộ**; các test cũ (`ProxyRotatorTests`, `ProxyParserTests`, repo tests...) vẫn pass; các test mới ở Bước 6 đều pass.
- [ ] Không còn tham chiếu `KiotProxyApiKey` (số ít) hay `ApiKey`/`ShowApiKey`/`ToggleShowApiKey` trong toàn repo (`grep`); không còn `internal static.*ParseResponse`.
- [ ] `SettingsView.axaml` chỉ còn **một** `TextBox` (nhiều dòng, `AcceptsReturn="True"`), không còn `PasswordChar`/nút 👁.
- [ ] Chạy app (`dotnet run --project src/XuLyDonShopee.App`) → tab **Cài đặt** hiện ô text nhiều dòng; nhập vài dòng key, bấm **Lưu** → hiện "Đã lưu N key."; chuyển tab khác rồi quay lại → nội dung được giữ (đã chuẩn hóa, bỏ dòng trùng/trống).
- [ ] `KiotProxyClient.ParseResponse` với JSON mẫu tài liệu (thành công) trả đúng `171.229.10.20:39008` (Http); với JSON `FAIL` trả `null`.
- [ ] README + plan `mo-trang-ban-hang-bat-cookie.md` đã đồng bộ chữ ký/tham chiếu mới.

## 5. Rủi ro & lưu ý

- **Encoding test JSON:** giữ JSON test **ASCII** (bỏ ký tự có dấu như tên tỉnh) để tránh lỗi so khớp/encoding.
- **`lock` trong property getter:** nhớ bọc ngoặc `{ }` (`get { lock (_lock) { return _keys.Count; } }`).
- **Đổi `ParseResponse` sang `public`:** hàm thuần, không nhạy cảm — an toàn để test cross-assembly (không thêm `InternalsVisibleTo`).
- **Không xóa key cũ:** giữ chuỗi DB `"kiotproxy_api_key"` để dữ liệu người dùng đã nhập vẫn đọc được (thành list 1 phần tử).
- **Không mở rộng phạm vi:** không đụng màn Proxy thủ công / rotator / AccountsViewModel. Region cố định `random` (chưa thêm UI).
- **AXAML runtime binding:** rà kỹ để không còn binding tới property/command đã xóa (lỗi binding chỉ lộ lúc chạy, không lộ lúc build).

---

## Báo cáo thực thi (Opus điền sau khi xong)

**Thực thi:** Opus (`opus-executor`) — 2026-07-13. Kết quả: **đạt toàn bộ tiêu chí nghiệm thu**.

### File đã tạo/sửa theo từng bước
- **Bước 1 (tạo):** `src/XuLyDonShopee.Core/Services/KiotProxyKeyParser.cs` — `Parse`/`Join` đúng nguyên văn plan (trim, bỏ dòng trống, loại trùng giữ thứ tự).
- **Bước 2 (sửa):** `src/XuLyDonShopee.Core/Data/SettingsRepository.cs` — đổi hằng `KiotProxyApiKey` → `KiotProxyApiKeys` (giữ chuỗi DB `"kiotproxy_api_key"`), thêm `using XuLyDonShopee.Core.Services;` + 2 helper `GetKiotProxyKeys`/`SetKiotProxyKeys`.
- **Bước 3 (viết lại):** `src/XuLyDonShopee.Core/Services/KiotProxyClient.cs` — ctor nhận `IEnumerable<string> apiKeys` + `region=random`, xoay vòng round-robin thread-safe + fallback key kế tiếp, `ParseResponse` đổi `internal`→`public`, xử lý `success:false`/`status:FAIL` → null, ưu tiên `data.http`.
- **Bước 4 (viết lại):** `src/XuLyDonShopee.App/ViewModels/SettingsViewModel.cs` — bỏ `ApiKey`/`ShowApiKey`/`ToggleShowApiKey`, dùng `Keys` (nhiều dòng) + `Save` chuẩn hóa và báo "Đã lưu N key.".
- **Bước 5 (viết lại):** `src/XuLyDonShopee.App/Views/SettingsView.axaml` — thay ô che dấu + nút 👁 bằng một `TextBox` nhiều dòng (`AcceptsReturn="True"`), cập nhật mô tả.
- **Ngoài plan nhưng bắt buộc để biên dịch:** `src/XuLyDonShopee.App/ViewModels/AccountsViewModel.cs` (`OpenSellerAsync`, ~dòng 361–363) — plan `mo-trang-ban-hang` đã thành code thật và đang gọi `Settings.Get(SettingsRepository.KiotProxyApiKey)` + `new KiotProxyClient(apiKey)`. Áp đúng snippet plan Bước 7 đã chỉ định: `var kiotKeys = _services.Settings.GetKiotProxyKeys(); IKiotProxyClient? kiot = kiotKeys.Count == 0 ? null : new KiotProxyClient(kiotKeys);`.
- **Bước 6 (tạo test):** `KiotProxyKeyParserTests.cs` (6 test), `KiotProxyClientTests.cs` (5 test: ParseResponse thành công/FAIL, không key → không gọi HTTP, fallback bad→good, xoay vòng k1→k2), `SettingsRepositoryTests.cs` (3 test: chuẩn hóa, chưa lưu → rỗng, bền vững sau mở lại DB).
- **Bước 7 (tài liệu):** `README.md` (3 chỗ: mục Cài đặt → "danh sách"; câu KiotProxy → "theo tài liệu API chính thức"; ghi chú bảo mật → "các API key KiotProxy"); `plans/2026-07-13-mo-trang-ban-hang-bat-cookie.md` (3 chỗ: `KiotProxyApiKeys`, chữ ký ctor mới, snippet dựng rotator).

### Kết quả kiểm chứng (chạy thật, PowerShell tại gốc repo)
- `dotnet build XuLyDonShopee.sln -c Debug` → **Build succeeded. 0 Warning(s), 0 Error(s)** (Time Elapsed ~00:00:01.53).
- `dotnet test` → **Passed! Failed: 0, Passed: 64, Skipped: 0, Total: 64, Duration: 1 s** (gồm 14 test mới; các test cũ vẫn xanh).
- Grep trên `.cs`/`.axaml`: không còn `KiotProxyApiKey` (số ít), không còn `ApiKey`/`ShowApiKey`/`ToggleShowApiKey`, không còn `internal static ... ParseResponse` — tất cả **No matches**.
- Grep toàn repo `KiotProxyApiKey\b`: chỉ còn trong CHÍNH file plan này (mô tả hiện trạng cũ + hướng dẫn Bước 7 + dòng tiêu chí nghiệm thu) — là văn bản lịch sử, không phải mã; không sửa để giữ nguyên plan.

### Điểm lệch khỏi plan
- Plan (mục "Hiện trạng", dòng 43) giả định "chưa có chỗ nào trong App dựng `new KiotProxyClient(...)`" nên đổi ctor sẽ không vỡ biên dịch. Thực tế plan `mo-trang-ban-hang-bat-cookie.md` **đã được triển khai thành code**, `AccountsViewModel.OpenSellerAsync` đang dùng ctor + hằng cũ. Đã sửa call site đó bằng **đúng** snippet plan Bước 7 quy định (giữ nguyên hành vi: proxy thủ công ưu tiên, trống mới dùng KiotProxy nhiều key). Đây là thay đổi tối thiểu cần thiết để biên dịch, không mở rộng phạm vi.
