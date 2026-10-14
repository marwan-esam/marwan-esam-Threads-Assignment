# Assignment 02 — Answers

## What is wrong

When running the un-fixed starter code across multiple runs, two severe concurrency defects occur:

### 1. Baseline Observations (R1)
In the original implementation, 40 concurrent threads attempt to book from an initial stock of 5 tables:
- **Run 1:** `Booked=37, TablesLeft=-33` (Sum = 4)
- **Run 2:** `Booked=40, TablesLeft=-33` (Sum = 7)
- **Run 3:** `Booked=38, TablesLeft=-35` (Sum = 3)

Instead of cleanly stopping after 5 bookings, almost all 40 customers book successfully, driving `TablesLeft` into negative numbers. Furthermore, the sum of `Booked + TablesLeft` does not even consistently add up to the original 5 tables.

### 2. Root Cause Analysis (R2)

#### A. Check-Then-Act Race Condition (Overselling / Overbooking)
- In `FridayNightBooking.Book()`:
  ```csharp
  if (TablesLeft <= 0)
      return;

  Thread.Sleep(20);
  TablesLeft--;
  Booked++;
  ```
- Before any thread has a chance to decrement `TablesLeft`, multiple threads concurrently evaluate the guard condition `if (TablesLeft <= 0)`. Because `TablesLeft` is initially 5, all threads observe that condition as `false` and enter the booking body.
- The 20 ms delay widens this race window significantly. While the first threads are sleeping, dozens of other threads pass the check. Once they wake up, every single thread proceeds to decrement `TablesLeft` and increment `Booked`, resulting in massive overbooking and negative table balances.

#### B. Data Race on Non-Atomic Updates (Lost Updates)
- In C#, the decrement and increment operators (`TablesLeft--` and `Booked++`) are **not atomic**. They translate into three discrete CPU operations:
  1. Read the current value from memory into a CPU register.
  2. Decrement or increment the register value.
  3. Write the updated register value back to memory.
- When multiple threads execute these instructions concurrently without synchronization, they overwrite each other's work (lost updates). For example:
  - Thread A reads `TablesLeft = 1`.
  - Before Thread A writes back `0`, Thread B also reads `TablesLeft = 1`.
  - Both threads decrement to `0` and write `0` back to memory. Two bookings occurred, but `TablesLeft` was only decremented once.
- The same race condition affects `Booked++`, explaining why `Booked + TablesLeft` diverges from the invariant total of 5.

---

## How you fixed it

### 1. Mutual Exclusion via Asynchronous Critical Section
- Introduced a `SemaphoreSlim _lock = new(1, 1)` to serve as a mutual-exclusion gate around the booking operation.
- Wrapped the validation and state mutation inside `await _lock.WaitAsync()` and a `try ... finally` block ensuring `_lock.Release()` is always executed:
  ```csharp
  public async Task BookAsync(string customerName)
  {
      await _lock.WaitAsync();
      try
      {
          if (TablesLeft <= 0)
              return;

          await Task.Delay(20);
          TablesLeft--;
          Booked++;
          Console.WriteLine($"{customerName} booked a table. Left={TablesLeft}");
      }
      finally
      {
          _lock.Release();
      }
  }
  ```

### 2. Atomicity & Correctness Guarantees
- Because the semaphore allows a maximum concurrency of 1, only **one task at a time** can execute within the critical section.
- The condition check (`TablesLeft <= 0`) and the state mutations (`TablesLeft--`, `Booked++`) now form an **atomic transaction**. No other customer task can read stale state or interleave write operations.
- Once the 5th table is booked, `TablesLeft` becomes 0. The remaining 35 queued tasks sequentially enter the critical section, immediately hit `if (TablesLeft <= 0) return;`, and exit cleanly without mutating state.

### 3. Asynchronous Non-Blocking Design (`SemaphoreSlim` vs `lock`)
- In modern C# applications, blocking OS threads via `Thread.Sleep` causes thread pool starvation under high concurrency.
- A standard C# `lock` block cannot contain an `await` statement. By choosing `SemaphoreSlim(1, 1)` and `await Task.Delay(20)`, the waiting task yields its thread back to the .NET ThreadPool during the 20 ms delay.
- In `Program.cs`, all 40 tasks are initiated concurrently and then awaited one by one, adhering to the assignment's rule prohibiting group-waiting APIs (`Task.WhenAll`).

### 4. Verified End Output
Every run now deterministically outputs:
```text
Customer-01 booked a table. Left=4
Customer-02 booked a table. Left=3
Customer-03 booked a table. Left=2
Customer-04 booked a table. Left=1
Customer-05 booked a table. Left=0

Booked=5, TablesLeft=0
```
