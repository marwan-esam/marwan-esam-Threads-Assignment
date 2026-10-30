# Assignment 04 — Answers

Student name: Marwan Mohamed Esam  
Group / ID: 2

## Why Expected and Actual disagree

In the original code, multiple threads concurrently execute `Views++`:
- The increment operator (`Views++`) is not an atomic operation. Under the hood, it translates into a three-step read-modify-write cycle at the CPU level:
  1. **Read:** Load the current value of `Views` from memory into a CPU register.
  2. **Modify:** Increment the value inside the register by 1.
  3. **Write:** Write the updated register value back to memory.
- When 8 threads execute this sequence 50,000 times concurrently without synchronization, thread operations interleave in memory. For instance, Thread A and Thread B can read the same value (e.g., `100`), both increment to `101`, and both write `101` back. This leads to massive **lost updates**, where thousands of increments are overwritten. In unfixed runs, the actual count fell drastically short of the expected 400,000 (e.g., recording only ~169,950).

---

## What you changed

Replaced the non-atomic `Views++` with **`Interlocked.Increment(ref Views)`**:

```csharp
namespace MenuViews;

public sealed class MenuViewCounter
{
    public int Views;

    public void RegisterView()
    {
        Interlocked.Increment(ref Views);
    }
}
```

### Why `Interlocked` is superior here:
- **Lock-Free Hardware Atomicity:** `Interlocked.Increment` compiles to a single atomic CPU instruction (such as `LOCK XADD` on x86/x64 architectures). The hardware bus/cache coherency protocol guarantees that the read, increment, and write occur atomically without allowing other cores to access the memory location mid-operation.
- **Zero Thread Blocking & No Context Switch Overhead:** Unlike a heavy `lock` / `Monitor`, `Interlocked` does not suspend or block threads, avoiding OS thread transitions, kernel switches, or thread pool starvation. It provides maximum throughput for updating a single shared numeric variable.

---

## When would you protect the update differently

While `Interlocked` is ideal for single primitive variables, you would choose alternative synchronization mechanisms in the following scenarios:

1. **Multi-Variable Invariants:** When an operation must atomically update multiple dependent variables at the same time (e.g., deducting `Balance` while simultaneously appending to a `TransactionHistory`, or updating `TablesLeft` and `Booked` together). `Interlocked` cannot atomically protect more than one memory location at once; here, a synchronous `lock` is necessary.
2. **Complex Conditional Logic (Check-Then-Act):** When an update depends on multi-step business rules or validations (e.g., `if (balance >= price && user.IsActive) { balance -= price; }`). Since `Interlocked` only performs simple atomic arithmetic or exchanges, complex branched decisions require a `lock`.
3. **Asynchronous Operations / I/O Latency:** When the protected section must perform asynchronous work (e.g., database queries, network requests, disk reads/writes with `await`). A `lock` cannot contain an `await` statement, so an asynchronous synchronization primitive like **`SemaphoreSlim`** must be used.
4. **Thread-Safe Collections:** If tracking multiple counters or key-value view associations dynamically per menu item, high-level concurrent data structures like **`ConcurrentDictionary<TKey, TValue>`** or partitioned accumulators are preferred over individual locks.
