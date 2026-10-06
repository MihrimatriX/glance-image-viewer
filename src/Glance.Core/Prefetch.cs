namespace Glance.Core;

public static class Prefetch
{
    /// <summary>
    /// Neighbor offsets to prefetch after showing an image, most important first.
    /// <paramref name="direction"/> is the last navigation step: +1 (next), -1 (previous), 0 (just opened).
    /// </summary>
    public static int[] Order(int direction)
    {
        // TODO(human): direction-aware neighbor order (spec: current ±2, more in the navigation direction).
        return [1, -1, 2, -2];
    }
}
