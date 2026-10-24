using System.Diagnostics;
using OrderProcessing.Phase1;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase3;

public class Phase3Runner
{
  private readonly JsonFileStore _store;

  public Phase3Runner(JsonFileStore? store = null)
  {
    _store = store ?? new();
  }

  public async Task RunAsync(CancellationToken ct = default)
  {
    Console.WriteLine("==========================================================");
    Console.WriteLine("  PHASE 3 — FULL CHECKOUT WORKFLOW");
    Console.WriteLine("==========================================================");

    // 1. Reset storage
    await DataSeeder.ResetAsync(_store, ct);

    // 2. Setup repositories & services with controlled failures (Task 9.2)
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

    // 3. Load 20 orders and run checkout concurrently
    var incomingOrders = await incomingRepo.GetAllAsync(ct);
    Console.WriteLine($"\nLoaded {incomingOrders.Count} incoming orders. Starting concurrent processing...\n");

    var sw = Stopwatch.StartNew();

    // Start all 20 orders without awaiting
    List<Task<bool>> orderTasks = [];
    foreach (var order in incomingOrders)
    {
      orderTasks.Add(orderProcessor.ProcessOrderAsync(order, ct));
    }

    // Await one by one (as required by rule until Phase 4)
    foreach (var task in orderTasks)
    {
      await task;
    }

    sw.Stop();
    Console.WriteLine($"\nAll 20 orders processed in {sw.Elapsed.TotalSeconds:F1} seconds.\n");

    // Copy sample output for Phase 3 right now before comparison resets Data/
    SaveSampleOutput();

    // 4. Print final table
    var finalOrders = await orderRepo.GetAllAsync(ct);
    Console.WriteLine("===== Final Orders Table =====");
    Console.WriteLine(string.Format("{0,-8} | {1,-15} | {2,-12} | {3}", "Order", "Status", "Total", "Reason"));
    Console.WriteLine(new string('-', 75));
    foreach (var o in finalOrders.OrderBy(x => x.Id))
    {
      string totalStr = o.Status == OrderStatus.Completed ? $"${o.TotalAmount:F2}" : "-";
      string reasonStr = o.FailureReason ?? "-";
      Console.WriteLine(string.Format("#{0,-7} | {1,-15} | {2,-12} | {3}", o.Id, o.Status, totalStr, reasonStr));
    }

    // Print summary counts
    int completed = finalOrders.Count(o => o.Status == OrderStatus.Completed);
    int rejected = finalOrders.Count(o => o.Status == OrderStatus.Rejected);
    int paymentFailed = finalOrders.Count(o => o.Status == OrderStatus.PaymentFailed);
    int failed = finalOrders.Count(o => o.Status == OrderStatus.Failed);

    Console.WriteLine($"\nSummary: {completed} Completed, {rejected} Rejected, {paymentFailed} PaymentFailed, {failed} Failed");

    // 5. Run Task 9.6: Sync vs Async comparison (Orders #1-#10 with failures disabled)
    await RunSyncVsAsyncComparisonAsync(ct);

    // 6. Run Task 9.7: Fire-and-forget demo
    await RunFireAndForgetDemoAsync(customerRepo, orderRepo, emailService);
  }

  private async Task RunSyncVsAsyncComparisonAsync(CancellationToken ct)
  {
    Console.WriteLine("\n==========================================================");
    Console.WriteLine("  TASK 9.6 — SYNC VS ASYNC COMPARISON (Orders #1-#10)");
    Console.WriteLine("==========================================================");

    var incomingRepo = new IncomingOrderRepository(_store);
    var allOrders = await incomingRepo.GetAllAsync(ct);
    var testOrders = allOrders.Where(o => o.Id >= 1 && o.Id <= 10).ToList();

    // Sync Run
    await DataSeeder.ResetAsync(_store, ct);
    int initialThreads = Process.GetCurrentProcess().Threads.Count;
    int initialPoolThreads = ThreadPool.ThreadCount;

    var syncProcessor = new SyncOrderProcessor(_store);
    var syncSw = Stopwatch.StartNew();

    List<Thread> threads = [];
    foreach (var order in testOrders)
    {
      var clone = CloneOrder(order);
      var th = new Thread(() => syncProcessor.ProcessOrder(clone));
      threads.Add(th);
      th.Start();
    }

    foreach (var th in threads)
    {
      th.Join();
    }
    syncSw.Stop();

    int syncPeakThreads = Process.GetCurrentProcess().Threads.Count;
    int syncPoolThreads = ThreadPool.ThreadCount;
    Console.WriteLine($"Sync Version:  {syncSw.Elapsed.TotalSeconds:F1} s | Process Threads: {syncPeakThreads} | ThreadPool.ThreadCount: {syncPoolThreads}");

    // Async Run
    await DataSeeder.ResetAsync(_store, ct);

    var customerRepo = new CustomerRepository(_store);
    var productRepo = new ProductRepository(_store);
    var couponRepo = new CouponRepository(_store);
    var orderRepo = new OrderRepository(_store); // no failures
    var customerService = new CustomerService(customerRepo);
    var inventoryService = new InventoryService(productRepo);
    var pricingService = new PricingService(productRepo, customerRepo, couponRepo);
    var paymentGateway = new PaymentGateway(_store); // no failures
    var emailService = new EmailService(_store); // no failures

    var asyncProcessor = new OrderProcessor(
      customerService,
      inventoryService,
      pricingService,
      paymentGateway,
      orderRepo,
      emailService,
      _store
    );

    var asyncSw = Stopwatch.StartNew();
    List<Task<bool>> asyncTasks = [];
    foreach (var order in testOrders)
    {
      var clone = CloneOrder(order);
      asyncTasks.Add(asyncProcessor.ProcessOrderAsync(clone, ct));
    }

    foreach (var t in asyncTasks)
    {
      await t;
    }
    asyncSw.Stop();

    int asyncPeakThreads = Process.GetCurrentProcess().Threads.Count;
    int asyncPoolThreads = ThreadPool.ThreadCount;
    Console.WriteLine($"Async Version: {asyncSw.Elapsed.TotalSeconds:F1} s | Process Threads: {asyncPeakThreads} | ThreadPool.ThreadCount: {asyncPoolThreads}");
  }

  private async Task RunFireAndForgetDemoAsync(CustomerRepository customerRepo, OrderRepository orderRepo, IEmailService emailService)
  {
    Console.WriteLine("\n==========================================================");
    Console.WriteLine("  TASK 9.7 — FIRE-AND-FORGET INVESTIGATION");
    Console.WriteLine("==========================================================");

    var demo = new FireAndForgetDemo(orderRepo, emailService);
    var customer = (await customerRepo.GetByIdAsync(1))!;
    var order = new Order
    {
      Id = 999,
      CustomerId = 1,
      Items = [],
      Status = OrderStatus.Completed,
      TotalAmount = 100m
    };

    Console.WriteLine("1. Running Broken Fire-and-Forget version:");
    await demo.ProcessOrderBrokenAsync(order, customer);

    Console.WriteLine("\n2. Running Fixed Awaited version:");
    await demo.ProcessOrderFixedAsync(order, customer);
  }

  private static Order CloneOrder(Order src)
  {
    return new Order
    {
      Id = src.Id,
      CustomerId = src.CustomerId,
      Items = src.Items.Select(i => new OrderItem
      {
        ProductId = i.ProductId,
        Quantity = i.Quantity,
        UnitPrice = i.UnitPrice
      }).ToList(),
      CouponCode = src.CouponCode,
      Status = OrderStatus.Pending,
      CreatedAt = src.CreatedAt
    };
  }

  private static void SaveSampleOutput()
  {
    try
    {
      string sampleDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../sample-output/phase3"));
      Directory.CreateDirectory(sampleDir);
      string dataDir = DataFiles.DataDir;
      foreach (var file in Directory.GetFiles(dataDir, "*.json"))
      {
        File.Copy(file, Path.Combine(sampleDir, Path.GetFileName(file)), overwrite: true);
      }
      Console.WriteLine($"[SampleOutput] Saved sample output to {sampleDir}");
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Error saving sample output: {ex.Message}");
    }
  }
}
