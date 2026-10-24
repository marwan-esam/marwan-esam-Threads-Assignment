using OrderProcessing.Shared.Storage;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Logging;
using System.Collections.Concurrent;

namespace OrderProcessing.Phase1;

public class ProductRepository
{
  private readonly JsonFileStore _store;
  private static readonly ConcurrentDictionary<int, SemaphoreSlim> _productLocks = new();

  public ProductRepository(JsonFileStore jsonFileStore)
  {
    _store = jsonFileStore;
  }

  public SemaphoreSlim GetLock(int productId)
  {
    return _productLocks.GetOrAdd(productId, _ => new SemaphoreSlim(1, 1));
  }

  public async Task<Product?> GetByIdAsync(int id, CancellationToken ct = default)
  {
    Log.Info($"{nameof(ProductRepository)} -> Opening connection...");
    await Task.Delay(200, ct);
    Log.Info($"{nameof(ProductRepository)} -> Executing query for Product #{id}...");
    await Task.Delay(Random.Shared.Next(300, 601), ct);
    var products = await _store.ReadListAsync<Product>(DataFiles.Products, ct);
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

  public async Task<List<Product?>> GetByIdsAsync(List<int> ids, CancellationToken ct = default)
  {
    Log.Info($"{nameof(ProductRepository)} -> Opening connection...");
    await Task.Delay(200, ct);
    Log.Info($"{nameof(ProductRepository)} -> Executing query for specified list of products...");
    await Task.Delay(Random.Shared.Next(300, 601), ct);
    var products = await _store.ReadListAsync<Product>(DataFiles.Products, ct);
    var idProduct = products.ToDictionary(product => product.Id);
    return [.. ids
    .Select(idProduct.GetValueOrDefault)];
  }

  public async Task<List<Product>> GetAllAsync(CancellationToken ct = default)
  {
    Log.Info($"{nameof(ProductRepository)} -> Opening Repository...");
    await Task.Delay(200, ct);
    Log.Info($"{nameof(ProductRepository)} -> Executing query to get all products...");
    await Task.Delay(Random.Shared.Next(300, 601), ct);
    var products = await _store.ReadListAsync<Product>(DataFiles.Products, ct);
    return products;
  }

  public async Task<bool> ReserveStockAsync(int productId, int qty, CancellationToken ct = default)
  {
    var productLock = GetLock(productId);
    await productLock.WaitAsync(ct);
    try
    {
      
      Log.Info($"{nameof(ProductRepository)} -> Opening connection...");
      await Task.Delay(200, ct);
      Log.Info($"{nameof(ProductRepository)} -> Begining transaction...");
      await Task.Delay(100, ct);
      Log.Info($"{nameof(ProductRepository)} -> Updating product #{productId}...");
      await Task.Delay(400, ct);
      var isUpdated = await _store.UpdateListAsync<Product>(
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
        },
        ct
      );
      Log.Info($"{nameof(ProductRepository)} -> Committing transaction...");
      await Task.Delay(200, ct);
      return isUpdated;
    } finally
    {
      productLock.Release();
    }
  }

  public async Task<bool> ReserveStockUnsafeAsync(int productId, int qty, CancellationToken ct = default)
  {
    Log.Info($"{nameof(ProductRepository)} -> Opening connection...");
    await Task.Delay(200, ct);
    var products = await _store.ReadListAsync<Product>(DataFiles.Products, ct);
    foreach(var product in products)
    {
      if(product.Id == productId)
      {
        if(product.StockQuantity >= qty)
        {
          Log.Info($"{nameof(ProductRepository)} -> Begining transaction...");
          await Task.Delay(100, ct);
          Log.Info($"{nameof(ProductRepository)} -> Updating product #{productId}...");
          await Task.Delay(400, ct);
          product.StockQuantity -= qty;
          await _store.WriteListAsync<Product>(DataFiles.Products, products, ct);
          
          Log.Info($"{nameof(ProductRepository)} -> Committing transaction...");
          await Task.Delay(200, ct);
          return true;
        } else
        {
          return false;
        }
      }
    }
    return false;
  }

  public async Task ReleaseStockAsync(int productId, int qty, CancellationToken ct = default)
  {
    var productLock = GetLock(productId);
    await productLock.WaitAsync(ct);
    try
    {
      
      Log.Info($"{nameof(ProductRepository)} -> Opening connection...");
      await Task.Delay(200, ct);
      Log.Info($"{nameof(ProductRepository)} -> Begining transaction...");
      await Task.Delay(100, ct);
      Log.Info($"{nameof(ProductRepository)} -> Updating product #{productId}...");
      await Task.Delay(400, ct);

      await _store.UpdateListAsync<Product>(
        DataFiles.Products,
        products =>
        {
          foreach(var product in products)
          {
            if(product.Id == productId)
            {
              product.StockQuantity += qty;
              return true;
            }
          }
          return false;
        },
        ct
      );
      Log.Info($"{nameof(ProductRepository)} -> Committing transaction...");
      await Task.Delay(200, ct);
    } finally
    {
      productLock.Release();
    }
  }
}