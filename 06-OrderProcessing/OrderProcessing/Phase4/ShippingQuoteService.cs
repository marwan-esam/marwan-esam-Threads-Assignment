using System.Diagnostics;
using OrderProcessing.Shared.Exceptions;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;

namespace OrderProcessing.Phase4;

public class ShippingQuoteService
{
  private readonly List<IShippingCarrier> _carriers;

  public ShippingQuoteService(IEnumerable<IShippingCarrier> carriers)
  {
    _carriers = carriers.ToList();
  }

  public async Task<ShippingQuote> GetFastestQuoteAsync(Order order, CancellationToken ct = default)
  {
    var sw = Stopwatch.StartNew();

    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1200));
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
    var token = linkedCts.Token;

    var carrierTasks = _carriers.Select(async carrier =>
    {
      try
      {
        decimal price = await carrier.GetQuoteAsync(order, token);
        return (Success: true, Carrier: carrier.Name, Price: price, Exception: (Exception?)null);
      }
      catch (OperationCanceledException) when (token.IsCancellationRequested)
      {
        Log.Info($"[{carrier.Name}] cancelled");
        throw;
      }
      catch (Exception ex)
      {
        return (Success: false, Carrier: carrier.Name, Price: 0m, Exception: ex);
      }
    }).ToList();

    List<Task<(bool Success, string Carrier, decimal Price, Exception? Exception)>> pendingTasks = [.. carrierTasks];

    while (pendingTasks.Count > 0)
    {
      Task<(bool Success, string Carrier, decimal Price, Exception? Exception)> completedTask;
      try
      {
        completedTask = await Task.WhenAny(pendingTasks);
      }
      catch (Exception)
      {
        break;
      }

      pendingTasks.Remove(completedTask);

      if (completedTask.IsCompletedSuccessfully)
      {
        var result = completedTask.Result;
        if (result.Success)
        {
          linkedCts.Cancel();
          sw.Stop();
          return new ShippingQuote
          {
            Carrier = result.Carrier,
            Price = result.Price,
            ElapsedMs = sw.ElapsedMilliseconds
          };
        }
      }
    }

    if (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
    {
      sw.Stop();
      return new ShippingQuote
      {
        Carrier = "Flat rate",
        Price = 25.00m,
        ElapsedMs = sw.ElapsedMilliseconds
      };
    }

    throw new ShippingProviderUnavailableException("All carriers failed");
  }
}
