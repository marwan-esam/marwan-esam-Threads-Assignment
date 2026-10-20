using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase1;

public class OrderRepository
{
  private readonly JsonFileStore _store;

  public OrderRepository(JsonFileStore store)
  {
    _store = store;
  }

  public async Task SaveAsync(Order order, CancellationToken ct = default)
  {
    Log.Info($"{nameof(OrderRepository)} -> Opening connection...");
    await Task.Delay(200, ct);
    Log.Info($"{nameof(OrderRepository)} -> Beginging transaction...");
    await Task.Delay(100, ct);
    Log.Info($"{nameof(OrderRepository)} -> Inserting Order...");
    await Task.Delay(400, ct);
    await _store.UpdateListAsync<Order>(
      DataFiles.Orders,
      orders =>
      {
        int index = orders.FindIndex(orderItem => order.Id == orderItem.Id);
        if(index != -1)
        {
          orders[index] = order;
        } else
        {
          orders.Add(order);
        }
        return true;
      },
      ct
    );
    Log.Info($"{nameof(OrderRepository)} -> Committing transaction...");
    await Task.Delay(200, ct);
  }

  public async Task<List<Order>> GetAllAsync(CancellationToken ct = default)
  {
    Log.Info($"{nameof(OrderRepository)} -> Opening connection...");
    await Task.Delay(200, ct);
    Log.Info($"{nameof(OrderRepository)} -> Executing query for getting all orders...");
    await Task.Delay(Random.Shared.Next(300, 601), ct);
    var orders = await _store.ReadListAsync<Order>(DataFiles.Orders, ct);
    return orders;
  }

  public async Task<List<Order>> GetByStatusAsync(OrderStatus s, CancellationToken ct = default)
  {
    Log.Info($"{nameof(OrderRepository)} -> Opening connection...");
    await Task.Delay(200, ct);
    Log.Info($"{nameof(OrderRepository)} -> Executing query for getting all orders...");
    await Task.Delay(Random.Shared.Next(300, 601), ct);
    var ordersByStatus = await _store.ReadListAsync<Order>(DataFiles.Orders, ct);
    return ordersByStatus.FindAll(order => order.Status == s);
  }
}