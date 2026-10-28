using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Glance.App;

/// <summary>Show / hide motion for the floating panels, run by the compositor off the UI thread.</summary>
public sealed partial class MainWindow
{
    private const double ShowMs = 200, HideMs = 140;

    /// <summary>
    /// Whenever <paramref name="element"/>'s Visibility flips (code or x:Bind), it fades in from <paramref name="offset"/>
    /// and fades back out towards it. Implicit animations: XAML plays them on every change, so callers keep just setting Visibility.
    /// </summary>
    private static void AnimateVisibility(UIElement element, Vector3 offset)
    {
        var compositor = ElementCompositionPreview.GetElementVisual(element).Compositor;
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        ElementCompositionPreview.SetImplicitShowAnimation(element, Motion(compositor, offset, Vector3.Zero, 0, 1, ShowMs,
            compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f))));     // Fluent "decelerate"
        ElementCompositionPreview.SetImplicitHideAnimation(element, Motion(compositor, Vector3.Zero, offset, 1, 0, HideMs,
            compositor.CreateCubicBezierEasingFunction(new Vector2(0.7f, 0f), new Vector2(1f, 0.5f))));       // Fluent "accelerate"
    }

    private static CompositionAnimationGroup Motion(Compositor compositor, Vector3 from, Vector3 to, float fromOpacity, float toOpacity,
        double ms, CompositionEasingFunction easing)
    {
        var duration = TimeSpan.FromMilliseconds(ms);
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Target = "Opacity";
        fade.InsertKeyFrame(0, fromOpacity);
        fade.InsertKeyFrame(1, toOpacity, easing);
        fade.Duration = duration;
        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.Target = "Translation";
        slide.InsertKeyFrame(0, from);
        slide.InsertKeyFrame(1, to, easing);
        slide.Duration = duration;
        var group = compositor.CreateAnimationGroup();
        group.Add(fade);
        group.Add(slide);
        return group;
    }
}
