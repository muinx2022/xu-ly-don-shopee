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

    // ===================== Query / AllStatuses (màn xem — plan 2) =====================

    /// <summary>Tạo nhanh một đơn với vài trường phục vụ test lọc.</summary>
    private static SyncedOrder Make(string sn, string? status = null, string? buyer = null,
        string? summary = null, int itemCount = 1) => new()
        {
            OrderSn = sn,
            Status = status,
            BuyerUsername = buyer,
            ItemSummary = summary,
            ItemCount = itemCount,
        };

    [Fact]
    public void Query_KhongLoc_TraTatCa_SyncMoiNhatTruoc()
    {
        using var temp = new TempDatabase();
        var repo = new OrdersRepository(temp.Open());
        var t1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc);
        repo.UpsertMany(1, new[] { Make("OLD") }, t1);
        repo.UpsertMany(1, new[] { Make("NEW") }, t2);

        var rows = repo.Query();

        Assert.Equal(2, rows.Count);
        Assert.Equal("NEW", rows[0].OrderSn); // synced_at mới hơn đứng trước
        Assert.Equal("OLD", rows[1].OrderSn);
    }

    [Fact]
    public void Query_MapDayDuTruong()
    {
        using var temp = new TempDatabase();
        var repo = new OrdersRepository(temp.Open());
        var syncedAt = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        repo.UpsertMany(42, new[] { Sample("SN1") }, syncedAt);

        var row = Assert.Single(repo.Query());
        Assert.Equal(42, row.AccountId);
        Assert.Equal("SN1", row.OrderSn);
        Assert.Equal("quynhsuugiacshoppi", row.BuyerUsername);
        Assert.Equal("Giày", row.ItemSummary);
        Assert.Equal(166500, row.TotalPrice);
        Assert.Equal("₫166.500", row.TotalPriceText);
        Assert.Equal("Đã hủy", row.Status);
        Assert.Equal("SPX Express", row.Carrier);
        Assert.Equal("SPXVN068067521447", row.TrackingNumber);
        Assert.Equal(syncedAt, row.SyncedAt);
    }

    [Fact]
    public void Query_LocTheoTaiKhoan()
    {
        using var temp = new TempDatabase();
        var repo = new OrdersRepository(temp.Open());
        repo.UpsertMany(1, new[] { Make("A1"), Make("A2") }, DateTime.UtcNow);
        repo.UpsertMany(2, new[] { Make("B1") }, DateTime.UtcNow);

        Assert.Equal(2, repo.Query(accountId: 1).Count);
        var only2 = Assert.Single(repo.Query(accountId: 2));
        Assert.Equal("B1", only2.OrderSn);
    }

    [Fact]
    public void Query_LocTrangThai_ChinhXac_KhongDinhChuoiCon()
    {
        using var temp = new TempDatabase();
        var repo = new OrdersRepository(temp.Open());
        repo.UpsertMany(1, new[]
        {
            Make("S1", status: "Đã hủy"),
            Make("S2", status: "Đã hủy một phần"),
            Make("S3", status: "Chờ lấy hàng"),
        }, DateTime.UtcNow);

        var huy = repo.Query(status: "Đã hủy");
        var only = Assert.Single(huy); // KHÔNG dính "Đã hủy một phần"
        Assert.Equal("S1", only.OrderSn);
    }

    [Fact]
    public void Query_TimKiem_TheoMaDon_NguoiMua_SanPham()
    {
        using var temp = new TempDatabase();
        var repo = new OrdersRepository(temp.Open());
        repo.UpsertMany(1, new[]
        {
            Make("ABC123", buyer: "nguyenvana", summary: "Áo thun"),
            Make("XYZ999", buyer: "tranthib", summary: "Quần jean"),
        }, DateTime.UtcNow);

        Assert.Single(repo.Query(searchText: "ABC"));      // theo mã đơn
        Assert.Single(repo.Query(searchText: "tranthi"));  // theo người mua
        Assert.Single(repo.Query(searchText: "jean"));     // theo tên sản phẩm
        Assert.Equal(2, repo.Query(searchText: "  ").Count); // chỉ khoảng trắng = không lọc
    }

    [Fact]
    public void Query_TimKiem_KyTuDaiDien_DuocEscape()
    {
        using var temp = new TempDatabase();
        var repo = new OrdersRepository(temp.Open());
        repo.UpsertMany(1, new[]
        {
            Make("N100", summary: "Giày 50%"),
            Make("N200", summary: "Giày x"),
        }, DateTime.UtcNow);

        // "%" phải khớp ĐÚNG ký tự '%' (nếu không escape, LIKE %50%% sẽ dính cả hai dòng).
        var rows = repo.Query(searchText: "50%");
        var only = Assert.Single(rows);
        Assert.Equal("N100", only.OrderSn);
    }

    [Fact]
    public void Query_KetHop_TaiKhoan_TrangThai_TimKiem()
    {
        using var temp = new TempDatabase();
        var repo = new OrdersRepository(temp.Open());
        repo.UpsertMany(1, new[]
        {
            Make("K1", status: "Đã hủy", buyer: "alpha"),
            Make("K2", status: "Đã hủy", buyer: "beta"),
            Make("K3", status: "Chờ lấy hàng", buyer: "alpha"),
        }, DateTime.UtcNow);
        repo.UpsertMany(2, new[] { Make("K4", status: "Đã hủy", buyer: "alpha") }, DateTime.UtcNow);

        var rows = repo.Query(accountId: 1, status: "Đã hủy", searchText: "alpha");
        var only = Assert.Single(rows);
        Assert.Equal("K1", only.OrderSn);
    }

    [Fact]
    public void AllStatuses_Distinct_SapXep_BoNull()
    {
        using var temp = new TempDatabase();
        var repo = new OrdersRepository(temp.Open());
        repo.UpsertMany(1, new[]
        {
            Make("A", status: "Chờ lấy hàng"),
            Make("B", status: "Đã hủy"),
            Make("C", status: "Chờ lấy hàng"), // trùng → gộp
            Make("D", status: null),           // null → bỏ
        }, DateTime.UtcNow);

        Assert.Equal(new[] { "Chờ lấy hàng", "Đã hủy" }, repo.AllStatuses());
    }

    [Fact]
    public void AllStatuses_LocTheoTaiKhoan()
    {
        using var temp = new TempDatabase();
        var repo = new OrdersRepository(temp.Open());
        repo.UpsertMany(1, new[] { Make("A", status: "Đã hủy") }, DateTime.UtcNow);
        repo.UpsertMany(2, new[] { Make("B", status: "Chờ lấy hàng") }, DateTime.UtcNow);

        Assert.Equal(new[] { "Đã hủy" }, repo.AllStatuses(accountId: 1));
        Assert.Equal(new[] { "Chờ lấy hàng" }, repo.AllStatuses(accountId: 2));
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
