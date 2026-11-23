using Glance.Core;

namespace Glance.Core.Tests;

public sealed class NavigationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("glance-nav-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Natural_sort_matches_explorer()
    {
        string[] names = ["img10.jpg", "img2.jpg", "Img1.jpg", "img2 (1).jpg", "a.jpg"];
        Assert.Equal(["a.jpg", "Img1.jpg", "img2 (1).jpg", "img2.jpg", "img10.jpg"], names.Order(NaturalStringComparer.Instance));
    }

    [Fact]
    public void Lists_supported_files_in_natural_order_and_moves()
    {
        Touch("b10.jpg", "b2.jpg", "b1.png", "notes.txt");
        using var nav = Open("b2.jpg");

        Assert.Equal((3, 1), (nav.Count, nav.Index));
        Assert.EndsWith("b10.jpg", nav.Move(+1));
        Assert.Null(nav.Move(+1));                      // no wrap-around
        Assert.EndsWith("b1.png", nav.MoveFirst());
        Assert.Null(nav.Peek(-1));
        Assert.EndsWith("b10.jpg", nav.MoveLast());
    }

    [Fact]
    public void Removing_current_keeps_position_for_next_and_previous()
    {
        Touch("1.jpg", "2.jpg", "3.jpg");
        using var nav = Open("2.jpg");

        nav.Remove(nav.Current!);

        Assert.Equal(-1, nav.Index);
        Assert.EndsWith("3.jpg", nav.Peek(+1));
        Assert.EndsWith("1.jpg", nav.Peek(-1));
        nav.Restore(Path.Combine(_dir, "2.jpg"));
        Assert.Equal(1, nav.Index);
    }

    [Fact]
    public async Task Watcher_picks_up_created_and_renamed_files()
    {
        Touch("1.jpg");
        using var nav = Open("1.jpg");
        var changed = new SemaphoreSlim(0);
        nav.Changed += (_, _) => changed.Release();

        Touch("2.jpg");
        await WaitFor(() => nav.Count == 2, changed);
        File.Move(Path.Combine(_dir, "2.jpg"), Path.Combine(_dir, "0.jpg"));
        await WaitFor(() => nav.Peek(-1)?.EndsWith("0.jpg") == true, changed);
    }

    [Fact]
    public void Sorts_by_size_and_date_and_supports_reverse_order()
    {
        File.WriteAllBytes(Path.Combine(_dir, "small.jpg"), new byte[10]);
        File.WriteAllBytes(Path.Combine(_dir, "large.jpg"), new byte[500]);
        File.WriteAllBytes(Path.Combine(_dir, "mid.jpg"), new byte[100]);
        File.SetLastWriteTimeUtc(Path.Combine(_dir, "small.jpg"), new DateTime(2025, 1, 3, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(Path.Combine(_dir, "mid.jpg"), new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(Path.Combine(_dir, "large.jpg"), new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        using var nav = Open("mid.jpg");
        nav.SetSort(SortBy.Size, descending: false);
        Assert.Equal(["small.jpg", "mid.jpg", "large.jpg"], nav.Snapshot().Select(Path.GetFileName));
        Assert.Equal(1, nav.Index);

        nav.SetSort(SortBy.Size, descending: true);
        Assert.Equal(["large.jpg", "mid.jpg", "small.jpg"], nav.Snapshot().Select(Path.GetFileName));

        nav.SetSort(SortBy.DateModified, descending: false);
        Assert.Equal(["mid.jpg", "large.jpg", "small.jpg"], nav.Snapshot().Select(Path.GetFileName));
        Assert.Equal(0, nav.Index);
    }

    [Fact]
    public void Rename_updates_current_and_keeps_list_sorted()
    {
        Touch("a.jpg", "b.jpg", "c.jpg");
        using var nav = Open("b.jpg");
        var oldPath = Path.Combine(_dir, "b.jpg");
        var newPath = Path.Combine(_dir, "z.jpg");

        nav.Rename(oldPath, newPath);

        Assert.Equal(newPath, nav.Current);
        Assert.Equal(["a.jpg", "c.jpg", "z.jpg"], nav.Snapshot().Select(Path.GetFileName));
        Assert.Equal(2, nav.Index);
    }

    private FolderNavigator Open(string name)
    {
        var nav = new FolderNavigator(p => Path.GetExtension(p) is ".jpg" or ".png");
        nav.Open(Path.Combine(_dir, name));
        return nav;
    }

    private void Touch(params string[] names)
    {
        foreach (var n in names)
            File.WriteAllBytes(Path.Combine(_dir, n), []);
    }

    private static async Task WaitFor(Func<bool> condition, SemaphoreSlim signal)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out waiting for watcher");
            await signal.WaitAsync(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
        }
    }
}
