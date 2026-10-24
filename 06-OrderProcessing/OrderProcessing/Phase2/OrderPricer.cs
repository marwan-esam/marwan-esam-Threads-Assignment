using OrderProcessing.Shared.Models;

namespace OrderProcessing.Phase2;

public class OrderPricer
{
  private readonly PricingService _pricingService;

  public OrderPricer(PricingService pricingService)
  {
    _pricingService = pricingService;
  }

  public async Task PriceOrderAsync(Order order, CancellationToken ct = default)
  {
    order.SubTotal = await _pricingService.CalculateSubtotalAsync(order, ct);

    var discountTask = _pricingService.GetDiscountAsync(order, ct);
    var shippingTask = _pricingService.CalculateShippingCostAsync(order, ct);

    order.Discount = await discountTask;
    order.ShippingCost = await shippingTask;

    order.TotalAmount = PricingService.RoundMoneyValue(order.SubTotal - order.Discount + order.ShippingCost);
  }

  public async Task PriceOrderSequentialAsync(Order order, CancellationToken ct = default)
  {
    order.SubTotal = await _pricingService.CalculateSubtotalAsync(order, ct);

    var discount = await _pricingService.GetDiscountAsync(order, ct);
    var shipping = await _pricingService.CalculateShippingCostAsync(order, ct);

    order.Discount = discount;
    order.ShippingCost = shipping;

    order.TotalAmount = PricingService.RoundMoneyValue(order.SubTotal - order.Discount + order.ShippingCost);
  }
}