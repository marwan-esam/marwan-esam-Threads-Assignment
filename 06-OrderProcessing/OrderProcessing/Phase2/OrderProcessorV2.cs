using OrderProcessing.Phase1;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase2;

public class OrderProcessorV2
{
  private readonly JsonFileStore _store;
  private readonly ProductRepository _productRepo;
  private readonly OrderPricer _orderPricer;
  private readonly InvoiceGenerator _invoiceGenerator;
  private readonly OrderRepository _orderRepo;

  public OrderProcessorV2(OrderPricer orderPricer, JsonFileStore? store = null)
  {
    _store = store ?? new();
    _productRepo = new(_store);
    _orderPricer = orderPricer;
    _invoiceGenerator = new(_store);
    _orderRepo = new(_store);
  }
  public async Task<bool> ProcessValidatedOrderAsync(Order order, CancellationToken ct = default)
  {
    List<OrderItem> reservedItems = [];
    foreach(var item in order.Items)
    {
      if(await _productRepo.ReserveStockAsync(item.ProductId, item.Quantity, ct))
      {
        reservedItems.Add(item);
      } else
      {
        await ReleaseReservedItemsAsync(reservedItems, ct);
        order.Status = OrderStatus.Rejected;
        var outOfStockProduct = await _productRepo.GetByIdAsync(item.ProductId, ct);
        order.FailureReason = $"Insufficient stock for '{outOfStockProduct?.Name}'";
        await _orderRepo.SaveAsync(order, ct);
        return false;
      }
    }
    await _orderPricer.PriceOrderAsync(order, ct);
    await _invoiceGenerator.GenerateAsync(order, ct);
    order.Status = OrderStatus.Completed;
    order.FailureReason = null;
    await _orderRepo.SaveAsync(order, ct);
    Log.Success($"Order #{order.Id}: Successfully completed and saved.");
    return true;
  }

  private async Task ReleaseReservedItemsAsync(List<OrderItem> reservedItems, CancellationToken ct = default)
  {
    foreach(var item in reservedItems)
    {
      await _productRepo.ReleaseStockAsync(item.ProductId, item.Quantity, ct);
    }
  }
}