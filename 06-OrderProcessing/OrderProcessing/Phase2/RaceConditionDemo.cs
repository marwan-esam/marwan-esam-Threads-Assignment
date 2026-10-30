using OrderProcessing.Phase1;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase2;

public class RaceConditionDemo
{
  private readonly JsonFileStore _store;
  private readonly ProductRepository _productRepo;

  public RaceConditionDemo(JsonFileStore? store = null)
  {
    _store = store ?? new();
    _productRepo = new(_store);
  }
  public async Task RunAsync(CancellationToken ct = default)
  {
    await _store.UpdateListAsync<Product>(
      DataFiles.Products,
      ResetProduct4Stock,
      ct
    );

    var order10Task = _productRepo.ReserveStockUnsafeAsync(4, 1, ct);
    var order11Task = _productRepo.ReserveStockUnsafeAsync(4, 1, ct);

    bool order10Result = await order10Task;
    bool order11Result = await order11Task;

    var product4 = await _productRepo.GetByIdAsync(4, ct);

    System.Console.WriteLine($"Did order 10 accquire product 4 ? {(order10Result ? "yes" : "no")}");
    System.Console.WriteLine($"Did order 11 accquire product 4 ? {(order11Result ? "yes" : "no")}");
    System.Console.WriteLine($"Product 4 remaining stock: {product4!.StockQuantity}");

    await _store.UpdateListAsync<Product>(
      DataFiles.Products,
      ResetProduct4Stock,
      ct
    );

    order10Task = _productRepo.ReserveStockAsync(4, 1, ct);
    order11Task = _productRepo.ReserveStockAsync(4, 1, ct);

    order10Result = await order10Task;
    order11Result = await order11Task;

    product4 = await _productRepo.GetByIdAsync(4, ct);

    System.Console.WriteLine($"Did order 10 accquire product 4 ? {(order10Result ? "yes" : "no")}");
    System.Console.WriteLine($"Did order 11 accquire product 4 ? {(order11Result ? "yes" : "no")}");
    System.Console.WriteLine($"Product 4 remaining stock: {product4!.StockQuantity}");
  }

  private bool ResetProduct4Stock(List<Product> products)
  {
    foreach(var product in products)
    {
      if(product.Id == 4)
      {
        product.StockQuantity = 1;
        return true;
      }
    }
    return false;
  }
}