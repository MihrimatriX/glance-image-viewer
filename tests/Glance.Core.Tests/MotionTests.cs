using System.Collections.ObjectModel;
using Glance.Core;

namespace Glance.Core.Tests;

public class MotionTests
{
    private static readonly TransitionKind[] Moving = [TransitionKind.Slide, TransitionKind.Crossfade, TransitionKind.Zoom, TransitionKind.Parallax];

    [Fact]
    public void Spring_starts_at_zero_ends_at_one_and_never_goes_back()
    {
        Assert.Equal(0, Motion.Spring(0), 6);
        Assert.Equal(1, Motion.Spring(1), 6);
        var previous = 0.0;
        for (var t = 0.0; t <= 1.0; t += 0.01)
        {
            var v = Motion.Spring(t);
            Assert.InRange(v, previous, 1);
            previous = v;
        }
    }

    [Fact]
    public void Spring_front_loads_the_motion()
    {
        // Most of the distance is covered early; the rest is the soft landing.
        Assert.True(Motion.Spring(0.3) > 0.7);
    }

    [Theory]
    [InlineData(TransitionKind.Slide, 1)]
    [InlineData(TransitionKind.Slide, -1)]
    [InlineData(TransitionKind.Crossfade, 1)]
    [InlineData(TransitionKind.Zoom, -1)]
    [InlineData(TransitionKind.Parallax, 1)]
    [InlineData(TransitionKind.Slide, 0)]
    public void Picture_stays_covered_through_the_whole_transition(TransitionKind kind, int direction)
    {
        // Incoming drawn over outgoing: combined coverage = in + out * (1 - in). The old 50/50 cross-fade dipped to 0.75.
        for (var t = 0.0; t <= 1.0; t += 0.02)
        {
            var (o, i) = Motion.Evaluate(kind, t, direction, 1000);
            var coverage = i.Opacity + o.Opacity * (1 - i.Opacity);
            Assert.True(coverage >= 0.8f, $"{kind} at t={t:0.00}: coverage {coverage:0.00}");
        }
    }

    [Fact]
    public void Every_transition_ends_at_rest_with_the_outgoing_image_gone()
    {
        foreach (var kind in Moving)
        {
            var (o, i) = Motion.Evaluate(kind, 1, 1, 1000);
            Assert.Equal(TransitionFrame.Rest, i);
            Assert.Equal(0f, o.Opacity);
        }
    }

    [Theory]
    [InlineData(TransitionKind.Slide)]
    [InlineData(TransitionKind.Parallax)]
    public void Forward_brings_the_new_image_in_from_the_right(TransitionKind kind)
    {
        var (o, i) = Motion.Evaluate(kind, 0.1, +1, 1000);
        Assert.True(i.OffsetX > 0);
        Assert.True(o.OffsetX < 0);
        var (ob, ib) = Motion.Evaluate(kind, 0.1, -1, 1000);
        Assert.True(ib.OffsetX < 0);
        Assert.True(ob.OffsetX > 0);
    }

    [Fact]
    public void None_cuts_straight_to_the_new_image()
    {
        var (o, i) = Motion.Evaluate(TransitionKind.None, 0, 1, 1000);
        Assert.Equal(0f, o.Opacity);
        Assert.Equal(TransitionFrame.Rest, i);
        Assert.Equal(0.0, Motion.DurationMs(TransitionKind.None));
    }

    [Fact]
    public void Card_emphasis_shrinks_and_dims_with_distance()
    {
        var (s0, o0) = Motion.CardEmphasis(0);
        Assert.Equal(1.2f, s0);
        Assert.Equal(1f, o0);
        for (var d = 1; d < 6; d++)
        {
            var (near, nearOpacity) = Motion.CardEmphasis(d - 1);
            var (far, farOpacity) = Motion.CardEmphasis(d);
            Assert.True(far <= near && farOpacity <= nearOpacity);
            Assert.Equal(Motion.CardEmphasis(d), Motion.CardEmphasis(-d));
        }
    }

    [Theory]
    [InlineData(-200, 0, 1000, +1)]   // dragged left past 15%: next
    [InlineData(200, 0, 1000, -1)]    // dragged right past 15%: previous
    [InlineData(-50, 0, 1000, 0)]     // short, slow: spring back
    [InlineData(-50, -0.9, 1000, +1)] // short but flicked left: next
    [InlineData(-50, 0.9, 1000, 0)]   // flicked back the other way: stay
    public void Swipe_commits_on_distance_or_flick(double offset, double velocity, double width, int expected) =>
        Assert.Equal(expected, Motion.SwipeOutcome(offset, velocity, width));
}

public class CollectionSyncTests
{
    private static ObservableCollection<string> Of(params string[] keys) => [.. keys];

    private static bool Sync(ObservableCollection<string> items, string[] keys, int maxEdits = 64) =>
        CollectionSync.TrySync(items, keys, s => s, s => s, StringComparer.OrdinalIgnoreCase, maxEdits);

    [Theory]
    [InlineData(new[] { "a", "b", "c" }, new[] { "a", "b", "c", "d" })]          // file added at the end
    [InlineData(new[] { "a", "b", "c" }, new[] { "a", "x", "b", "c" })]          // file added in the middle
    [InlineData(new[] { "a", "b", "c" }, new[] { "a", "c" })]                    // file deleted
    [InlineData(new[] { "a", "b", "c" }, new[] { "c", "a", "b" })]               // renamed to sort first
    [InlineData(new[] { "a", "b", "c" }, new[] { "d", "e" })]                    // different folder
    public void Ends_up_equal_to_the_keys(string[] start, string[] target)
    {
        var items = Of(start);
        Assert.True(Sync(items, target));
        Assert.Equal(target, items);
    }

    [Fact]
    public void Fills_an_empty_list_and_empties_a_full_one()
    {
        var items = new ObservableCollection<string>();
        Assert.True(Sync(items, ["a", "b"]));
        Assert.Equal(new[] { "a", "b" }, items);
        Assert.True(Sync(items, []));
        Assert.Empty(items);
    }

    [Fact]
    public void Keeps_existing_items_instead_of_recreating_them()
    {
        var created = 0;
        var items = new ObservableCollection<string>(["a", "b", "c"]);
        Assert.True(CollectionSync.TrySync(items, ["a", "new", "b", "c"], s => s, s => { created++; return s; }, StringComparer.Ordinal));
        Assert.Equal(1, created);
    }

    [Fact]
    public void Gives_up_untouched_when_a_rebuild_is_cheaper()
    {
        var items = Of("a", "b", "c", "d", "e", "f");
        Assert.False(Sync(items, ["f", "e", "d", "c", "b", "a"], maxEdits: 2));
        Assert.Equal(new[] { "a", "b", "c", "d", "e", "f" }, items);
    }
}
