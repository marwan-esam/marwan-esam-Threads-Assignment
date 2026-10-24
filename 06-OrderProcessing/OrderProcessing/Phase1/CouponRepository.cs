using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase1;

public class CouponRepository
{
  private readonly JsonFileStore _store;

  public CouponRepository(JsonFileStore store)
  {
    _store =  store;
  }

  public async Task<Coupon?> GetByCodeAsync(string code, CancellationToken ct = default)
  {
    Log.Info($"{nameof(CouponRepository)} -> Opening connection...");
    await Task.Delay(200, ct);
    Log.Info($"{nameof(CouponRepository)} -> Executing query for Coupon {code}...");
    await Task.Delay(Random.Shared.Next(300, 601), ct);
    var coupons = await _store.ReadListAsync<Coupon>(DataFiles.Coupons, ct);
    foreach(var coupon in coupons)
    {
      if(coupon.Code.Equals(code, StringComparison.OrdinalIgnoreCase))
      {
        Log.Info($"{nameof(CouponRepository)} -> Coupon {code} found.");
        return coupon;
      }
    }
    Log.Info($"{nameof(CouponRepository)} -> Coupon not found.");
    return null;
  }
}