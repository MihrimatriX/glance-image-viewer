using Glance.Core;

namespace Glance.Core.Tests;

public class ViewportTests
{
    [Fact]
    public void Fit_centers_and_downscales_large_image()
    {
        var v = Viewport.Fit(4000, 2000, 1000, 1000);
        Assert.Equal(0.25, v.Scale);
        Assert.Equal((0.0, 250.0), (v.OffsetX, v.OffsetY));
    }

    [Fact]
    public void Fit_never_upscales_small_image()
    {
        var v = Viewport.Fit(100, 50, 1000, 1000);
        Assert.Equal(1, v.Scale);
        Assert.Equal((450.0, 475.0), (v.OffsetX, v.OffsetY));
    }

    [Theory]
    [InlineData(2.0, 300, 200)]
    [InlineData(0.5, 0, 0)]
    [InlineData(1.2, 999, 17)]
    public void ZoomAt_keeps_point_under_cursor_fixed(double factor, double x, double y)
    {
        var before = new Viewport(0.8, 40, -60);
        var after = before.ZoomAt(factor, x, y);

        // Image point under the cursor, before and after.
        Assert.Equal((x - before.OffsetX) / before.Scale, (x - after.OffsetX) / after.Scale, 9);
        Assert.Equal((y - before.OffsetY) / before.Scale, (y - after.OffsetY) / after.Scale, 9);
        Assert.Equal(before.Scale * factor, after.Scale, 9);
    }

    [Fact]
    public void ZoomAt_clamps_scale()
    {
        Assert.Equal(Viewport.MaxScale, new Viewport(1, 0, 0).ZoomAt(1e6, 10, 10).Scale);
        Assert.Equal(Viewport.MinScale, new Viewport(1, 0, 0).ZoomAt(1e-6, 10, 10).Scale);
    }
}
