using OrderProcessing.Phase1;
using OrderProcessing.Shared.Exceptions;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;

namespace OrderProcessing.Phase4;

public abstract class BaseShippingCarrier : IShippingCarrier
{
  public abstract string Name { get; }
  protected abstract int MinResponseMs { get; }
  protected abstract int MaxResponseMs { get; }
  protected abstract decimal PriceFactor { get; }
  private static readonly Dictionary<int, (decimal Price, decimal WeightKg)> ProductCatalog = new()
  {
    [1] = (1200.00m, 2.2m),
    [2] = (800.00m, 0.4m),
    [3] = (25.00m, 0.1m),
    [4] = (90.00m, 0.9m),
    [5] = (45.00m, 0.2m),
    [6] = (150.00m, 0.3m),
    [7] = (350.00m, 5.0m),
    [8] = (70.00m, 0.2m)
  };
  protected readonly HashSet<int>? FailForOrderIds;

  protected BaseShippingCarrier(IEnumerable<int>? failForOrderIds = null)
  {
    FailForOrderIds = failForOrderIds?.ToHashSet();
  }

  public virtual async Task<decimal> GetQuoteAsync(Order order, CancellationToken ct = default)
  {
    await Task.Delay(150, ct);

    if (FailForOrderIds?.Contains(order.Id) == true)
    {
      throw new ShippingProviderUnavailableException($"[{Name}] Carrier unavailable for Order #{order.Id}");
    }

    int delay = Random.Shared.Next(MinResponseMs, MaxResponseMs + 1);
    await Task.Delay(delay, ct);

    decimal baseShipping = CalculateBaseShipping(order);
    return Math.Round(baseShipping * PriceFactor, 2, MidpointRounding.AwayFromZero);
  }

  protected static decimal CalculateBaseShipping(Order order)
  {
    decimal subtotal = 0m;
    var totalWeight = 0m;
    var totalQuantity = 0;

    foreach (var item in order.Items)
    {
      if (ProductCatalog.TryGetValue(item.ProductId, out var info))
      {
        subtotal += info.Price * item.Quantity;
        totalWeight += info.WeightKg * item.Quantity;
      }
      totalQuantity += item.Quantity;
    }

    if (subtotal >= 1000m)
    {
      return 0m;
    }

    return 5m + 1.5m * totalQuantity + 2m * totalWeight;
  }
}

public class FastShipCarrier : BaseShippingCarrier
{
  public override string Name => "FastShip";
  protected override int MinResponseMs => 500;
  protected override int MaxResponseMs => 1500;
  protected override decimal PriceFactor => 1.00m;

  public FastShipCarrier(IEnumerable<int>? failForOrderIds = null)
    : base(failForOrderIds) { }
}

public class CairoExpressCarrier : BaseShippingCarrier
{
  public override string Name => "CairoExpress";
  protected override int MinResponseMs => 300;
  protected override int MaxResponseMs => 2000;
  protected override decimal PriceFactor => 0.95m;

  public CairoExpressCarrier(IEnumerable<int>? failForOrderIds = null)
    : base(failForOrderIds) { }

  public override async Task<decimal> GetQuoteAsync(Order order, CancellationToken ct = default)
  {
    await Task.Delay(150, ct);

    if (order.Id % 2 == 0 || FailForOrderIds?.Contains(order.Id) == true)
    {
      throw new ShippingProviderUnavailableException($"[{Name}] Carrier unavailable for Order #{order.Id}");
    }

    int delay = Random.Shared.Next(MinResponseMs, MaxResponseMs + 1);
    await Task.Delay(delay, ct);

    decimal baseShipping = CalculateBaseShipping(order);
    return Math.Round(baseShipping * PriceFactor, 2, MidpointRounding.AwayFromZero);
  }
}

public class NileCourierCarrier : BaseShippingCarrier
{
  public override string Name => "NileCourier";
  protected override int MinResponseMs => 800;
  protected override int MaxResponseMs => 1200;
  protected override decimal PriceFactor => 1.10m;

  public NileCourierCarrier(IEnumerable<int>? failForOrderIds = null)
    : base(failForOrderIds) { }
}
