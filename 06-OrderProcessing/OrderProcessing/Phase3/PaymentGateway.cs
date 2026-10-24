using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase3;

public class PaymentGateway : IPaymentGateway
{
  private readonly JsonFileStore _store;
  private readonly HashSet<int>? _timeoutOrderIds;
  private readonly HashSet<int>? _declineOrderIds;

  public PaymentGateway(JsonFileStore store, IEnumerable<int>? timeoutOrderIds = null, IEnumerable<int>? declineOrderIds = null)
  {
    _store = store;
    _timeoutOrderIds = timeoutOrderIds?.ToHashSet();
    _declineOrderIds = declineOrderIds?.ToHashSet();
  }

  public async Task<PaymentResult> ProcessPaymentAsync(int orderId, decimal amount, CancellationToken ct = default)
  {
    Log.Info($"{nameof(PaymentGateway)} -> Authorizing payment for Order #{orderId}...");
    await Task.Delay(800, ct);

    Log.Info($"{nameof(PaymentGateway)} -> Fraud check for Order #{orderId}...");
    await Task.Delay(500, ct);

    if (_declineOrderIds?.Contains(orderId) == true)
    {
      await _store.UpdateListAsync<PaymentTransaction>(
        DataFiles.Payments,
        payments =>
        {
          payments.Add(new PaymentTransaction()
          {
            OrderId = orderId,
            Amount = amount,
            Status = PaymentStatus.Declined,
            TransactionId = null
          });
          return true;
        },
        ct
      );
      return new PaymentResult()
      {
        Success = false,
        Status = PaymentStatus.Declined,
        Message = "Card declined by issuer"
      };
    }

    if (_timeoutOrderIds?.Contains(orderId) == true)
    {
      await Task.Delay(5000, ct);
      return new PaymentResult()
      {
        Success = false,
        Status = PaymentStatus.Declined,
        Message = "Payment gateway timeout (3s)"
      };
    }

    await Task.Delay(700, ct);

    var transactionId = $"TXN-{Guid.NewGuid().ToString("N")[..12].ToUpper()}";
    await _store.UpdateListAsync<PaymentTransaction>(
      DataFiles.Payments,
      payments =>
      {
        payments.Add(new PaymentTransaction()
        {
          OrderId = orderId,
          Amount = amount,
          Status = PaymentStatus.Captured,
          TransactionId = transactionId
        });
        return true;
      },
      ct
    );

    return new PaymentResult()
    {
      Success = true,
      TransactionId = transactionId,
      Status = PaymentStatus.Captured,
      Message = "Payment processed successfully"
    };
  }

  public async Task RefundAsync(string transactionId, CancellationToken ct = default)
  {
    Log.Info($"{nameof(PaymentGateway)} -> Refunding transaction {transactionId}: DNS/handshake...");
    await Task.Delay(150, ct);
    Log.Info($"{nameof(PaymentGateway)} -> Refunding transaction {transactionId}: Sending request...");
    await Task.Delay(300, ct);
    Log.Info($"{nameof(PaymentGateway)} -> Refunding transaction {transactionId}: Waiting for response...");
    await Task.Delay(Random.Shared.Next(500, 1501), ct);

    await _store.UpdateListAsync<PaymentTransaction>(
      DataFiles.Payments,
      payments =>
      {
        var txn = payments.FirstOrDefault(p => p.TransactionId == transactionId);
        if (txn is not null)
        {
          txn.Status = PaymentStatus.Refunded;
          return true;
        }
        return false;
      },
      ct
    );
  }
}
