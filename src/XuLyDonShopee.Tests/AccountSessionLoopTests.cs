using XuLyDonShopee.App.Services;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// Test hàm thuần <see cref="AccountSession.NextLoopDecision"/> — quyết định DỪNG/TIẾP + đếm chuỗi lỗi LIÊN
/// TIẾP của vòng xử lý đơn (<c>ProcessOrdersAsync</c>): Ok reset, lỗi tăng chuỗi, 3 lỗi liên tiếp dừng,
/// NoOrder dừng. (Luồng browser thật kiểm ở smoke như các phần browser khác của dự án.)
/// </summary>
public class AccountSessionLoopTests
{
    [Fact]
    public void Ok_KhongDung_ResetChuoiLoi()
    {
        var (stop, consecutive) = AccountSession.NextLoopDecision(ArrangeShipmentResult.Ok, consecutiveFails: 2);
        Assert.False(stop);
        Assert.Equal(0, consecutive);              // 1 đơn Ok → reset chuỗi lỗi rải rác
    }

    [Fact]
    public void NoOrder_Dung_GiuNguyenChuoiLoi()
    {
        var (stop, consecutive) = AccountSession.NextLoopDecision(ArrangeShipmentResult.NoOrder, consecutiveFails: 1);
        Assert.True(stop);                         // hết đơn → dừng vòng
        Assert.Equal(1, consecutive);              // không đụng bộ đếm
    }

    [Fact]
    public void Loi_LanDau_KhongDung_TangChuoiLoi()
    {
        var (stop, consecutive) = AccountSession.NextLoopDecision(ArrangeShipmentResult.PrepareNotFound, consecutiveFails: 0);
        Assert.False(stop);                        // lỗi 1 lần → bỏ qua, chạy tiếp
        Assert.Equal(1, consecutive);
    }

    [Fact]
    public void Loi_LanHai_KhongDung_TangChuoiLoi()
    {
        var (stop, consecutive) = AccountSession.NextLoopDecision(ArrangeShipmentResult.PrintFailed, consecutiveFails: 1);
        Assert.False(stop);
        Assert.Equal(2, consecutive);
    }

    [Fact]
    public void Loi_LanBaLienTiep_Dung()
    {
        var (stop, consecutive) = AccountSession.NextLoopDecision(ArrangeShipmentResult.ConfirmFailed, consecutiveFails: 2);
        Assert.True(stop);                         // 3 lỗi liên tiếp → dừng để người xem
        Assert.Equal(3, consecutive);
    }

    [Theory]
    [InlineData(ArrangeShipmentResult.Failed)]
    [InlineData(ArrangeShipmentResult.OrdersPageNotOpened)]
    [InlineData(ArrangeShipmentResult.ShipModalNotOpened)]
    [InlineData(ArrangeShipmentResult.DetailModalNotOpened)]
    public void MoiLoi_DeuTangChuoiLoiNhuNhau(ArrangeShipmentResult err)
    {
        var (stop, consecutive) = AccountSession.NextLoopDecision(err, consecutiveFails: 0);
        Assert.False(stop);
        Assert.Equal(1, consecutive);
    }

    [Fact]
    public void MaxTuyChinh_DungSomHon()
    {
        // maxConsecutiveFails = 2 → lỗi thứ 2 liên tiếp đã dừng (kiểm tham số cấu hình được).
        var (stop, consecutive) = AccountSession.NextLoopDecision(
            ArrangeShipmentResult.PrintFailed, consecutiveFails: 1, maxConsecutiveFails: 2);
        Assert.True(stop);
        Assert.Equal(2, consecutive);
    }
}
