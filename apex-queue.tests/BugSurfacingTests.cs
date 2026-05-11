using System.Collections.Concurrent;
using DJ.Codes;
using Xunit;

namespace ApexQueue.Tests;

// Tests in this class surface known issues. Some are expected to fail;
// fixed issues are noted inline.
public class BugSurfacingTests
{
    // EXPECTED TO FAIL — empty ConcurrentQueue instances are never removed from
    // the internal dictionary after a priority level drains. Over time this
    // accumulates unbounded entries (memory leak) and slows ComputeMaxPriority.
    [Fact]
    public void Take_DrainAllItems_LeavesNoEmptyQueuesInDictionary()
    {
        ApexQueue<int> q = new();
        q.Add(1, priority: 5);
        q.Add(2, priority: 10);

        while (q.Count() > 0) q.Take();

        // After a full drain the internal dictionary should have no entries;
        // with the current code it retains two empty ConcurrentQueue objects.
        Assert.Empty(q.GetQueues());
    }

    // FIXED — the original Math.Max(priority, maxPriority) + Interlocked.Exchange
    // had a TOCTOU (Time-of-Check to Time-of-Use) race: a lower-priority thread
    // could read maxPriority before a higher-priority write was visible and then
    // overwrite it. Replaced with a CAS spin loop that uses the return value of
    // each failed CompareExchange as the next comparand, closing that window.
    // The test runs many rounds with a Barrier to maximize thread overlap.
    [Fact]
    public async Task Add_Concurrent_MaxPriorityNeverDowngrades()
    {
        const int rounds = 50;

        for (int round = 0; round < rounds; round++)
        {
            ApexQueue<int> q = new();
            Barrier barrier = new(2);

            // Thread A: one add at the highest priority.
            Task t1 = Task.Run(() =>
            {
                barrier.SignalAndWait();
                q.Add(1, priority: 100);
            });

            // Thread B: adds at every lower priority — 99 chances to race and
            // overwrite maxPriority with a stale lower value.
            Task t2 = Task.Run(() =>
            {
                barrier.SignalAndWait();
                for (int p = 1; p < 100; p++) q.Add(1, priority: p);
            });

            await Task.WhenAll(t1, t2);

            Assert.Equal(100, q.MaxPriority);
        }
    }

    // EXPECTED TO FAIL — GetQueues() returns references to the live internal
    // ConcurrentQueue<T> objects. Enqueueing via a live ref bypasses Add(), so
    // maxPriority is never updated. The first Take() after a direct enqueue hits
    // the else-branch, corrects maxPriority as a side-effect, and returns default
    // instead of the item that was injected.
    [Fact]
    public void GetQueues_DirectEnqueue_BypassesMaxPriorityTracking()
    {
        ApexQueue<string> q = new();
        q.Add("task", priority: 5);
        ConcurrentQueue<string> liveRef = q.GetQueues()[0]; // grab reference to the live inner queue

        q.Take();                    // drain; maxPriority resets to 0
        liveRef.Enqueue("injected"); // bypass Add() — maxPriority stays 0

        Assert.Equal(1, q.Count()); // Count sees the item via the live ref ✓
        // FAILS: Take() reads MaxPriority=0, finds no key 0, falls into the else
        // branch and returns default(string)=null instead of "injected".
        Assert.Equal("injected", q.Take());
    }

    // EXPECTED TO FAIL intermittently — Count() sums inner queue counts one-by-one
    // via payload.Values.Sum(q => q.Count). If a shuttle thread moves an item from
    // queue p2 to queue p1 in the gap between reading p2.Count and p1.Count, the
    // item is observed in both queues and the sum exceeds the true total.
    // p2 is inserted first so it is iterated first, making the race reachable.
    // NOTE: The scenario is acceptable for a concurrent structure
    // and more likely to occur under high contention and with a large number of items.
    // At this point, it's more theoretical than anything since the test seems to always pass.
    [Fact]
    public async Task Count_DuringCrossQueueMoves_CanOvercount()
    {
        const int n = 1_000;
        ApexQueue<int> q = new();
        for (int i = 0; i < n; i++) q.Add(i, priority: 2); // inserted first → iterated first
        for (int i = 0; i < n; i++) q.Add(i, priority: 1); // inserted second → iterated second

        bool overcountSeen = false;
        CancellationTokenSource cts = new();

        // Shuttle: continuously moves items from p2 (highest) to p1, giving
        // Count() a chance to read p2 before the Take and p1 after the Add.
        Task shuttle = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                int? item = q.Take();
                q.Add(item == default ? 0 : item.Value, priority: item == default ? 2 : 1);
            }
        });

        Task sampler = Task.Run(() =>
        {
            for (int i = 0; i < 100_000; i++)
                if (q.Count() > 2 * n) overcountSeen = true;
            cts.Cancel();
        });

        await Task.WhenAll(shuttle, sampler);

        // FAILS when the race is hit: Count() returns 2n+1 because the shuttled
        // item is counted once in p2 (before Take) and once in p1 (after Add).
        Assert.False(overcountSeen, $"Count() returned more than the total {2 * n} items");
    }
}
