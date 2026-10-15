# Assignment 05 — Answers

Student name: Marwan Mohamed Esam  
Group / ID: 2

## Failures you observed

When running the un-synchronized starter code across multiple runs, three distinct concurrency failures were observed:

1. **`System.ArgumentException: An item with the same key has already been added. Key: koshary-bowl`**:
   - Multiple viewer threads simultaneously executed the check `if (!Views.ContainsKey(product))` and evaluated it to `true`.
   - Both threads subsequently attempted to call `Views.Add(product, 0)`. The first thread succeeded in inserting the key, while the second thread crashed with an unhandled `ArgumentException`.
   - Furthermore, `Dictionary<TKey, TValue>` is completely unsafe for concurrent writes; concurrent modifications can corrupt internal hash buckets and linked entry lists.

2. **Severe Lost Updates on Views**:
   - Even when the application didn't crash from duplicate key insertion, the view count severely diverged from the expected 50,000 views (actual recorded counts ranged between 14,000 and 16,000).
   - This happened because `Views[product] = Views[product] + 1` is a non-atomic three-step read-modify-write cycle. Multiple threads read the same stale count from memory and overwrote each other's updates.

3. **Email Queue Exceptions and Dropped Jobs**:
   - In `EmailWorker`, workers checked `if (EmailJobs.Count > 0)` and then executed `EmailJobs.Dequeue()`.
   - When only one job remained in the queue, two workers concurrently observed `Count > 0` as `true`. Worker 1 dequeued the item, while Worker 2 attempted to call `Dequeue()` on an empty queue, throwing an `InvalidOperationException`.
   - In addition, non-thread-safe operations on `Queue<T>` corrupted internal circular array pointers, causing lost email jobs (e.g., `unique sent=18` instead of 20).

---

## What you changed

1. **Atomic View Tracking with `ConcurrentDictionary`**:
   - Replaced standard `Dictionary<string, int>` with `ConcurrentDictionary<string, int>`.
   - Replaced the vulnerable `ContainsKey` check and indexer assignment with `Views.AddOrUpdate`:
     ```csharp
     Views.AddOrUpdate(key: product, addValue: 1, updateValueFactory: (_, val) => val + 1);
     ```
   - **Why:** `ConcurrentDictionary` utilizes fine-grained bucket-level locks. It guarantees that checking for the key, inserting an initial value of `1`, or executing the increment delegate occurs as a single atomic transaction without race conditions or lost updates.

2. **Thread-Safe Email Queue with `ConcurrentQueue`**:
   - Replaced standard `Queue<string>` with `ConcurrentQueue<string>`.
   - Replaced the check-then-act `Count > 0` and `Dequeue()` pattern with `TryDequeue`:
     ```csharp
     if (EmailJobs.TryDequeue(out string? job))
     {
         SentEmails.Add(job);
         continue;
     }
     ```
   - **Why:** `ConcurrentQueue.TryDequeue` performs a lock-free, atomic compare-and-swap (CAS) operation that tests for availability and extracts the item in one step. It returns `false` cleanly when empty, completely eliminating `InvalidOperationException` and queue pointer corruption.

3. **Thread-Safe Sent Emails Collection**:
   - Replaced `List<string>` with `ConcurrentBag<string> SentEmails` to support concurrent lock-free insertions across background workers, and removed the unused lock object `_sentSync` and dead `try/catch` code.

---

## Collection safety vs business-rule safety

**Making a collection thread-safe does not automatically make the business logic that uses it thread-safe.**

### 1. Operation-Level Safety vs Transaction-Level Safety
- A thread-safe collection (like `ConcurrentDictionary` or `ConcurrentQueue`) guarantees **operation-level thread safety**: individual method calls (such as `TryAdd`, `TryGetValue`, `Enqueue`, or reading `Count`) will maintain internal memory integrity without corrupting internal pointers or throwing collection structure exceptions.
- However, business rules almost always span **multiple sequential operations**. When a developer combines multiple thread-safe calls into a sequence, the overall sequence is **NOT atomic**.

### 2. Concrete Example: Check-Then-Act Race
Consider a stock reservation business rule:
```csharp
// Both methods are thread-safe in isolation, but the business rule is broken!
if (stock["keyboard"] > 0)          // Step 1: Thread-safe read
{
    stock["keyboard"] = stock["keyboard"] - 1; // Step 2: Thread-safe write
}
```
If two customer threads execute this concurrently when `stock["keyboard"] == 1`:
1. Thread A checks `stock["keyboard"] > 0` $\to$ `true`.
2. Thread B checks `stock["keyboard"] > 0` $\to$ `true`.
3. Thread A decrements stock to `0`.
4. Thread B decrements stock to `-1` (oversold!).

Even though `stock` is a thread-safe collection and no dictionary corruption occurred, the **business invariant (stock cannot fall below 0) was violated**.

### 3. Conclusion
To make a business rule safe under concurrency:
- The entire multi-step workflow must either be condensed into an atomic method provided by the concurrent collection (such as `AddOrUpdate`, `GetOrAdd`, or `TryUpdate`).
- Or the entire compound sequence must be synchronized using an external critical section (`lock` or `SemaphoreSlim`) that encompasses both the check and the act.
