# Plan: Vòng lặp xử lý MỌI đơn (lặp ProcessFirstOrder tới khi hết)

- **Ngày:** 2026-07-16
- **Trạng thái:** đang làm
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-executor`)
- **Nghiệm thu:** (Fable điền sau)

## 1. Bối cảnh & yêu cầu

Smoke live: **một đơn đã xử lý trọn vẹn** (Chuẩn bị hàng → tự mang ra bưu cục → Xác nhận → In phiếu giao →
tab phiếu tải PDF + In phiếu → đóng tab → đóng modal). Sau đó app quay về "Tất cả" nhưng **không xử lý
đơn tiếp** vì Plan trước (`2026-07-15-xu-ly-mot-don-...`) cố ý chỉ chạy **1 đơn**. Yêu cầu người dùng:
xử lý **lần lượt mọi đơn** cho tới khi hết.

### Hiện trạng (đã đối chiếu DOM người dùng cung cấp 16/7)
- `AccountSession.ProcessOrdersAsync` (App): sau `SetPickupAddressAsync == Ok`, gọi
  `s.ProcessFirstOrderAsync(@"D:\Phieu-giao-hang", log, tok)` **MỘT lần** rồi báo StatusText theo enum.
- `ProcessFirstOrderAsync` (Core) **tự chứa trọn 1 đơn**: điều hướng "Tất cả" → `FindFirstProcessableOrderAsync`
  (quét mọi card, lấy đơn ĐẦU có nút text "Chuẩn bị hàng") → arrange → In phiếu → đóng modal → trả
  `ArrangeShipmentResult` (`Ok`/`NoOrder`/các lỗi bước).
- **Đơn đã xử lý MẤT nút "Chuẩn bị hàng"** (DOM: đơn vừa arrange chuyển "Chờ lấy hàng", nút thành "Thông
  tin vận chuyển"). ⇒ Lặp "quét đơn đầu có Chuẩn bị hàng → xử lý → lặp lại" sẽ **tự dừng** khi mọi đơn đã
  xử lý (không còn nút "Chuẩn bị hàng" → `NoOrder`). Không cần theo dõi mã đơn ở v1.
- Danh sách phân trang (DOM có `.eds-pagination`), nhưng mỗi lần `ProcessFirstOrderAsync` điều hướng lại
  "Tất cả" (trang 1) → luôn xử lý đơn cần-xử-lý ở trang 1; khi trang 1 hết đơn "Chuẩn bị hàng" → `NoOrder`.
  (Đơn đã arrange vẫn hiện ở trang 1 nhưng KHÔNG có "Chuẩn bị hàng" nên bị bỏ qua — vòng lặp vẫn dừng đúng.)

## 2. Phạm vi
- **Làm:** biến lời gọi `ProcessFirstOrderAsync` một-lần trong `ProcessOrdersAsync` thành **VÒNG LẶP** tới
  khi `NoOrder`; chốt chặn an toàn (cap) + dừng-khi-lỗi + log tiến độ + dừng ngẫu nhiên kiểu người giữa
  các đơn; báo StatusText tổng kết.
- **Không làm:** CHƯA đổi địa chỉ sang địa chỉ không-mặc-định khi hết đơn (Plan sau). KHÔNG đụng
  `ProcessFirstOrderAsync`/Core (đã chạy đúng 1 đơn). KHÔNG tự commit.

## 3. Các bước thực hiện

### Bước 1 — Vòng lặp trong `AccountSession.ProcessOrdersAsync`
`src/XuLyDonShopee.App/Services/AccountSession.cs`: thay khối gọi `ProcessFirstOrderAsync` một-lần (sau
`pick == Ok`) bằng vòng lặp:

```csharp
var log = (Action<string>)(m => _services.Log.Append(_logLabel, m));
const int MaxOrders = 200;              // chốt chặn an toàn (tránh lặp vô hạn nếu 1 đơn kẹt ở "Chuẩn bị hàng")
var loopRng = new Random();
int done = 0;
ArrangeShipmentResult last = ArrangeShipmentResult.NoOrder;
while (done < MaxOrders)
{
    StatusText = $"Đang xử lý đơn thứ {done + 1}...";
    last = await s.ProcessFirstOrderAsync(@"D:\Phieu-giao-hang", log, tok).ConfigureAwait(false);
    if (last == ArrangeShipmentResult.NoOrder)
    {
        break;                          // hết đơn cần "Chuẩn bị hàng"
    }
    if (last != ArrangeShipmentResult.Ok)
    {
        break;                          // lỗi ở 1 đơn (PrintFailed/ConfirmFailed/...) → dừng, báo bước lỗi
    }
    done++;
    // Dừng ngẫu nhiên kiểu người giữa các đơn.
    try { await Task.Delay(loopRng.Next(1500, 3500), tok).ConfigureAwait(false); }
    catch (OperationCanceledException) { throw; }
}

StatusText = last switch
{
    ArrangeShipmentResult.NoOrder =>
        done > 0 ? $"Đã xử lý xong {done} đơn. Không còn đơn nào cần xử lý."
                 : "Không có đơn nào cần xử lý.",
    ArrangeShipmentResult.Ok => $"Đã xử lý {done} đơn (đạt chốt chặn {MaxOrders}).", // hiếm khi tới cap
    ArrangeShipmentResult.OrdersPageNotOpened => $"Đã xử lý {done} đơn; không mở được danh sách đơn.",
    ArrangeShipmentResult.PrepareNotFound     => $"Đã xử lý {done} đơn; không bấm được Chuẩn bị hàng ở đơn kế.",
    ArrangeShipmentResult.ShipModalNotOpened  => $"Đã xử lý {done} đơn; không mở được ô Giao Đơn Hàng ở đơn kế.",
    ArrangeShipmentResult.ConfirmFailed       => $"Đã xử lý {done} đơn; không Xác nhận được ở đơn kế.",
    ArrangeShipmentResult.DetailModalNotOpened=> $"Đã xử lý {done} đơn; không mở được Thông Tin Chi Tiết ở đơn kế.",
    ArrangeShipmentResult.PrintFailed         => $"Đã xử lý {done} đơn; không In phiếu giao được ở đơn kế.",
    _ => $"Đã xử lý {done} đơn; gặp lỗi ở đơn kế — kiểm tra tay trong Brave.",
};
return last is ArrangeShipmentResult.NoOrder or ArrangeShipmentResult.Ok || done > 0;
```

Lưu ý:
- `ProcessFirstOrderAsync` re-throw `OperationCanceledException` khi hủy → vòng lặp bị OCE → thoát lên
  `catch (OperationCanceledException)` sẵn có của `ProcessOrdersAsync` (dừng sạch). GIỮ nguyên `finally`
  tắt `_navigating`.
- `_navigating = true` được bật SUỐT `ProcessOrdersAsync` (bao trùm cả vòng lặp) như hiện tại — vòng đọc
  đơn 30' và watchdog proxy KHÔNG chen ngang giữa các đơn. (Kiểm: watchdog gate `!_navigating` nên tạm
  hoãn tới khi xử lý đơn xong — chấp nhận, xử lý đơn ưu tiên.)
- Dừng-khi-lỗi (không skip): 1 đơn lỗi giữa chừng → dừng vòng, báo đã xử lý `done` đơn + bước lỗi. (Skip-
  và-tiếp có thể lặp vô hạn nếu đơn kẹt ở "Chuẩn bị hàng" — để v2 nếu cần, kèm theo dõi mã đơn đã làm.)

### Bước 2 — Build + test
- `dotnet build` 0/0; `dotnet test` toàn bộ pass (nền hiện tại **390**, không đỏ). WDAC → `-p:Deterministic=false`.
- Không thêm test (logic vòng lặp App-layer, phụ thuộc phiên thật — không unit-test được; enum switch thuần).

## 4. Tiêu chí nghiệm thu
- [ ] Sau khi đặt địa chỉ Ok: app xử lý **lần lượt** các đơn có "Chuẩn bị hàng" (mỗi đơn: điều hướng Tất
      cả → arrange → In phiếu → tải/in → đóng modal), tới khi `NoOrder` thì dừng.
- [ ] Log tiến độ "Đang xử lý đơn thứ N..."; StatusText tổng kết "Đã xử lý xong X đơn...".
- [ ] Có chốt chặn cap (không lặp vô hạn); 1 đơn lỗi → dừng vòng + báo bước lỗi (KHÔNG treo).
- [ ] Dừng nhanh khi bấm Dừng (OCE lan qua vòng lặp); `_navigating` được tắt ở finally.
- [ ] Build 0/0; test 390 nền pass.
- [ ] Chỉ sửa: `AccountSession.cs` (+ file plan này).

## 5. Rủi ro & lưu ý
- **Lặp vô hạn** nếu 1 đơn arrange-FAIL (giữ nút "Chuẩn bị hàng") mà vòng skip: đã chọn **dừng-khi-lỗi**
  (không skip) + cap → an toàn. Trường hợp arrange OK nhưng list chưa refresh (đơn vẫn hiện "Chuẩn bị
  hàng") hiếm — `ProcessFirstOrderAsync` điều hướng lại "Tất cả" mỗi vòng (tải lại) nên list tươi; nếu
  smoke thấy xử lý trùng 1 đơn thì thêm theo-dõi-mã-đơn (v2).
- Xử lý nhiều đơn liên tiếp = nhiều lần mở/đóng tab phiếu + in — mỗi đơn có dừng ngẫu nhiên kiểu người.
- Watchdog proxy tạm hoãn suốt lúc xử lý đơn (gate `_navigating`) — nếu batch dài, proxy check lùi lại;
  chấp nhận (ưu tiên xử lý đơn liền mạch).
- WDAC/ISG khi test như plan trước.

---

## Báo cáo thực thi (Opus điền sau khi xong)

(để trống)
