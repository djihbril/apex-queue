using DJ.Codes;
using Xunit;

namespace ApexQueue.Tests;

// Tests in this class surface known issues. Some are expected to fail;
// fixed issues are noted inline.
public class BugSurfacingTests
{
    // FIXED — empty queues now expire based on the emptyQueueExpiryMs
    // constructor parameter. Passing 0 removes them immediately on drain,
    // eliminating unbounded dictionary growth for non-recycling workloads.
    // The default (-1) retains empty queues, which is cheaper for workloads
    // that recycle the same priority levels. See EmptyQueueExpiryTests for
    // full trade-off documentation and coverage of all three modes.
    [Fact]
    public void Take_DrainAllItems_LeavesNoEmptyQueuesInDictionary()
    {
        ApexQueue<int> q = new(emptyQueueExpiryMs: 0);
        q.Add(1, priority: 5);
        q.Add(2, priority: 10);

        while (q.Count() > 0) q.Take();

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

    // FIXED — GetQueues() previously returned live ConcurrentQueue<T> references,
    // allowing callers to enqueue directly and bypass Add(), leaving maxPriority
    // stale. GetQueues() now returns a T[] snapshot per priority level, so
    // external enqueues are impossible and the internal state cannot be corrupted.
    [Fact]
    public void GetQueues_ReturnsSnapshot_IsolatedFromLiveQueue()
    {
        ApexQueue<string> q = new();
        q.Add("task", priority: 5);
        string[] snapshot = q.GetQueues()[0];

        q.Add("after", priority: 5); // added after snapshot was taken

        Assert.Single(snapshot);        // snapshot captured only "task"
        Assert.Equal(2, q.Count());     // internal queue has both items
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
