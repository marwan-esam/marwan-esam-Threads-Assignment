namespace OrderProcessing.Shared.Models;

public enum OrderStatus
{
  Pending,
  Validated,
  Rejected,
  PaymentFailed,
  Completed,
  Failed
}