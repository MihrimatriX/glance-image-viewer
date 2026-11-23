using System.ComponentModel;

namespace Glance.Core;

/// <summary>
/// The supported images of one folder in the chosen order (default: Explorer name order), a cursor into them, and a watcher keeping the list current.
/// Thread-safe; <see cref="Changed"/> fires on a thread-pool thread.
/// </summary>
public sealed class FolderNavigator(Func<string, bool> isSupported) : IDisposable
{
    private static readonly IComparer<string> NameOrder =
        Comparer<string>.Create((a, b) => NaturalStringComparer.Instance.Compare(Path.GetFileName(a), Path.GetFileName(b)));

    private readonly Lock _lock = new();
    private List<string> _files = [];
    private FileSystemWatcher? _watcher;
    private int _removedIndex = -1;

    public event EventHandler? Changed;

    public string? Current { get; private set; }
    public SortBy SortBy { get; private set; } = SortBy.Name;
    public bool SortDescending { get; private set; }

    public int Count { get { lock (_lock) return _files.Count; } }

    /// <summary>Zero-based position of <see cref="Current"/>, or -1 if it is no longer in the folder.</summary>
    public int Index { get { lock (_lock) return Current is null ? -1 : FindIndexLocked(Current); } }

    /// <summary>Changes folder ordering and re-sorts the current list, preserving <see cref="Current"/>.</summary>
    public void SetSort(SortBy sortBy, bool descending)
    {
        lock (_lock)
        {
            if (SortBy == sortBy && SortDescending == descending)
                return;
            SortBy = sortBy;
            SortDescending = descending;
            SortLocked(_files, sortBy, descending);
            _removedIndex = -1;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Lists the folder of <paramref name="path"/> (blocking I/O — call off the UI thread) and selects the file.</summary>
    public void Open(string path)
    {
        path = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(path)!;
        SortBy sortBy;
        bool descending;
        lock (_lock)
        {
            sortBy = SortBy;
            descending = SortDescending;
        }
        var files = Directory.EnumerateFiles(dir).Where(isSupported).ToList();
        SortLocked(files, sortBy, descending);

        lock (_lock)
        {
            _files = files;
            Current = path;
            _removedIndex = -1;
        }

        _watcher?.Dispose();
        _watcher = null;
        try
        {
            var watcher = new FileSystemWatcher(dir) { NotifyFilter = NotifyFilters.FileName, IncludeSubdirectories = false };
            watcher.Created += (_, e) => Update(add: e.FullPath);
            watcher.Deleted += (_, e) => Update(remove: e.FullPath);
            watcher.Renamed += (_, e) => Update(remove: e.OldFullPath, add: e.FullPath);
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or Win32Exception)
        {
            // Network shares, virtual/read-only folders or MTP paths may not support watching; navigation still works.
            Log.Error($"Folder watcher unavailable for {dir}", ex);
        }
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
            var pos = FindIndexLocked(Current);
            if (pos < 0)
            {
                // Current was removed: use where it used to sit, i.e. already "one step forward".
                pos = _removedIndex >= 0 ? Math.Min(_removedIndex, _files.Count)
                    : SortBy == SortBy.Name && !SortDescending ? ~_files.BinarySearch(Current, NameOrder)
                    : 0;
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

    /// <summary>Replaces a renamed file's path in-place (preserving selection if it was current).</summary>
    public void Rename(string oldPath, string newPath)
    {
        lock (_lock)
        {
            if (string.Equals(Current, oldPath, StringComparison.OrdinalIgnoreCase))
                Current = newPath;
        }
        Update(remove: oldPath, add: newPath);
    }

    public void Dispose() => _watcher?.Dispose();

    private string? Select(string? path)
    {
        if (path is not null)
            lock (_lock)
            {
                Current = path;
                _removedIndex = -1;
            }
        return path;
    }

    private void Update(string? remove = null, string? add = null)
    {
        lock (_lock)
        {
            if (remove is not null && FindIndexLocked(remove) is >= 0 and var r)
            {
                if (string.Equals(Current, remove, StringComparison.OrdinalIgnoreCase))
                    _removedIndex = r;
                _files.RemoveAt(r);
            }
            if (add is not null && isSupported(add) && FindIndexLocked(add) < 0)
            {
                if (SortBy == SortBy.Name)
                {
                    var cmp = SortDescending ? Comparer<string>.Create((a, b) => NameOrder.Compare(b, a)) : NameOrder;
                    var pos = _files.BinarySearch(add, cmp);
                    _files.Insert(pos < 0 ? ~pos : pos, add);
                }
                else
                {
                    _files.Add(add);
                    SortLocked(_files, SortBy, SortDescending);
                }
                if (string.Equals(Current, add, StringComparison.OrdinalIgnoreCase))
                    _removedIndex = -1;
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private int FindIndexLocked(string path)
    {
        if (SortBy == SortBy.Name && !SortDescending)
        {
            var idx = _files.BinarySearch(path, NameOrder);
            if (idx >= 0 && string.Equals(_files[idx], path, StringComparison.OrdinalIgnoreCase))
                return idx;
        }
        return _files.FindIndex(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
    }

    private static void SortLocked(List<string> files, SortBy sortBy, bool descending)
    {
        if (sortBy == SortBy.Name)
        {
            files.Sort(descending ? Comparer<string>.Create((a, b) => NameOrder.Compare(b, a)) : NameOrder);
            return;
        }
        // Snapshot file metadata once so the sort is O(N) I/O and strictly consistent even if files mutate mid-sort.
        var keyed = files.Select(p => (Path: p, Key: MetadataKey(p, sortBy))).ToArray();
        Array.Sort(keyed, (a, b) =>
        {
            var cmp = a.Key.CompareTo(b.Key);
            if (cmp != 0)
                return descending ? -cmp : cmp;
            var name = NameOrder.Compare(a.Path, b.Path);
            return descending ? -name : name;
        });
        files.Clear();
        foreach (var (p, _) in keyed)
            files.Add(p);
    }

    private static long MetadataKey(string path, SortBy sortBy)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return 0;
            return sortBy == SortBy.DateModified ? info.LastWriteTimeUtc.Ticks : info.Length;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
