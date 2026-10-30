using System.Diagnostics;
using OrderProcessing.Phase1;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase2;

public class Phase2Runner
{
  private readonly JsonFileStore _store;
  private readonly OrderRepository _orderRepo;
  private readonly IncomingOrderRepository _incomingOrderRepo;
  private readonly ProductRepository _productRepo;
  private readonly ReportGenerator _reportGenertor;
  private readonly RaceConditionDemo _raceDemo;
  private readonly OrderPricer _orderPricer;
  private readonly OrderValidator _orderValidator;
  private readonly OrderProcessorV2 _orderProcessor;

  public Phase2Runner(JsonFileStore? store = null)
  {
    _store = store ?? new();
    _orderRepo = new(_store);
    _incomingOrderRepo = new(_store);
    _productRepo = new(_store);
    _reportGenertor = new(_store);
    _raceDemo = new(_store);

    var customerRepo = new CustomerRepository(_store);
    var couponRepo = new CouponRepository(_store);
    var pricingService = new PricingService(_productRepo, customerRepo, couponRepo, [7]);
    _orderPricer = new(pricingService);
    _orderValidator = new(customerRepo, _productRepo, couponRepo);
    _orderProcessor = new(_orderPricer, _store);
  }

  public async Task RunAsync(CancellationToken ct = default)
  {
    await DataSeeder.ResetAsync(_store, ct);
    Console.WriteLine("\n===== Step 1A: Race Condition Demo =====");
    await _raceDemo.RunAsync(ct);

    await DataSeeder.ResetAsync(_store, ct);
    Console.WriteLine("\n===== Step 1B: Pricing Comparison (Order #1) =====");
    var order1 = (await _incomingOrderRepo.GetAllAsync(ct)).FirstOrDefault(order => order.Id == 1);

    if (order1 is not null)
    {
      Stopwatch sw = Stopwatch.StartNew();
      await _orderPricer.PriceOrderSequentialAsync(order1, ct);
      sw.Stop();
      var seqTime = sw.Elapsed.TotalSeconds;

      sw.Restart();
      await _orderPricer.PriceOrderAsync(order1, ct);
      sw.Stop();
      var concTime = sw.Elapsed.TotalSeconds;

      Console.WriteLine($"Order #1 Pricing -> SubTotal: ${order1.SubTotal:F2}, Discount: ${order1.Discount:F2}, Shipping: ${order1.ShippingCost:F2}, Total: ${order1.TotalAmount:F2}");
      Console.WriteLine($"Sequential Time: {seqTime:F1} seconds");
      Console.WriteLine($"Concurrent Time: {concTime:F1} seconds");
    }

    Console.WriteLine("\n===== Step 2: Concurrent Validation of 20 Orders =====");
    Stopwatch phase2Sw = Stopwatch.StartNew();

    var incomingOrders = await _incomingOrderRepo.GetAllAsync(ct);
    List<Task<bool>> validatingOrderTasks = [];
    foreach (var order in incomingOrders)
    {
      validatingOrderTasks.Add(_orderValidator.ValidateOrderAsync(order, ct));
    }

    foreach (var task in validatingOrderTasks)
    {
      await task;
    }

    List<Task> saveTasks = [];
    foreach (var order in incomingOrders)
    {
      saveTasks.Add(_orderRepo.SaveAsync(order, ct));
    }

    foreach(var task in saveTasks)
    {
      await task;
    }

    Console.WriteLine("\n===== Step 3: Starting Concurrent Processing of Validated Orders =====");
    var validatedOrders = incomingOrders
      .Where(order => order.Status == OrderStatus.Validated)
      .ToList();

    List<Task<bool>> processValidatedOrderTasks = [];
    foreach (var order in validatedOrders)
    {
      processValidatedOrderTasks.Add(_orderProcessor.ProcessValidatedOrderAsync(order, ct));
    }

    Console.WriteLine($"Immediate Status of task[0] (Order #{validatedOrders[0].Id}): {processValidatedOrderTasks[0].Status}");

    await Task.Delay(1000, ct);
    Console.WriteLine("\n===== Step 4: Task Statuses After 1000ms (While Running) =====");
    for (int i = 0; i < processValidatedOrderTasks.Count; i++)
    {
      Console.WriteLine($"  - Task[{i}] (Order #{validatedOrders[i].Id}): {processValidatedOrderTasks[i].Status}");
    }

    Console.WriteLine("\n===== Step 5: Awaiting Validated Order Tasks One by One =====");
    for (int i = 0; i < processValidatedOrderTasks.Count; i++)
    {
      var order = validatedOrders[i];
      var task = processValidatedOrderTasks[i];
      try
      {
        await task;
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        Log.Error($"Order #{order.Id} faulted! Task Status: {task.Status} | Error: {ex.Message}");
        order.Status = OrderStatus.Failed;
        order.FailureReason = ex.Message;

        foreach (var item in order.Items)
        {
          await _productRepo.ReleaseStockAsync(item.ProductId, item.Quantity, ct);
        }

        await _orderRepo.SaveAsync(order, ct);
      }
    }

    phase2Sw.Stop();

    Console.WriteLine("\n===== Step 6: Final Task Statuses =====");
    for (int i = 0; i < processValidatedOrderTasks.Count; i++)
    {
      Console.WriteLine($"  - Task[{i}] (Order #{validatedOrders[i].Id}): {processValidatedOrderTasks[i].Status}");
    }
    Console.WriteLine($"Total Phase 2 Concurrent Processing Time: {phase2Sw.Elapsed.TotalSeconds:F1} seconds");

    Console.WriteLine("\n===== Step 7: Verifying orders.json =====");
    var savedOrders = await _orderRepo.GetAllAsync(ct);
    var completedCount = savedOrders.Count(o => o.Status == OrderStatus.Completed);
    var rejectedCount = savedOrders.Count(o => o.Status == OrderStatus.Rejected);
    var failedCount = savedOrders.Count(o => o.Status == OrderStatus.Failed);

    Console.WriteLine($"orders.json now contains {savedOrders.Count} records:");
    Console.WriteLine($"  - Completed: {completedCount}");
    Console.WriteLine($"  - Rejected:  {rejectedCount}");
    Console.WriteLine($"  - Failed:    {failedCount}");
    foreach (var order in savedOrders.Where(o => o.Status != OrderStatus.Completed).OrderBy(o => o.Id))
    {
      Console.WriteLine($"    * Order #{order.Id} [{order.Status}]: {order.FailureReason}");
    }

    Console.WriteLine("\n===== Task 8.6: Report Generation =====");
    var reportTask = _reportGenertor.GenerateAsync(ct);
    Console.WriteLine("Generating report...");
    var report = await reportTask;
    Console.WriteLine(_reportGenertor.PrintReport(report));
  }
  
  public void BlockingWaitDemo()
  {
    Log.Info($"{nameof(BlockingWaitDemo)} -> Starting async query and blocking calling thread with .Result...");
    Task<Product?> productTask = _productRepo.GetByIdAsync(1);
    Product? product = productTask.Result;
    Log.Info($"{nameof(BlockingWaitDemo)} -> Blocked thread resumed with product: {product?.Name}");
  }
}