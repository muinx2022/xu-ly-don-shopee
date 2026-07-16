using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using XuLyDonShopee.App.Views;

namespace XuLyDonShopee.App.Services;

/// <summary>
/// Hiển thị các hộp thoại modal (xác nhận, nhập proxy). Cửa sổ chính được gán khi app khởi động.
/// </summary>
public static class DialogService
{
    public static Window? MainWindow { get; set; }

    /// <summary>Hộp thoại xác nhận (Đồng ý/Hủy). Trả true nếu người dùng đồng ý.</summary>
    public static async Task<bool> ConfirmAsync(string title, string message)
    {
        if (MainWindow is null)
        {
            return false;
        }

        var dialog = new ConfirmDialog(title, message);
        return await dialog.ShowDialog<bool>(MainWindow);
    }

    /// <summary>Hộp thoại thông báo (chỉ nút Đóng).</summary>
    public static async Task InfoAsync(string title, string message)
    {
        if (MainWindow is null)
        {
            return;
        }

        var dialog = new ConfirmDialog(title, message, infoOnly: true);
        await dialog.ShowDialog<bool>(MainWindow);
    }

    /// <summary>Hộp thoại dán danh sách proxy. Trả về văn bản đã dán, hoặc null nếu Hủy.</summary>
    public static async Task<string?> ImportProxyAsync()
    {
        if (MainWindow is null)
        {
            return null;
        }

        var dialog = new ImportProxyDialog();
        return await dialog.ShowDialog<string?>(MainWindow);
    }

    /// <summary>
    /// Mở SaveFileDialog (StorageProvider của cửa sổ chính) cho người dùng chọn nơi lưu, rồi GHI
    /// <paramref name="content"/> (đã gồm BOM) ra file đó. Trả về đường dẫn đã lưu, hoặc null nếu chưa
    /// gán cửa sổ chính / người dùng bấm Hủy. Ghi đè file cũ thì cắt phần đuôi dư (SetLength).
    /// </summary>
    public static async Task<string?> SaveCsvAsync(string suggestedFileName, byte[] content)
    {
        if (MainWindow is null)
        {
            return null;
        }

        var file = await MainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Xuất đơn hàng ra CSV",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "csv",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("CSV (mở bằng Excel)") { Patterns = new[] { "*.csv" } }
            }
        });
        if (file is null)
        {
            return null;
        }

        await using (var stream = await file.OpenWriteAsync())
        {
            await stream.WriteAsync(content);
            if (stream.CanSeek)
            {
                stream.SetLength(content.Length);
            }
        }

        return file.TryGetLocalPath() ?? file.Name;
    }
}
