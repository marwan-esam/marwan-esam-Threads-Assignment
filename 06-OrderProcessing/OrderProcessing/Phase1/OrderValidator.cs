using OrderProcessing.Shared.Models;

namespace OrderProcessing.Phase1;

public class OrderValidator
{
  private readonly CustomerRepository _customerRepository;
  private readonly ProductRepository _productRepository;
  private readonly CouponRepository _couponRepository;

  public OrderValidator(CustomerRepository customerRepository, ProductRepository productRepository, CouponRepository couponRepository)
  {
    _customerRepository = customerRepository;
    _productRepository = productRepository;
    _couponRepository = couponRepository;
  }

  public async Task<bool> ValidateOrderAsync(Order order, CancellationToken ct = default)
  {
    var customer = await _customerRepository.GetByIdAsync(order.CustomerId, ct); 
    if(customer == null)
    {
      return Reject(order, $"Customer #{order.CustomerId} not found");
    }
    if(! customer.IsActive)
    {
      return Reject(order, "Customer is inactive");
    }
    var productIds = order.Items.Select(item => item.ProductId).ToList();
    var filteredProducts = await _productRepository.GetByIdsAsync(productIds, ct);
    for(int i = 0 ; i < filteredProducts.Count ; i++)
    {
      if(filteredProducts[i] == null)
      {
        return Reject(order, $"Product #{order.Items[i].ProductId} not found");
      }

      if(filteredProducts[i]!.StockQuantity < order.Items[i].Quantity)
      {
        return Reject(order, $"Insufficient stock for '{filteredProducts[i]!.Name}'");
      }
    }
    if(order.CouponCode != null)
    {
      Coupon? coupon = await _couponRepository.GetByCodeAsync(order.CouponCode, ct);
      if(coupon == null)
      {
        return Reject(order, $"Coupon '{order.CouponCode}' does not exist");
      }
      if(coupon.ExpiryDate < DateTime.Today)
      {
        return Reject(order, $"Coupon '{order.CouponCode}' has expired");
      }  
    }
    order.Status = OrderStatus.Validated;
    order.FailureReason = null;
    return true;
  }

  private static bool Reject(Order order, string reason)
  {
    order.Status = OrderStatus.Rejected;
    order.FailureReason = reason;
    return false;
  }
}