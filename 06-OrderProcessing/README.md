# 06 — E-Commerce Order Processing System (File Storage Edition)

Student name: Marwan Mohamed Esam  
Group / ID: 2

---

## 1. How to Run & Project Structure

### Running the Application
From `06-OrderProcessing/OrderProcessing`:
```bash
dotnet run -- phase1
dotnet run -- phase2
dotnet run -- phase3
dotnet run -- phase4
dotnet run -- all
dotnet run -- phase3 --cancel-after 4000
```
Running `dotnet run` without arguments launches the interactive numbered menu.

### Data Directory Location
At runtime, all JSON collections, generated invoices, and reports are stored in:
`Path.Combine(AppContext.BaseDirectory, "Data")` (e.g., `bin/Debug/net10.0/Data/`).

---

## 2. Storage Design (`JsonFileStore` & `DataSeeder`)

### JSON Files in `Data/`
| File / Folder | Purpose |
| :--- | :--- |
| `customers.json` | 10 seeded customer profiles (read-only during run) |
| `products.json` | 8 seeded products (`StockQuantity` updated on reserve/release) |
| `coupons.json` | 4 seeded discount coupons (read-only) |
| `incoming_orders.json` | 20 seeded incoming orders (read-only) |
| `orders.json` | Processed orders saved/upserted here (`[]` at start) |
| `payments.json` | `PaymentTransaction` records (`[]` at start) |
| `emails.json` | Sent confirmation emails outbox (`[]` at start) |
| `email_retry.json` | Emails that failed all retry attempts in Phase 3 (`[]` at start) |
| `invoices/` | Generated invoice text files (`invoice_{orderId}.txt`, Phase 2) |
| `reports/` | Generated JSON summary reports (`report_{yyyyMMdd_HHmmss}.json`, Phase 2) |

### Concurrency & Crash-Safety Design in `JsonFileStore`
1. **Per-File Asynchronous Mutual Exclusion:** `JsonFileStore` maintains a `static readonly ConcurrentDictionary<string, SemaphoreSlim>` keyed by file name. Operations on distinct files (`customers.json` vs. `orders.json`) execute in parallel without contention, while concurrent reads/writes targeting the same file are serialized safely.
2. **Atomic Read-Modify-Write (`UpdateListAsync` / `UpdateList`):** Acquires the per-file `SemaphoreSlim` once, reads the current collection via `ReadCoreAsync`, executes the in-memory mutation delegate, and writes the updated collection via `WriteCoreAsync` before releasing the semaphore. This prevents lost updates and non-reentrant semaphore self-deadlocks.
3. **Crash-Safe Atomic Temp-Swap:** Writes serialize UTF-8 JSON (with `WriteIndented = true`, `JsonNamingPolicy.CamelCase`, and `JsonStringEnumConverter`) to a temporary file (`{fileName}.{Guid}.tmp`) and atomically replace the destination via `File.Move(tempPath, targetPath, overwrite: true)`. A crash mid-write never leaves a half-written or corrupted target file.
4. **Batch Product Lookup (`GetByIdsAsync`):** Added to `ProductRepository` to avoid N+1 query overhead when validating multi-item orders.

---

## 3. Performance Measurements

| Phase / Benchmark | Mode | Elapsed Time | Notes |
| :--- | :--- | :--- | :--- |
| **Phase 1: Order Validation (20 orders)** | Sync (Sequential) | `30.3 s` | `Thread.Sleep` + sync file reads, one order after another |
| **Phase 1: Order Validation (20 orders)** | Async (Sequential) | `30.3 s` | `await Task.Delay` + async file reads, awaited one after another |
| **Phase 2: Order #1 Pricing** | Sequential | `3.7 s` | `CalculateSubtotalAsync` $\to$ `GetDiscountAsync` $\to$ `CalculateShippingCostAsync` |
| **Phase 2: Order #1 Pricing** | Concurrent | `2.3 s` | `CalculateSubtotalAsync`, then `GetDiscountAsync` & `CalculateShippingCostAsync` concurrently |
| **Phase 2: Full Pipeline (20 orders)** | Concurrent | `13.2 s` | Concurrent validation + stock reservation + pricing + CPU invoice generation + saving (`2.3x` faster than Phase 1 validation alone) |
| **Phase 3: Full Checkout (20 orders)** | Concurrent (10 tasks) | `10.8 s` | Full checkout pipeline with payment gateway, refunds, compensation, email retries |
| **Phase 3: Comparison (Orders #1-#10)** | Sync (10 threads) | `9.0 s` | Dedicated OS threads with `Thread.Sleep`, `Join()` |
| **Phase 3: Comparison (Orders #1-#10)** | Async (10 tasks) | `9.8 s` | Non-blocking async tasks with `Task.Delay`, `await` |
| **Phase 4: Task 10.1 (Live Checkout, 20 orders)** | `Task.WhenEach` | `12.4 s` | Live streamed progress in completion order |
| **Phase 4: Task 10.2 (Customer Dashboard)** | `Task.WhenAll` | `1.14 s` | 4 concurrent loads (profile, orders, payments, emails) |
| **Phase 4: Task 10.3 (Fastest Shipping Quote)** | `Task.WhenAny` | `460 - 1200 ms` | Fastest winning quote with losers cancelled |
| **Phase 4: Task 10.4 A (Fraud Scoring, 10k)** | Sequential | `5.58 s` | CPU-bound loop on single thread |
| **Phase 4: Task 10.4 A (Fraud Scoring, 10k)** | `Parallel.For` | `0.41 s` | `13.7x` speed-up using `Environment.ProcessorCount` cores |
| **Phase 4: Task 10.4 B (Email Re-send, 30 emails)** | `Parallel.ForEachAsync` | `8.02 s` | Throttled with `MaxDegreeOfParallelism = 4` (Peak = exactly 4) |

---

## 4. Phase 1 — Validation Summary & Questions

### Validation Summary Output
```text
===== Validation Summary =====
Total Orders:    20
Validated:       15
Rejected:        5
  - Order #4:  Customer is inactive
  - Order #9:  Insufficient stock for 'Wireless Mouse'
  - Order #12: Coupon 'OLD20' has expired
  - Order #15: Customer #99 not found
  - Order #18: Coupon 'FAKE50' does not exist
Sync Time:  30.3 seconds
Async Time: 30.3 seconds
orders.json now contains 20 records.
```

### Phase 1 Questions & Answers

#### 1. Why are the sync and async times almost the same when orders are awaited one after another?
`async`/`await` by itself does not make a sequential loop execute in parallel—it only makes each wait non-blocking by returning the thread to the `ThreadPool`. Because the `foreach` loop awaits `ValidateOrderAsync(order)` before starting the next iteration, Order $N+1$ still cannot begin until Order $N$ finishes all of its simulated database delays. Consequently, total wall-clock latency is the sum of all 20 orders in both runs (`30.3 s` vs. `30.3 s`).

#### 2. Is the thread ID before an `await` always the same as after it? Why? Use your own log lines as evidence.
No, the thread ID is not necessarily the same after an `await`. When an `await` yields on an incomplete `Task`, the current thread returns to the `.NET ThreadPool`. Once the awaited timer or I/O completes, in a Console application (where `SynchronizationContext.Current` is `null`), the state-machine continuation is scheduled back onto the `ThreadPool`, and whichever `ThreadPool` thread is available picks up execution:
```text
[04:50:12.104] [Thread 1] CustomerRepository -> Opening connection...
[04:50:12.308] [Thread 4] CustomerRepository -> Executing query for Customer #1...
[04:50:12.742] [Thread 7] CustomerRepository -> Customer #1 found (Ahmed Ali)
```

#### 3. What does the thread do while `Task.Delay()` is waiting? And while `Thread.Sleep()` is waiting?
- **During `Task.Delay()`:** No thread is blocked waiting. `Task.Delay` registers an asynchronous system timer and returns an incomplete `Task`. The calling thread is immediately released back to the `ThreadPool` where it can execute other queued work items (or remain idle in the pool ready for work).
- **During `Thread.Sleep()`:** The OS/ThreadPool thread is completely blocked and suspended for the duration of the sleep. It holds onto its ~1 MB call stack and cannot execute any other work until the sleep timer expires.

#### 4. Why should a repository method return `Task<T>` instead of `T`?
`Task<T>` represents an ongoing asynchronous operation (a future/promise) managed by the compiler-generated async state machine. Returning `Task<T>` allows the caller to:
1. Yield the thread via `await` without blocking while I/O completes.
2. Start multiple independent operations concurrently before awaiting their results.
3. Observe completion status (`Status`, `IsCompleted`, `IsFaulted`, `IsCanceled`) and receive either the eventual `T` result or any propagated `Exception`.

#### 5. What is the difference between `File.ReadAllText` and `File.ReadAllTextAsync`? Does the async version occupy a thread while the disk is reading?
- `File.ReadAllText` performs a synchronous OS read syscall that blocks the calling CPU thread until the storage device finishes transferring bytes into memory.
- `File.ReadAllTextAsync` uses OS-level asynchronous I/O (I/O Completion Ports on Windows / kernel async I/O and Direct Memory Access on Linux). **No thread is occupied while the disk is reading**—the storage controller transfers data via DMA and raises an interrupt upon completion, signaling the .NET runtime to schedule the continuation on a `ThreadPool` thread.

---

## 5. Phase 2 — Concurrent Processing Summary & Questions

### Phase 2 Console Output Summary
```text
===== Step 1A: Race Condition Demo =====
Did order 10 accquire product 4 ? yes
Did order 11 accquire product 4 ? yes
Product 4 remaining stock: 0
Did order 10 accquire product 4 ? yes
Did order 11 accquire product 4 ? no
Product 4 remaining stock: 0

===== Step 1B: Pricing Comparison (Order #1) =====
Order #1 Pricing -> SubTotal: $1225.00, Discount: $245.00, Shipping: $0.00, Total: $980.00
Sequential Time: 3.7 seconds
Concurrent Time: 2.3 seconds

===== Step 3: Starting Concurrent Processing of Validated Orders =====
Immediate Status of task[0] (Order #1): WaitingForActivation

===== Step 6: Final Task Statuses =====
  - Task[0] (Order #1): RanToCompletion
  - Task[1] (Order #2): RanToCompletion
  - Task[2] (Order #3): RanToCompletion
  - Task[3] (Order #5): RanToCompletion
  - Task[4] (Order #6): RanToCompletion
  - Task[5] (Order #7): Faulted
  - Task[6] (Order #8): RanToCompletion
  - Task[7] (Order #10): RanToCompletion
  - Task[8] (Order #11): RanToCompletion
  - Task[9] (Order #13): RanToCompletion
  - Task[10] (Order #14): RanToCompletion
  - Task[11] (Order #16): RanToCompletion
  - Task[12] (Order #17): RanToCompletion
  - Task[13] (Order #19): RanToCompletion
  - Task[14] (Order #20): RanToCompletion
Total Phase 2 Concurrent Processing Time: 13.2 seconds

===== Step 7: Verifying orders.json =====
orders.json now contains 20 records:
  - Completed: 13
  - Rejected:  6
  - Failed:    1
    * Order #4 [Rejected]: Customer is inactive
    * Order #7 [Failed]: Order #7: Shipping provider unavailable
    * Order #9 [Rejected]: Insufficient stock for 'Wireless Mouse'
    * Order #11 [Rejected]: Insufficient stock for 'Mechanical Keyboard'
    * Order #12 [Rejected]: Coupon 'OLD20' has expired
    * Order #15 [Rejected]: Customer #99 not found
    * Order #18 [Rejected]: Coupon 'FAKE50' does not exist

===== Task 8.6: Report Generation =====
Generating report...
--- Order Report ---
Total Orders: 20
  - Completed: 13
  - Rejected: 6
  - Failed: 1
  - Payment Failed: 0
Total Revenue: $5970.95
Average Order Value: $459.30
Top Selling Product: USB-C Hub (5 units)
```

### Phase 2 Questions & Answers

#### 1. What is the difference between starting a task and awaiting it?
- **Starting a task** (calling an `async` method without `await` and storing the returned `Task` in a variable or list) begins executing the method synchronously on the current thread until it hits its first incomplete `await` point, at which point control immediately returns to the caller with an in-progress `Task` (`WaitingForActivation`). The caller is free to continue executing subsequent lines (such as starting additional tasks concurrently).
- **Awaiting a task** (`await task`) non-blockingly suspends the caller's execution if the task is not yet complete, returning the current thread to the `ThreadPool` as idle/available for other work. Once the task finishes, a `ThreadPool` thread resumes the caller after the `await`, unwrapping the result or rethrowing any exception.

#### 2. Does every `Task` create a new thread? Does `Task.Delay()` use a thread while waiting?
No, creating a `Task` does **not** create a new thread.
- I/O-bound tasks and timer-based tasks like `Task.Delay()` use **zero threads** while waiting (`Task.Delay()` registers a callback on the runtime's timer queue).
- Even CPU-bound tasks queued via `Task.Run()` do not create a dedicated OS thread per task; they are queued onto existing worker threads managed by the `.NET ThreadPool`.

#### 3. What happens if you call `.Result` or `.Wait()` on an unfinished task instead of `await`? Why is it dangerous?
Calling `.Result` or `.Wait()` synchronously blocks the calling thread until the task completes (implemented in the unused `Phase2Runner.BlockingWaitDemo()` method). Even if the underlying operation is pure I/O, a thread remains tied up doing nothing. This is dangerous because:
1. **Thread-Pool Starvation:** Under high concurrency, blocked `ThreadPool` threads cannot process other work items or continuations, degrading responsiveness and forcing slow thread injection.
2. **Deadlocks:** In environments with a single-threaded `SynchronizationContext` (or when all `ThreadPool` threads are blocked waiting on tasks whose continuations require a `ThreadPool` thread), blocking with `.Result` / `.Wait()` causes a deadlock.
3. **Exception Wrapping:** `.Result` and `.Wait()` wrap thrown exceptions inside an `AggregateException` instead of throwing the original exception directly like `await`.

#### 4. What happens to an exception thrown inside a task that is never awaited?
When an unhandled exception is thrown inside an `async` method, the state machine catches it and transitions the returned `Task` to `TaskStatus.Faulted`, storing the exception in `task.Exception`. If that `Task` is **never awaited** (or its `Exception` property is never inspected), the exception is silently swallowed from the caller's perspective and will not be caught by surrounding `try/catch` blocks (only raising `TaskScheduler.UnobservedTaskException` when the `Task` is eventually finalized by the GC).

#### 5. When should you use `Task.Run()`, and when should you avoid it? Why does `Task.Run()` fit `InvoiceGenerator.GenerateAsync` but not the database calls?
- **When to use `Task.Run()`:** Use it to offload **CPU-bound** work (heavy calculations, loops, hashing, PDF rendering) onto a `ThreadPool` worker thread so the calling thread is not blocked and multiple CPU-bound tasks can run in parallel across CPU cores. This fits `InvoiceGenerator.GenerateAsync(Order)` because computing the 20,000,000-iteration hash is pure CPU work.
- **When to avoid `Task.Run()`:** Avoid wrapping **I/O-bound** operations (database queries, file reads/writes, external API calls) in `Task.Run()`. Async I/O methods already yield their thread at `await` without occupying any thread during the I/O wait; wrapping them in `Task.Run()` only adds unnecessary thread-switching overhead.

#### 6. What is the difference between `Task` and `Task<T>`?
- `Task` represents an asynchronous operation that does not return a value (the asynchronous equivalent of `void`).
- `Task<T>` (which inherits from `Task`) represents an asynchronous operation that produces a result of type `T` when awaited.

#### 7. Why can a `lock` block not contain an `await`?
A C# `lock` (`Monitor.Enter` / `Monitor.Exit`) enforces **thread affinity**: the exact same OS/managed thread that acquired the lock must be the thread that releases it. When an `await` yields on an incomplete task, the current thread returns to the `ThreadPool`, and when the awaited operation completes, a **different** `ThreadPool` thread may resume the continuation after the `await`. Attempting to release the `Monitor` from a different thread throws `SynchronizationLockException` (and while paused at `await`, the original thread could also re-enter the re-entrant lock on behalf of another work item). Therefore, the C# compiler forbids `await` inside `lock` (`CS1996`), and we use `SemaphoreSlim.WaitAsync()` instead, which is thread-agnostic.

#### 8. Why did the unprotected stock reservation oversell, and how did you prevent it?
In `ReserveStockUnsafeAsync`, reading `products.json`, checking `if (product.StockQuantity >= qty)`, and writing the decremented stock were separated by `await Task.Delay(...)` points with no lock spanning the entire transaction. When Orders #10 and #11 ran concurrently for Product #4 (`StockQuantity = 1`), Order #11 read `products.json` while Order #10 was suspended at `await Task.Delay(...)`. Both tasks observed `StockQuantity == 1`, both passed the check, both wrote `1 - 1 = 0`, and both returned `true`—overselling the last keyboard (a classic check-then-act race condition and lost update).

We prevented it in `ReserveStockAsync` and `ReleaseStockAsync` by guarding the entire check + delay + write sequence with a per-product `SemaphoreSlim(1, 1)` (`_productLocks.GetOrAdd(productId, _ => new SemaphoreSlim(1, 1))`) using `await productLock.WaitAsync(ct)` in a `try / finally` block alongside `JsonFileStore.UpdateListAsync`.

---

## 6. Phase 3 — Full Checkout Workflow & Questions

### Phase 3 Checkout Summary
```text
==========================================================
  PHASE 3 — FULL CHECKOUT WORKFLOW
==========================================================
Loaded 20 incoming orders. Starting concurrent processing...
All 20 orders processed in 10.8 seconds.

===== Final Orders Table =====
Order    | Status          | Total        | Reason
---------------------------------------------------------------------------
#1       | Completed       | $980.00      | -
#2       | Completed       | $767.30      | -
#3       | Completed       | $146.70      | -
#4       | Rejected        | -            | Customer is inactive
#5       | PaymentFailed   | -            | Payment gateway timeout (3s)
#6       | Completed       | $42.90       | -
#7       | Completed       | $218.00      | -
#8       | PaymentFailed   | -            | Card declined by issuer
#9       | Rejected        | -            | Insufficient stock for 'Wireless Mouse'
#10      | Completed       | $93.80       | -
#11      | Rejected        | -            | Insufficient stock for 'Mechanical Keyboard'
#12      | Rejected        | -            | Coupon 'OLD20' has expired
#13      | Failed          | -            | Database save failed after payment; payment TXN-9B4B7FDC8872 refunded
#14      | Completed       | $149.45      | -
#15      | Rejected        | -            | Customer #99 not found
#16      | Completed       | $413.40      | -
#17      | Completed       | $687.30      | -
#18      | Rejected        | -            | Coupon 'FAKE50' does not exist
#19      | Completed       | $76.90       | -
#20      | Completed       | $1012.50     | -

Summary: 11 Completed, 6 Rejected, 2 PaymentFailed, 1 Failed
```

### Final File State Verification (Matches Task 9.4)
- **`orders.json`**: 20 records (11 Completed, 6 Rejected [#4, #9, #11, #12, #15, #18], 2 PaymentFailed [#5, #8], 1 Failed [#13]).
- **`payments.json`**: 13 records (11 Captured, 1 Declined [#8], 1 Refunded [#13]). Order #5 timed out before capture, so no payment record was saved.
- **`emails.json`**: 10 records (all Completed orders except #17). Order #3 succeeded on attempt 3 (`Attempts = 3`).
- **`email_retry.json`**: 1 record for Order #17 (`Attempts = 4`, `LastError = "SMTP connection failed for Order #17 (attempt 4)"`).
- **`products.json`**: Stock was decremented only by Completed orders. Mechanical Keyboard stock is `0` (Order #10 succeeded, Order #11 was Rejected).

---

### Phase 3 Questions & Answers

#### Task 9.6: Sync vs. Async Comparison
```text
Sync Version (10 threads):  9.0 s | Process Threads: 15 | ThreadPool.ThreadCount: 5
Async Version (10 tasks):   9.8 s | Process Threads: 15 | ThreadPool.ThreadCount: 5
```

##### 1. Does async make one order's workflow faster?
No. An individual order's sequential workflow takes approximately the same wall-clock time in both versions (around 5–7 seconds of simulated database, payment, and email latencies). Asynchrony does not make an I/O operation faster; it simply frees up the calling thread while the I/O operation is in flight.

##### 2. Why does async scale better for many orders?
In the synchronous model, each concurrent order requires a dedicated OS thread blocked on `Thread.Sleep` or synchronous I/O. Each thread consumes approximately 1 MB of stack memory, plus OS kernel resources and context-switching overhead.
In contrast, the asynchronous model releases the thread back to the `ThreadPool` at each `await` point. As a result, hundreds or thousands of orders in flight can be serviced by a tiny pool of worker threads without allocating thousands of operating system threads.

##### 3. What happens to the sync version with 1,000 orders?
Attempting to create and run 1,000 active operating system threads (`new Thread(...)`) will:
1. Consume ~1 GB of virtual memory just for thread stacks.
2. Trigger significant OS context switching and scheduling thrashing, severely degrading CPU cache efficiency.
3. Potentially cause `OutOfMemoryException` or thread creation failures if OS thread limits or memory limits are exceeded.

##### 4. Why is async essential for apps that call databases, files, and APIs?
Databases, disk files, and remote web APIs are external I/O operations where the CPU is idle >99% of the time waiting for bytes to travel over network interfaces or storage controllers. Async I/O frees worker threads during these waits, enabling backend services to achieve high throughput and scalability with minimal hardware resources.

---

#### Task 9.7: Fire-and-Forget Investigation
```csharp
public async Task ProcessOrderAsync(Order order, Customer customer)
{
    await _orderRepository.SaveAsync(order);
    _emailService.SendConfirmationAsync(order, customer); // not awaited
    Console.WriteLine("Order processing finished");
}
```

##### 1. What is the problem in this code?
`_emailService.SendConfirmationAsync(order, customer)` returns a `Task` that is never awaited. The method prints `"Order processing finished"` and returns to the caller while the email operation is still running in the background.

##### 2. What happens if the email is still being sent when the method returns, or when the console app exits?
If the console application exits while the background task is running, the process terminates abruptly, killing any in-flight thread-pool work and leaving the email unsent (or half-written).

##### 3. What happens if the email operation throws an exception?
Because the task is not awaited, the exception is stored in the unobserved `Task` object. The caller cannot catch it via a surrounding `try/catch` block. When the unobserved task is later collected by the Garbage Collector, it triggers `TaskScheduler.UnobservedTaskException`.

##### 4. How was it fixed?
Add `await` before the async call:
```csharp
await _emailService.SendConfirmationAsync(order, customer);
```

##### 5. When is `await` necessary, when is fire-and-forget acceptable, and why is `async void` dangerous?
- **When `await` is necessary:** For any asynchronous operation where the caller depends on the outcome, needs to guarantee completion before proceeding, or needs to catch and handle exceptions.
- **When fire-and-forget is acceptable:** Almost never in reliable business workflows. The rare exception is non-critical background telemetry/logging where failure does not impact the transaction and unhandled exceptions are caught internally inside the fire-and-forget task.
- **Why `async void` is dangerous:** `async void` methods cannot be awaited and caller code cannot catch exceptions thrown from them. Any unhandled exception in an `async void` method crashes the process (terminates the CLR app domain).
- **Why `async void` is acceptable for event handlers:** In UI frameworks (WPF, WinForms, MAUI) and button click handlers, event handler signatures are defined as `void EventHandler(object? sender, EventArgs e)`. An `async void` handler allows asynchronous code to run from a synchronous event delegate signature.

---

## 7. Phase 4 — Scale-Up & Modern Concurrency Questions

### Phase 4 Console Output Summary
```text
==========================================================
  PHASE 4 — SCALE-UP
==========================================================

----------------------------------------------------------
  TASK 10.1 — LIVE CHECKOUT PROGRESS
----------------------------------------------------------
[  1/20] Order #4  -> Rejected (Customer is inactive) after 0.5 s
[  2/20] Order #15 -> Rejected (Customer #99 not found) after 0.5 s
[  3/20] Order #9  -> Rejected (Insufficient stock for 'Wireless Mouse') after 2.6 s
[  4/20] Order #10 -> Rejected (Insufficient stock for 'Mechanical Keyboard') after 3.3 s
[  5/20] Order #18 -> Rejected (Coupon 'FAKE50' does not exist) after 3.6 s
[  6/20] Order #12 -> Rejected (Coupon 'OLD20' has expired) after 3.7 s
[  7/20] Order #8  -> PaymentFailed (Card declined by issuer) after 6.4 s
[  8/20] Order #6  -> Completed after 7.3 s
[  9/20] Order #19 -> Completed after 7.5 s
[ 10/20] Order #11 -> Completed after 7.7 s
[ 11/20] Order #16 -> Completed after 9.1 s
[ 12/20] Order #2  -> Completed after 9.3 s
[ 13/20] Order #7  -> Completed after 9.5 s
[ 14/20] Order #1  -> Completed after 9.7 s
[ 15/20] Order #5  -> PaymentFailed (Payment gateway timeout (3s)) after 9.9 s
[ 16/20] Order #14 -> Completed after 9.9 s
[ 17/20] Order #20 -> Completed after 11.0 s
[ 18/20] Order #17 -> Completed after 11.9 s
[ 19/20] Order #13 -> Failed (Database save failed after payment; payment TXN-FDE9D1292DEB refunded) after 12.0 s
[ 20/20] Order #3  -> Completed after 12.4 s

All orders completed in 12.4 s.
Status counts: 11 Completed, 6 Rejected, 2 PaymentFailed, 1 Failed

----------------------------------------------------------
  TASK 10.2 — CUSTOMER DASHBOARD
----------------------------------------------------------
Customer #3: Omar Hassan — 2 orders (#3 Completed, #13 Failed), 2 payments (#13 Refunded, #3 Captured), 1 email (#3) in 1.14 s

Customer #99 failures reported (2 failures):
  - Customer #99 not found
  - Payments store timeout

----------------------------------------------------------
  TASK 10.3 — FASTEST SHIPPING QUOTE
----------------------------------------------------------
Order #1: Winner=CairoExpress | Price=$0.00 | Time=460 ms
Order #2: Winner=Flat rate    | Price=$25.00 | Time=1200 ms
Order #3: Winner=NileCourier  | Price=$11.77 | Time=1045 ms
Order #4: Winner=NileCourier  | Price=$7.81 | Time=959 ms
Order #5: Winner=FastShip     | Price=$16.50 | Time=1156 ms

Testing all carriers failing on Order #6:
Order #6 successfully caught expected exception: All carriers failed

----------------------------------------------------------
  TASK 10.4 — FRAUD SCORING & EMAIL RE-SEND
----------------------------------------------------------
[Part A: Fraud Scoring]
  - Flagged count (Seq):      1005 (First 5: 4, 8, 12, 29, 33)
  - Flagged count (Parallel): 1005 (First 5: 4, 8, 12, 29, 33)
  - Sequential time:          5.581 s
  - Parallel time:            0.408 s
  - Speed-up:                 13.68x
  - Distinct threads used:    16
  - Environment.ProcessorCount: 16

[Part B: Email Re-send (30 emails with MaxDegreeOfParallelism = 4)]
  - Total time:        8.02 s (Expected ~8 s)
  - Peak concurrency:  4 (Must be exactly 4)
```

### Phase 4 Questions & Answers

#### 1. For each of Tasks 10.1–10.4: which tool did you use, and why can't "start, then await one by one" meet the requirement?
- **Task 10.1 (Live progress):** Used `Task.WhenEach`. "Start then await one by one" awaits tasks in the order they were started (Order #1, Order #2, etc.), which blocks reporting on fast orders that finish earlier (e.g. Order #4, #15) until slower preceding orders finish. `Task.WhenEach` streams tasks as an `IAsyncEnumerable<Task>` as soon as each completes without maintaining a manual pending list.
- **Task 10.2 (Dashboard):** Used `Task.WhenAll`. Awaiting one by one would only catch the exception of the first failed task that is awaited and immediately abort, hiding failures in subsequently awaited tasks. `Task.WhenAll` awaits all tasks concurrently and aggregates all failures into an `AggregateException`.
- **Task 10.3 (Fastest quote):** Used `Task.WhenAny`. Awaiting one by one blocks on the first carrier even if another carrier finishes much earlier. `Task.WhenAny` returns as soon as the fastest carrier task completes, allowing us to pick the winner and immediately cancel slower losing carriers.
- **Task 10.4 A (Fraud scoring):** Used `Parallel.For`. "Start then await one by one" queues tasks or runs sequentially on a single thread. `Parallel.For` partitions CPU-bound iterations across hardware threads up to `Environment.ProcessorCount`.
- **Task 10.4 B (Throttled email):** Used `Parallel.ForEachAsync` with `MaxDegreeOfParallelism = 4`. Awaiting one by one is sequential (concurrency 1), while starting all 30 at once would run with concurrency 30. `Parallel.ForEachAsync` enforces a strict concurrency ceiling of 4.

#### 2. In 10.2, when several loads fail, how do you get every exception? What does a plain await give you?
When using `await Task.WhenAll(task1, task2, ...)`, the `await` keyword unwraps the `AggregateException` and re-throws only the **first** inner exception encountered.
To retrieve **every exception**, we inspect the `allLoadsTask.Exception` property (or catch `AggregateException` by awaiting the task directly without unwrapping, or using `try { await allTasks; } catch { throw allTasks.Exception; }`), which contains the full `InnerExceptions` collection of all faulted tasks.

#### 3. In 10.3, what happens to the losing carriers if you never cancel them?
If losing carriers are never cancelled, their ongoing HTTP requests/delays and background worker threads continue running in the background until they complete or time out. This wastes bandwidth, socket connections, memory, and thread-pool resources, which can lead to connection exhaustion under high load.

#### 4. Why does 10.4 A get faster with more CPU cores, while 10.4 B does not depend on the number of cores?
- **10.4 A is CPU-bound:** Computing 100,000 hash iterations per order requires active CPU cycles. Splitting the 10,000 orders across 16 CPU cores allows 16 chunks of computations to run truly in parallel, providing near-linear speedup (`13.7x`).
- **10.4 B is I/O-bound:** Sending emails involves non-blocking network delays (`await Task.Delay` / socket transmission). The rate-limiting factor is not CPU computation, but the configured concurrency ceiling (`MaxDegreeOfParallelism = 4`) and external network latency. Adding more CPU cores does not increase the speed of 4 concurrent network I/O operations.

#### 5. What is the difference between parallel work (10.4 A) and concurrent asynchronous work (10.1)?
- **Parallel work (10.4 A):** Multiple threads executing CPU instructions on separate physical CPU cores at the exact same physical instant.
- **Concurrent asynchronous work (10.1):** Multiple logical tasks in flight at the same time, interleaving execution on one or few threads when work is ready, and releasing threads while waiting on asynchronous I/O.

#### 6. Show a cancelled run (`--cancel-after`) and explain what happened to the orders, the stock and the payments.
Running:
```bash
dotnet run --project OrderProcessing -- phase4 --cancel-after 4000
```
Output:
```text
[Cancellation] --cancel-after set to 4000 ms
[  1/20] Order #4  -> Rejected (Customer is inactive) after 0.5 s
[  2/20] Order #15 -> Rejected (Customer #99 not found) after 0.5 s
[  3/20] Order #9  -> Rejected (Insufficient stock for 'Wireless Mouse') after 3.2 s
[  4/20] Order #3  -> Pending after 3.4 s
[  5/20] Order #16 -> Pending after 3.4 s
...
Run cancelled — operation aborted via cancellation token.
```
**What happened:**
- Orders that completed before 4,000 ms retained their final status (`Rejected` for #4, #9, #15).
- Orders in flight when the cancellation token was triggered caught `OperationCanceledException`, released any reserved inventory back to `products.json`, refunded any captured payments, and saved the orders with `Status = OrderStatus.Pending` and `FailureReason = "Cancelled"`. No order was marked `Failed` or `PaymentFailed` due to cancellation. All JSON files remained in a valid, consistent state.
