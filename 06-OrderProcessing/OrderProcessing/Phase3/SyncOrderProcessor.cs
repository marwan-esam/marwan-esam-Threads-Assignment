using System.Diagnostics;
using OrderProcessing.Phase1;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase3;

public class SyncOrderProcessor
{
  private readonly JsonFileStore _store;

  public SyncOrderProcessor(JsonFileStore store)
  {
    _store = store;
  }

  public bool ProcessOrder(Order order)
  {
    Thread.Sleep(200);
    Thread.Sleep(450);
    var customers = _store.ReadList<Customer>(DataFiles.Customers);
    var customer = customers.FirstOrDefault(c => c.Id == order.CustomerId);
    if (customer == null || !customer.IsActive)
    {
      order.Status = OrderStatus.Rejected;
      order.FailureReason = customer == null ? $"Customer #{order.CustomerId} not found" : "Customer is inactive";
      SaveOrderSync(order);
      return false;
    }

    Thread.Sleep(200);
    Thread.Sleep(100);
    Thread.Sleep(400);
    bool stockOk = _store.UpdateList<Product>(
      DataFiles.Products,
      products =>
      {
        foreach (var item in order.Items)
        {
          var prod = products.FirstOrDefault(p => p.Id == item.ProductId);
          if (prod == null || prod.StockQuantity < item.Quantity)
          {
            return false;
          }
          prod.StockQuantity -= item.Quantity;
        }
        return true;
      }
    );
    Thread.Sleep(200);

    if (!stockOk)
    {
      order.Status = OrderStatus.Rejected;
      order.FailureReason = "Insufficient stock";
      SaveOrderSync(order);
      return false;
    }

    Thread.Sleep(200);
    Thread.Sleep(450);
    var productsList = _store.ReadList<Product>(DataFiles.Products);
    decimal subtotal = 0m;
    foreach (var item in order.Items)
    {
      var p = productsList.First(x => x.Id == item.ProductId);
      item.UnitPrice = p.Price;
      subtotal += item.UnitPrice * item.Quantity;
    }
    order.SubTotal = Math.Round(subtotal, 2, MidpointRounding.AwayFromZero);

    Thread.Sleep(150);
    Thread.Sleep(300);
    Thread.Sleep(1000);
    decimal discount = 0m;
    if (customer.LoyaltyTier == LoyaltyTier.Silver) discount = 5m;
    if (customer.LoyaltyTier == LoyaltyTier.Gold) discount = 10m;
    order.Discount = Math.Round(order.SubTotal * discount / 100m, 2, MidpointRounding.AwayFromZero);

    Thread.Sleep(150);
    Thread.Sleep(300);
    Thread.Sleep(1000);
    order.ShippingCost = order.SubTotal >= 1000m ? 0m : 15m;
    order.TotalAmount = Math.Round(order.SubTotal - order.Discount + order.ShippingCost, 2, MidpointRounding.AwayFromZero);

    Thread.Sleep(800);
    Thread.Sleep(500);
    Thread.Sleep(700);
    string txnId = $"TXN-{Guid.NewGuid().ToString("N")[..12].ToUpper()}";
    _store.UpdateList<PaymentTransaction>(
      DataFiles.Payments,
      list =>
      {
        list.Add(new PaymentTransaction { OrderId = order.Id, Amount = order.TotalAmount, Status = PaymentStatus.Captured, TransactionId = txnId });
        return true;
      }
    );

    order.Status = OrderStatus.Completed;
    order.TransactionId = txnId;
    SaveOrderSync(order);

    Thread.Sleep(200);
    Thread.Sleep(300);
    Thread.Sleep(500);
    _store.UpdateList<EmailMessage>(
      DataFiles.Emails,
      list =>
      {
        list.Add(new EmailMessage { Id = Guid.NewGuid(), OrderId = order.Id, To = customer.Email, Subject = "Confirmation", SentAt = DateTime.Now, Attempts = 1 });
        return true;
      }
    );

    return true;
  }

  private void SaveOrderSync(Order order)
  {
    Thread.Sleep(200);
    Thread.Sleep(100);
    Thread.Sleep(400);
    _store.UpdateList<Order>(
      DataFiles.Orders,
      orders =>
      {
        int idx = orders.FindIndex(o => o.Id == order.Id);
        if (idx != -1) orders[idx] = order;
        else orders.Add(order);
        return true;
      }
    );
    Thread.Sleep(200);
  }
}
