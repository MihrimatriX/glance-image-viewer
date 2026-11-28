using System.Collections.ObjectModel;

namespace Glance.Core;

/// <summary>How one image hands over to the next when browsing.</summary>
public enum TransitionKind { Slide, Crossfade, Zoom, Parallax, None }

/// <summary>One image's placement during a transition, relative to its resting viewport: shift (physical px), scale about the view centre, opacity.</summary>
public readonly record struct TransitionFrame(double OffsetX, double Scale, float Opacity)
{
    public static TransitionFrame Rest { get; } = new(0, 1, 1);
}

/// <summary>Timing and shape of every navigation transition. Pure maths, so it is unit-tested and the renderer only draws.</summary>
public static class Motion
{
    /// <summary>The slideshow hands over slowly whatever the browsing transition is.</summary>
    public const double SlideshowMs = 700;

    /// <summary>A new step while the previous transition is still running: keep up with the key instead of replaying the full move.</summary>
    public const double RapidMs = 140;

    /// <summary>Windows "Animation effects" off: a short fade only, nothing moves.</summary>
    public const double ReducedMs = 120;

    private const double SlideDistance = 0.08;    // of the view width
    private const double ParallaxBehind = 0.3;    // the outgoing image moves at 30% of the incoming one's speed
    private const double Omega = 9;               // spring stiffness: settles visibly by ~60% of the duration

    public static double DurationMs(TransitionKind kind) => kind switch
    {
        TransitionKind.Slide => 280,
        TransitionKind.Crossfade => 240,
        TransitionKind.Zoom => 320,
        TransitionKind.Parallax => 360,
        _ => 0,
    };

    /// <summary>
    /// Critically damped spring response, normalised to land exactly on 1 at <paramref name="t"/> = 1.
    /// Fast start, long soft landing: feels physical where a cubic ease feels mechanical.
    /// </summary>
    public static double Spring(double t)
    {
        t = Math.Clamp(t, 0, 1);
        var raw = 1 - (1 + Omega * t) * Math.Exp(-Omega * t);
        var end = 1 - (1 + Omega) * Math.Exp(-Omega);
        return Math.Min(1, raw / end);
    }

    /// <summary>
    /// The outgoing image stays opaque until the incoming one has mostly arrived, then fades. Drawn under the incoming image this
    /// keeps the picture covered all the way through (the old "both at 50%" cross-fade let the background shine through mid-way).
    /// </summary>
    public static float OutgoingOpacity(double t) => (float)(1 - Spring((Math.Clamp(t, 0, 1) - 0.35) / 0.65));

    /// <summary>
    /// Outgoing and incoming placement at progress <paramref name="t"/> (0..1). <paramref name="direction"/>: +1 forward, -1 back,
    /// 0 for a fresh open (nothing to slide away from). <paramref name="width"/> is the view width in physical px.
    /// </summary>
    public static (TransitionFrame Out, TransitionFrame In) Evaluate(TransitionKind kind, double t, int direction, double width)
    {
        t = Math.Clamp(t, 0, 1);
        var e = Spring(t);
        var d = Math.Sign(direction);
        var fade = OutgoingOpacity(t);

        if (kind == TransitionKind.None || t >= 1)
            return (new TransitionFrame(0, 1, 0), TransitionFrame.Rest);

        // A fresh open has no direction: whatever the style, the new image settles in from slightly small.
        if (d == 0 || kind == TransitionKind.Crossfade)
            return (new TransitionFrame(0, 1, fade),
                    new TransitionFrame(0, kind == TransitionKind.Crossfade ? 1 : 0.97 + 0.03 * e, (float)e));

        return kind switch
        {
            TransitionKind.Slide => (
                new TransitionFrame(-d * SlideDistance * width * e, 1 - 0.02 * e, fade),
                new TransitionFrame(d * SlideDistance * width * (1 - e), 1, (float)e)),
            TransitionKind.Zoom => (
                new TransitionFrame(0, 1 + 0.06 * e, fade),
                new TransitionFrame(0, 0.94 + 0.06 * e, (float)e)),
            // Phone-gallery handover: the new image slides in over the old, which drifts back and dims.
            _ => (
                new TransitionFrame(-d * ParallaxBehind * width * e, 1, (float)(1 - 0.6 * e)),
                new TransitionFrame(d * width * (1 - e), 1, 1)),
        };
    }

    /// <summary>
    /// Film strip "dock" look: the current card is large and bright, neighbours shrink and dim with distance.
    /// Scales stay small enough that a card never overlaps the next one at the strip's 6 DIP spacing.
    /// </summary>
    public static (float Scale, float Opacity) CardEmphasis(int distance) => Math.Abs(distance) switch
    {
        0 => (1.2f, 1f),
        1 => (0.92f, 0.85f),
        2 => (0.86f, 0.7f),
        _ => (0.82f, 0.55f),
    };

    /// <summary>A horizontal swipe past this fraction of the view width (or a flick) turns the page.</summary>
    public const double SwipeCommitFraction = 0.15;

    /// <summary>Release speed (physical px per ms) that counts as a flick regardless of distance.</summary>
    public const double SwipeFlickSpeed = 0.5;

    /// <summary>+1 next, -1 previous, 0 spring back. A finger moving left (negative offset) means "next".</summary>
    public static int SwipeOutcome(double offset, double velocity, double width)
    {
        if (Math.Abs(offset) >= SwipeCommitFraction * width || (Math.Abs(velocity) >= SwipeFlickSpeed && Math.Sign(velocity) == Math.Sign(offset)))
            return offset < 0 ? +1 : -1;
        return 0;
    }
}

/// <summary>Keeps an observable list in step with a list of keys by small edits, so bound views animate and keep their scroll position.</summary>
public static class CollectionSync
{
    /// <summary>
    /// Removes, moves and inserts until <paramref name="items"/> matches <paramref name="keys"/> (unique keys). Returns false and
    /// leaves the list untouched when it would take more than <paramref name="maxEdits"/> edits: a re-sort is cheaper as a rebuild.
    /// </summary>
    public static bool TrySync<T>(ObservableCollection<T> items, IReadOnlyList<string> keys, Func<T, string> keyOf,
        Func<string, T> create, IEqualityComparer<string> comparer, int maxEdits = 64)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(keyOf);
        ArgumentNullException.ThrowIfNull(create);

        // Dry run on the keys alone to count the edits.
        var plan = items.Select(keyOf).ToList();
        if (Edits(plan, keys, k => k, k => k, comparer, maxEdits) is null)
            return false;
        Edits(items, keys, keyOf, create, comparer, int.MaxValue);
        return true;
    }

    private static int? Edits<TItem>(IList<TItem> list, IReadOnlyList<string> keys, Func<TItem, string> keyOf,
        Func<string, TItem> create, IEqualityComparer<string> comparer, int maxEdits)
    {
        void Move(int from, int to)
        {
            if (list is ObservableCollection<TItem> observable)
            {
                observable.Move(from, to);
                return;
            }
            var item = list[from];
            list.RemoveAt(from);
            list.Insert(to, item);
        }

        var wanted = new HashSet<string>(keys, comparer);
        var edits = 0;
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (wanted.Contains(keyOf(list[i])))
                continue;
            list.RemoveAt(i);
            if (++edits > maxEdits)
                return null;
        }
        for (var i = 0; i < keys.Count; i++)
        {
            if (i < list.Count && comparer.Equals(keyOf(list[i]), keys[i]))
                continue;
            var j = -1;
            for (var k = i + 1; k < list.Count; k++)
            {
                if (comparer.Equals(keyOf(list[k]), keys[i]))
                {
                    j = k;
                    break;
                }
            }
            if (j >= 0)
                Move(j, i);
            else
                list.Insert(i, create(keys[i]));
            if (++edits > maxEdits)
                return null;
        }
        while (list.Count > keys.Count)
            list.RemoveAt(list.Count - 1);
        return edits;
    }
}
