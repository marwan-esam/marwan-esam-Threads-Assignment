using OrderProcessing.Phase1;
using OrderProcessing.Shared.Exceptions;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;

namespace OrderProcessing.Phase2;

public class PricingService
{
  private readonly ProductRepository _productRepo;
  private readonly CustomerRepository _customerRepo;
  private readonly CouponRepository _couponRepo;
  private readonly HashSet<int>? _failShippingForOrderIds;

  public PricingService(ProductRepository productRepo, CustomerRepository customerRepo, CouponRepository couponRepo, IEnumerable<int>? failShippingForOrderIds = null)
  {
    _productRepo = productRepo;
    _customerRepo = customerRepo;
    _couponRepo = couponRepo;
    _failShippingForOrderIds = failShippingForOrderIds?.ToHashSet();
  }

  public async Task<decimal> CalculateSubtotalAsync(Order order, CancellationToken ct = default)
  {
    var productIds = order.Items.Select(item => item.ProductId).ToList();
    var products = await _productRepo.GetByIdsAsync(productIds, ct);
    decimal subtotal = 0;
    for(int i = 0 ; i < order.Items.Count ; i++)
    {
      order.Items[i].UnitPrice = products[i]?.Price ?? default;
      subtotal += order.Items[i].UnitPrice * order.Items[i].Quantity;
    }
    return RoundMoneyValue(subtotal);
  }

  public async Task<decimal> GetDiscountAsync(Order order, CancellationToken ct = default)
  {
    Log.Info($"{nameof(GetDiscountAsync)} -> Performing DNS & TLS handshake...");
    await Task.Delay(150, ct);
    Log.Info($"{nameof(GetDiscountAsync)} -> Sending request...");
    await Task.Delay(300, ct);
    Log.Info($"{nameof(GetDiscountAsync)} -> Waiting for response...");
    var delay = Task.Delay(Random.Shared.Next(500, 1501), ct);
    var customerTask = _customerRepo.GetByIdAsync(order.CustomerId, ct);
    var couponTask = order.CouponCode is not null ? _couponRepo.GetByCodeAsync(order.CouponCode, ct) : null;
    
    await delay;
    var customer = await customerTask;
    var coupon = couponTask is not null ? await couponTask : null;

    decimal discountPercentage = 0m;
    decimal loyaltyDiscount = customer?.LoyaltyTier switch
    {
      LoyaltyTier.Bronze => 0m,
      LoyaltyTier.Silver => 5m,
      LoyaltyTier.Gold => 10m,
      _ => 0m
    };

    discountPercentage += loyaltyDiscount;
    if(coupon is not null) discountPercentage += coupon.DiscountPercent;
    discountPercentage = Math.Min(discountPercentage, 30);
    
    return RoundMoneyValue(order.SubTotal * discountPercentage / 100);
  }

  public async Task<decimal> CalculateShippingCostAsync(Order order, CancellationToken ct = default)
  {
    Log.Info($"{nameof(CalculateShippingCostAsync)} -> Performing DNS & TLS handshake...");
    await Task.Delay(150, ct);
    if (_failShippingForOrderIds?.Contains(order.Id) == true)
    {
      throw new ShippingProviderUnavailableException($"Order #{order.Id}: Shipping provider unavailable");
    } 
    Log.Info($"{nameof(CalculateShippingCostAsync)} -> Sending request...");
    await Task.Delay(300, ct);
    Log.Info($"{nameof(CalculateShippingCostAsync)} -> Waiting for response...");
    var delay = Task.Delay(Random.Shared.Next(500, 1501), ct);
    var productIds = order.Items.Select(item => item.ProductId).ToList();
    var productsTask = _productRepo.GetByIdsAsync(productIds, ct);
    
    await delay;
    var products = await productsTask;

    var totalWeight = 0m;
    var totalQuantity = 0;
    
    for(int i = 0 ; i < order.Items.Count ; i++)
    {
      totalWeight += (products[i]?.WeightKg ?? 0m) * order.Items[i].Quantity;
      totalQuantity += order.Items[i].Quantity;
    }

    var shippingCost = order.SubTotal >= 1000m ? 0m : 5m + 1.5m * totalQuantity + 2m * totalWeight;
    return RoundMoneyValue(shippingCost);
  }

  public static decimal RoundMoneyValue(decimal value)
  {
    return Math.Round(value, 2, MidpointRounding.AwayFromZero);
  }
}