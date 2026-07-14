using System.Linq;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// Test hồi quy cho AccountsViewModel. ViewModel không cần Avalonia runtime để chạy logic
/// (chỉ dùng ObservableObject + repository trên DB tạm), nên test được bằng xunit thuần.
/// Lưu ý: đường dẫn xóa/hủy dùng DialogService (Avalonia Window) nên KHÔNG test ở đây.
/// </summary>
public class AccountsViewModelTests
{
    // ===== Lỗi 1: lưu tài khoản mới khi đang có bộ lọc tìm kiếm =====
    [Fact]
    public void Save_TaoMoiKhiDangLoc_ChonDungBanGhi_VaKhongTaoTrung()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        // Có sẵn 1 tài khoản để bộ lọc "abc" có ý nghĩa.
        services.Accounts.Insert(new Account { Email = "abc@mail.com", Password = "p" });

        var vm = new AccountsViewModel(services);
        vm.SearchText = "abc";
        Assert.Single(vm.Accounts); // chỉ hiển thị abc@

        vm.AddCommand.Execute(null);
        vm.EditEmail = "xyz@mail.com"; // email KHÔNG chứa "abc"
        vm.EditPassword = "123";
        vm.SaveCommand.Execute(null);

        // Sau khi lưu: form về trạng thái sửa bình thường, bản ghi mới được chọn và hiển thị.
        Assert.False(vm.IsNew);
        Assert.NotNull(vm.SelectedAccount);
        Assert.Equal("xyz@mail.com", vm.SelectedAccount!.Email);
        Assert.Contains(vm.Accounts, a => a.Email == "xyz@mail.com");
        Assert.Null(vm.ErrorMessage);
        Assert.Equal(2, services.Accounts.GetAll().Count);

        // Lưu lần 2 (không đổi gì): KHÔNG tạo bản ghi trùng, KHÔNG báo lỗi "email đã tồn tại".
        vm.SaveCommand.Execute(null);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal(2, services.Accounts.GetAll().Count);
    }

    // ===== Phần 1: cho phép nhập user KHÔNG phải email (bỏ ràng buộc định dạng email) =====
    [Fact]
    public void Save_UserKhongPhaiEmail_VanLuuDuoc()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);

        var vm = new AccountsViewModel(services);
        vm.AddCommand.Execute(null);
        vm.EditEmail = "shopee_user01"; // KHÔNG có '@'
        vm.EditPassword = "123";
        vm.SaveCommand.Execute(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Single(services.Accounts.GetAll());
        Assert.Equal("shopee_user01", services.Accounts.GetAll().First().Email);
        Assert.NotNull(vm.SelectedAccount);
        Assert.Equal("shopee_user01", vm.SelectedAccount!.Email);
    }

    // ===== Phần 1: user rỗng vẫn phải báo lỗi và KHÔNG ghi DB =====
    [Fact]
    public void Save_UserRong_BaoLoi()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);

        var vm = new AccountsViewModel(services);
        vm.AddCommand.Execute(null);
        vm.EditEmail = "";
        vm.EditPassword = "123";
        vm.SaveCommand.Execute(null);

        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(services.Accounts.GetAll());
    }

    // ===== Lỗi A: proxy thủ công phải round-robin BỀN qua các lần mở (không reset về [0]) =====
    [Fact]
    public void NextManualProxy_XoayVongBenQuaCacLanGoi()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        var vm = new AccountsViewModel(services);

        var manual = new List<ProxyEntry>
        {
            new() { Host = "P0", Port = 8080 },
            new() { Host = "P1", Port = 8080 },
            new() { Host = "P2", Port = 8080 },
        };

        var hosts = new List<string?>();
        for (var i = 0; i < 4; i++)
        {
            hosts.Add(vm.NextManualProxy(manual)?.Host);
        }

        // 4 lần gọi trên list 3 proxy → P0,P1,P2,P0 (chỉ số bền, không reset về 0 mỗi lần).
        Assert.Equal(new[] { "P0", "P1", "P2", "P0" }, hosts);

        // List rỗng → null.
        Assert.Null(vm.NextManualProxy(new List<ProxyEntry>()));
    }

    // ===== Lỗi 2: làm mới danh sách khi đang sửa dở không được mất thay đổi chưa lưu =====
    // Trigger thật trong app là gõ ô tìm kiếm (ListBox tự đẩy SelectedItem về null khi ItemsSource
    // bị Clear). Cơ chế lỗi gốc — "thao tác chọn lại tài khoản làm nạp đè form" — được tái hiện ở
    // đây qua Reload() (re-fetch sinh instance mới, khiến code cũ kích hoạt nạp đè).
    [Fact]
    public void Reload_KhiDangSuaDo_GiuNguyenThayDoiChuaLuu()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        services.Accounts.Insert(new Account { Email = "keep@mail.com", Password = "orig" });

        var vm = new AccountsViewModel(services);
        vm.SelectedAccount = vm.Accounts.First(a => a.Email == "keep@mail.com");
        Assert.Equal("orig", vm.EditPassword);

        vm.EditPassword = "changed"; // sửa dở, chưa lưu

        vm.Reload(); // giống lúc gõ tìm kiếm / quay lại tab

        Assert.Equal("changed", vm.EditPassword); // KHÔNG bị nạp lại về "orig"
        Assert.True(vm.IsEditing);
        Assert.NotNull(vm.SelectedAccount);
        Assert.Equal("keep@mail.com", vm.SelectedAccount!.Email);
    }

    // ===== Chọn sang tài khoản KHÁC vẫn phải nạp form của tài khoản đó (không hồi quy) =====
    [Fact]
    public void ChonTaiKhoanKhac_NapFormCuaTaiKhoanDo()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        services.Accounts.Insert(new Account { Email = "a@mail.com", Password = "pa" });
        services.Accounts.Insert(new Account { Email = "b@mail.com", Password = "pb" });

        var vm = new AccountsViewModel(services);
        vm.SelectedAccount = vm.Accounts.First(a => a.Email == "a@mail.com");
        Assert.Equal("pa", vm.EditPassword);

        vm.SelectedAccount = vm.Accounts.First(a => a.Email == "b@mail.com");
        Assert.Equal("b@mail.com", vm.EditEmail);
        Assert.Equal("pb", vm.EditPassword);
    }

    // ===== Cập nhật tài khoản hiện có (không lọc) vẫn ghi đúng DB =====
    [Fact]
    public void Save_CapNhatTaiKhoanHienCo_GhiDungDb()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        services.Accounts.Insert(new Account { Email = "c@mail.com", Password = "old" });

        var vm = new AccountsViewModel(services);
        vm.SelectedAccount = vm.Accounts.First();
        vm.EditPassword = "new";
        vm.EditStatus = AccountStatus.BiKhoa;
        vm.SaveCommand.Execute(null);

        Assert.Null(vm.ErrorMessage);
        Assert.Single(services.Accounts.GetAll());
        var saved = services.Accounts.GetAll().First();
        Assert.Equal("new", saved.Password);
        Assert.Equal(AccountStatus.BiKhoa, saved.Status);
    }

    // Chuỗi cookie JSON hợp lệ (không rỗng) để mô phỏng phiên đã đăng nhập.
    private static string SampleCookieJson()
        => CookieJson.Serialize(new[]
        {
            new StoredCookie("SPC_EC", "abc123", ".shopee.vn", "/", 0, true, true, "Lax")
        });

    // ===== Lỗi A: đổi chọn tài khoản giữa chừng → cookie phải ghi vào ĐÚNG targetId, không sang tài khoản khác =====
    [Fact]
    public void LuuCookie_DoiChonGiuaChung_GhiVaoTargetId_KhongDeSangTaiKhoanDangXem()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        services.Accounts.Insert(new Account { Email = "a@mail.com", Password = "pa" });
        services.Accounts.Insert(new Account { Email = "b@mail.com", Password = "pb" });

        var vm = new AccountsViewModel(services);
        var a = vm.Accounts.First(x => x.Email == "a@mail.com");
        var b = vm.Accounts.First(x => x.Email == "b@mail.com");
        long aId = a.Id, bId = b.Id;

        // Bắt đầu luồng cho A (targetId = aId), rồi người dùng đổi chọn sang B giữa chừng.
        vm.SelectedAccount = a;
        vm.SelectedAccount = b; // _editingId giờ = bId

        var json = SampleCookieJson();
        var result = vm.SaveCapturedCookie(aId, json);

        Assert.Equal(AccountsViewModel.SaveCookieResult.Saved, result);
        // Cookie ghi vào ĐÚNG A, KHÔNG ghi vào B.
        Assert.Equal(json, services.Accounts.GetById(aId)!.Cookie);
        Assert.Null(services.Accounts.GetById(bId)!.Cookie);
        // Form đang xem B → KHÔNG bị đè cookie của A, vẫn hiển thị B.
        Assert.NotEqual(json, vm.EditCookie);
        Assert.Equal("b@mail.com", vm.EditEmail);
    }

    // ===== Lỗi A: bấm "+ Thêm" giữa chừng (_editingId = null) → không ném, vẫn ghi đúng targetId =====
    [Fact]
    public void LuuCookie_KhiEditingIdNull_KhongNem_VanGhiTargetId()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        services.Accounts.Insert(new Account { Email = "a@mail.com", Password = "pa" });

        var vm = new AccountsViewModel(services);
        long aId = vm.Accounts.First().Id;
        vm.SelectedAccount = vm.Accounts.First();

        // Người dùng bấm "+ Thêm" giữa chừng → _editingId = null.
        vm.AddCommand.Execute(null);

        var json = SampleCookieJson();
        var ex = Record.Exception(() => vm.SaveCapturedCookie(aId, json));

        Assert.Null(ex); // không ném dù _editingId đã thành null
        Assert.Equal(json, services.Accounts.GetById(aId)!.Cookie); // vẫn ghi vào A
        Assert.True(vm.IsNew); // vẫn ở form tạo mới, không bị kéo về A
    }

    // ===== Lỗi B: sau khi lưu cookie, chọn lại tài khoản phải thấy cookie; Save không xóa mất cookie =====
    [Fact]
    public void LuuCookie_ChonLaiTaiKhoan_FormHienDungCookie_SaveKhongMatCookie()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        services.Accounts.Insert(new Account { Email = "a@mail.com", Password = "pa" });
        services.Accounts.Insert(new Account { Email = "b@mail.com", Password = "pb" });

        var vm = new AccountsViewModel(services);
        long aId = vm.Accounts.First(x => x.Email == "a@mail.com").Id;
        vm.SelectedAccount = vm.Accounts.First(x => x.Email == "a@mail.com"); // đang mở A

        var json = SampleCookieJson();
        var result = vm.SaveCapturedCookie(aId, json);
        Assert.Equal(AccountsViewModel.SaveCookieResult.Saved, result);

        // Chọn sang B rồi quay lại A từ danh sách hiển thị (instance trong Accounts).
        vm.SelectedAccount = vm.Accounts.First(x => x.Email == "b@mail.com");
        vm.SelectedAccount = vm.Accounts.First(x => x.Email == "a@mail.com");

        // Form hiển thị đúng cookie vừa lưu (Accounts đã được dựng lại với instance mới có cookie).
        Assert.Equal(json, vm.EditCookie);

        // Bấm Lưu KHÔNG được xóa cookie (trước đây instance cũ rỗng → Save ghi null).
        vm.SaveCommand.Execute(null);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal(json, services.Accounts.GetById(aId)!.Cookie);
    }

    // ===== Lỗi B (biến thể): người dùng đang mở A, lưu cookie xong không cần chọn lại vẫn có cookie trên form =====
    [Fact]
    public void LuuCookie_DangMoDungTaiKhoan_FormCapNhatCookieNgay()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        services.Accounts.Insert(new Account { Email = "a@mail.com", Password = "pa" });

        var vm = new AccountsViewModel(services);
        long aId = vm.Accounts.First().Id;
        vm.SelectedAccount = vm.Accounts.First();

        var json = SampleCookieJson();
        vm.SaveCapturedCookie(aId, json);

        // Form cập nhật cookie ngay và instance trong danh sách cũng có cookie.
        Assert.Equal(json, vm.EditCookie);
        Assert.Equal(json, vm.Accounts.First(x => x.Id == aId).Cookie);
    }
}
