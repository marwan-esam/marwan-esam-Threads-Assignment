using System.Diagnostics;
using OrderProcessing.Phase1;
using OrderProcessing.Phase3;
using OrderProcessing.Shared.Exceptions;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase4;

public class Phase4Runner
{
  private readonly JsonFileStore _store;

  public Phase4Runner(JsonFileStore? store = null)
  {
    _store = store ?? new();
  }

  public async Task RunAsync(CancellationToken ct = default)
  {
    Console.WriteLine("==========================================================");
    Console.WriteLine("  PHASE 4 — SCALE-UP");
    Console.WriteLine("==========================================================");

    // Task 10.1: Live checkout progress using Task.WhenEach
    await RunTask10_1_LiveCheckoutProgressAsync(ct);

    // Task 10.2: Customer dashboard
    await RunTask10_2_CustomerDashboardAsync(ct);

    // Task 10.3: Fastest shipping quote
    await RunTask10_3_ShippingQuotesAsync(ct);

    // Task 10.4: Fraud scoring and email re-send
    await RunTask10_4_FraudAndEmailAsync(ct);
  }

  private async Task RunTask10_1_LiveCheckoutProgressAsync(CancellationToken ct)
  {
    Console.WriteLine("\n----------------------------------------------------------");
    Console.WriteLine("  TASK 10.1 — LIVE CHECKOUT PROGRESS");
    Console.WriteLine("----------------------------------------------------------");

    await DataSeeder.ResetAsync(_store, ct);

    var customerRepo = new CustomerRepository(_store);
    var productRepo = new ProductRepository(_store);
    var couponRepo = new CouponRepository(_store);
    var orderRepo = new OrderRepository(_store, failSaveForOrderIds: [13]);
    var incomingRepo = new IncomingOrderRepository(_store);

    var customerService = new CustomerService(customerRepo);
    var inventoryService = new InventoryService(productRepo);
    var pricingService = new PricingService(productRepo, customerRepo, couponRepo);
    var paymentGateway = new PaymentGateway(_store, timeoutOrderIds: [5], declineOrderIds: [8]);
    var emailService = new EmailService(_store, failAttempts: new Dictionary<int, int>
    {
      [3] = 2,
      [17] = int.MaxValue
    });

    var orderProcessor = new OrderProcessor(
      customerService,
      inventoryService,
      pricingService,
      paymentGateway,
      orderRepo,
      emailService,
      _store
    );

    var incomingOrders = await incomingRepo.GetAllAsync(ct);
    var sw = Stopwatch.StartNew();

    var orderTaskMap = new Dictionary<Task<bool>, (Order Order, Stopwatch Sw)>();
    foreach (var o in incomingOrders)
    {
      var orderSw = Stopwatch.StartNew();
      var task = orderProcessor.ProcessOrderAsync(o, ct);
      orderTaskMap[task] = (o, orderSw);
    }

    int counter = 0;
    await foreach (var completedTask in Task.WhenEach(orderTaskMap.Keys))
    {
      counter++;
      var (order, orderSw) = orderTaskMap[completedTask];
      orderSw.Stop();

      string statusDesc = order.Status.ToString();
      if (!string.IsNullOrEmpty(order.FailureReason))
      {
        statusDesc += $" ({order.FailureReason})";
      }

      Console.WriteLine($"[ {counter,2}/{incomingOrders.Count}] Order #{order.Id,-2} -> {statusDesc} after {orderSw.Elapsed.TotalSeconds:F1} s");
    }

    sw.Stop();
    Console.WriteLine($"\nAll orders completed in {sw.Elapsed.TotalSeconds:F1} s.");

    SaveSampleOutput();

    var finalOrders = await orderRepo.GetAllAsync(ct);
    int completed = finalOrders.Count(o => o.Status == OrderStatus.Completed);
    int rejected = finalOrders.Count(o => o.Status == OrderStatus.Rejected);
    int paymentFailed = finalOrders.Count(o => o.Status == OrderStatus.PaymentFailed);
    int failed = finalOrders.Count(o => o.Status == OrderStatus.Failed);

    Console.WriteLine($"Status counts: {completed} Completed, {rejected} Rejected, {paymentFailed} PaymentFailed, {failed} Failed");
  }

  private async Task RunTask10_2_CustomerDashboardAsync(CancellationToken ct)
  {
    Console.WriteLine("\n----------------------------------------------------------");
    Console.WriteLine("  TASK 10.2 — CUSTOMER DASHBOARD");
    Console.WriteLine("----------------------------------------------------------");

    var customerRepo = new CustomerRepository(_store);
    var orderRepo = new OrderRepository(_store);

    var service = new CustomerDashboardService(_store, customerRepo, orderRepo, failPaymentsForCustomerIds: [99]);

    // Run for Customer #3
    var sw3 = Stopwatch.StartNew();
    var dash3 = await service.LoadAsync(3, ct);
    sw3.Stop();

    Console.WriteLine($"Customer #3: {dash3.Customer?.Name} — {dash3.Orders.Count} orders ({string.Join(", ", dash3.Orders.Select(o => $"#{o.Id} {o.Status}"))}), {dash3.Payments.Count} payments ({string.Join(", ", dash3.Payments.Select(p => $"#{p.OrderId} {p.Status}"))}), {dash3.Emails.Count} email (#{dash3.Orders.FirstOrDefault()?.Id}) in {sw3.Elapsed.TotalSeconds:F2} s");

    // Run for Customer #99 (failures)
    try
    {
      await service.LoadAsync(99, ct);
    }
    catch (AggregateException ex)
    {
      Console.WriteLine($"Customer #99 failures reported ({ex.InnerExceptions.Count} failures):");
      foreach (var inner in ex.InnerExceptions)
      {
        Console.WriteLine($"  - {inner.Message}");
      }
    }
  }

  private async Task RunTask10_3_ShippingQuotesAsync(CancellationToken ct)
  {
    Console.WriteLine("\n----------------------------------------------------------");
    Console.WriteLine("  TASK 10.3 — FASTEST SHIPPING QUOTE");
    Console.WriteLine("----------------------------------------------------------");

    var productRepo = new ProductRepository(_store);
    var incomingRepo = new IncomingOrderRepository(_store);
    var orders = await incomingRepo.GetAllAsync(ct);

    var carriers = new List<IShippingCarrier>
    {
      new FastShipCarrier(),
      new CairoExpressCarrier(),
      new NileCourierCarrier()
    };

    var quoteService = new ShippingQuoteService(carriers);

    for (int i = 1; i <= 5; i++)
    {
      var order = orders.First(o => o.Id == i);
      var quote = await quoteService.GetFastestQuoteAsync(order, ct);
      Console.WriteLine($"Order #{order.Id}: Winner={quote.Carrier,-12} | Price=${quote.Price:F2} | Time={quote.ElapsedMs} ms");
    }

    Console.WriteLine("\nTesting all carriers failing on Order #6:");
    var allFailingCarriers = new List<IShippingCarrier>
    {
      new FastShipCarrier(failForOrderIds: [6]),
      new CairoExpressCarrier(failForOrderIds: [6]),
      new NileCourierCarrier(failForOrderIds: [6])
    };
    var failingQuoteService = new ShippingQuoteService(allFailingCarriers);

    try
    {
      var order6 = orders.First(o => o.Id == 6);
      await failingQuoteService.GetFastestQuoteAsync(order6, ct);
    }
    catch (ShippingProviderUnavailableException ex)
    {
      Console.WriteLine($"Order #6 successfully caught expected exception: {ex.Message}");
    }
  }

  private async Task RunTask10_4_FraudAndEmailAsync(CancellationToken ct)
  {
    Console.WriteLine("\n----------------------------------------------------------");
    Console.WriteLine("  TASK 10.4 — FRAUD SCORING & EMAIL RE-SEND");
    Console.WriteLine("----------------------------------------------------------");

    var fraudAndEmail = new FraudAndEmailService();

    // Part A: Fraud scoring
    var (seqFlagged, seqTime) = fraudAndEmail.RunSequentialFraudScoring(10000);
    var (parFlagged, parTime, distinctThreads) = fraudAndEmail.RunParallelFraudScoring(10000, ct);

    double speedup = seqTime / (parTime > 0 ? parTime : 0.001);
    Console.WriteLine($"[Part A: Fraud Scoring]");
    Console.WriteLine($"  - Flagged count (Seq):      {seqFlagged.Count} (First 5: {string.Join(", ", seqFlagged.Take(5))})");
    Console.WriteLine($"  - Flagged count (Parallel): {parFlagged.Count} (First 5: {string.Join(", ", parFlagged.Take(5))})");
    Console.WriteLine($"  - Sequential time:          {seqTime:F3} s");
    Console.WriteLine($"  - Parallel time:            {parTime:F3} s");
    Console.WriteLine($"  - Speed-up:                 {speedup:F2}x");
    Console.WriteLine($"  - Distinct threads used:    {distinctThreads}");
    Console.WriteLine($"  - Environment.ProcessorCount: {Environment.ProcessorCount}");

    // Part B: Email re-send
    Console.WriteLine($"\n[Part B: Email Re-send (30 emails with MaxDegreeOfParallelism = 4)]");
    var emailService = new EmailService(_store); // no failures
    var (emailTime, peakConcurrency) = await fraudAndEmail.RunEmailResendAsync(emailService, ct);

    Console.WriteLine($"  - Total time:        {emailTime:F2} s (Expected ~8 s)");
    Console.WriteLine($"  - Peak concurrency:  {peakConcurrency} (Must be exactly 4)");
  }

  private static void SaveSampleOutput()
  {
    try
    {
      string sampleDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../sample-output/phase4"));
      Directory.CreateDirectory(sampleDir);
      string dataDir = DataFiles.DataDir;
      foreach (var file in Directory.GetFiles(dataDir, "*.json"))
      {
        File.Copy(file, Path.Combine(sampleDir, Path.GetFileName(file)), overwrite: true);
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Error saving sample output: {ex.Message}");
    }
  }
}
