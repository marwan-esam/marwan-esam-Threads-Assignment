using OrderProcessing.Shared.Models;

namespace OrderProcessing.Phase1;

public class SyncOrderValidator
{
  private readonly SyncCustomerRepository _customerRepository;
  private readonly SyncProductRepository _productRepository;
  private readonly SyncCouponRepository _couponRepository;

  public SyncOrderValidator(SyncCustomerRepository customerRepository, SyncProductRepository productRepository, SyncCouponRepository couponRepository)
  {
    _customerRepository = customerRepository;
    _productRepository = productRepository;
    _couponRepository = couponRepository;
  }

  public bool ValidateOrder(Order order)
  {
    var customer = _customerRepository.GetById(order.CustomerId); 
    if(customer == null)
    {
      return Reject(order, $"Customer #{order.CustomerId} not found");
    }
    if(! customer.IsActive)
    {
      return Reject(order, "Customer is inactive");
    }
    var productIds = order.Items.Select(item => item.ProductId).ToList();
    var filteredProducts = _productRepository.GetByIds(productIds);
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
      Coupon? coupon = _couponRepository.GetByCode(order.CouponCode);
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