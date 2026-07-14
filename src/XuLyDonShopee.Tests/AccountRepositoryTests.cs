using XuLyDonShopee.Core.Data;
using XuLyDonShopee.Core.Models;

namespace XuLyDonShopee.Tests;

public class AccountRepositoryTests
{
    private static Account SampleAccount() => new()
    {
        Email = "test@shopee.vn",
        Password = "matkhau123",
        Phone = "0900000000",
        Cookie = "SPC_SI=abc",
        Note = "tài khoản test",
        Status = AccountStatus.HoatDong
    };

    [Fact]
    public void Insert_RoiGetById_TraVeDayDu()
    {
        using var temp = new TempDatabase();
        var repo = new AccountRepository(temp.Open());

        var account = SampleAccount();
        var id = repo.Insert(account);

        Assert.True(id > 0);
        Assert.Equal(id, account.Id);
        Assert.NotEqual(default, account.CreatedAt);

        var loaded = repo.GetById(id);
        Assert.NotNull(loaded);
        Assert.Equal("test@shopee.vn", loaded!.Email);
        Assert.Equal("matkhau123", loaded.Password);
        Assert.Equal("0900000000", loaded.Phone);
        Assert.Equal("SPC_SI=abc", loaded.Cookie);
        Assert.Equal("tài khoản test", loaded.Note);
        Assert.Equal(AccountStatus.HoatDong, loaded.Status);
    }

    [Fact]
    public void GetAll_TraVeTatCa()
    {
        using var temp = new TempDatabase();
        var repo = new AccountRepository(temp.Open());

        repo.Insert(new Account { Email = "a@x.com", Password = "1" });
        repo.Insert(new Account { Email = "b@x.com", Password = "2" });

        var all = repo.GetAll();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void Update_ThayDoiDuLieu_VaUpdatedAt()
    {
        using var temp = new TempDatabase();
        var repo = new AccountRepository(temp.Open());

        var account = SampleAccount();
        repo.Insert(account);
        var createdAt = account.CreatedAt;

        account.Email = "new@shopee.vn";
        account.Status = AccountStatus.BiKhoa;
        repo.Update(account);

        var loaded = repo.GetById(account.Id);
        Assert.NotNull(loaded);
        Assert.Equal("new@shopee.vn", loaded!.Email);
        Assert.Equal(AccountStatus.BiKhoa, loaded.Status);
        Assert.True(loaded.UpdatedAt >= createdAt);
    }

    [Fact]
    public void Delete_XoaKhoiDb()
    {
        using var temp = new TempDatabase();
        var repo = new AccountRepository(temp.Open());

        var account = SampleAccount();
        repo.Insert(account);

        repo.Delete(account.Id);

        Assert.Null(repo.GetById(account.Id));
        Assert.Empty(repo.GetAll());
    }

    [Fact]
    public void DuLieu_ConNguyen_SauKhiMoLaiDatabase()
    {
        using var temp = new TempDatabase();

        long id;
        // Phiên 1: ghi dữ liệu.
        {
            var repo1 = new AccountRepository(temp.Open());
            var account = SampleAccount();
            id = repo1.Insert(account);
        }

        // Phiên 2: mở lại Database mới trỏ cùng file, dữ liệu vẫn còn.
        {
            var repo2 = new AccountRepository(temp.Open());
            var loaded = repo2.GetById(id);
            Assert.NotNull(loaded);
            Assert.Equal("test@shopee.vn", loaded!.Email);
            Assert.Equal(AccountStatus.HoatDong, loaded.Status);
        }
    }
}
