namespace Glance.Core;

public static class Prefetch
{
    /// <summary>How far ahead to look in the direction of travel (people browse forward through a folder).</summary>
    public const int Ahead = 16;

    /// <summary>
    /// Neighbor offsets to prefetch after showing an image, most important first.
    /// <paramref name="direction"/> is the last navigation step: +1 (next), -1 (previous), 0 (just opened).
    /// </summary>
    public static int[] Order(int direction)
    {
        // Just opened: assume forward, the common case (and what the slideshow does).
        var d = direction == 0 ? 1 : Math.Sign(direction);
        // Next and the one we came from first (a quick step back is common), then a second step each way,
        // then a long run ahead.
        List<int> order = [d, -d, 2 * d, -2 * d];
        for (var i = 3; i <= Ahead; i++)
            order.Add(i * d);
        return [.. order];
    }
}
