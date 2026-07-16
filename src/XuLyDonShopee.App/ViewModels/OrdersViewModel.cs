using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.App.ViewModels;

/// <summary>Một lựa chọn ở ComboBox lọc theo tài khoản. <see cref="Id"/> null = "Tất cả tài khoản".</summary>
public sealed record AccountFilterOption(long? Id, string Label);

/// <summary>
/// Màn "Đơn hàng" (ĐỌC-CHỈ): lọc theo tài khoản / trạng thái / tìm kiếm, hiển thị bảng đơn đã sync và
/// xuất các dòng đang lọc ra CSV (UTF-8 BOM). Không sửa/xóa đơn ở đây.
/// </summary>
public partial class OrdersViewModel : ViewModelBase
{
    /// <summary>Sentinel cho mục "tất cả" ở ComboBox trạng thái.</summary>
    public const string AllStatusesLabel = "Tất cả trạng thái";

    private readonly AppServices _services;

    /// <summary>Chặn requery khi đang dựng lại danh sách lựa chọn trong <see cref="Reload"/>.</summary>
    private bool _suppressApply;

    public OrdersViewModel(AppServices services)
    {
        _services = services;
        Reload();
    }

    /// <summary>Lựa chọn của ComboBox tài khoản: "Tất cả" + từng tài khoản.</summary>
    public ObservableCollection<AccountFilterOption> AccountOptions { get; } = new();

    /// <summary>Lựa chọn của ComboBox trạng thái: <see cref="AllStatusesLabel"/> + các trạng thái có thật.</summary>
    public ObservableCollection<string> StatusOptions { get; } = new();

    /// <summary>Các dòng đơn đang hiển thị (đã lọc).</summary>
    public ObservableCollection<OrderRowViewModel> Rows { get; } = new();

    [ObservableProperty]
    private AccountFilterOption? _selectedAccount;

    [ObservableProperty]
    private string? _selectedStatus;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Thông báo kết quả xuất CSV (null = ẩn).</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Nhãn tổng số đơn đang hiển thị.</summary>
    public string TotalText => $"Đang hiển thị: {Rows.Count} đơn";

    /// <summary>
    /// Nạp lại từ DB: danh sách tài khoản + trạng thái cho bộ lọc (giữ lựa chọn cũ nếu còn), rồi truy vấn
    /// theo bộ lọc hiện tại. Gọi khi mở màn hoặc bấm "Làm mới" (sau khi vừa sync thêm đơn).
    /// </summary>
    public void Reload()
    {
        _suppressApply = true;

        var prevAccountId = SelectedAccount?.Id;
        var prevStatus = SelectedStatus;

        AccountOptions.Clear();
        AccountOptions.Add(new AccountFilterOption(null, "Tất cả tài khoản"));
        foreach (var a in _services.Accounts.GetAll())
        {
            AccountOptions.Add(new AccountFilterOption(a.Id, a.Email));
        }
        SelectedAccount = AccountOptions.FirstOrDefault(o => o.Id == prevAccountId) ?? AccountOptions[0];

        // Trạng thái CHỈ của tài khoản đang lọc → không chọn phải trạng thái không có đơn nào của TK đó.
        ReloadStatuses(SelectedAccount?.Id, prevStatus);

        _suppressApply = false;
        Apply();
    }

    /// <summary>
    /// Dựng lại danh sách trạng thái cho ComboBox theo tài khoản đang lọc (<paramref name="accountId"/>
    /// null = mọi tài khoản). Giữ <paramref name="preferredStatus"/> nếu còn hợp lệ, không thì về
    /// <see cref="AllStatusesLabel"/>. Người gọi tự quản lý cờ <see cref="_suppressApply"/>.
    /// </summary>
    private void ReloadStatuses(long? accountId, string? preferredStatus)
    {
        StatusOptions.Clear();
        StatusOptions.Add(AllStatusesLabel);
        foreach (var s in _services.Orders.AllStatuses(accountId))
        {
            StatusOptions.Add(s);
        }
        SelectedStatus = preferredStatus is not null && StatusOptions.Contains(preferredStatus)
            ? preferredStatus
            : AllStatusesLabel;
    }

    /// <summary>Chạy truy vấn theo bộ lọc hiện tại và đổ vào bảng (map account_id → nhãn tài khoản).</summary>
    private void Apply()
    {
        var accountId = SelectedAccount?.Id;
        var status = string.IsNullOrEmpty(SelectedStatus) || SelectedStatus == AllStatusesLabel
            ? null
            : SelectedStatus;
        var search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText;

        var labels = _services.Accounts.GetAll().ToDictionary(a => a.Id, a => a.Email);

        Rows.Clear();
        foreach (var row in _services.Orders.Query(accountId, status, search))
        {
            var label = labels.TryGetValue(row.AccountId, out var email) ? email : $"(TK #{row.AccountId})";
            // notify: link "In phiếu" của dòng báo trạng thái (thiếu file / lỗi mở) ra StatusMessage của màn.
            Rows.Add(new OrderRowViewModel(row, label, msg => StatusMessage = msg));
        }

        OnPropertyChanged(nameof(TotalText));
    }

    partial void OnSelectedAccountChanged(AccountFilterOption? value)
    {
        if (_suppressApply)
        {
            return;
        }

        // Đổi tài khoản → nạp lại trạng thái theo tài khoản đó (giữ trạng thái đang chọn nếu còn hợp lệ).
        _suppressApply = true;
        ReloadStatuses(value?.Id, SelectedStatus);
        _suppressApply = false;

        Apply();
    }

    partial void OnSelectedStatusChanged(string? value)
    {
        if (!_suppressApply)
        {
            Apply();
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        if (!_suppressApply)
        {
            Apply(); // tìm kiếm trực tiếp theo từng ký tự
        }
    }

    /// <summary>Nút "Làm mới": nạp lại toàn bộ từ DB (đón tài khoản/trạng thái/đơn mới sau khi sync) + áp bộ lọc.</summary>
    [RelayCommand]
    private void Refresh()
    {
        StatusMessage = null;
        Reload();
    }

    /// <summary>Nút "Xuất CSV": ghi các dòng ĐANG hiển thị ra file (UTF-8 BOM) qua SaveFileDialog.</summary>
    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        if (Rows.Count == 0)
        {
            StatusMessage = "Không có đơn nào để xuất.";
            return;
        }

        var count = Rows.Count;
        var bytes = OrderCsvExporter.BuildCsvWithBom(Rows.Select(r => r.ToExportRow()));

        string? saved;
        try
        {
            saved = await DialogService.SaveCsvAsync(SuggestFileName(), bytes);
        }
        catch (OperationCanceledException)
        {
            throw; // hủy tác vụ → ném xuyên, không nuốt
        }
        catch (Exception ex)
        {
            // Lỗi GHI thật (file .csv đang mở trong Excel, thiếu quyền, đĩa đầy...) → báo, KHÔNG để app crash.
            var failMessage = $"Xuất CSV thất bại: {ex.Message}";
            StatusMessage = failMessage;
            _services.Log.Append("Đơn hàng", failMessage);
            return;
        }

        if (saved is null)
        {
            return; // người dùng bấm Hủy → im lặng
        }

        var message = $"Đã xuất {count} đơn → {saved}";
        StatusMessage = message;
        _services.Log.Append("Đơn hàng", message);
    }

    /// <summary>Tên file gợi ý: <c>don-hang-{email|tatca}-{yyyyMMdd-HHmm}.csv</c> (email đã bỏ ký tự cấm).</summary>
    private string SuggestFileName()
    {
        var acc = SelectedAccount;
        var who = acc is null || acc.Id is null ? "tatca" : Sanitize(acc.Label);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
        return $"don-hang-{who}-{stamp}.csv";
    }

    /// <summary>Thay các ký tự không hợp lệ trong tên file bằng '_'.</summary>
    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrEmpty(cleaned) ? "tk" : cleaned;
    }
}
