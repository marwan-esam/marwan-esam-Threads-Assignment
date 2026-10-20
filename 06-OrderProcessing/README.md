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
