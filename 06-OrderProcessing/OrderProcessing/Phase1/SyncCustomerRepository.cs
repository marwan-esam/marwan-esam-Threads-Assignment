using OrderProcessing.Shared.Storage;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Logging;

namespace OrderProcessing.Phase1;

public class SyncCustomerRepository
{
  private readonly JsonFileStore _store;

  public SyncCustomerRepository(JsonFileStore jsonFileStore)
  {
    _store = jsonFileStore;
  }

  public Customer? GetById(int id)
  {
    Log.Info($"{nameof(CustomerRepository)} -> Opening connection...");
    Thread.Sleep(200);
    Log.Info($"{nameof(CustomerRepository)} -> Executing query for Customer #{id}...");
    Thread.Sleep(Random.Shared.Next(300, 601));
    var customersList = _store.ReadList<Customer>(DataFiles.Customers);
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