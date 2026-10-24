using OrderProcessing.Phase1;
using OrderProcessing.Shared.Exceptions;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase4;

public class CustomerDashboardService
{
  private readonly JsonFileStore _store;
  private readonly CustomerRepository _customerRepo;
  private readonly OrderRepository _orderRepo;
  private readonly HashSet<int>? _failPaymentsForCustomerIds;

  public CustomerDashboardService(
    JsonFileStore store,
    CustomerRepository customerRepo,
    OrderRepository orderRepo,
    IEnumerable<int>? failPaymentsForCustomerIds = null)
  {
    _store = store;
    _customerRepo = customerRepo;
    _orderRepo = orderRepo;
    _failPaymentsForCustomerIds = failPaymentsForCustomerIds?.ToHashSet();
  }

  public async Task<CustomerDashboard> LoadAsync(int customerId, CancellationToken ct = default)
  {
    var customerTask = LoadCustomerProfileAsync(customerId, ct);
    var ordersTask = LoadCustomerOrdersAsync(customerId, ct);
    var paymentsTask = LoadCustomerPaymentsAsync(customerId, ct);
    var emailsTask = LoadCustomerEmailsAsync(customerId, ct);

    var allLoadsTask = Task.WhenAll(customerTask, ordersTask, paymentsTask, emailsTask);

    try
    {
      await allLoadsTask;
    }
    catch (Exception)
    {
      if (allLoadsTask.Exception != null)
      {
        throw allLoadsTask.Exception;
      }
      throw;
    }

    return new CustomerDashboard
    {
      Customer = await customerTask,
      Orders = await ordersTask,
      Payments = await paymentsTask,
      Emails = await emailsTask
    };
  }

  private async Task<Customer> LoadCustomerProfileAsync(int customerId, CancellationToken ct)
  {
    var customer = await _customerRepo.GetByIdAsync(customerId, ct);
    if (customer == null)
    {
      throw new CustomerNotFoundException($"Customer #{customerId} not found");
    }
    return customer;
  }

  private async Task<List<Order>> LoadCustomerOrdersAsync(int customerId, CancellationToken ct)
  {
    var orders = await _orderRepo.GetAllAsync(ct);
    return orders.Where(o => o.CustomerId == customerId).ToList();
  }

  private async Task<List<PaymentTransaction>> LoadCustomerPaymentsAsync(int customerId, CancellationToken ct)
  {
    Log.Info($"{nameof(CustomerDashboardService)} -> Opening payments connection...");
    await Task.Delay(200, ct);
    Log.Info($"{nameof(CustomerDashboardService)} -> Executing payments query for Customer #{customerId}...");
    await Task.Delay(Random.Shared.Next(300, 601), ct);

    if (_failPaymentsForCustomerIds?.Contains(customerId) == true)
    {
      throw new TimeoutException("Payments store timeout");
    }

    var orders = await _orderRepo.GetAllAsync(ct);
    var customerOrderIds = orders.Where(o => o.CustomerId == customerId).Select(o => o.Id).ToHashSet();
    var payments = await _store.ReadListAsync<PaymentTransaction>(DataFiles.Payments, ct);
    return payments.Where(p => customerOrderIds.Contains(p.OrderId)).ToList();
  }

  private async Task<List<EmailMessage>> LoadCustomerEmailsAsync(int customerId, CancellationToken ct)
  {
    Log.Info($"{nameof(CustomerDashboardService)} -> Opening emails connection...");
    await Task.Delay(200, ct);
    Log.Info($"{nameof(CustomerDashboardService)} -> Executing emails query for Customer #{customerId}...");
    await Task.Delay(Random.Shared.Next(300, 601), ct);

    var emails = await _store.ReadListAsync<EmailMessage>(DataFiles.Emails, ct);
    var customer = await _customerRepo.GetByIdAsync(customerId, ct);
    if (customer == null)
    {
      return [];
    }
    return emails.Where(e => string.Equals(e.To, customer.Email, StringComparison.OrdinalIgnoreCase)).ToList();
  }
}
