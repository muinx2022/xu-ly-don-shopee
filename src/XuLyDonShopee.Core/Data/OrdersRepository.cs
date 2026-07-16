using System.Text;
using Microsoft.Data.Sqlite;
using XuLyDonShopee.Core.Models;

namespace XuLyDonShopee.Core.Data;

/// <summary>
/// Lưu/đọc đơn hàng đã sync trong bảng <c>orders</c>. Khóa nghiệp vụ là cặp
/// <c>(account_id, order_sn)</c> (UNIQUE) → mỗi đơn của một tài khoản chỉ một dòng; sync lại thì
/// CẬP NHẬT chứ không thêm trùng.
/// </summary>
public class OrdersRepository
{
    private readonly Database _db;

    public OrdersRepository(Database db) => _db = db;

    /// <summary>
    /// Upsert (thêm mới hoặc cập nhật) nhiều đơn của MỘT tài khoản trong một transaction. Đơn đã có
    /// (khớp <c>(account_id, order_sn)</c>) → cập nhật mọi cột dữ liệu + <c>updated_at</c>/<c>synced_at</c>,
    /// GIỮ <c>created_at</c>; đơn mới → thêm với <c>created_at = updated_at = synced_at</c>. Đơn không có
    /// mã (<see cref="SyncedOrder.OrderSn"/> rỗng) bị BỎ QUA (không thể làm khóa). Trả về số đơn thêm mới
    /// và số đơn cập nhật.
    /// </summary>
    public (int Inserted, int Updated) UpsertMany(long accountId, IEnumerable<SyncedOrder> orders, DateTime syncedAt)
    {
        var syncedAtStr = DbSerialization.FormatDate(syncedAt);
        var inserted = 0;
        var updated = 0;

        using var conn = _db.OpenConnection();
        using var tx = conn.BeginTransaction();

        foreach (var o in orders)
        {
            if (string.IsNullOrWhiteSpace(o.OrderSn))
            {
                continue; // không có mã đơn → không thể làm khóa upsert
            }

            // Có sẵn chưa? (khóa nghiệp vụ account_id + order_sn)
            long? existingId = null;
            using (var sel = conn.CreateCommand())
            {
                sel.Transaction = tx;
                sel.CommandText = "SELECT id FROM orders WHERE account_id = $account AND order_sn = $sn;";
                sel.Parameters.AddWithValue("$account", accountId);
                sel.Parameters.AddWithValue("$sn", o.OrderSn);
                var res = sel.ExecuteScalar();
                if (res is not null && res != DBNull.Value)
                {
                    existingId = (long)res;
                }
            }

            if (existingId is null)
            {
                using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = @"INSERT INTO orders
    (account_id, order_sn, shopee_order_id, buyer_username, items_json, item_count, item_summary,
     total_price, total_price_text, payment_method, status, status_description, cancel_reason,
     channel, carrier, tracking_number, synced_at, created_at, updated_at)
    VALUES
    ($account, $sn, $shopeeId, $buyer, $items, $itemCount, $itemSummary,
     $totalPrice, $totalText, $payment, $status, $statusDesc, $cancelReason,
     $channel, $carrier, $tracking, $synced, $synced, $synced);";
                ins.Parameters.AddWithValue("$account", accountId);
                ins.Parameters.AddWithValue("$sn", o.OrderSn);
                BindData(ins, o);
                ins.Parameters.AddWithValue("$synced", syncedAtStr);
                ins.ExecuteNonQuery();
                inserted++;
            }
            else
            {
                using var upd = conn.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = @"UPDATE orders SET
    shopee_order_id = $shopeeId, buyer_username = $buyer, items_json = $items, item_count = $itemCount,
    item_summary = $itemSummary, total_price = $totalPrice, total_price_text = $totalText,
    payment_method = $payment, status = $status, status_description = $statusDesc, cancel_reason = $cancelReason,
    channel = $channel, carrier = $carrier, tracking_number = $tracking,
    synced_at = $synced, updated_at = $synced
    WHERE id = $id;";
                BindData(upd, o);
                upd.Parameters.AddWithValue("$synced", syncedAtStr);
                upd.Parameters.AddWithValue("$id", existingId.Value);
                upd.ExecuteNonQuery();
                updated++;
            }
        }

        tx.Commit();
        return (inserted, updated);
    }

    /// <summary>Số đơn đã lưu của một tài khoản (dùng cho màn xem — plan 2).</summary>
    public int CountByAccount(long accountId)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM orders WHERE account_id = $account;";
        cmd.Parameters.AddWithValue("$account", accountId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// Đọc các đơn theo bộ lọc (màn "Đơn hàng"). Mọi tham số đều tùy chọn — bỏ trống là không lọc:
    /// <list type="bullet">
    /// <item><paramref name="accountId"/>: chỉ đơn của một tài khoản.</item>
    /// <item><paramref name="status"/>: KHỚP CHÍNH XÁC giá trị trạng thái (ComboBox nạp từ
    /// <see cref="AllStatuses"/> nên luôn là giá trị có thật; dùng "=" thay vì LIKE để "Đã hủy" không
    /// dính "Đã hủy một phần").</item>
    /// <item><paramref name="searchText"/>: LIKE <c>%từ%</c> trên mã đơn / người mua / tên sản phẩm; các
    /// ký tự đại diện của LIKE (<c>% _ \</c>) trong từ khóa được escape để tìm đúng nghĩa đen.</item>
    /// </list>
    /// Sắp xếp đơn sync mới nhất lên đầu.
    /// </summary>
    public List<OrderRow> Query(long? accountId = null, string? status = null, string? searchText = null)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();

        var sql = new StringBuilder(@"SELECT id, account_id, order_sn, buyer_username, item_count, item_summary,
    total_price, total_price_text, payment_method, status, status_description, cancel_reason,
    channel, carrier, tracking_number, synced_at
    FROM orders WHERE 1 = 1");

        if (accountId is not null)
        {
            sql.Append(" AND account_id = $account");
            cmd.Parameters.AddWithValue("$account", accountId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            sql.Append(" AND status = $status");
            cmd.Parameters.AddWithValue("$status", status);
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            sql.Append(@" AND (order_sn LIKE $q ESCAPE '\'
                           OR buyer_username LIKE $q ESCAPE '\'
                           OR item_summary LIKE $q ESCAPE '\')");
            cmd.Parameters.AddWithValue("$q", "%" + EscapeLike(searchText.Trim()) + "%");
        }

        sql.Append(" ORDER BY synced_at DESC, id DESC;");
        cmd.CommandText = sql.ToString();

        var list = new List<OrderRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(MapRow(reader));
        }
        return list;
    }

    /// <summary>
    /// Danh sách trạng thái PHÂN BIỆT (khác null/rỗng) đang có trong bảng — nạp ComboBox lọc. Có thể giới
    /// hạn theo <paramref name="accountId"/>. Sắp xếp tăng dần.
    /// </summary>
    public List<string> AllStatuses(long? accountId = null)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();

        var sql = new StringBuilder(
            "SELECT DISTINCT status FROM orders WHERE status IS NOT NULL AND TRIM(status) <> ''");
        if (accountId is not null)
        {
            sql.Append(" AND account_id = $account");
            cmd.Parameters.AddWithValue("$account", accountId.Value);
        }
        sql.Append(" ORDER BY status;");
        cmd.CommandText = sql.ToString();

        var list = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
            {
                list.Add(reader.GetString(0));
            }
        }
        return list;
    }

    /// <summary>Escape các ký tự đại diện của LIKE để tìm theo nghĩa đen (đi kèm <c>ESCAPE '\'</c>).</summary>
    private static string EscapeLike(string term)
        => term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    /// <summary>Map một dòng kết quả <see cref="Query"/> sang <see cref="OrderRow"/> (theo thứ tự cột SELECT).</summary>
    private static OrderRow MapRow(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        AccountId = r.GetInt64(1),
        OrderSn = r.GetString(2),
        BuyerUsername = r.IsDBNull(3) ? null : r.GetString(3),
        ItemCount = r.IsDBNull(4) ? 0 : r.GetInt32(4),
        ItemSummary = r.IsDBNull(5) ? null : r.GetString(5),
        TotalPrice = r.IsDBNull(6) ? null : r.GetInt64(6),
        TotalPriceText = r.IsDBNull(7) ? null : r.GetString(7),
        PaymentMethod = r.IsDBNull(8) ? null : r.GetString(8),
        Status = r.IsDBNull(9) ? null : r.GetString(9),
        StatusDescription = r.IsDBNull(10) ? null : r.GetString(10),
        CancelReason = r.IsDBNull(11) ? null : r.GetString(11),
        Channel = r.IsDBNull(12) ? null : r.GetString(12),
        Carrier = r.IsDBNull(13) ? null : r.GetString(13),
        TrackingNumber = r.IsDBNull(14) ? null : r.GetString(14),
        SyncedAt = r.IsDBNull(15) ? default : DbSerialization.ParseDate(r.GetString(15)),
    };

    /// <summary>Gắn các cột DỮ LIỆU (không gồm account_id/order_sn/khóa/thời gian) vào lệnh. Null → DBNull.</summary>
    private static void BindData(SqliteCommand cmd, SyncedOrder o)
    {
        cmd.Parameters.AddWithValue("$shopeeId", (object?)o.ShopeeOrderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$buyer", (object?)o.BuyerUsername ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$items", (object?)o.ItemsJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$itemCount", o.ItemCount);
        cmd.Parameters.AddWithValue("$itemSummary", (object?)o.ItemSummary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$totalPrice", (object?)o.TotalPrice ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$totalText", (object?)o.TotalPriceText ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$payment", (object?)o.PaymentMethod ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", (object?)o.Status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$statusDesc", (object?)o.StatusDescription ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cancelReason", (object?)o.CancelReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$channel", (object?)o.Channel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$carrier", (object?)o.Carrier ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tracking", (object?)o.TrackingNumber ?? DBNull.Value);
    }
}
