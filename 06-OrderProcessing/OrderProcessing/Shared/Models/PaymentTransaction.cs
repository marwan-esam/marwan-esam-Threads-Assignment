namespace OrderProcessing.Shared.Models;

public class PaymentTransaction
{
  public string? TransactionId {get; set;}
  public int OrderId {get; set;}
  public decimal Amount {get; set;}
  public PaymentStatus Status {get; set;}
  public DateTime CreatedAt {get; set;}

  public PaymentTransaction()
  {
    CreatedAt = DateTime.Now;
  }
}