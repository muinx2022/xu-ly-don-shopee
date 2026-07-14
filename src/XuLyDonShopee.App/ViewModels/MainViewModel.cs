using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using XuLyDonShopee.App.Services;

namespace XuLyDonShopee.App.ViewModels;

/// <summary>Một mục điều hướng trên sidebar (nhãn + icon).</summary>
public record NavItem(string Label, string Icon);

/// <summary>
/// ViewModel cửa sổ chính: điều hướng giữa các màn hình Tài khoản / Proxy / Cài đặt.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly AccountsViewModel _accountsVm;
    private readonly ProxiesViewModel _proxiesVm;
    private readonly SettingsViewModel _settingsVm;

    public MainViewModel(AppServices services)
    {
        _accountsVm = new AccountsViewModel(services);
        _proxiesVm = new ProxiesViewModel(services);
        _settingsVm = new SettingsViewModel(services);
        _currentViewModel = _accountsVm;
    }

    /// <summary>Các mục trên sidebar.</summary>
    public ObservableCollection<NavItem> NavItems { get; } = new()
    {
        new NavItem("Tài khoản", "◵"),
        new NavItem("Proxy", "⇄"),
        new NavItem("Cài đặt", "⚙")
    };

    [ObservableProperty]
    private int _selectedNavIndex;

    [ObservableProperty]
    private ViewModelBase _currentViewModel;

    partial void OnSelectedNavIndexChanged(int value)
    {
        switch (value)
        {
            case 0:
                _accountsVm.Reload();
                CurrentViewModel = _accountsVm;
                break;
            case 1:
                _proxiesVm.Reload();
                CurrentViewModel = _proxiesVm;
                break;
            case 2:
                _settingsVm.Reload();
                CurrentViewModel = _settingsVm;
                break;
        }
    }
}
