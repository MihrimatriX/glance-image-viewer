namespace Glance.Core;

/// <summary>Least-recently-used cache bounded by a byte budget, not an item count. Thread-safe.</summary>
public sealed class LruCache<TKey, TValue>(long budgetBytes, Func<TValue, long> sizeOf, Action<TValue>? onEvict = null)
    where TKey : notnull
{
    private readonly Lock _lock = new();
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map = [];
    private readonly LinkedList<(TKey Key, TValue Value)> _order = new();   // head = most recent

    public long BudgetBytes => budgetBytes;
    public long SizeBytes { get; private set; }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }
        value = default!;
        return false;
    }

    /// <summary>Adds or replaces. A value larger than the whole budget is not cached (returns false).</summary>
    public bool Add(TKey key, TValue value)
    {
        var size = sizeOf(value);
        List<TValue> evicted = [];
        lock (_lock)
        {
            if (_map.Remove(key, out var old))
            {
                _order.Remove(old);
                SizeBytes -= sizeOf(old.Value.Value);
                evicted.Add(old.Value.Value);
            }
            if (size <= budgetBytes)
            {
                _map[key] = _order.AddFirst((key, value));
                SizeBytes += size;
                while (SizeBytes > budgetBytes && _order.Last is { } last)
                {
                    _order.RemoveLast();
                    _map.Remove(last.Value.Key);
                    SizeBytes -= sizeOf(last.Value.Value);
                    evicted.Add(last.Value.Value);
                }
            }
        }
        foreach (var v in evicted)
            onEvict?.Invoke(v);
        return size <= budgetBytes;
    }

    public void Remove(TKey key)
    {
        TValue removed;
        lock (_lock)
        {
            if (!_map.Remove(key, out var node))
                return;
            _order.Remove(node);
            SizeBytes -= sizeOf(node.Value.Value);
            removed = node.Value.Value;
        }
        onEvict?.Invoke(removed);
    }

    public void Clear()
    {
        List<TValue> all;
        lock (_lock)
        {
            all = [.. _order.Select(e => e.Value)];
            _order.Clear();
            _map.Clear();
            SizeBytes = 0;
        }
        foreach (var v in all)
            onEvict?.Invoke(v);
    }

    /// <summary>Spec default: min(10% of RAM, 1 GB).</summary>
    public static long DefaultBudget() =>
        Math.Min(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 10, 1L << 30);
}
