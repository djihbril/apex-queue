using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace DJ.Codes;

// emptyQueueExpiryMs controls when drained priority-level queues are removed
// from the internal dictionary:
//   -1 (default) — never remove; best for bounded, recycled priority sets
//                  where the remove+alloc churn exceeds the scan cost.
//    0            — remove immediately on drain; best for non-recycling
//                  workloads where unbounded key growth is the concern.
//   >0            — sliding window in ms; lazy cleanup on the next Take()
//                  after expiry; re-adding to a priority resets the clock.
//
// Cost trade-off: TryRemove costs one per-bucket lock + a future
// ConcurrentQueue<T> allocation if the priority is reused. Keeping an
// empty queue costs nothing on the hot path but grows the key set that
// ComputeMaxPriority() must scan on every drain.
public class ApexQueue<T>(int emptyQueueExpiryMs = -1)
{
    private readonly ConcurrentDictionary<int, ConcurrentQueue<T>> payload = new();
    private readonly ConcurrentDictionary<int, long> emptyQueueTimestamps = new();
    private int maxPriority;
    public int MaxPriority => maxPriority;

    public T? Take()
    {
        if (emptyQueueExpiryMs > 0) CleanupExpiredQueues();

        if (payload.IsEmpty) return default;

        int currentPriority = MaxPriority;
        if (payload.TryGetValue(currentPriority, out ConcurrentQueue<T>? queue))
        {
            T? item = default;
            if (queue.TryDequeue(out T? dequeuedItem)) item = dequeuedItem;

            if (queue.IsEmpty)
            {
                if (emptyQueueExpiryMs == 0)
                {
                    payload.TryRemove(currentPriority, out _);
                }
                else if (emptyQueueExpiryMs > 0)
                {
                    emptyQueueTimestamps[currentPriority] = Environment.TickCount64;
                }
                Interlocked.Exchange(ref maxPriority, ComputeMaxPriority());
            }
            return item;
        }
        else
        {
            Interlocked.Exchange(ref maxPriority, ComputeMaxPriority());
            return default;
        }
    }

    public void Add(T item, int priority)
    {
        payload.GetOrAdd(priority, _ => new ConcurrentQueue<T>()).Enqueue(item);

        if (emptyQueueExpiryMs > 0) emptyQueueTimestamps.TryRemove(priority, out _);

        int snap = Volatile.Read(ref maxPriority);
        while (priority > snap)
        {
            int old = Interlocked.CompareExchange(ref maxPriority, priority, snap);
            if (old == snap) break;
            snap = old;
        }
    }

    public int Count() => payload.IsEmpty ? 0 : payload.Values.Sum(q => q.Count);

    public IReadOnlyList<T[]> GetQueues() =>
        [.. payload.Values.Select(q => q.ToArray())];

    private void CleanupExpiredQueues()
    {
        long now = Environment.TickCount64;
        bool anyRemoved = false;
        foreach ((int priority, long emptySinceTick) in emptyQueueTimestamps)
        {
            if (now - emptySinceTick < emptyQueueExpiryMs) continue;

            emptyQueueTimestamps.TryRemove(priority, out _);
            if (payload.TryGetValue(priority, out ConcurrentQueue<T>? q) && q.IsEmpty)
            {
                payload.TryRemove(priority, out _);
                anyRemoved = true;
            }
        }
        if (anyRemoved) Interlocked.Exchange(ref maxPriority, ComputeMaxPriority());
    }

    private int ComputeMaxPriority()
    {
        if (payload.IsEmpty || payload.Keys.Count == 0) return 0;
        try
        {
            return payload.Keys.ToImmutableList().Max(k => payload[k].IsEmpty ? 0 : k);
        }
        catch
        {
            return 0;
        }
    }
}
