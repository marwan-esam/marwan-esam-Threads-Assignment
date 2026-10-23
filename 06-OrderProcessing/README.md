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
