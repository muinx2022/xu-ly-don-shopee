using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using XuLyDonShopee.App.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// Test logic Start/Stop/StopAll/IsRunning của <see cref="AccountSessionManager"/> bằng stub
/// <see cref="IAccountSession"/> (không cần Brave/Playwright thật). Đây là phần logic thuần của engine
/// đa phiên; luồng browser thật được kiểm ở smoke test (như các phần browser khác của dự án).
/// </summary>
public class AccountSessionManagerTests
{
    /// <summary>Stub phiên: chỉ đổi State khi Start/Stop, đếm số lần gọi, phát Changed như phiên thật.</summary>
    private sealed class StubSession : IAccountSession
    {
        public long AccountId { get; }
        public SessionState State { get; private set; } = SessionState.Stopped;
        public string? StatusText => null;
        public int? ToShipCount => null;
        public string? LastError => null;
        public Process? BraveProcess => null;

        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }

        public event Action? Changed;
        public event Action<long>? CookieSaved;

        public StubSession(long id) => AccountId = id;

        public Task StartAsync()
        {
            StartCalls++;
            State = SessionState.Running;
            Changed?.Invoke();
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StopCalls++;
            State = SessionState.Stopped;
            Changed?.Invoke();
            return Task.CompletedTask;
        }

        /// <summary>Mô phỏng phiên phát lại sự kiện Changed (vd event Stopped TRỄ) mà không đổi State.</summary>
        public void RaiseChanged() => Changed?.Invoke();

        // Không dùng trong test nhưng cần để tránh cảnh báo "event không được dùng".
        internal void RaiseCookieSaved() => CookieSaved?.Invoke(AccountId);
    }

    [Fact]
    public void Start_HaiLanCungId_ChiMotSession_KhongMoTrung()
    {
        var factoryCalls = 0;
        var mgr = new AccountSessionManager(id => { factoryCalls++; return new StubSession(id); });

        var s1 = mgr.Start(5);
        var s2 = mgr.Start(5);

        Assert.Same(s1, s2);          // cùng một phiên, không tạo phiên thứ hai
        Assert.Equal(1, factoryCalls); // factory chỉ được gọi 1 lần cho id 5
        Assert.Single(mgr.Active);
    }

    [Fact]
    public void IsRunning_DungTheoTungTaiKhoan()
    {
        var mgr = new AccountSessionManager(id => new StubSession(id));

        Assert.False(mgr.IsRunning(1)); // chưa mở

        mgr.Start(1);

        Assert.True(mgr.IsRunning(1));
        Assert.False(mgr.IsRunning(2)); // mở tài khoản 1 KHÔNG khiến tài khoản 2 "đang chạy"
    }

    [Fact]
    public void Stop_GoKhoiActive_VaIsRunningFalse()
    {
        var mgr = new AccountSessionManager(id => new StubSession(id));
        mgr.Start(7);
        Assert.True(mgr.IsRunning(7));

        mgr.Stop(7);

        Assert.False(mgr.IsRunning(7));
        Assert.Empty(mgr.Active);
        Assert.Null(mgr.Get(7));
    }

    [Fact]
    public async Task StopAll_DungHetVaActiveRong()
    {
        var mgr = new AccountSessionManager(id => new StubSession(id));
        mgr.Start(1);
        mgr.Start(2);
        mgr.Start(3);
        Assert.Equal(3, mgr.Active.Count);

        await mgr.StopAllAsync();

        Assert.Empty(mgr.Active);
        Assert.False(mgr.IsRunning(1));
        Assert.False(mgr.IsRunning(2));
        Assert.False(mgr.IsRunning(3));
    }

    [Fact]
    public void Get_TraVePhienDangChay_HoacNull()
    {
        var mgr = new AccountSessionManager(id => new StubSession(id));

        Assert.Null(mgr.Get(9));

        var s = mgr.Start(9);
        Assert.Same(s, mgr.Get(9));
    }

    // ===== Lỗi 1 (concurrency): event Stopped TRỄ của phiên cũ KHÔNG được xóa nhầm phiên mới cùng id =====
    // Kịch bản: id 5 mở phiên A → Dừng (A bị gỡ) → Start lại 5 tạo phiên B đang chạy → event Stopped TRỄ
    // của A chạy sau. Gỡ theo KEY sẽ xóa nhầm B (B mồ côi); gỡ theo VALUE thì thấy dict[5]=B≠A → giữ B.
    [Fact]
    public void StoppedTre_CuaPhienCu_KhongXoaNhamPhienMoiCungId()
    {
        var mgr = new AccountSessionManager(id => new StubSession(id));

        var a = (StubSession)mgr.Start(5);   // phiên A
        mgr.Stop(5);                         // A.StopAsync → State=Stopped → OnSessionChanged gỡ A
        Assert.False(mgr.IsRunning(5));
        Assert.Null(mgr.Get(5));

        var b = (StubSession)mgr.Start(5);   // phiên MỚI B (khác instance), đang chạy
        Assert.NotSame(a, b);
        Assert.Same(b, mgr.Get(5));
        Assert.True(mgr.IsRunning(5));

        // Event Stopped TRỄ của A (A vẫn còn subscribe, State đang Stopped) chạy sau khi B đã vào dict.
        a.RaiseChanged();

        // B KHÔNG bị xóa nhầm.
        Assert.Same(b, mgr.Get(5));
        Assert.True(mgr.IsRunning(5));
    }
}
