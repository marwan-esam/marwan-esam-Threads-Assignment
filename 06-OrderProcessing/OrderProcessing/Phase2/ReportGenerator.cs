using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrderProcessing.Phase1;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase2;

public class ReportGenerator
{
  private readonly JsonFileStore _store;
  private readonly OrderRepository _orderRepo;
  private readonly ProductRepository _productRepo;
  private static readonly SemaphoreSlim _fileLock = new(1, 1);
  public ReportGenerator(JsonFileStore store)
  {
    _store = store;
    _orderRepo = new(_store);
    _productRepo = new(_store);
  }

  public async Task<Report> GenerateAsync(CancellationToken ct = default)
  {
    var ordersTask = _orderRepo.GetAllAsync(ct);
    var productsTask = _productRepo.GetAllAsync(ct);

    var orders = await ordersTask;
    var products = await productsTask;

    Report report = new()
    {
      TotalOrders = orders.Count,
      CompletedCount = orders.Count(order => order.Status == OrderStatus.Completed),
      RejectedCount = orders.Count(order => order.Status == OrderStatus.Rejected),
      FailedCount = orders.Count(order => order.Status == OrderStatus.Failed),
      PaymentFailedCount = orders.Count(order => order.Status == OrderStatus.PaymentFailed)
    };

    report.TotalRevenue = PricingService.RoundMoneyValue(
    orders
    .Where(order => order.Status == OrderStatus.Completed)
    .Sum(order => order.TotalAmount)
    );

    report.AverageOrderValue = PricingService.RoundMoneyValue(
      report.CompletedCount > 0 ? report.TotalRevenue / report.CompletedCount : 0m
    );

    var topSellingProduct = orders
    .Where(order => order.Status == OrderStatus.Completed)
    .SelectMany(order => order.Items)
    .GroupBy(item => item.ProductId)
    .Select(group => new
    {
      ProductId = group.Key,
      TotalQuantity = group.Sum(item => item.Quantity)
    })
    .OrderByDescending(p => p.TotalQuantity)
    .ThenBy(p => p.ProductId)
    .FirstOrDefault();

    if(topSellingProduct is not null)
    {
      var productName = products.First(product => product.Id == topSellingProduct.ProductId).Name;
      report.TopSellingProduct = productName;
      report.TopSellingQuantity = topSellingProduct.TotalQuantity;
    }
    var reportDirectoryPath = Path.Combine(DataFiles.DataDir, DataFiles.ReportsFolder);
    Directory.CreateDirectory(reportDirectoryPath);
    var reportFilePath = Path.Combine(reportDirectoryPath, $"report_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}.json");
    var json = JsonSerializer.Serialize<Report>(report, JsonFileStore.Options);
    await _fileLock.WaitAsync(ct);
    try
    {
      await File.WriteAllTextAsync(reportFilePath, json, Encoding.UTF8, ct);
    } finally
    {
      _fileLock.Release();
    }
    return report;
  }

  public string PrintReport(Report report)
  {
    var reportText = 
    $"--- Order Report ---\n" +
    $"Total Orders: {report.TotalOrders}\n" +
    $"  - Completed: {report.CompletedCount}\n" +
    $"  - Rejected: {report.RejectedCount}\n" +
    $"  - Failed: {report.FailedCount}\n" +
    $"  - Payment Failed: {report.PaymentFailedCount}\n" +
    $"Total Revenue: ${report.TotalRevenue:F2}\n" +
    $"Average Order Value: ${report.AverageOrderValue:F2}\n" +
    $"Top Selling Product: {report.TopSellingProduct} ({report.TopSellingQuantity} units)";
    return reportText;
  }
}