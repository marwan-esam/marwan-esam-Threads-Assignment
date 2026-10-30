using InvoiceExport;

string dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
string invoiceDir = Path.Combine(Directory.GetCurrentDirectory(), "invoices");

await SeedCustomerFiles(dataDir);

var db = new OrderDatabase();
var customers = new CustomerFileStore(dataDir);
var invoices = new InvoiceFileWriter(invoiceDir);
var export = new InvoiceExportService(db, customers, invoices);

Console.WriteLine("Starting invoice export...");
Console.WriteLine($"Customer files: {dataDir}");
Console.WriteLine($"Invoice output: {invoiceDir}");
Console.WriteLine();

await export.ExportAllPending();

static async Task SeedCustomerFiles(string folder)
{
    Directory.CreateDirectory(folder);
    await File.WriteAllTextAsync(Path.Combine(folder, "customer-1.txt"), "1|Mona Hassan|mona@example.com");
    await File.WriteAllTextAsync(Path.Combine(folder, "customer-2.txt"), "2|Ali Kamal|ali@example.com");
    await File.WriteAllTextAsync(Path.Combine(folder, "customer-3.txt"), "3|Sara Nabil|sara@example.com");
    await File.WriteAllTextAsync(Path.Combine(folder, "customer-4.txt"), "4|Omar Fathy|omar@example.com");
}
