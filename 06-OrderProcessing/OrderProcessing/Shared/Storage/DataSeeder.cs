using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;

namespace OrderProcessing.Shared.Storage;

public static class DataSeeder
{
  public static async Task ResetAsync(JsonFileStore store, CancellationToken ct = default)
  {
    if(Directory.Exists(DataFiles.DataDir)) Directory.Delete(DataFiles.DataDir, recursive: true);
    Directory.CreateDirectory(Path.Combine(DataFiles.DataDir, DataFiles.InvoicesFolder));
    Directory.CreateDirectory(Path.Combine(DataFiles.DataDir, DataFiles.ReportsFolder));
    var customers = CreateCustomers();
    await store.WriteListAsync<Customer>(DataFiles.Customers, customers, ct);
    Log.Info($"[Seeder] {DataFiles.Customers} -> {customers.Count} records");
    var products = CreateProducts();
    await store.WriteListAsync<Product>(DataFiles.Products, products, ct);
    Log.Info($"[Seeder] {DataFiles.Products} -> {products.Count} records");
    var coupons = CreateCoupons();
    await store.WriteListAsync<Coupon>(DataFiles.Coupons, coupons, ct);
    Log.Info($"[Seeder] {DataFiles.Coupons} -> {coupons.Count} records");
    var incomingOrders = CreateIncomingOrders();
    await store.WriteListAsync<Order>(DataFiles.IncomingOrders, incomingOrders, ct);
    Log.Info($"[Seeder] {DataFiles.IncomingOrders} -> {incomingOrders.Count} records");

    
    await store.WriteListAsync<Order>(DataFiles.Orders, [], ct);
    Log.Info($"[Seeder] {DataFiles.Orders} -> 0 records");
    await store.WriteListAsync<PaymentTransaction>(DataFiles.Payments, [], ct);
    Log.Info($"[Seeder] {DataFiles.Payments} -> 0 records");
    await store.WriteListAsync<EmailMessage>(DataFiles.Emails, [], ct);
    Log.Info($"[Seeder] {DataFiles.Emails} -> 0 records");
    await store.WriteListAsync<EmailMessage>(DataFiles.EmailRetry, [], ct);
    Log.Info($"[Seeder] {DataFiles.EmailRetry} -> 0 records");
  }
  private static List<Customer> CreateCustomers() => [
    new() { Id = 1,  Name = "Ahmed Ali",     Email = "ahmed.ali@example.com",     IsActive = true,  LoyaltyTier = LoyaltyTier.Gold },
    new() { Id = 2,  Name = "Sara Mohamed",  Email = "sara.mohamed@example.com",  IsActive = true,  LoyaltyTier = LoyaltyTier.Silver },
    new() { Id = 3,  Name = "Omar Hassan",   Email = "omar.hassan@example.com",   IsActive = true,  LoyaltyTier = LoyaltyTier.Bronze },
    new() { Id = 4,  Name = "Mona Ibrahim",  Email = "mona.ibrahim@example.com",  IsActive = false, LoyaltyTier = LoyaltyTier.Silver },
    new() { Id = 5,  Name = "Youssef Adel",  Email = "youssef.adel@example.com",  IsActive = true,  LoyaltyTier = LoyaltyTier.Gold },
    new() { Id = 6,  Name = "Nour Khaled",   Email = "nour.khaled@example.com",   IsActive = true,  LoyaltyTier = LoyaltyTier.Bronze },
    new() { Id = 7,  Name = "Karim Samir",   Email = "karim.samir@example.com",   IsActive = true,  LoyaltyTier = LoyaltyTier.Silver },
    new() { Id = 8,  Name = "Laila Mahmoud", Email = "laila.mahmoud@example.com", IsActive = true,  LoyaltyTier = LoyaltyTier.Gold },
    new() { Id = 9,  Name = "Hassan Tarek",  Email = "hassan.tarek@example.com",  IsActive = true,  LoyaltyTier = LoyaltyTier.Bronze },
    new() { Id = 10, Name = "Dina Fathy",    Email = "dina.fathy@example.com",    IsActive = true,  LoyaltyTier = LoyaltyTier.Silver }
  ];
  private static List<Product> CreateProducts() => [
    new() { Id = 1, Name = "Laptop",              Price = 1200.00m, StockQuantity = 10, WeightKg = 2.2m },
    new() { Id = 2, Name = "Smartphone",          Price = 800.00m,  StockQuantity = 15, WeightKg = 0.4m },
    new() { Id = 3, Name = "Wireless Mouse",      Price = 25.00m,   StockQuantity = 2,  WeightKg = 0.1m },
    new() { Id = 4, Name = "Mechanical Keyboard", Price = 90.00m,   StockQuantity = 1,  WeightKg = 0.9m },
    new() { Id = 5, Name = "USB-C Hub",           Price = 45.00m,   StockQuantity = 30, WeightKg = 0.2m },
    new() { Id = 6, Name = "Headphones",          Price = 150.00m,  StockQuantity = 12, WeightKg = 0.3m },
    new() { Id = 7, Name = "27\" Monitor",        Price = 350.00m,  StockQuantity = 6,  WeightKg = 5.0m },
    new() { Id = 8, Name = "Webcam",              Price = 70.00m,   StockQuantity = 20, WeightKg = 0.2m }
  ];
  private static List<Coupon> CreateCoupons()
  {
    var today = DateTime.Today;
    return
    [
        new() { Code = "SAVE10",    DiscountPercent = 10, ExpiryDate = today.AddDays(180) },
        new() { Code = "WELCOME15", DiscountPercent = 15, ExpiryDate = today.AddDays(90) },
        new() { Code = "FLASH20",   DiscountPercent = 20, ExpiryDate = today.AddDays(7) },
        new() { Code = "OLD20",     DiscountPercent = 20, ExpiryDate = today.AddDays(-30) }
    ];
  }
  private static List<Order> CreateIncomingOrders()
  {
    var now = DateTime.Now;
    Order Make(int id, int custId, string? coupon, params (int pId, int qty)[] items) => new()
    {
        Id = id,
        CustomerId = custId,
        CouponCode = coupon,
        Status = OrderStatus.Pending,
        CreatedAt = now,
        Items = items.Select(i => new OrderItem { ProductId = i.pId, Quantity = i.qty, UnitPrice = 0m }).ToList()
    };

    return
    [
        Make(1,  1,  "SAVE10",    (1, 1), (3, 1)),
        Make(2,  2,  null,        (2, 1)),
        Make(3,  3,  "WELCOME15", (5, 2), (8, 1)),
        Make(4,  4,  null,        (6, 1)),
        Make(5,  5,  null,        (7, 1)),
        Make(6,  6,  "FLASH20",   (5, 1)),
        Make(7,  7,  null,        (6, 1), (8, 1)),
        Make(8,  8,  "SAVE10",    (1, 1)),
        Make(9,  9,  null,        (3, 5)),
        Make(10, 10, null,        (4, 1)),
        Make(11, 1,  null,        (4, 1)),
        Make(12, 2,  "OLD20",     (2, 1)),
        Make(13, 3,  null,        (6, 2)),
        Make(14, 5,  "WELCOME15", (8, 2), (5, 1)),
        Make(15, 99, null,        (2, 1)),
        Make(16, 6,  null,        (7, 1), (5, 1)),
        Make(17, 7,  "SAVE10",    (2, 1)),
        Make(18, 8,  "FAKE50",    (6, 1)),
        Make(19, 9,  null,        (8, 1)),
        Make(20, 10, "FLASH20",   (1, 1), (6, 1))
    ];
  }

}