using OrderProcessing.Shared.Models;

namespace OrderProcessing.Phase4;

public interface IShippingCarrier
{
  string Name { get; }
  Task<decimal> GetQuoteAsync(Order order, CancellationToken ct = default);
}

public class ShippingQuote
{
  public string Carrier { get; set; } = string.Empty;
  public decimal Price { get; set; }
  public long ElapsedMs { get; set; }
}
