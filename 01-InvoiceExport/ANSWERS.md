# Assignment 01 — Answers

Student name: Marwan Mohamed Esam  
Group / ID: 2

## Problems with the current design

1. **Synchronous Thread-Blocking I/O**:
   - The original code relies on synchronous blocking calls (`Thread.Sleep`, `File.ReadAllText`, and `File.WriteAllText`).
   - While waiting on simulated database queries and disk operations, the executing thread is held hostage and kept idle instead of returning to the .NET ThreadPool to do useful work. Under high load or in server environments, this pattern leads to thread pool starvation.

2. **Strictly Sequential Pipeline (Lack of Concurrency)**:
   - Orders were processed one after another in a sequential `foreach` loop. Order #2 could not even begin querying the database until Order #1 completed all its database, customer read, and invoice write operations.
   - With an initial fetch time of 800 ms plus ~1,000 ms per order (400 ms DB query + 300 ms customer read + 300 ms invoice write), processing 5 orders took 5.8 seconds.
   - If scaled to 1,000 nightly orders, this sequential design would take over 1,000 seconds (~17 minutes), making it completely unviable for production batch exports.

3. **Independent Units of Work Executed Serially**:
   - Each order export is an independent workflow targeting distinct customer data and writing to a distinct invoice file. There is no shared state or data dependency forcing Order #2 to wait on Order #1.

---

## What you changed and why

1. **Converted to Non-Blocking Asynchronous I/O**:
   - Replaced `Thread.Sleep` with `await Task.Delay` across `OrderDatabase`, `CustomerFileStore`, and `InvoiceFileWriter`.
   - Replaced synchronous file I/O with `await File.ReadAllTextAsync` and `await File.WriteAllTextAsync`.
   - Replaced synchronous method signatures with async `Task` and `Task<T>` equivalents. This frees OS threads during I/O latency.

2. **Concurrent Task Dispatch with Sequential Awaits**:
   - Encapsulated the single order lifecycle (`GetOrderById` -> `ReadProfile` -> `WriteInvoice`) into `ProcessOrderAsync(Order order)`.
   - In `ExportAllPending()`, initiated all order tasks concurrently by iterating through the order list and collecting the started tasks into a `List<Task>`.
   - Iterated over the task list and awaited each task one by one (`foreach (var task in tasks) await task;`). This strictly adheres to the assignment rule prohibiting group-waiting APIs like `Task.WhenAll` while allowing all 5 orders to run concurrently in-flight.

3. **Preserved Business Flow and Integrity**:
   - Maintained the exact sequence for each order: load fresh order from database -> read customer profile from disk -> write invoice to disk.
   - Maintained identical invoice formatting and file naming (`invoice-{order.Id}.txt`).
   - Retained the invariant guard exception `?? throw new InvalidOperationException(...)`.
   - Because each order writes to its own unique file path, each file write is isolated without lock contention.

---

## Console Output Before and After

### Before Fix (Synchronous & Sequential):
```text
Starting invoice export...
Customer files: /home/marwan/simulationeg/lectures/instructor-content/Advanced/assignment2/marwan-esam-Threads-Assignment/01-InvoiceExport/InvoiceExport/bin/Debug/net10.0/Data
Invoice output: /home/marwan/simulationeg/lectures/instructor-content/Advanced/assignment2/marwan-esam-Threads-Assignment/invoices

Wrote /home/marwan/simulationeg/lectures/instructor-content/Advanced/assignment2/marwan-esam-Threads-Assignment/invoices/invoice-1001.txt
Wrote /home/marwan/simulationeg/lectures/instructor-content/Advanced/assignment2/marwan-esam-Threads-Assignment/invoices/invoice-1002.txt
Wrote /home/marwan/simulationeg/lectures/instructor-content/Advanced/assignment2/marwan-esam-Threads-Assignment/invoices/invoice-1003.txt
Wrote /home/marwan/simulationeg/lectures/instructor-content/Advanced/assignment2/marwan-esam-Threads-Assignment/invoices/invoice-1004.txt
Wrote /home/marwan/simulationeg/lectures/instructor-content/Advanced/assignment2/marwan-esam-Threads-Assignment/invoices/invoice-1005.txt
Export finished in 5.8 s
```

### After Fix (Asynchronous & Concurrent):
```text
Starting invoice export...
Customer files: /home/marwan/simulationeg/lectures/instructor-content/Advanced/assignment2/marwan-esam-Threads-Assignment/01-InvoiceExport/InvoiceExport/bin/Debug/net10.0/Data
Invoice output: /home/marwan/simulationeg/lectures/instructor-content/Advanced/assignment2/marwan-esam-Threads-Assignment/invoices

Wrote /home/marwan/simulationeg/lectures/instructor-content/Advanced/assignment2/marwan-esam-Threads-Assignment/invoices/invoice-1004.txt
Wrote /home/marwan/simulationeg/lectures/instructor-content/Advanced/assignment2/marwan-esam-Threads-Assignment/invoices/invoice-1005.txt
Wrote /home/marwan/simulationeg/lectures/instructor-content/Advanced/assignment2/marwan-esam-Threads-Assignment/invoices/invoice-1001.txt
Wrote /home/marwan/simulationeg/lectures/instructor-content/Advanced/assignment2/marwan-esam-Threads-Assignment/invoices/invoice-1002.txt
Wrote /home/marwan/simulationeg/lectures/instructor-content/Advanced/assignment2/marwan-esam-Threads-Assignment/invoices/invoice-1003.txt
Export finished in 1.8 s
```
