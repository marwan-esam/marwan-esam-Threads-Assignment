using System.Collections.Concurrent;
using OrderProcessing.Shared.Exceptions;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase3;

public class EmailService : IEmailService
{
  private readonly JsonFileStore _store;
  private readonly IDictionary<int, int>? _failAttempts;
  private readonly ConcurrentDictionary<int, int> _orderAttempts = new();

  public EmailService(JsonFileStore store, IDictionary<int, int>? failAttempts = null)
  {
    _store = store;
    _failAttempts = failAttempts;
  }

  public async Task SendConfirmationAsync(Order order, Customer customer, CancellationToken ct = default)
  {
    Log.Info($"{nameof(EmailService)} -> Rendering template for Order #{order.Id}...");
    await Task.Delay(200, ct);

    Log.Info($"{nameof(EmailService)} -> Connecting to SMTP for Order #{order.Id}...");
    await Task.Delay(300, ct);

    int attempt = _orderAttempts.AddOrUpdate(order.Id, 1, (_, current) => current + 1);

    if (_failAttempts != null && _failAttempts.TryGetValue(order.Id, out int maxFails) && attempt <= maxFails)
    {
      throw new SmtpUnavailableException($"SMTP connection failed for Order #{order.Id} (attempt {attempt})");
    }

    Log.Info($"{nameof(EmailService)} -> Sending email for Order #{order.Id}...");
    await Task.Delay(500, ct);

    var email = new EmailMessage
    {
      Id = Guid.NewGuid(),
      OrderId = order.Id,
      To = customer.Email,
      Subject = $"Order #{order.Id} Confirmation",
      Body = $"Thank you for your order, {customer.Name}! Total: ${order.TotalAmount:F2}",
      SentAt = DateTime.Now,
      Attempts = attempt,
      LastError = null
    };

    await _store.UpdateListAsync<EmailMessage>(
      DataFiles.Emails,
      emails =>
      {
        emails.Add(email);
        return true;
      },
      ct
    );
  }
}
