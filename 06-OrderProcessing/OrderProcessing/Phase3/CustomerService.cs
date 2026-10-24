using System.Formats.Asn1;
using OrderProcessing.Phase1;
using OrderProcessing.Shared.Exceptions;
using OrderProcessing.Shared.Models;

namespace OrderProcessing.Phase3;

public class CustomerService
{
  private readonly CustomerRepository _customerRepo;
  public CustomerService(CustomerRepository customerRepo)
  {
    _customerRepo = customerRepo;
  }

  public async Task<Customer> GetCustomerAsync(int id, CancellationToken ct = default)
  {
    var customer = await _customerRepo.GetByIdAsync(id, ct);
    if(customer is null) throw new CustomerNotFoundException($"Customer #{id} not found");
    return customer;
  }
}