using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase1;

public class SyncCouponRepository
{
  private readonly JsonFileStore _store;

  public SyncCouponRepository(JsonFileStore store)
  {
    _store =  store;
  }

  public Coupon? GetByCode(string code)
  {
    Log.Info($"{nameof(CouponRepository)} -> Opening connection...");
    Thread.Sleep(200);
    Log.Info($"{nameof(CouponRepository)} -> Executing query for Coupon {code}...");
    Thread.Sleep(Random.Shared.Next(300, 601));
    var coupons = _store.ReadList<Coupon>(DataFiles.Coupons);
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