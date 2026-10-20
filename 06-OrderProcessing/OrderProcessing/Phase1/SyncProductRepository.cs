using OrderProcessing.Shared.Storage;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Logging;

namespace OrderProcessing.Phase1;

public class SyncProductRepository
{
  private readonly JsonFileStore _store;

  public SyncProductRepository(JsonFileStore jsonFileStore)
  {
    _store = jsonFileStore;
  }

  public Product? GetById(int id)
  {
    Log.Info($"{nameof(ProductRepository)} -> Opening connection...");
    Thread.Sleep(200);
    Log.Info($"{nameof(ProductRepository)} -> Executing query for Product #{id}...");
    Thread.Sleep(Random.Shared.Next(300, 601));
    var products = _store.ReadList<Product>(DataFiles.Products);
    foreach(var product in products)
    {
      if(product.Id == id)
      {
        Log.Info($"{nameof(ProductRepository)} -> Product #{id} found ({product.Name})");
        return product;
      }
    }
    Log.Info($"{nameof(ProductRepository)} -> Product not found");
    return null;
  }

  public List<Product?> GetByIds(List<int> ids)
  {
    Log.Info($"{nameof(ProductRepository)} -> Opening connection...");
    Thread.Sleep(200);
    Log.Info($"{nameof(ProductRepository)} -> Executing query for specified list of products...");
    Thread.Sleep(Random.Shared.Next(300, 601));
    var products = _store.ReadList<Product>(DataFiles.Products);
    var idProduct = products.ToDictionary(product => product.Id);
    return [.. ids
    .Select(idProduct.GetValueOrDefault)];
  }

  public bool ReserveStock(int productId, int qty)
  {
    Log.Info($"{nameof(ProductRepository)} -> Opening connection...");
    Thread.Sleep(200);
    Log.Info($"{nameof(ProductRepository)} -> Begining transaction...");
    Thread.Sleep(100);
    Log.Info($"{nameof(ProductRepository)} -> Updating product #{productId}...");
    Thread.Sleep(400);
    var isUpdated = _store.UpdateList<Product>(
      DataFiles.Products,
      products =>
      {
        foreach(var product in products)
        {
          if(product.Id == productId)
          {
            if(product.StockQuantity >= qty)
            {
              product.StockQuantity -= qty;
              return true;
            } else
            {
              return false;
            }
          }
        }
        return false;
      }
    );
    Log.Info($"{nameof(ProductRepository)} -> Committing transaction...");
    Thread.Sleep(200);
    return isUpdated;
  }
}