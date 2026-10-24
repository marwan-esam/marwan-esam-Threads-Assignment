using System.Collections.Concurrent;
using System.Diagnostics;
using OrderProcessing.Phase3;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase4;

public class FraudAndEmailService
{
  public static int Score(int orderId)
  {
    long h = orderId;
    for (int i = 0; i < 100_000; i++)
    {
      h = (h * 31 + i) % 1_000_000_007;
    }
    return (int)(h % 100);
  }

  public (List<int> FlaggedOrders, double ElapsedSeconds) RunSequentialFraudScoring(int count = 10000)
  {
    var sw = Stopwatch.StartNew();
    List<int> flagged = [];
    for (int id = 1; id <= count; id++)
    {
      if (Score(id) >= 90)
      {
        flagged.Add(id);
      }
    }
    sw.Stop();
    return (flagged, sw.Elapsed.TotalSeconds);
  }

  public (List<int> FlaggedOrders, double ElapsedSeconds, int DistinctThreads) RunParallelFraudScoring(
    int count = 10000,
    CancellationToken ct = default)
  {
    var sw = Stopwatch.StartNew();
    var flaggedBag = new ConcurrentBag<int>();
    var threadIds = new ConcurrentDictionary<int, byte>();

    Parallel.For(1, count + 1, new ParallelOptions
    {
      MaxDegreeOfParallelism = Environment.ProcessorCount,
      CancellationToken = ct
    }, id =>
    {
      threadIds.TryAdd(Environment.CurrentManagedThreadId, 0);
      if (Score(id) >= 90)
      {
        flaggedBag.Add(id);
      }
    });

    sw.Stop();
    var flagged = flaggedBag.OrderBy(x => x).ToList();
    return (flagged, sw.Elapsed.TotalSeconds, threadIds.Count);
  }

  public async Task<(double ElapsedSeconds, int PeakConcurrency)> RunEmailResendAsync(
    IEmailService emailService,
    CancellationToken ct = default)
  {
    var testOrders = Enumerable.Range(1001, 30).Select(id => (
      Order: new Order { Id = id, CustomerId = 1, Items = [], TotalAmount = 50m, Status = OrderStatus.Completed },
      Customer: new Customer { Id = 1, Name = "Ahmed Ali", Email = "ahmed.ali@example.com", IsActive = true, LoyaltyTier = LoyaltyTier.Gold }
    )).ToList();

    int activeSends = 0;
    int peakConcurrency = 0;
    var sw = Stopwatch.StartNew();

    await Parallel.ForEachAsync(testOrders, new ParallelOptions
    {
      MaxDegreeOfParallelism = 4,
      CancellationToken = ct
    }, async (pair, token) =>
    {
      int current = Interlocked.Increment(ref activeSends);

      int initialPeak;
      do
      {
        initialPeak = peakConcurrency;
        if (current <= initialPeak) break;
      } while (Interlocked.CompareExchange(ref peakConcurrency, current, initialPeak) != initialPeak);

      try
      {
        await emailService.SendConfirmationAsync(pair.Order, pair.Customer, token);
      }
      finally
      {
        Interlocked.Decrement(ref activeSends);
      }
    });

    sw.Stop();
    return (sw.Elapsed.TotalSeconds, peakConcurrency);
  }
}
