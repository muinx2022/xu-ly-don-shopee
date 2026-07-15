using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using XuLyDonShopee.App.ViewModels;

namespace XuLyDonShopee.App.Views;

public partial class AccountsView : UserControl
{
    // Đang lắng nghe collection nào (để gỡ đăng ký khi DataContext đổi, tránh rò rỉ / cuộn nhầm).
    private INotifyCollectionChanged? _watchedLog;

    public AccountsView()
    {
        InitializeComponent();
        // DataContext gắn sau khi khởi tạo → theo dõi để đăng ký cuộn khi có VM.
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// Khi DataContext (AccountsViewModel) gắn/đổi: đăng ký lắng nghe <c>LogEntries</c> để mỗi khi có dòng
    /// log mới thì tự cuộn panel xuống dòng cuối. Gỡ đăng ký cũ trước để không rò rỉ. Nuốt lỗi an toàn.
    /// </summary>
    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_watchedLog is not null)
        {
            _watchedLog.CollectionChanged -= OnLogEntriesChanged;
            _watchedLog = null;
        }

        if (DataContext is AccountsViewModel vm && vm.LogEntries is INotifyCollectionChanged incc)
        {
            _watchedLog = incc;
            incc.CollectionChanged += OnLogEntriesChanged;
        }
    }

    /// <summary>
    /// Có dòng log mới (Add) → cuộn ListBox xuống dòng cuối để luôn thấy hoạt động mới nhất. Marshal về UI
    /// thread cho chắc (LogEntries chỉ mutate trên UI thread nhưng vẫn phòng hờ). Nuốt mọi lỗi (panel có
    /// thể chưa gắn xong).
    /// </summary>
    private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var list = this.FindControl<ListBox>("LogList");
                if (list?.ItemCount > 0)
                {
                    list.ScrollIntoView(list.ItemCount - 1);
                }
            }
            catch
            {
                // Bỏ qua: panel chưa dựng xong / control đã tháo.
            }
        });
    }
}
