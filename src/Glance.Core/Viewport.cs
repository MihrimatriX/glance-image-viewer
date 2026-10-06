namespace Glance.Core;

/// <summary>Maps image space to view space: <c>view = image * Scale + Offset</c>. All units are DIPs.</summary>
public readonly record struct Viewport(double Scale, double OffsetX, double OffsetY)
{
    public const double MinScale = 0.01;
    public const double MaxScale = 64;

    /// <summary>Centers the image and fits it into the view. Never upscales past 1:1.</summary>
    public static Viewport Fit(double imageWidth, double imageHeight, double viewWidth, double viewHeight)
    {
        var scale = Math.Min(1, Math.Min(viewWidth / imageWidth, viewHeight / imageHeight));
        return Centered(scale, imageWidth, imageHeight, viewWidth, viewHeight);
    }

    public static Viewport Centered(double scale, double imageWidth, double imageHeight, double viewWidth, double viewHeight) =>
        new(scale, (viewWidth - imageWidth * scale) / 2, (viewHeight - imageHeight * scale) / 2);

    public Viewport Pan(double dx, double dy) => this with { OffsetX = OffsetX + dx, OffsetY = OffsetY + dy };

    /// <summary>Multiplies the scale by <paramref name="factor"/>, keeping the image point under (viewX, viewY) fixed on screen.</summary>
    public Viewport ZoomAt(double factor, double viewX, double viewY)
    {
        // TODO(human): implement zoom-around-cursor.
        throw new NotImplementedException();
    }
}
