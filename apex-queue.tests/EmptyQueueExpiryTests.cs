using DJ.Codes;
using Xunit;

namespace ApexQueue.Tests;

// Tests for the configurable empty-queue expiry introduced to address the
// memory leak where drained ConcurrentQueue<T> instances accumulated
// indefinitely in the internal dictionary.
//
// Cost trade-off
// --------------
// Removing an empty queue on every drain costs one per-bucket lock
// acquisition on TryRemove, plus a heap allocation and a GC cycle the next
// time the same priority is reused (GetOrAdd must construct a new
// ConcurrentQueue<T>). Keeping an empty queue costs nothing on the Add/Take
// hot path, but ComputeMaxPriority() iterates all keys on every drain, so
// unbounded key growth degrades that scan over time.
//
// The configurable expiry lets callers choose the trade-off:
//   -1 (default) — never remove; best for bounded, recycled priority sets
//                  where the remove+alloc churn would exceed the scan cost.
//    0            — remove immediately; best for non-recycling workloads
//                  (e.g. monotonically increasing priorities) where
//                  unbounded dictionary growth is the dominant concern.
//   >0            — sliding window in milliseconds; lazy cleanup fires on
//                  the next Take() call after expiry; re-adding to a
//                  priority resets the clock, avoiding alloc churn for
//                  bursty workloads that recycle the same priority.
//
// Lazy cleanup on Take() was chosen over a background timer to avoid extra
// thread-pool callbacks, timer-lifecycle complexity, and wake-up overhead
// when the queue is idle.
public class EmptyQueueExpiryTests
{
    // Default (-1): empty queues are retained indefinitely. ComputeMaxPriority
    // still skips empty keys correctly, so correctness is unaffected; the
    // trade-off is modest dictionary growth for recycled priority workloads.
    [Fact]
    public void Default_DrainedQueue_IsRetainedInDictionary()
    {
        ApexQueue<int> q = new();
        q.Add(1, priority: 5);
        q.Take();
        Assert.Equal(1, q.GetQueues().Count);
    }

    // Immediate (0): each drain removes the empty queue from the dictionary
    // immediately. Best when priorities are not recycled and unbounded growth
    // is the concern. Pays one lock + alloc if the same priority is reused.
    [Fact]
    public void ImmediateExpiry_DrainedQueues_AreRemovedFromDictionary()
    {
        ApexQueue<int> q = new(emptyQueueExpiryMs: 0);
        q.Add(1, priority: 5);
        q.Add(2, priority: 10);
        q.Take(); // drains priority 10
        q.Take(); // drains priority 5
        Assert.Empty(q.GetQueues());
    }

    // Sliding: re-adding to a priority before its window elapses resets the
    // clock, so the queue is not removed on the next Take() sweep.
    [Fact]
    public void SlidingExpiry_ReaddBeforeWindow_QueueIsNotRemoved()
    {
        ApexQueue<int> q = new(emptyQueueExpiryMs: 200);
        q.Add(1, priority: 5);
        q.Take();              // queue empty, sliding timer starts at T≈0
        Thread.Sleep(50);      // T≈50ms — still within 200ms window
        q.Add(2, priority: 5); // re-add resets the timer to T≈50ms
        Thread.Sleep(150);     // T≈200ms total, but only 150ms since reset
        q.Take();              // lazy sweep: 150ms < 200ms, priority 5 kept
        Assert.Equal(1, q.GetQueues().Count);
    }

    // Sliding: a queue that stays empty past the window is removed on the
    // next Take() call (lazy cleanup sweep).
    [Fact]
    public void SlidingExpiry_StaysEmptyPastWindow_RemovedOnNextTake()
    {
        ApexQueue<int> q = new(emptyQueueExpiryMs: 100);
        q.Add(1, priority: 5);
        q.Add(2, priority: 10);
        q.Take();          // drains priority 10, timer starts at T≈0
        Thread.Sleep(150); // T≈150ms > 100ms window
        q.Take();          // sweep removes expired priority 10; dequeues from priority 5
        Assert.Equal(1, q.GetQueues().Count);
    }

    // Sliding: a queue that is still within its window is kept even when
    // the Take() sweep runs.
    [Fact]
    public void SlidingExpiry_StillWithinWindow_QueueIsRetained()
    {
        ApexQueue<int> q = new(emptyQueueExpiryMs: 500);
        q.Add(1, priority: 5);
        q.Take();          // queue empty, timer starts
        Thread.Sleep(50);  // T≈50ms — well within 500ms window
        q.Take();          // sweep: 50ms < 500ms, priority 5 not removed
        Assert.Equal(1, q.GetQueues().Count);
    }
}
