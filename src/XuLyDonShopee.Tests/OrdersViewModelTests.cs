using System;
using System.Linq;
using System.Text;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// Test OrdersViewModel chạy headless (ObservableObject + repository trên DB tạm, không dựng cửa sổ):
/// nạp/map nhãn tài khoản, dựng chuỗi hiển thị (sản phẩm "(+n)", tổng tiền ₫), lọc theo tài
/// khoản/trạng thái/tìm kiếm, và map dòng đang hiển thị sang CSV.
/// </summary>
public class OrdersViewModelTests
{
    private static long SeedAccount(AppServices services, string email)
        => services.Accounts.Insert(new Account { Email = email, Password = "p" });

    [Fact]
    public void Reload_HienThiTatCaDon_MapNhanTaiKhoan_VaDinhDangHienThi()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        var accId = SeedAccount(services, "shopA@mail.com");
        services.Orders.UpsertMany(accId, new[]
        {
            new SyncedOrder
            {
                OrderSn = "SN1", Status = "Đã hủy", ItemSummary = "Giày", ItemCount = 3,
                TotalPrice = 166500, BuyerUsername = "buyer1",
                CancelReason = "Giao dịch bất thường", Carrier = "SPX", TrackingNumber = "SPX1",
            }
        }, DateTime.UtcNow);

        var vm = new OrdersViewModel(services);

        var row = Assert.Single(vm.Rows);
        Assert.Equal("shopA@mail.com", row.AccountLabel);
        Assert.Equal("Giày (+2)", row.Product);           // item_count 3 → +2 sản phẩm còn lại
        Assert.Equal("₫166.500", row.Total);              // định dạng nhóm nghìn bằng dấu chấm
        Assert.Equal("Giao dịch bất thường", row.Note);   // ưu tiên lý do hủy
        Assert.Equal("Đang hiển thị: 1 đơn", vm.TotalText);
    }

    [Fact]
    public void ItemCount1_KhongThemHauTo()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        var accId = SeedAccount(services, "a@mail.com");
        services.Orders.UpsertMany(accId, new[]
        {
            new SyncedOrder { OrderSn = "SN1", ItemSummary = "Áo", ItemCount = 1 }
        }, DateTime.UtcNow);

        var vm = new OrdersViewModel(services);
        Assert.Equal("Áo", Assert.Single(vm.Rows).Product);
    }

    [Fact]
    public void LocTheoTaiKhoan_ChiHienDonCuaTaiKhoanDo()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        var a1 = SeedAccount(services, "a1@mail.com");
        var a2 = SeedAccount(services, "a2@mail.com");
        services.Orders.UpsertMany(a1, new[] { new SyncedOrder { OrderSn = "A1" } }, DateTime.UtcNow);
        services.Orders.UpsertMany(a2, new[]
        {
            new SyncedOrder { OrderSn = "B1" }, new SyncedOrder { OrderSn = "B2" }
        }, DateTime.UtcNow);

        var vm = new OrdersViewModel(services);
        Assert.Equal(3, vm.Rows.Count);

        vm.SelectedAccount = vm.AccountOptions.First(o => o.Id == a2);
        Assert.Equal(2, vm.Rows.Count);
        Assert.All(vm.Rows, r => Assert.Equal("a2@mail.com", r.AccountLabel));
    }

    [Fact]
    public void LocTheoTrangThai_VaTimKiemTrucTiep()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        var a1 = SeedAccount(services, "a1@mail.com");
        services.Orders.UpsertMany(a1, new[]
        {
            new SyncedOrder { OrderSn = "SN1", Status = "Đã hủy", BuyerUsername = "alpha", ItemSummary = "Áo" },
            new SyncedOrder { OrderSn = "SN2", Status = "Chờ lấy hàng", BuyerUsername = "beta", ItemSummary = "Quần" },
        }, DateTime.UtcNow);

        var vm = new OrdersViewModel(services);
        Assert.Equal(2, vm.Rows.Count);

        // ComboBox trạng thái nạp đủ 2 trạng thái + mục "tất cả".
        Assert.Contains("Đã hủy", vm.StatusOptions);
        Assert.Contains("Chờ lấy hàng", vm.StatusOptions);
        Assert.Equal(OrdersViewModel.AllStatusesLabel, vm.StatusOptions[0]);

        vm.SelectedStatus = "Đã hủy";
        Assert.Equal("SN1", Assert.Single(vm.Rows).OrderSn);

        vm.SelectedStatus = OrdersViewModel.AllStatusesLabel; // bỏ lọc trạng thái
        Assert.Equal(2, vm.Rows.Count);

        vm.SearchText = "beta"; // tìm theo người mua, áp dụng ngay
        Assert.Equal("SN2", Assert.Single(vm.Rows).OrderSn);
    }

    [Fact]
    public void Reload_GiuLuaChonTaiKhoan_KhiConTonTai()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        var a1 = SeedAccount(services, "a1@mail.com");
        var a2 = SeedAccount(services, "a2@mail.com");
        services.Orders.UpsertMany(a2, new[] { new SyncedOrder { OrderSn = "B1" } }, DateTime.UtcNow);

        var vm = new OrdersViewModel(services);
        vm.SelectedAccount = vm.AccountOptions.First(o => o.Id == a2);

        vm.RefreshCommand.Execute(null); // như bấm "Làm mới"

        Assert.Equal(a2, vm.SelectedAccount!.Id); // vẫn giữ tài khoản đã chọn
        Assert.Equal("B1", Assert.Single(vm.Rows).OrderSn);
    }

    [Fact]
    public void ExportRows_DungDeDungCsvCoBom()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        var a1 = SeedAccount(services, "a1@mail.com");
        services.Orders.UpsertMany(a1, new[]
        {
            new SyncedOrder { OrderSn = "SN1", ItemSummary = "Giày", ItemCount = 1, TotalPrice = 100000 }
        }, DateTime.UtcNow);

        var vm = new OrdersViewModel(services);
        var bytes = OrderCsvExporter.BuildCsvWithBom(vm.Rows.Select(r => r.ToExportRow()));

        Assert.Equal(0xEF, bytes[0]);
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.Contains("SN1", text);
        Assert.Contains("a1@mail.com", text);
        Assert.Contains("₫100.000", text);
    }

    [Fact]
    public void ComboBoxTrangThai_TheoTaiKhoanDangLoc()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        var a1 = SeedAccount(services, "a1@mail.com");
        var a2 = SeedAccount(services, "a2@mail.com");
        services.Orders.UpsertMany(a1, new[] { new SyncedOrder { OrderSn = "A1", Status = "Đã hủy" } }, DateTime.UtcNow);
        services.Orders.UpsertMany(a2, new[] { new SyncedOrder { OrderSn = "B1", Status = "Chờ lấy hàng" } }, DateTime.UtcNow);

        var vm = new OrdersViewModel(services);
        // "Tất cả tài khoản" → thấy cả 2 trạng thái (sắp xếp: 'C' trước 'Đ').
        Assert.Equal(new[] { OrdersViewModel.AllStatusesLabel, "Chờ lấy hàng", "Đã hủy" }, vm.StatusOptions);

        vm.SelectedAccount = vm.AccountOptions.First(o => o.Id == a1);
        Assert.Equal(new[] { OrdersViewModel.AllStatusesLabel, "Đã hủy" }, vm.StatusOptions);

        vm.SelectedAccount = vm.AccountOptions.First(o => o.Id == a2);
        Assert.Equal(new[] { OrdersViewModel.AllStatusesLabel, "Chờ lấy hàng" }, vm.StatusOptions);
    }

    [Fact]
    public void DoiTaiKhoan_TrangThaiCuKhongCon_VeTatCa_VaHienDungBang()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        var a1 = SeedAccount(services, "a1@mail.com");
        var a2 = SeedAccount(services, "a2@mail.com");
        services.Orders.UpsertMany(a1, new[] { new SyncedOrder { OrderSn = "A1", Status = "Đã hủy" } }, DateTime.UtcNow);
        services.Orders.UpsertMany(a2, new[] { new SyncedOrder { OrderSn = "B1", Status = "Chờ lấy hàng" } }, DateTime.UtcNow);

        var vm = new OrdersViewModel(services);
        vm.SelectedAccount = vm.AccountOptions.First(o => o.Id == a1);
        vm.SelectedStatus = "Đã hủy";
        Assert.Equal("A1", Assert.Single(vm.Rows).OrderSn);

        // Đổi sang a2 (không có trạng thái "Đã hủy") → về "Tất cả", bảng hiện đơn của a2 (không rỗng).
        vm.SelectedAccount = vm.AccountOptions.First(o => o.Id == a2);
        Assert.Equal(OrdersViewModel.AllStatusesLabel, vm.SelectedStatus);
        Assert.Equal("B1", Assert.Single(vm.Rows).OrderSn);
    }
}
