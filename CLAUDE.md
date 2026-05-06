# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
dotnet build              # Debug build
dotnet build -c Release   # Release build
dotnet run                # Run the console entry point (Program.cs)
```

No test project exists yet. If one is added, the standard command is `dotnet test`.

## Architecture

This is a .NET 9 project containing a single data structure: `ApexQueue<T>` (namespace `DJ.Codes`, file [ApexQueue.cs](ApexQueue.cs)).

`Program.cs` is a placeholder console entry point and is not the focus of the project.

### ApexQueue\<T\>

A thread-safe, generic priority queue backed by a `ConcurrentDictionary<int, ConcurrentQueue<T>>` — one inner `ConcurrentQueue` per priority level. Items are always dequeued from the highest-priority non-empty queue.

**Key design decisions:**

- `maxPriority` (an `int` field) is updated via `Interlocked.Exchange` rather than a lock, keeping the hot path lock-free.
- When the highest-priority queue drains, `ComputeMaxPriority()` scans all keys with `.ToImmutableList()` to avoid mutation during iteration, then re-sets `maxPriority`.
- `Add` lazily creates a new `ConcurrentQueue` via `GetOrAdd` on first use of a priority level.
- `Take()` returns `default(T)` (i.e. `null` for reference types) when the queue is empty or the priority key is missing — it does not throw.

**Public API surface:**

| Member | Description |
|--------|-------------|
| `Add(T item, int priority)` | Enqueue an item at the given priority level |
| `Take()` | Dequeue from the highest-priority queue; returns `default` if empty |
| `Count()` | Total items across all priority levels |
| `MaxPriority` | Current highest non-empty priority level |
| `GetQueues()` | Snapshot list of all inner queues |
