using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase1;

public class IncomingOrderRepository
{
  private readonly JsonFileStore _store;

  public IncomingOrderRepository(JsonFileStore store)
  {
    _store = store;
  }

  public async Task<List<Order>> GetAllAsync(CancellationToken ct = default)
  {
    Log.Info($"{nameof(IncomingOrderRepository)} -> Opening connection...");
    await Task.Delay(200, ct);
    Log.Info($"{nameof(IncomingOrderRepository)} -> Executing query for incoming orders...");
    await Task.Delay(Random.Shared.Next(300, 601), ct);
    var incomingOrders = await _store.ReadListAsync<Order>(DataFiles.IncomingOrders, ct);
    return incomingOrders;
  }
}