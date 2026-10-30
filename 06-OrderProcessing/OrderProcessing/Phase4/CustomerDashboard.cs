using OrderProcessing.Shared.Models;

namespace OrderProcessing.Phase4;

public class CustomerDashboard
{
  public Customer? Customer { get; set; }
  public List<Order> Orders { get; set; } = [];
  public List<PaymentTransaction> Payments { get; set; } = [];
  public List<EmailMessage> Emails { get; set; } = [];
}
