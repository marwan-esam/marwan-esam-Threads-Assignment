# Assignment 03 — Answers

Student name: Marwan Mohamed Esam  
Group / ID: 2

## 1. What is System.Threading.Monitor used for?

`System.Threading.Monitor` is a fundamental synchronization primitive in .NET that provides mutual exclusion and inter-thread coordination for blocks of code associated with a specific object lock:
- **Mutual Exclusion:** It guarantees that only one thread at a time can enter a critical section protected by an object's internal synchronization block (monitor). Other threads attempting to enter are blocked until the lock is released.
- **Inter-Thread Signaling & Coordination:** It provides mechanisms for threads to coordinate based on condition states using `Monitor.Wait`, `Monitor.Pulse`, and `Monitor.PulseAll`. A thread can safely release the lock and enter a wait queue until signaled by another thread that modified state.
- **Timed Lock Acquisition:** It supports non-blocking or timed lock attempts via `Monitor.TryEnter`, allowing a thread to proceed with alternative logic if the lock cannot be acquired within a specified timeout.

---

## 2. What does the C# lock statement compile to?

The C# `lock` statement is language-level syntactic sugar that compiles directly into a `try ... finally` block utilizing `Monitor.Enter` and `Monitor.Exit` along with a boolean tracking variable (`lockTaken`):

```csharp
bool lockTaken = false;
try
{
    Monitor.Enter(obj, ref lockTaken);
    // Critical section code
}
finally
{
    if (lockTaken)
    {
        Monitor.Exit(obj);
    }
}
```

### Why `ref lockTaken` is used:
In C# 4.0+, `bool lockTaken = false;` is declared outside the `try` block and passed by reference to `Monitor.Enter(obj, ref lockTaken)`. This atomic check ensures that if an asynchronous exception occurs (such as `OutOfMemoryException` or `ThreadAbortException`) while entering the lock, `lockTaken` is set to `true` if and only if the lock was actually acquired. The `finally` block then uses `if (lockTaken)` to guarantee that `Monitor.Exit(obj)` is called only when the thread successfully owns the lock, preventing `SynchronizationLockException`.

---

## 3. Differences between using lock and calling Monitor APIs directly

1. **Safety and Exception Handling (Ergonomics):**
   - The `lock` statement automatically generates the robust `try ... finally` and `ref lockTaken` pattern, ensuring the lock is released even if an exception is thrown inside the critical section.
   - Calling `Monitor.Enter` manually requires the developer to explicitly write the `try ... finally` block and manage lock release. Forgetting `Monitor.Exit` or failing to place it in a `finally` block leads to permanent deadlocks.

2. **Advanced Capabilities (Timeouts and Signaling):**
   - The `lock` statement only supports unconditional, indefinite blocking.
   - Calling `Monitor` APIs directly unlocks advanced synchronization features:
     - `Monitor.TryEnter(...)`: Enables timeouts and non-blocking lock attempts.
     - `Monitor.Wait(...)`, `Monitor.Pulse(...)`, and `Monitor.PulseAll(...)`: Enables complex condition-based thread coordination (e.g., producer-consumer queues) which cannot be expressed using `lock`.

---

## 4. When to use Monitor.TryEnter, Monitor.Pulse, or Monitor.Wait instead of a plain lock

- **`Monitor.TryEnter`**:
  - **Deadlock Avoidance:** When acquiring multiple resources in arbitrary order, a thread can use `TryEnter` with a timeout; if a lock is contested, it can back off, release any held locks, and retry.
  - **Thread Responsiveness & Load Shedding:** In UI threads or real-time systems where blocking indefinitely would freeze the interface or tie up thread pool threads, `TryEnter` allows bypassing the operation or falling back to alternate handling.
- **`Monitor.Wait` and `Monitor.Pulse` / `Monitor.PulseAll`**:
  - **Condition-Based Synchronization (Producer-Consumer Patterns):** When a thread enters a critical section but cannot proceed because a business condition is unmet (e.g., attempting to dequeue from an empty collection or enqueue to a full bounded buffer).
  - Calling `Monitor.Wait(obj)` temporarily releases the monitor lock and suspends the thread on the object's wait queue.
  - Another thread that subsequently modifies state (e.g., enqueuing an item) calls `Monitor.Pulse(obj)` or `Monitor.PulseAll(obj)` to awaken waiting threads. The awakened threads re-acquire the lock before returning from `Wait`.

---

## 5. Critical section comparison

### Using `lock`:
```csharp
private readonly object _gate = new();
private int _counter;

public void Increment()
{
    lock (_gate)
    {
        _counter++;
    }
}
```

### Using `Monitor.Enter` / `Monitor.Exit`:
```csharp
private readonly object _gate = new();
private int _counter;

public void Increment()
{
    bool lockTaken = false;
    try
    {
        Monitor.Enter(_gate, ref lockTaken);
        _counter++;
    }
    finally
    {
        if (lockTaken)
        {
            Monitor.Exit(_gate);
        }
    }
}
```

---

## Links:

- [System.Threading.Monitor Class — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.threading.monitor)
- [The lock statement — C# Reference — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/lock)
