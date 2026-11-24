using Glance.Core;

namespace Glance.Core.Tests;

public class CacheTests
{
    [Fact]
    public void Evicts_least_recently_used_when_over_byte_budget()
    {
        List<string> evicted = [];
        var cache = new LruCache<string, string>(10, s => s.Length, evicted.Add);

        cache.Add("a", "aaaa");
        cache.Add("b", "bbbb");
        Assert.True(cache.TryGet("a", out _));   // a is now most recent
        cache.Add("c", "cccc");                  // 12 > 10 -> evict b

        Assert.Equal(["bbbb"], evicted);
        Assert.False(cache.TryGet("b", out _));
        Assert.Equal(8, cache.SizeBytes);
    }

    [Fact]
    public void Value_larger_than_budget_is_not_cached()
    {
        var cache = new LruCache<string, string>(3, s => s.Length);
        Assert.False(cache.Add("big", "toolong"));
        Assert.Equal(0, cache.SizeBytes);
    }

    [Fact]
    public void Replacing_a_key_evicts_the_old_value()
    {
        List<string> evicted = [];
        var cache = new LruCache<string, string>(100, s => s.Length, evicted.Add);
        cache.Add("k", "old");
        cache.Add("k", "newer");
        Assert.Equal(["old"], evicted);
        Assert.Equal(5, cache.SizeBytes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(0)]
    public void Prefetch_order_covers_two_neighbors_each_side_and_favors_direction(int direction)
    {
        var order = Prefetch.Order(direction);

        Assert.Subset(order.ToHashSet(), new HashSet<int> { -2, -1, 1, 2 });
        Assert.DoesNotContain(0, order);
        Assert.Equal(order.Length, order.Distinct().Count());
        if (direction != 0)
        {
            Assert.Equal(direction, order[0]);
            Assert.True(order.Count(o => Math.Sign(o) == direction) > order.Count(o => Math.Sign(o) == -direction));
        }
    }
}
