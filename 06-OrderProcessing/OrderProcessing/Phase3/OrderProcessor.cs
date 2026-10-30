using System.Diagnostics;
using OrderProcessing.Phase1;
using OrderProcessing.Shared.Exceptions;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase3;

public class OrderProcessor
{
  private readonly CustomerService _customerService;
  private readonly InventoryService _inventoryService;
  private readonly PricingService _pricingService;
  private readonly IPaymentGateway _paymentGateway;
  private readonly OrderRepository _orderRepository;
  private readonly IEmailService _emailService;
  private readonly JsonFileStore _store;

  public OrderProcessor(
    CustomerService customerService,
    InventoryService inventoryService,
    PricingService pricingService,
    IPaymentGateway paymentGateway,
    OrderRepository orderRepository,
    IEmailService emailService,
    JsonFileStore? store = null)
  {
    _customerService = customerService;
    _inventoryService = inventoryService;
    _pricingService = pricingService;
    _paymentGateway = paymentGateway;
    _orderRepository = orderRepository;
    _emailService = emailService;
    _store = store ?? new();
  }

  public async Task<bool> ProcessOrderAsync(Order order, CancellationToken ct = default)
  {
    Customer customer;

    var stepSw = Stopwatch.StartNew();
    try
    {
      customer = await _customerService.GetCustomerAsync(order.CustomerId, ct);
      if (!customer.IsActive)
      {
        order.Status = OrderStatus.Rejected;
        order.FailureReason = "Customer is inactive";
        await _orderRepository.SaveAsync(order, ct);
        Log.Info($"Order #{order.Id} -> Step 1/6 Customer ... REJECTED (Customer is inactive) in {stepSw.ElapsedMilliseconds} ms");
        return false;
      }
      Log.Info($"Order #{order.Id} -> Step 1/6 Customer ... OK in {stepSw.ElapsedMilliseconds} ms");
    }
    catch (CustomerNotFoundException ex)
    {
      order.Status = OrderStatus.Rejected;
      order.FailureReason = ex.Message;
      await _orderRepository.SaveAsync(order, ct);
      Log.Info($"Order #{order.Id} -> Step 1/6 Customer ... REJECTED ({ex.Message}) in {stepSw.ElapsedMilliseconds} ms");
      return false;
    }

    stepSw.Restart();
    bool stockReserved = await _inventoryService.ReserveStockAsync(order, ct);
    if (!stockReserved)
    {
      order.Status = OrderStatus.Rejected;
      await _orderRepository.SaveAsync(order, ct);
      Log.Info($"Order #{order.Id} -> Step 2/6 Stock ... REJECTED ({order.FailureReason}) in {stepSw.ElapsedMilliseconds} ms");
      return false;
    }
    Log.Info($"Order #{order.Id} -> Step 2/6 Stock ... OK in {stepSw.ElapsedMilliseconds} ms");

    stepSw.Restart();
    try
    {
      await _pricingService.CalculateTotalAsync(order, ct);
      Log.Info($"Order #{order.Id} -> Step 3/6 Pricing ... OK (${order.TotalAmount:F2}) in {stepSw.ElapsedMilliseconds} ms");
    }
    catch (InvalidCouponException ex)
    {
      await _inventoryService.ReleaseStockAsync(order, ct);
      order.Status = OrderStatus.Rejected;
      order.FailureReason = ex.Message;
      await _orderRepository.SaveAsync(order, ct);
      Log.Info($"Order #{order.Id} -> Step 3/6 Pricing ... REJECTED ({ex.Message}) in {stepSw.ElapsedMilliseconds} ms");
      return false;
    }

    stepSw.Restart();
    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));

    PaymentResult paymentResult;
    try
    {
      paymentResult = await _paymentGateway.ProcessPaymentAsync(order.Id, order.TotalAmount, timeoutCts.Token);
      if (!paymentResult.Success)
      {
        await _inventoryService.ReleaseStockAsync(order, ct);
        order.Status = OrderStatus.PaymentFailed;
        order.FailureReason = paymentResult.Message ?? "Card declined by issuer";
        await _orderRepository.SaveAsync(order, ct);
        Log.Info($"Order #{order.Id} -> Step 4/6 Payment ... DECLINED ({order.FailureReason}) in {stepSw.ElapsedMilliseconds} ms, released");
        return false;
      }
    }
    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
    {
      await _inventoryService.ReleaseStockAsync(order, ct);
      order.Status = OrderStatus.PaymentFailed;
      order.FailureReason = "Payment gateway timeout (3s)";
      await _orderRepository.SaveAsync(order, ct);
      Log.Info($"Order #{order.Id} -> Step 4/6 Payment ... TIMEOUT after {stepSw.ElapsedMilliseconds} ms, released");
      return false;
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
      await _inventoryService.ReleaseStockAsync(order, CancellationToken.None);
      order.Status = OrderStatus.Pending;
      order.FailureReason = "Cancelled";
      await _orderRepository.SaveAsync(order, CancellationToken.None);
      return false;
    }

    Log.Info($"Order #{order.Id} -> Step 4/6 Payment ... OK ({paymentResult.TransactionId}) in {stepSw.ElapsedMilliseconds} ms");

    stepSw.Restart();
    order.TransactionId = paymentResult.TransactionId;
    order.Status = OrderStatus.Completed;
    order.FailureReason = null;

    try
    {
      await _orderRepository.SaveAsync(order, ct);
      Log.Success($"Order #{order.Id} -> Step 5/6 Save ... OK in {stepSw.ElapsedMilliseconds} ms");
    }
    catch (DatabaseWriteException)
    {
      if (order.TransactionId != null)
      {
        await _paymentGateway.RefundAsync(order.TransactionId, ct);
      }
      await _inventoryService.ReleaseStockAsync(order, ct);

      order.Status = OrderStatus.Failed;
      order.FailureReason = $"Database save failed after payment; payment {order.TransactionId} refunded";
      await _orderRepository.SaveAsync(order, ct);
      Log.Error($"Order #{order.Id} -> Step 5/6 Save ... FAILED (Compensated and refunded) in {stepSw.ElapsedMilliseconds} ms");
      return false;
    }

    stepSw.Restart();
    await SendConfirmationWithRetryAsync(order, customer, ct);
    Log.Info($"Order #{order.Id} -> Step 6/6 Email finished in {stepSw.ElapsedMilliseconds} ms");

    return true;
  }

  private async Task SendConfirmationWithRetryAsync(Order order, Customer customer, CancellationToken ct)
  {
    int[] delays = [500, 1000, 2000];
    string? lastError = null;

    for (int attempt = 1; attempt <= 4; attempt++)
    {
      try
      {
        await _emailService.SendConfirmationAsync(order, customer, ct);
        return;
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        lastError = ex.Message;
        Log.Info($"Email attempt {attempt} for Order #{order.Id} failed: {ex.Message}");

        if (attempt < 4)
        {
          await Task.Delay(delays[attempt - 1], ct);
        }
      }
    }

    // 4 attempts failed: record in email_retry.json
    Log.Info($"Email for Order #{order.Id} queued for retry later");
    var retryEmail = new EmailMessage
    {
      Id = Guid.NewGuid(),
      OrderId = order.Id,
      To = customer.Email,
      Subject = $"Order #{order.Id} Confirmation",
      Body = $"Thank you for your order, {customer.Name}! Total: ${order.TotalAmount:F2}",
      SentAt = DateTime.MinValue,
      Attempts = 4,
      LastError = lastError
    };

    await _store.UpdateListAsync<EmailMessage>(
      DataFiles.EmailRetry,
      list =>
      {
        list.Add(retryEmail);
        return true;
      },
      ct
    );
  }
}
