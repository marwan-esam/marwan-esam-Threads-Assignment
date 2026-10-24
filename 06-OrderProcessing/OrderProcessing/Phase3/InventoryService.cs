using OrderProcessing.Phase1;
using OrderProcessing.Shared.Models;

namespace OrderProcessing.Phase3;

public class InventoryService
{
  private readonly ProductRepository _productRepo;

  public InventoryService(ProductRepository productRepo)
  {
    _productRepo = productRepo;
  }

  public async Task<bool> ReserveStockAsync(Order order, CancellationToken ct = default)
  {
    List<OrderItem> reservedItems = [];
    foreach (var item in order.Items)
    {
      bool reserved = await _productRepo.ReserveStockAsync(item.ProductId, item.Quantity, ct);
      if (reserved)
      {
        reservedItems.Add(item);
      }
      else
      {
        foreach (var r in reservedItems)
        {
          await _productRepo.ReleaseStockAsync(r.ProductId, r.Quantity, ct);
        }
        var outOfStockProduct = await _productRepo.GetByIdAsync(item.ProductId, ct);
        order.FailureReason = $"Insufficient stock for '{outOfStockProduct?.Name}'";
        return false;
      }
    }
    return true;
  }

  public async Task ReleaseStockAsync(Order order, CancellationToken ct = default)
  {
    foreach (var item in order.Items)
    {
      await _productRepo.ReleaseStockAsync(item.ProductId, item.Quantity, ct);
    }
  }
}
