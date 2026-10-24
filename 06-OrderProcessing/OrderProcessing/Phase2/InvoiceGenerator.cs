using System.Text;
using OrderProcessing.Phase1;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase2;

public class InvoiceGenerator
{
  private readonly JsonFileStore _store;
  private readonly ProductRepository _productRepo;

  public InvoiceGenerator(JsonFileStore? store = null)
  {
    _store = store ?? new();
    _productRepo = new(_store);
  }
  public async Task GenerateAsync(Order order, CancellationToken ct = default)
  {
    var invoiceText = await Task.Run(() =>
    {
      Log.Info($"{nameof(InvoiceGenerator)} -> Computing invoice hash for Order #{order.Id}...");
      ulong hashAcc = (ulong)order.Id;
      for(int i = 0 ; i < 20_000_000 ; i++)
      {
        hashAcc = (hashAcc * 31) ^ (ulong) i;
      }
      StringBuilder content = new StringBuilder();
      content.AppendLine($"Order ID: {order.Id}");
      content.AppendLine($"Customer ID: {order.CustomerId}");
      content.AppendLine("Order items:");
      foreach(var item in order.Items)
      {
        content.AppendLine($"   - Product #{item.ProductId} x{item.Quantity} @ ${item.UnitPrice:F2}");
      }
      content.AppendLine($"Subtotal: {order.SubTotal}");
      content.AppendLine($"Discount: {order.Discount}");
      content.AppendLine($"Shipping Cost: {order.ShippingCost}");
      content.AppendLine($"Total Amount: {order.TotalAmount}");
      content.AppendLine($"Checksum: {hashAcc}");
      return content.ToString();
    }, ct);

    var invoicesDirectoryPath = Path.Combine(DataFiles.DataDir, DataFiles.InvoicesFolder);
    Directory.CreateDirectory(invoicesDirectoryPath);
    var filePath = Path.Combine(invoicesDirectoryPath, $"invoice_{order.Id}.txt");
    Log.Info($"{nameof(InvoiceGenerator)} -> Writing to file invoice_{order.Id},.txt...");
    await Task.Delay(200, ct);
    await File.WriteAllTextAsync(filePath, invoiceText, ct);
  }
}