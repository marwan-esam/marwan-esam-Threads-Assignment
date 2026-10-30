using System.Diagnostics;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase1;

public class Phase1Runner
{
  private readonly JsonFileStore _store;
  private readonly IncomingOrderRepository _incomingOrderRepository;
  private readonly OrderRepository _orderRepository;
  private readonly OrderValidator _asyncValidator;
  private readonly SyncOrderValidator _syncValidator;

  public Phase1Runner(JsonFileStore? store = null)
  {
    _store = store ?? new();

    var customerRepository = new CustomerRepository(_store);
    var productRepository = new ProductRepository(_store);
    var couponRepository = new CouponRepository(_store);
    _asyncValidator = new(customerRepository, productRepository, couponRepository);

    var syncCustomerRepository = new SyncCustomerRepository(_store);
    var syncProductRepository = new SyncProductRepository(_store);
    var syncCouponRepository = new SyncCouponRepository(_store);
    _syncValidator = new(syncCustomerRepository, syncProductRepository, syncCouponRepository);

    _incomingOrderRepository = new(_store);
    _orderRepository = new(_store);
  }
  public async Task RunAsync(CancellationToken ct = default)
  {
    await DataSeeder.ResetAsync(_store, ct);

    var incomingOrders = await _incomingOrderRepository.GetAllAsync(ct);
    Stopwatch sw = new();
    sw.Start();
    foreach(var order in incomingOrders)
    {
      _syncValidator.ValidateOrder(order);
    }
    sw.Stop();
    var syncSeconds = sw.Elapsed.TotalSeconds;
    
    await DataSeeder.ResetAsync(_store, ct);
    incomingOrders = await _incomingOrderRepository.GetAllAsync(ct);
    sw.Reset();
    foreach(var order in incomingOrders)
    {
      sw.Start();
      await _asyncValidator.ValidateOrderAsync(order, ct);
      sw.Stop();
      await _orderRepository.SaveAsync(order, ct);
    }
    sw.Stop();
    var asyncSeconds = sw.Elapsed.TotalSeconds;

    var validOrders = await _orderRepository.GetByStatusAsync(OrderStatus.Validated, ct);
    var rejectedOrders = await _orderRepository.GetByStatusAsync(OrderStatus.Rejected, ct);
    
    var totalOrders = validOrders.Count + rejectedOrders.Count;
    System.Console.WriteLine("===== Validation Summary =====");
    System.Console.WriteLine($"{"Total Orders:", -16} {totalOrders}");
    System.Console.WriteLine($"{"Validated:", -16} {validOrders.Count}");
    System.Console.WriteLine($"{"Rejected:", -16} {rejectedOrders.Count}");
    foreach(var order in rejectedOrders)
    {
      string prefix = $"  - Order #{order.Id}:".PadRight(14);
      System.Console.WriteLine($"{prefix} {order.FailureReason}");
    }
    System.Console.WriteLine($"{"Sync Time:", -11} {syncSeconds:F1} seconds");
    System.Console.WriteLine($"{"Async Time:", -11} {asyncSeconds:F1} seconds");
    System.Console.WriteLine($"{DataFiles.Orders} now contains {totalOrders} records.");
  }
}