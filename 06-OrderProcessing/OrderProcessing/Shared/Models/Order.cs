namespace OrderProcessing.Shared.Models;

public class Order
{
  public int Id {get; set;}
  public int CustomerId {get; set;}
  public string? CouponCode {get; set;}
  public List<OrderItem> Items {get; set;} = new();

  public decimal SubTotal {get; set;}
  public decimal Discount {get; set;}
  public decimal ShippingCost {get; set;}
  public decimal TotalAmount {get; set;}

  public OrderStatus Status {get; set;}
  public DateTime CreatedAt {get; set;}
  public string? FailureReason {get; set;}
  public string? TransactionId {get; set;}

  public Order() {}

}