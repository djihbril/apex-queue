# ApexQueue&lt;T&gt;

![CI](https://github.com/djihbril/apex-queue/actions/workflows/ci.yml/badge.svg)

A thread-safe, lock-free generic priority queue for .NET 9. Items are dequeued from the highest-priority non-empty bucket in First In, First Out (FIFO) order within each priority level.

> This project was developed iteratively with [Claude Code](https://claude.ai/code) as a showcase of Application Programming Interface (API) design, concurrent correctness, and AI-assisted engineering. The full development story is in [Development journey](#development-journey) below.

---

## Table of contents

- [Features](#features)
- [Requirements](#requirements)
- [Setup](#setup)
- [Build](#build)
- [Run](#run)
- [Test](#test)
- [API reference](#api-reference)
- [Concurrency model](#concurrency-model)
- [Development journey](#development-journey)

---

## Features

- **Lock-free hot path** — `maxPriority` is tracked with a compare-and-swap (CAS) spin loop (`Interlocked.CompareExchange`), not a global lock
- **Configurable empty-queue expiry** — retain drained queues for recycled-priority workloads, remove immediately for non-recycling workloads, or use a sliding expiry window
- **Safe inspection** — `GetQueues()` returns per-priority item snapshots (`T[]`), never live internal references
- **FIFO within priority** — items at the same priority level dequeue in insertion order
- **21 tests** across normal use, edge cases, concurrency, and API-safety scenarios

---

## Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9)

---

## Setup

```bash
git clone https://github.com/djihbril/apex-queue.git
cd apex-queue
dotnet restore
```

---

## Build

```bash
dotnet build            # Debug
dotnet build -c Release # Release
```

---

## Run

```bash
dotnet run --project src
```

---

## Test

```bash
dotnet test
```

---

## API reference

| Member | Description |
|--------|-------------|
| `ApexQueue(int emptyQueueExpiryMs = -1)` | `-1` never removes empty queues (default), `0` removes immediately on drain, `> 0` sliding expiry window in milliseconds (ms) |
| `Add(T item, int priority)` | Enqueue an item at the given priority level |
| `Take()` | Dequeue from the highest-priority non-empty queue; returns `default` if empty |
| `Count()` | Total item count across all priority levels |
| `MaxPriority` | Highest non-empty priority currently tracked |
| `GetQueues()` | `IReadOnlyList<T[]>` — one array snapshot per priority level |

---

## Concurrency model

`ApexQueue<T>` is backed by a `ConcurrentDictionary<int, ConcurrentQueue<T>>` — one inner queue per priority level. The `maxPriority` field is kept consistent lock-free using a CAS spin loop:

```csharp
int snap = Volatile.Read(ref maxPriority);
while (priority > snap)
{
    int old = Interlocked.CompareExchange(ref maxPriority, priority, snap);
    if (old == snap) break;
    snap = old; // use the CAS return value as the next comparand — not a re-read
}
```

The key detail: on a failed CAS, the returned value (`old`) is used directly as the next comparand rather than re-reading from memory. Re-reading opens a window where another write can slip between the failed CAS and the next snapshot — the root cause of a 50% failure rate in the concurrency test under the initial do-while formulation.

`Count()` sums inner-queue counts non-atomically — an item moving between queues during iteration can be counted twice. This is an accepted trade-off of the lock-free design since items will not be expected to change queues, and is documented in `BugSurfacingTests`.

---

## Development journey

This project started as a working but imperfect `ApexQueue<T>` implementation. The commits from `f2656d5` to `25b039c` document an iterative improvement process driven entirely through [Claude Code](https://claude.ai/code), Anthropic's AI coding assistant. Each phase below maps to one or more commits.

### Phase 1 — Static analysis

Before writing a single test, Claude Code performed a full static analysis of the implementation and identified five distinct issues:

| # | Issue | Category |
|---|-------|----------|
| 1 | `Math.Max(priority, maxPriority)` read `maxPriority` non-atomically before `Interlocked.Exchange` — a lower-priority thread could overwrite a higher value written by a concurrent thread | Thread safety (Time-of-Check to Time-of-Use (TOCTOU) race) |
| 2 | Drained `ConcurrentQueue<T>` instances were never removed from the dictionary, causing unbounded growth and degrading `ComputeMaxPriority()` scans over time | Memory leak |
| 3 | `GetOrAdd(priority, new ConcurrentQueue<T>())` allocated a new queue object on every `Add()` call, even when the key already existed and the instance was immediately discarded | Wasted allocation |
| 4 | `Count()` sums inner-queue sizes one-by-one; an item moved between queues mid-iteration is counted twice | Non-atomic aggregate (accepted trade-off) |
| 5 | `GetQueues()` returned live `ConcurrentQueue<T>` references, allowing callers to enqueue directly and bypass `Add()`, silently corrupting `maxPriority` | API safety |

### Phase 2 — Test-driven bug surfacing

Rather than fixing issues immediately, Claude Code first wrote a `BugSurfacingTests` class with one test per identified issue, **each intentionally expected to fail**. This produced a concrete regression baseline before any code changed — a discipline that is easy to skip under time pressure but invaluable when validating fixes.

The concurrent `maxPriority` downgrade test used a `Barrier` to synchronize two threads at the start of each round and ran 50 rounds to maximize scheduling overlap and race exposure.

### Phase 3 — Fixes, with a diagnostic detour

**Issue #3 (wasted allocation):** Single-character fix — `new ConcurrentQueue<T>()` → `_ => new ConcurrentQueue<T>()`. The factory lambda is only invoked when the key is absent.

**Issue #5 (live references):** `GetQueues()` return type changed from `List<ConcurrentQueue<T>>` to `IReadOnlyList<T[]>`. Each inner queue is now snapshotted via `.ToArray()` at call time.

**Issue #1 (TOCTOU race):** This required a diagnostic detour worth noting. The initial CAS fix used a `do-while` formulation that appeared logically correct but failed ~50% of the time in the concurrency test. Rather than dismissing it as noise, Claude Code ran the test 10 consecutive times to establish the failure rate, then identified the root cause: on a failed `CompareExchange`, the `do-while` re-read `maxPriority` via `Volatile.Read`, opening a window where another write could slip in. The fix was switching to a `while` loop that feeds the CAS return value directly back as the next comparand — no re-read, no window. 10/10 passes after the change.

**Issue #2 (memory leak):** Rather than always removing drained queues immediately, Claude Code first framed the cost trade-off:

- *Removing* costs one per-bucket lock acquisition on `TryRemove`, plus a future heap allocation if the priority is reused (`GetOrAdd` must construct a new `ConcurrentQueue<T>`)
- *Retaining* costs nothing on the hot path but grows the key set that `ComputeMaxPriority()` scans on every drain

This analysis led to a configurable `emptyQueueExpiryMs` primary constructor parameter. Lazy cleanup on `Take()` was chosen over a background timer to avoid extra thread-pool callbacks and timer-lifecycle complexity. The full cost analysis is preserved in both source comments and the `EmptyQueueExpiryTests` class comment for future maintainers.

### Phase 4 — Code quality

A `var` → explicit-type pass was applied across all files, making every local variable's type immediately visible at the declaration site. Target-typed `new()` was used on the right-hand side. Primary constructor notation was adopted. All commits follow [Conventional Commits](https://www.conventionalcommits.org/) style with a 60-character subject line limit.

### Phase 5 — Continuous Integration/Continuous Deployment (CI/CD)

A GitHub Actions workflow was added to run the full test suite on every push and pull request. Claude Code fetched and analyzed the first run's logs, caught two categories of warnings, and resolved both:

- **xUnit2013** — four `Assert.Equal(1, collection.Count)` calls replaced with `Assert.Single(collection)`
- **Node.js 20 deprecation** — an initial `FORCE_JAVASCRIPT_ACTIONS_TO_NODE24` workaround was tried but the warning persisted; Claude Code checked the action release history and identified that `actions/checkout@v6` and `actions/setup-dotnet@v5` are the versions that natively target Node.js 24, resolving the warning cleanly

---

*Built with [Claude Code](https://claude.ai/code)*
