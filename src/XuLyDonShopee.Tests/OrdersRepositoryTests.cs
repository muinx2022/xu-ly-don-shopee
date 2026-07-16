using XuLyDonShopee.Core.Data;
using XuLyDonShopee.Core.Models;

namespace XuLyDonShopee.Tests;

/// <summary>
/// Test upsert bảng <c>orders</c>: thêm mới đếm đúng inserted; sync lại cùng mã đơn thì CẬP NHẬT (không tạo
/// trùng) + giữ created_at; khóa là cặp (account_id, order_sn); đơn không có mã bị bỏ; lưu đủ trường.
/// </summary>
public class OrdersRepositoryTests
{
    private static SyncedOrder Sample(string sn) => new()
    {
        OrderSn = sn,
        ShopeeOrderId = "237900524283161",
        BuyerUsername = "quynhsuugiacshoppi",
        ItemsJson = "[{\"name\":\"Giày\",\"variation\":\"ĐEN,37\",\"amount\":\"1\",\"image\":\"x\"}]",
        ItemCount = 1,
        ItemSummary = "Giày",
        TotalPriceText = "₫166.500",
        TotalPrice = 166500,
        PaymentMethod = "Thanh toán khi nhận hàng",
        Status = "Đã hủy",
        StatusDescription = "Đã hủy tự động bởi hệ thống Shopee",
        CancelReason = "Hủy đơn hàng vì hành vi giao dịch bất thường.",
        Channel = "Nhanh",
        Carrier = "SPX Express",
        TrackingNumber = "SPXVN068067521447",
    };

    [Fact]
    public void UpsertMany_ThemMoi_DemDungInserted()
    {
        using var temp = new TempDatabase();
        var repo = new OrdersRepository(temp.Open());

        var (inserted, updated) = repo.UpsertMany(1, new[] { Sample("SN1"), Sample("SN2") }, DateTime.UtcNow);

        Assert.Equal(2, inserted);
        Assert.Equal(0, updated);
        Assert.Equal(2, repo.CountByAccount(1));
    }

    [Fact]
    public void UpsertMany_SyncLai_CapNhat_KhongTaoTrung_GiuCreatedAt()
    {
        using var temp = new TempDatabase();
        var db = temp.Open();
        var repo = new OrdersRepository(db);

        var t1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        repo.UpsertMany(1, new[] { Sample("SN1") }, t1);
        var created1 = ReadString(db, "SN1", "created_at");

        // Sync lại cùng mã đơn, dữ liệu đổi, thời điểm sync KHÁC.
        var t2 = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc);
        var again = Sample("SN1");
        again.Status = "Chờ lấy hàng";
        again.TotalPrice = 200000;
        var (inserted, updated) = repo.UpsertMany(1, new[] { again }, t2);

        Assert.Equal(0, inserted);
        Assert.Equal(1, updated);
        Assert.Equal(1, repo.CountByAccount(1)); // KHÔNG tạo dòng trùng

        Assert.Equal("Chờ lấy hàng", ReadString(db, "SN1", "status"));      // dữ liệu cập nhật
        Assert.Equal("200000", ReadString(db, "SN1", "total_price"));
        Assert.Equal(created1, ReadString(db, "SN1", "created_at"));         // created_at GIỮ nguyên
        Assert.NotEqual(created1, ReadString(db, "SN1", "updated_at"));      // updated_at đổi theo lần sync mới
    }

    [Fact]
    public void UpsertMany_KhacTaiKhoan_CungMaDon_HaiDong()
    {
        using var temp = new TempDatabase();
        var repo = new OrdersRepository(temp.Open());

        repo.UpsertMany(1, new[] { Sample("SN1") }, DateTime.UtcNow);
        var (inserted, updated) = repo.UpsertMany(2, new[] { Sample("SN1") }, DateTime.UtcNow);

        Assert.Equal(1, inserted);   // khóa là (account_id, order_sn) → tài khoản 2 là dòng MỚI
        Assert.Equal(0, updated);
        Assert.Equal(1, repo.CountByAccount(1));
        Assert.Equal(1, repo.CountByAccount(2));
    }

    [Fact]
    public void UpsertMany_BoQuaDonKhongCoMa()
    {
        using var temp = new TempDatabase();
        var repo = new OrdersRepository(temp.Open());

        var noSn = Sample("");        // OrderSn rỗng → không làm khóa được, bị bỏ
        var (inserted, updated) = repo.UpsertMany(1, new[] { noSn, Sample("SN1") }, DateTime.UtcNow);

        Assert.Equal(1, inserted);    // chỉ SN1 được thêm
        Assert.Equal(0, updated);
        Assert.Equal(1, repo.CountByAccount(1));
    }

    [Fact]
    public void UpsertMany_LuuDayDuTruong()
    {
        using var temp = new TempDatabase();
        var db = temp.Open();
        var repo = new OrdersRepository(db);

        repo.UpsertMany(7, new[] { Sample("SN9") }, DateTime.UtcNow);

        Assert.Equal("237900524283161", ReadString(db, "SN9", "shopee_order_id"));
        Assert.Equal("quynhsuugiacshoppi", ReadString(db, "SN9", "buyer_username"));
        Assert.Equal("1", ReadString(db, "SN9", "item_count"));
        Assert.Equal("166500", ReadString(db, "SN9", "total_price"));
        Assert.Equal("₫166.500", ReadString(db, "SN9", "total_price_text"));
        Assert.Equal("SPXVN068067521447", ReadString(db, "SN9", "tracking_number"));
        Assert.Equal("Hủy đơn hàng vì hành vi giao dịch bất thường.", ReadString(db, "SN9", "cancel_reason"));
    }

    [Fact]
    public void CountByAccount_KhongCoDon_TraVe0()
    {
        using var temp = new TempDatabase();
        var repo = new OrdersRepository(temp.Open());
        Assert.Equal(0, repo.CountByAccount(999));
    }

    /// <summary>Đọc 1 cột (dạng chuỗi) của đơn theo order_sn — kiểm chứng trực tiếp trên DB.</summary>
    private static string? ReadString(Database db, string orderSn, string column)
    {
        using var conn = db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {column} FROM orders WHERE order_sn = $sn;";
        cmd.Parameters.AddWithValue("$sn", orderSn);
        var res = cmd.ExecuteScalar();
        return res is null || res == DBNull.Value ? null : res.ToString();
    }
}
