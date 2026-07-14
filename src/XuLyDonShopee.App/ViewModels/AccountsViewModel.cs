using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.Core.Data;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.App.ViewModels;

/// <summary>
/// Màn hình tài khoản: panel trái là danh sách + tìm kiếm, panel phải là form CRUD.
/// </summary>
public partial class AccountsViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly ShopeeLoginService _loginService = new();
    private readonly IProxyHealthChecker _healthChecker = new ProxyHealthChecker();
    private List<Account> _all = new();
    private bool _isRefreshing;

    // Chỉ số round-robin BỀN cho proxy thủ công, giữ qua các lần mở trang bán hàng (không reset mỗi lần).
    private int _manualProxyIndex;

    public AccountsViewModel(AppServices services)
    {
        _services = services;
        Reload();
    }

    /// <summary>Danh sách tài khoản đang hiển thị (sau khi lọc).</summary>
    public ObservableCollection<Account> Accounts { get; } = new();

    /// <summary>Các lựa chọn trạng thái cho ComboBox.</summary>
    public static AccountStatus[] StatusOptions { get; } =
    {
        AccountStatus.ChuaKiemTra,
        AccountStatus.HoatDong,
        AccountStatus.BiKhoa
    };

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private Account? _selectedAccount;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _isNew;

    [ObservableProperty]
    private string _editEmail = string.Empty;

    [ObservableProperty]
    private string _editPassword = string.Empty;

    [ObservableProperty]
    private string _editPhone = string.Empty;

    [ObservableProperty]
    private string _editCookie = string.Empty;

    [ObservableProperty]
    private string _editNote = string.Empty;

    [ObservableProperty]
    private AccountStatus _editStatus = AccountStatus.ChuaKiemTra;

    [ObservableProperty]
    private string? _createdAtText;

    [ObservableProperty]
    private string? _updatedAtText;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _showPassword;

    /// <summary>Đang bận (đang mở/tải trình duyệt, bắt cookie...). Khóa nút "Mở trang bán hàng".</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Dòng hướng dẫn/trạng thái hiển thị trong lúc mở trang bán hàng (null = ẩn).</summary>
    [ObservableProperty]
    private string? _busyStatus;

    /// <summary>Panel phải hiện chữ mờ khi không ở chế độ xem/sửa.</summary>
    public bool ShowPlaceholder => !IsEditing;

    /// <summary>Nhãn kích thước cookie hiển thị cạnh tiêu đề khối cookie.</summary>
    public string CookieSizeText => string.IsNullOrEmpty(EditCookie)
        ? "JSON · trống"
        : $"JSON · {System.Text.Encoding.UTF8.GetByteCount(EditCookie) / 1024.0:0.0} KB";

    /// <summary>
    /// Chỉ cho mở trang bán hàng khi đang xem/sửa một tài khoản đã lưu (có Id) và không đang bận.
    /// Tài khoản mới chưa lưu (chưa có Id) không mở được vì chưa có nơi ghi cookie.
    /// </summary>
    public bool CanOpenSeller => IsEditing && !IsNew && _editingId is not null && !IsBusy;

    /// <summary>Id của tài khoản đang được nạp trong form (null = form trống / tạo mới).</summary>
    private long? _editingId;

    partial void OnIsEditingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowPlaceholder));
        OnPropertyChanged(nameof(CanOpenSeller));
    }

    partial void OnIsNewChanged(bool value) => OnPropertyChanged(nameof(CanOpenSeller));

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanOpenSeller));

    partial void OnEditCookieChanged(string value) => OnPropertyChanged(nameof(CookieSizeText));

    partial void OnSearchTextChanged(string value)
    {
        // Bỏ qua khi đang cập nhật danh sách bằng code (tránh chạy lọc hai lần).
        if (_isRefreshing)
        {
            return;
        }

        // Lọc lại và giữ nguyên form đang sửa: reselect theo tài khoản đang chỉnh sửa.
        RefreshList(_editingId ?? SelectedAccount?.Id);
    }

    partial void OnSelectedAccountChanged(Account? value)
    {
        if (_isRefreshing)
        {
            return;
        }

        if (value != null)
        {
            // Chọn lại đúng tài khoản đang sửa dở → giữ nguyên form (không nạp đè, tránh mất dữ liệu).
            if (value.Id == _editingId)
            {
                return;
            }

            IsNew = false;
            LoadIntoForm(value);
            IsEditing = true;
        }
        else if (!IsNew)
        {
            IsEditing = false;
            ClearForm();
        }
    }

    /// <summary>Nạp lại danh sách từ DB, giữ lựa chọn/form nếu bản ghi còn tồn tại.</summary>
    public void Reload()
    {
        var selectId = _editingId ?? SelectedAccount?.Id;
        _all = _services.Accounts.GetAll();
        RefreshList(selectId);
    }

    /// <summary>
    /// Dựng lại danh sách hiển thị theo bộ lọc hiện tại rồi chọn lại bản ghi <paramref name="selectId"/>.
    /// Việc gán SelectedAccount được thực hiện dưới cờ <c>_isRefreshing</c> nên KHÔNG nạp đè form.
    /// </summary>
    private void RefreshList(long? selectId)
    {
        _isRefreshing = true;
        ApplyFilter();
        var match = selectId is long id ? Accounts.FirstOrDefault(a => a.Id == id) : null;
        SelectedAccount = match;
        _isRefreshing = false;
    }

    private void ApplyFilter()
    {
        Accounts.Clear();
        foreach (var account in _all.Where(a => PassesFilter(a, SearchText)))
        {
            Accounts.Add(account);
        }
    }

    private static bool PassesFilter(Account a, string? searchText)
    {
        var query = searchText?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            return true;
        }

        return (a.Email?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (a.Note?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    [RelayCommand]
    private void Add()
    {
        _isRefreshing = true;
        SelectedAccount = null;
        _isRefreshing = false;

        IsNew = true;
        ClearForm();
        IsEditing = true;
    }

    [RelayCommand]
    private void Save()
    {
        // User đăng nhập: có thể là email HOẶC tên đăng nhập bất kỳ (vd shopee_user01).
        // Chỉ bắt buộc không rỗng và không trùng; KHÔNG ép định dạng email nữa.
        var user = EditEmail?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(user))
        {
            ErrorMessage = "Tên đăng nhập (user) không được để trống.";
            return;
        }

        if (string.IsNullOrEmpty(EditPassword))
        {
            ErrorMessage = "Mật khẩu không được để trống.";
            return;
        }

        var duplicated = _all.Any(a =>
            a.Id != (_editingId ?? -1) &&
            string.Equals(a.Email, user, StringComparison.OrdinalIgnoreCase));
        if (duplicated)
        {
            ErrorMessage = "Tài khoản này đã tồn tại ở một tài khoản khác.";
            return;
        }

        ErrorMessage = null;

        Account account;
        if (IsNew || _editingId is null)
        {
            account = new Account
            {
                Email = user,
                Password = EditPassword,
                Phone = NullIfEmpty(EditPhone),
                Cookie = NullIfEmpty(EditCookie),
                Note = NullIfEmpty(EditNote),
                Status = EditStatus
            };
            _services.Accounts.Insert(account);
        }
        else
        {
            var existing = _services.Accounts.GetById(_editingId.Value);
            if (existing is null)
            {
                // Đã bị xóa ở đâu đó — báo lỗi và làm mới danh sách.
                ErrorMessage = "Không tìm thấy tài khoản để cập nhật (có thể đã bị xóa).";
                Reload();
                return;
            }

            existing.Email = user;
            existing.Password = EditPassword;
            existing.Phone = NullIfEmpty(EditPhone);
            existing.Cookie = NullIfEmpty(EditCookie);
            existing.Note = NullIfEmpty(EditNote);
            existing.Status = EditStatus;
            _services.Accounts.Update(existing);
            account = existing;
        }

        // Trạng thái nhất quán ngay sau khi ghi: form đang giữ đúng bản ghi vừa lưu.
        IsNew = false;
        _editingId = account.Id;

        // Nạp lại toàn bộ từ DB (lấy CreatedAt/UpdatedAt chuẩn).
        _all = _services.Accounts.GetAll();
        var saved = _all.FirstOrDefault(a => a.Id == account.Id);

        // Nếu bộ lọc hiện tại đang ẩn bản ghi vừa lưu → xóa từ khóa để nó luôn hiển thị và chọn được.
        if (saved != null && !PassesFilter(saved, SearchText))
        {
            _isRefreshing = true;
            SearchText = string.Empty;
            _isRefreshing = false;
        }

        RefreshList(account.Id);

        if (saved != null)
        {
            LoadIntoForm(saved);
            IsEditing = true;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (IsNew)
        {
            IsNew = false;
            IsEditing = false;
            ClearForm();
        }
        else if (_editingId is long id)
        {
            var record = _all.FirstOrDefault(a => a.Id == id) ?? _services.Accounts.GetById(id);
            if (record != null)
            {
                LoadIntoForm(record);
            }
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedAccount is null)
        {
            return;
        }

        var target = SelectedAccount;
        var ok = await DialogService.ConfirmAsync(
            "Xóa tài khoản",
            $"Bạn có chắc muốn xóa tài khoản \"{target.Email}\"? Thao tác này không thể hoàn tác.");
        if (!ok)
        {
            return;
        }

        _services.Accounts.Delete(target.Id);
        IsNew = false;
        _isRefreshing = true;
        SelectedAccount = null;
        _isRefreshing = false;
        IsEditing = false;
        ClearForm();
        Reload();
    }

    [RelayCommand]
    private void ToggleShowPassword() => ShowPassword = !ShowPassword;

    /// <summary>
    /// Mở trang bán hàng Shopee (Seller Centre) bằng một <b>hồ sơ (profile) persistent riêng</b> cho
    /// từng tài khoản (nằm trong <c>&lt;thư-mục-DB&gt;/profiles/&lt;id&gt;</c>) để người dùng tự đăng nhập —
    /// lần sau mở lại vẫn còn đăng nhập. Trong lúc cửa sổ mở, app <b>tự động bắt & lưu cookie</b> (không
    /// hỏi nữa) và kết thúc khi người dùng đóng cửa sổ.
    /// Định tuyến proxy: danh sách proxy thủ công dùng round-robin; nếu trống thì kiểm proxy KiotProxy
    /// còn sống (API + thử kết nối), chết thì dùng IP máy.
    /// </summary>
    [RelayCommand]
    private async Task OpenSellerAsync()
    {
        // Phòng hờ (nút vốn đã disable khi chưa lưu): cần Id để biết ghi cookie vào đâu.
        if (_editingId is null)
        {
            await DialogService.InfoAsync(
                "Mở trang bán hàng",
                "Hãy lưu tài khoản trước khi mở trang bán hàng.");
            return;
        }

        // Chụp Id tài khoản mục tiêu NGAY tại đây. Sau nhiều await dài (tải Chromium, mở browser,
        // chờ đăng nhập) người dùng có thể chọn sang tài khoản khác hoặc bấm "+ Thêm" khiến
        // _editingId đổi/null. Cookie phải luôn được ghi vào ĐÚNG tài khoản này, và không được
        // đọc lại _editingId.Value (tránh ghi nhầm / ném InvalidOperationException khi null).
        var targetId = _editingId.Value;

        IsBusy = true;
        try
        {
            // Hồ sơ persistent riêng cho tài khoản này → mở lại vẫn còn đăng nhập.
            var baseDir = System.IO.Path.GetDirectoryName(_services.Database.Path) ?? ".";
            var userDataDir = BrowserProfilePaths.ForAccount(baseDir, targetId);
            System.IO.Directory.CreateDirectory(userDataDir); // đảm bảo có thư mục

            // 1) Chọn proxy: proxy thủ công → round-robin (không kiểm); trống → kiểm KiotProxy còn sống.
            BusyStatus = "Đang kiểm tra proxy...";
            var manual = _services.Proxies.GetAll();
            ProxyEntry? proxy;
            if (manual.Count > 0)
            {
                proxy = NextManualProxy(manual); // proxy thủ công: round-robin BỀN qua các lần mở, KHÔNG kiểm
            }
            else
            {
                var kiotKeys = _services.Settings.GetKiotProxyKeys();
                IKiotProxyClient? kiot = kiotKeys.Count == 0 ? null : new KiotProxyClient(kiotKeys);
                proxy = await ProxySelector.SelectKiotProxyAsync(kiot, _healthChecker);
            }

            // 2) Đảm bảo Chromium đã cài (tải lần đầu ~150MB) — chạy nền để không treo UI.
            BusyStatus = "Đang chuẩn bị trình duyệt...";
            var installCode = await Task.Run(() => _loginService.EnsureBrowserInstalled());
            if (installCode != 0)
            {
                await DialogService.InfoAsync(
                    "Mở trang bán hàng",
                    "Không cài được trình duyệt. Kiểm tra mạng rồi thử lại.");
                return;
            }

            // 3) Mở cửa sổ trình duyệt (profile persistent) tới trang bán hàng.
            ILoginSession session;
            try
            {
                session = await _loginService.OpenAsync(userDataDir, proxy);
            }
            catch (Exception ex)
            {
                await DialogService.InfoAsync("Mở trang bán hàng", ex.Message);
                return;
            }

            await using (session)
            {
                // 4) Tự động bắt & lưu cookie trong lúc cửa sổ mở; kết thúc khi người dùng đóng cửa sổ.
                //    KHÔNG hỏi "Đồng ý để lưu" nữa — cookie tự lưu.
                BusyStatus = "Đã mở trình duyệt. Hãy đăng nhập; đăng nhập xong ĐÓNG cửa sổ — cookie sẽ tự lưu.";
                string? lastSaved = null;
                // Poll 1s (thu hẹp cửa sổ bỏ lỡ cookie phút chót khi đăng nhập rồi đóng nhanh).
                const int PollMs = 1000;
                while (!session.IsClosed)
                {
                    await Task.WhenAny(session.Closed, Task.Delay(PollMs));
                    string json;
                    try { json = await session.CaptureCookiesJsonAsync(); }
                    catch { break; } // context đã đóng giữa chừng
                    // CHỈ lưu khi đã có cookie ĐĂNG NHẬP Shopee — trang bán hàng set nhiều cookie theo dõi
                    // (SPC_F, SPC_CDS, csrftoken...) ngay cả khi CHƯA đăng nhập; nếu lưu bừa sẽ đè cookie
                    // hợp lệ cũ và báo "Đã lưu" sai sự thật.
                    if (!string.IsNullOrEmpty(json) && json != lastSaved && ShopeeLoginCookies.IsLoggedIn(json))
                    {
                        if (SaveCapturedCookie(targetId, json) == SaveCookieResult.Saved)
                        {
                            lastSaved = json;
                        }
                    }
                }

                // Thông báo kết quả (KHÔNG hỏi — chỉ báo).
                await DialogService.InfoAsync("Mở trang bán hàng",
                    lastSaved != null
                        ? "Đã lưu cookie đăng nhập vào tài khoản."
                        : "Chưa thấy cookie đăng nhập (có thể bạn chưa đăng nhập).");
            }
        }
        finally
        {
            IsBusy = false;
            BusyStatus = null;
        }
    }

    /// <summary>Chọn proxy thủ công kế tiếp theo round-robin BỀN qua các lần mở (không reset mỗi lần mở).</summary>
    public ProxyEntry? NextManualProxy(IReadOnlyList<ProxyEntry> manual)
    {
        if (manual.Count == 0) return null;
        var p = manual[_manualProxyIndex % manual.Count];
        _manualProxyIndex++;
        return p;
    }

    /// <summary>Kết quả của thao tác lưu cookie đã bắt được vào tài khoản.</summary>
    public enum SaveCookieResult
    {
        /// <summary>JSON không chứa cookie nào (người dùng có thể chưa đăng nhập xong).</summary>
        NoCookie,

        /// <summary>Không còn tài khoản targetId trong DB (có thể đã bị xóa).</summary>
        AccountMissing,

        /// <summary>Đã ghi cookie vào tài khoản.</summary>
        Saved
    }

    /// <summary>
    /// Ghi chuỗi cookie JSON đã bắt được vào ĐÚNG tài khoản <paramref name="targetId"/>. KHÔNG đọc lại
    /// <c>_editingId</c> nên không bị ảnh hưởng khi người dùng đổi chọn/tạo mới trong lúc chờ browser
    /// (chống race ghi nhầm/crash). Tách khỏi Playwright để test được ở mức ViewModel.
    /// </summary>
    /// <remarks>
    /// Luôn dựng lại danh sách (<see cref="RefreshList"/>) để instance trong <see cref="Accounts"/> có
    /// cookie mới — tránh mất cookie khi người dùng chọn lại tài khoản (instance cũ có Cookie rỗng rồi
    /// bị Save ghi đè về null). Chỉ cập nhật FORM và kéo lựa chọn về targetId khi người dùng VẪN đang
    /// mở đúng tài khoản đó; nếu đã chuyển đi thì vẫn lưu DB cho targetId nhưng giữ nguyên form/lựa chọn.
    /// </remarks>
    public SaveCookieResult SaveCapturedCookie(long targetId, string cookieJson)
    {
        if (CookieJson.Deserialize(cookieJson).Count == 0)
        {
            return SaveCookieResult.NoCookie;
        }

        var acc = _services.Accounts.GetById(targetId);
        if (acc is null)
        {
            return SaveCookieResult.AccountMissing;
        }

        acc.Cookie = cookieJson;
        _services.Accounts.Update(acc);

        // Làm mới cache trước khi dựng lại danh sách.
        _all = _services.Accounts.GetAll();

        if (_editingId == targetId)
        {
            // Người dùng vẫn đang mở tài khoản này → cập nhật form + chọn lại instance mới có cookie.
            EditCookie = cookieJson;
            UpdatedAtText = FormatDate(acc.UpdatedAt);
            RefreshList(targetId);
        }
        else
        {
            // Đã chuyển sang tài khoản khác / đang tạo mới → dựng lại danh sách (để instance của
            // targetId có cookie) nhưng giữ nguyên lựa chọn & form hiện tại.
            RefreshList(_editingId ?? SelectedAccount?.Id);
        }

        return SaveCookieResult.Saved;
    }

    private void LoadIntoForm(Account a)
    {
        _editingId = a.Id;
        EditEmail = a.Email;
        EditPassword = a.Password;
        EditPhone = a.Phone ?? string.Empty;
        EditCookie = a.Cookie ?? string.Empty;
        EditNote = a.Note ?? string.Empty;
        EditStatus = a.Status;
        CreatedAtText = FormatDate(a.CreatedAt);
        UpdatedAtText = FormatDate(a.UpdatedAt);
        ErrorMessage = null;
        ShowPassword = false;
        OnPropertyChanged(nameof(CanOpenSeller));
    }

    private void ClearForm()
    {
        _editingId = null;
        EditEmail = string.Empty;
        EditPassword = string.Empty;
        EditPhone = string.Empty;
        EditCookie = string.Empty;
        EditNote = string.Empty;
        EditStatus = AccountStatus.ChuaKiemTra;
        CreatedAtText = null;
        UpdatedAtText = null;
        ErrorMessage = null;
        ShowPassword = false;
        OnPropertyChanged(nameof(CanOpenSeller));
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FormatDate(DateTime utc)
        => utc == default ? string.Empty : utc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
}
