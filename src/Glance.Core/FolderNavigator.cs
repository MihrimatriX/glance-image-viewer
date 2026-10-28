namespace Glance.Core;

/// <summary>
/// The supported images of one folder in Explorer name order, a cursor into them, and a watcher keeping the list current.
/// Thread-safe; <see cref="Changed"/> fires on a thread-pool thread.
/// </summary>
public sealed class FolderNavigator(Func<string, bool> isSupported) : IDisposable
{
    private static readonly IComparer<string> Order =
        Comparer<string>.Create((a, b) => NaturalStringComparer.Instance.Compare(Path.GetFileName(a), Path.GetFileName(b)));

    private readonly Lock _lock = new();
    private List<string> _files = [];
    private FileSystemWatcher? _watcher;

    public event EventHandler? Changed;

    public string? Current { get; private set; }

    public int Count { get { lock (_lock) return _files.Count; } }

    /// <summary>Zero-based position of <see cref="Current"/>, or -1 if it is no longer in the folder.</summary>
    public int Index { get { lock (_lock) return Current is null ? -1 : Math.Max(-1, _files.BinarySearch(Current, Order)); } }

    /// <summary>Lists the folder of <paramref name="path"/> (blocking I/O — call off the UI thread) and selects the file.</summary>
    public void Open(string path)
    {
        path = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(path)!;
        var files = Directory.EnumerateFiles(dir).Where(isSupported).ToList();
        files.Sort(Order);

        lock (_lock)
        {
            _files = files;
            Current = path;
        }

        _watcher?.Dispose();
        _watcher = new FileSystemWatcher(dir) { NotifyFilter = NotifyFilters.FileName, IncludeSubdirectories = false };
        _watcher.Created += (_, e) => Update(add: e.FullPath);
        _watcher.Deleted += (_, e) => Update(remove: e.FullPath);
        _watcher.Renamed += (_, e) => Update(remove: e.OldFullPath, add: e.FullPath);
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Copy of the current list, in display order.</summary>
    public string[] Snapshot() { lock (_lock) return [.. _files]; }

    /// <summary>The file <paramref name="offset"/> steps from the current one, or null past either end. Does not move.</summary>
    public string? Peek(int offset)
    {
        lock (_lock)
        {
            if (Current is null)
                return null;
            var pos = _files.BinarySearch(Current, Order);
            if (pos < 0)
            {
                // Current was removed: ~pos is where it used to be, i.e. already "one step forward".
                pos = ~pos;
                if (offset > 0)
                    offset--;
            }
            var target = pos + offset;
            return target >= 0 && target < _files.Count ? _files[target] : null;
        }
    }

    public string? Move(int offset) => Select(Peek(offset));

    /// <summary>Selects a specific file (e.g. after undoing a delete).</summary>
    public string MoveTo(string path) => Select(path)!;

    public string? MoveFirst() { lock (_lock) return Select(_files.FirstOrDefault()); }

    public string? MoveLast() { lock (_lock) return Select(_files.LastOrDefault()); }

    /// <summary>Drops a file from the list right away (e.g. pending delete), without waiting for the watcher.</summary>
    public void Remove(string path) => Update(remove: path);

    /// <summary>Puts a file back (e.g. undo of a pending delete).</summary>
    public void Restore(string path) => Update(add: path);

    public void Dispose() => _watcher?.Dispose();

    private string? Select(string? path)
    {
        if (path is not null)
            lock (_lock) Current = path;
        return path;
    }

    private void Update(string? remove = null, string? add = null)
    {
        lock (_lock)
        {
            if (remove is not null && _files.BinarySearch(remove, Order) is >= 0 and var r)
                _files.RemoveAt(r);
            if (add is not null && isSupported(add) && _files.BinarySearch(add, Order) is < 0 and var a)
                _files.Insert(~a, add);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
