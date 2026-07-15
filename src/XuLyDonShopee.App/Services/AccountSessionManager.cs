using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.App.Services;

/// <summary>
/// Quản lý các <see cref="IAccountSession"/> đang chạy — MỖI TÀI KHOẢN MỘT PHIÊN ĐỘC LẬP, song song.
/// Thay cho cờ <c>IsBusy</c> toàn cục (khóa mọi tài khoản) trước đây: giờ mỗi tài khoản mở/dừng riêng.
/// <para>
/// Factory phiên tách được để test (ctor nhận <c>Func&lt;long, IAccountSession&gt;</c>): mặc định tạo
/// <see cref="AccountSession"/> thật; test truyền stub (không cần Brave).
/// </para>
/// </summary>
public class AccountSessionManager
{
    private readonly ConcurrentDictionary<long, IAccountSession> _sessions = new();
    private readonly Func<long, IAccountSession> _factory;
    private readonly object _gate = new();

    // Round-robin BỀN cho proxy thủ công, CHIA SẺ cho tất cả phiên (nhiều tài khoản trải đều trên danh
    // sách proxy). Giữ qua các lần mở (không reset), thread-safe.
    private int _manualProxyIndex;
    private readonly object _manualLock = new();

    /// <summary>Phát khi bất kỳ phiên nào đổi trạng thái — VM/UI nghe để cập nhật (marshal về UI thread).</summary>
    public event Action? Changed;

    /// <summary>Chuyển tiếp sự kiện "đã lưu cookie" của các phiên (kèm accountId) cho VM làm mới danh sách.</summary>
    public event Action<long>? CookieSaved;

    /// <summary>Ctor thật: tạo <see cref="AccountSession"/> dùng chung <see cref="ShopeeLoginService"/> +
    /// <see cref="ProxyHealthChecker"/> và round-robin proxy thủ công chia sẻ của manager.</summary>
    public AccountSessionManager(AppServices services)
    {
        var loginService = new ShopeeLoginService();
        IProxyHealthChecker healthChecker = new ProxyHealthChecker();
        _factory = id => new AccountSession(id, services, loginService, healthChecker, NextManualProxy);
    }

    /// <summary>Ctor test: cho phép thay factory phiên bằng stub.</summary>
    public AccountSessionManager(Func<long, IAccountSession> sessionFactory)
    {
        _factory = sessionFactory;
    }

    /// <summary>
    /// Bắt đầu (hoặc lấy) phiên cho tài khoản <paramref name="id"/>. Idempotent: gọi nhiều lần cùng id
    /// KHÔNG mở trùng — trả về đúng phiên đang có. KHÔNG khóa các tài khoản khác.
    /// </summary>
    public IAccountSession Start(long id)
    {
        IAccountSession session;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(id, out var existing))
            {
                existing = _factory(id);
                // Đăng ký sự kiện MỘT LẦN khi tạo phiên (lock đảm bảo không đăng ký trùng).
                existing.Changed += () => OnSessionChanged(existing);
                existing.CookieSaved += accId => CookieSaved?.Invoke(accId);
                _sessions[id] = existing;
            }

            session = existing;
        }

        // StartAsync tự idempotent (đang chạy → no-op); Error/Stopped → chạy lại.
        _ = session.StartAsync();
        return session;
    }

    /// <summary>
    /// Dừng phiên của một tài khoản (nếu có). KHÔNG gỡ khỏi dictionary ngay: giữ <see cref="IsRunning"/>
    /// = true tới khi Brave chết THẬT (RunAsync finally đặt State→Stopped SAU khi dispose kill Brave). Nhờ
    /// đó nút "Mở" còn khóa trong lúc dừng → không mở lại vào CÙNG hồ sơ đang bị khóa (tránh Error khóa hồ
    /// sơ). Việc gỡ khỏi dictionary do <see cref="OnSessionChanged"/> làm (gỡ theo VALUE — xem Lỗi 1).
    /// </summary>
    public void Stop(long id)
    {
        if (_sessions.TryGetValue(id, out var session))
        {
            _ = session.StopAsync(); // fire-and-forget: UI không phải chờ kill Brave (State→Stopped sẽ tự gỡ)
        }
    }

    /// <summary>Dừng TẤT CẢ phiên (dùng khi thoát app) — chờ kill hết Brave để không mồ côi.</summary>
    public async Task StopAllAsync()
    {
        List<IAccountSession> all;
        lock (_gate)
        {
            all = _sessions.Values.ToList();
            _sessions.Clear();
        }

        await Task.WhenAll(all.Select(SafeStopAsync)).ConfigureAwait(false);
        Changed?.Invoke();
    }

    private static async Task SafeStopAsync(IAccountSession session)
    {
        try { await session.StopAsync().ConfigureAwait(false); }
        catch { /* bỏ qua khi thoát */ }
    }

    /// <summary>True nếu tài khoản có phiên ĐANG chuẩn bị/đang chạy (dùng để khóa nút theo TỪNG tài khoản).</summary>
    public bool IsRunning(long id)
        => _sessions.TryGetValue(id, out var s) && IsActiveState(s.State);

    /// <summary>Lấy phiên của một tài khoản (hoặc null nếu không có) — VM đọc trạng thái để hiển thị.</summary>
    public IAccountSession? Get(long id)
        => _sessions.TryGetValue(id, out var s) ? s : null;

    /// <summary>Các phiên đang chạy (đang chuẩn bị/đang chạy).</summary>
    public IReadOnlyCollection<IAccountSession> Active
        => _sessions.Values.Where(s => IsActiveState(s.State)).ToList();

    private static bool IsActiveState(SessionState state)
        => state is SessionState.Opening or SessionState.Running;

    private void OnSessionChanged(IAccountSession session)
    {
        // Phiên đã kết thúc bình thường (đóng cửa sổ / dừng) → dọn khỏi danh sách. Phiên lỗi (Error) giữ
        // lại để còn hiển thị lỗi; người dùng bấm "Mở" lại sẽ chạy lại phiên đó.
        //
        // GỠ THEO (KEY, VALUE) — KHÔNG theo key đơn thuần: chỉ xóa khi ĐÚNG instance vừa phát event. Nếu
        // phiên cũ (A) phát Stopped TRỄ trong khi id đã được Start lại thành phiên mới (B) đang chạy, gỡ
        // theo key sẽ xóa NHẦM B (B mồ côi). Gỡ theo value: dict[id] == A mới xóa; == B thì bỏ qua (Lỗi 1).
        if (session.State == SessionState.Stopped)
        {
            ((ICollection<KeyValuePair<long, IAccountSession>>)_sessions)
                .Remove(new KeyValuePair<long, IAccountSession>(session.AccountId, session));
        }

        Changed?.Invoke();
    }

    /// <summary>Chọn proxy thủ công kế tiếp theo round-robin BỀN, chia sẻ giữa các phiên (thread-safe).</summary>
    public ProxyEntry? NextManualProxy(IReadOnlyList<ProxyEntry> manual)
    {
        if (manual.Count == 0)
        {
            return null;
        }

        lock (_manualLock)
        {
            var p = manual[_manualProxyIndex % manual.Count];
            _manualProxyIndex++;
            return p;
        }
    }
}
