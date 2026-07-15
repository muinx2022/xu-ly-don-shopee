using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.App.Views;

namespace XuLyDonShopee.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Khởi tạo Database + repositories một lần rồi truyền vào ViewModel.
            var services = new AppServices();

            var mainWindow = new MainWindow
            {
                DataContext = new MainViewModel(services),
            };

            // Cửa sổ chính dùng để mở các hộp thoại modal.
            DialogService.MainWindow = mainWindow;

            desktop.MainWindow = mainWindow;

            // Thoát app → dừng TẤT CẢ phiên (kill hết Brave, không để tiến trình mồ côi giữ khóa hồ sơ).
            // Chờ đồng bộ tại đây để đảm bảo Brave chết trước khi tiến trình app kết thúc.
            desktop.ShutdownRequested += (_, _) =>
            {
                try { services.Sessions.StopAllAsync().GetAwaiter().GetResult(); }
                catch { /* bỏ qua khi thoát */ }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
