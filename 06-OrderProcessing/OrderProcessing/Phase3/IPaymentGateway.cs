using OrderProcessing.Shared.Models;

namespace OrderProcessing.Phase3;

public interface IPaymentGateway
{
  Task<PaymentResult> ProcessPaymentAsync(int orderId, decimal amount, CancellationToken ct = default);
  Task RefundAsync(string transactionId, CancellationToken ct = default);
}