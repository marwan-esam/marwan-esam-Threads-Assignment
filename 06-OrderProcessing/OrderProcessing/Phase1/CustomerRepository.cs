using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase1;

public class CustomerRepository
{
  private readonly JsonFileStore _store;

  public CustomerRepository(JsonFileStore jsonFileStore)
  {
    _store = jsonFileStore;
  }

  public async Task<Customer?> GetByIdAsync(int id, CancellationToken ct = default)
  {
    Log.Info($"{nameof(CustomerRepository)} -> Opening connection...");
    await Task.Delay(200, ct);
    Log.Info($"{nameof(CustomerRepository)} -> Executing query for Customer #{id}...");
    await Task.Delay(Random.Shared.Next(300, 601), ct);
    var customersList = await _store.ReadListAsync<Customer>(DataFiles.Customers, ct);
    foreach(var customer in customersList)
    {
      if(customer.Id == id)
      {
        Log.Info($"{nameof(CustomerRepository)} -> Customer #{id} found ({customer.Name})");
        return customer;
      }
    }
    Log.Info($"{nameof(CustomerRepository)} -> Customer not found");
    return null;
  }
}