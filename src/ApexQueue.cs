using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace DJ.Codes;

public class ApexQueue<T>
{
    private readonly ConcurrentDictionary<int, ConcurrentQueue<T>> payload = new();
    private int maxPriority;
    public int MaxPriority => maxPriority;

    public T? Take()
    {
        if (payload.IsEmpty)
        {
            return default;
        }
        if (payload.TryGetValue(MaxPriority, out ConcurrentQueue<T>? queue))
        {
            T? item = default;
            if (queue.TryDequeue(out T? dequeuedItem))
            {
                item = dequeuedItem;
            }
            if (queue.IsEmpty)
            {
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

        int snap = Volatile.Read(ref maxPriority);
        while (priority > snap)
        {
            int old = Interlocked.CompareExchange(ref maxPriority, priority, snap);
            if (old == snap) break;
            snap = old;
        }
    }

    public int Count() => payload.IsEmpty ? 0 : payload.Values.Sum(q => q.Count);

    public List<ConcurrentQueue<T>> GetQueues() => [.. payload.Values];

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
