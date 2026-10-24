using OrderProcessing.Phase1;
using OrderProcessing.Shared.Exceptions;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;

namespace OrderProcessing.Phase3;

public class PricingService
{
  private readonly ProductRepository _productRepo;
  private readonly CustomerRepository _customerRepo;
  private readonly CouponRepository _couponRepo;

  public PricingService(ProductRepository productRepo, CustomerRepository customerRepo, CouponRepository couponRepo)
  {
    _productRepo = productRepo;
    _customerRepo = customerRepo;
    _couponRepo = couponRepo;
  }

  public async Task<decimal> CalculateTotalAsync(Order order, CancellationToken ct = default)
  {
    var productIds = order.Items.Select(item => item.ProductId).ToList();
    var products = await _productRepo.GetByIdsAsync(productIds, ct);

    decimal subtotal = 0m;
    for (int i = 0; i < order.Items.Count; i++)
    {
      order.Items[i].UnitPrice = products[i]?.Price ?? default;
      subtotal += order.Items[i].UnitPrice * order.Items[i].Quantity;
    }
    order.SubTotal = RoundMoneyValue(subtotal);

    var discountTask = GetDiscountAsync(order, ct);
    var shippingTask = CalculateShippingCostAsync(order, products, ct);

    order.Discount = await discountTask;
    order.ShippingCost = await shippingTask;

    order.TotalAmount = RoundMoneyValue(order.SubTotal - order.Discount + order.ShippingCost);
    return order.TotalAmount;
  }

  private async Task<decimal> GetDiscountAsync(Order order, CancellationToken ct = default)
  {
    Log.Info($"{nameof(GetDiscountAsync)} -> Performing DNS & TLS handshake...");
    await Task.Delay(150, ct);
    Log.Info($"{nameof(GetDiscountAsync)} -> Sending request...");
    await Task.Delay(300, ct);
    Log.Info($"{nameof(GetDiscountAsync)} -> Waiting for response...");
    var delay = Task.Delay(Random.Shared.Next(500, 1501), ct);

    var customerTask = _customerRepo.GetByIdAsync(order.CustomerId, ct);
    Task<Coupon?>? couponTask = order.CouponCode is not null
      ? _couponRepo.GetByCodeAsync(order.CouponCode, ct)
      : null;

    await delay;
    var customer = await customerTask;
    var coupon = couponTask is not null ? await couponTask : null;

    if (order.CouponCode is not null)
    {
      if (coupon is null)
      {
        throw new InvalidCouponException($"Coupon '{order.CouponCode}' does not exist");
      }
      if (coupon.ExpiryDate < DateTime.Today)
      {
        throw new InvalidCouponException($"Coupon '{order.CouponCode}' has expired");
      }
    }

    decimal discountPercentage = 0m;
    decimal loyaltyDiscount = customer?.LoyaltyTier switch
    {
      LoyaltyTier.Bronze => 0m,
      LoyaltyTier.Silver => 5m,
      LoyaltyTier.Gold => 10m,
      _ => 0m
    };

    discountPercentage += loyaltyDiscount;
    if (coupon is not null)
    {
      discountPercentage += coupon.DiscountPercent;
    }
    discountPercentage = Math.Min(discountPercentage, 30);

    return RoundMoneyValue(order.SubTotal * discountPercentage / 100m);
  }

  private async Task<decimal> CalculateShippingCostAsync(Order order, List<Product?> products, CancellationToken ct = default)
  {
    Log.Info($"{nameof(CalculateShippingCostAsync)} -> Performing DNS & TLS handshake...");
    await Task.Delay(150, ct);
    Log.Info($"{nameof(CalculateShippingCostAsync)} -> Sending request...");
    await Task.Delay(300, ct);
    Log.Info($"{nameof(CalculateShippingCostAsync)} -> Waiting for response...");
    await Task.Delay(Random.Shared.Next(500, 1501), ct);

    if (order.SubTotal >= 1000m)
    {
      return 0m;
    }

    var totalWeight = 0m;
    var totalQuantity = 0;

    for (int i = 0; i < order.Items.Count; i++)
    {
      totalWeight += (products[i]?.WeightKg ?? 0m) * order.Items[i].Quantity;
      totalQuantity += order.Items[i].Quantity;
    }

    var shippingCost = 5m + 1.5m * totalQuantity + 2m * totalWeight;
    return RoundMoneyValue(shippingCost);
  }

  public static decimal RoundMoneyValue(decimal value)
  {
    return Math.Round(value, 2, MidpointRounding.AwayFromZero);
  }
}
