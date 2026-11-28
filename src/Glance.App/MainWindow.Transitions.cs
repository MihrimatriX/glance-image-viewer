using Glance.Core;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.UI.ViewManagement;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Glance.App;

/// <summary>
/// Navigation transitions (slide, cross-fade, zoom, parallax), the film-strip "hero" zoom, and finger / touchpad swipes.
/// The shapes live in <see cref="Motion"/> (unit-tested); this file only keeps the state and draws it.
/// </summary>
public sealed partial class MainWindow
{
    private const double HeroMs = 420;           // film-strip card grows into the full image
    private const double SwipeReturnMs = 220;    // a swipe that didn't commit springs back
    private const double SwipeCooldownMs = 350;  // touchpad momentum after a committed swipe must not start another

    private readonly UISettings _uiSettings = new();
    private TransitionKind _transition = TransitionKind.Slide;

    // The outgoing image keeps its own viewport while it leaves.
    private (LoadedImage Image, Viewport View, int Rotation, bool FlipH, bool FlipV)? _outgoing;
    private double _transitionStart, _transitionMs;
    private TransitionKind _activeKind;
    private int _transitionDirection;

    // Hero: the card the user clicked, in canvas physical px, and when (a slow decode shouldn't zoom from a stale card).
    private Rect? _heroFrom;
    private double _heroStamp;
    private bool _heroActive;
    private Rect _heroRect;

    // Swipe: horizontal offset of the current image while a finger / two fingers drag it (physical px).
    private double _swipeOffset, _swipeReturnFrom, _swipeReturnStart = -1, _swipeCarry, _swipeCooldownUntil;
    private DispatcherQueueTimer? _swipeIdleTimer;

    private void InitTransitions()
    {
        _swipeIdleTimer = DispatcherQueue.CreateTimer();
        _swipeIdleTimer.Interval = TimeSpan.FromMilliseconds(140);
        _swipeIdleTimer.IsRepeating = false;
        _swipeIdleTimer.Tick += async (_, _) => await FinishSwipeAsync(0);
    }

    private void BeginTransition()
    {
        var now = Now;
        var stillRunning = _outgoing is not null && now - _transitionStart < _transitionMs;
        _outgoing = (_image!, _view.Pan(_swipeOffset, 0), _rotation, _flipH, _flipV);
        _loader.Previous = _image;
        _transitionStart = now;
        _transitionDirection = ViewModel.LastDirection;

        // A swipe already moved the old image under the finger: finish it as one continuous page turn.
        _swipeCarry = _transitionDirection != 0 ? Math.Abs(_swipeOffset) : 0;
        _swipeOffset = 0;
        _swipeReturnStart = -1;

        _heroActive = _heroFrom is not null && now - _heroStamp < 1000;
        if (_heroActive)
            _heroRect = _heroFrom!.Value;
        _heroFrom = null;

        (_activeKind, _transitionMs) =
            !_uiSettings.AnimationsEnabled ? (TransitionKind.Crossfade, Motion.ReducedMs)
            : _slideshow ? (TransitionKind.Crossfade, Motion.SlideshowMs)
            : _heroActive ? (TransitionKind.Crossfade, HeroMs)
            : _swipeCarry > 0 ? (TransitionKind.Parallax, 240.0)
            : _transition == TransitionKind.None ? (TransitionKind.None, 0.0)
            : stillRunning ? (_transition, Motion.RapidMs)   // holding an arrow key: keep up, don't replay the full move
            : (_transition, Motion.DurationMs(_transition));

        if (_transitionMs <= 0)
            EndTransition();
    }

    private void EndTransition()
    {
        _outgoing = null;
        _loader.Previous = null;
        _heroActive = false;
        _swipeCarry = 0;
    }

    /// <summary>Draws the outgoing image (if a transition is running) and returns the incoming image's viewport and opacity.</summary>
    private (Viewport View, float Opacity, bool Animating) DrawTransition(CanvasDrawingSession ds, LoadedImage? image, double now, double vw, double vh)
    {
        var swipe = SwipeOffset(now);
        if (_outgoing is not { } outgoing)
            return (_view.Pan(swipe, 0), 1, _swipeReturnStart >= 0);

        var t = (now - _transitionStart) / _transitionMs;
        if (t >= 1)
        {
            EndTransition();
            return (_view.Pan(swipe, 0), 1, false);
        }

        TransitionFrame outFrame, inFrame;
        if (_swipeCarry > 0)
        {
            // The pair moves together: the new image is attached one view-width behind the old one.
            var e = Motion.Spring(t);
            var d = Math.Sign(_transitionDirection);
            var rest = Math.Max(0, vw - _swipeCarry);
            outFrame = new TransitionFrame(-d * rest * e, 1, 1);
            inFrame = new TransitionFrame(d * rest * (1 - e), 1, 1);
        }
        else
        {
            (outFrame, inFrame) = Motion.Evaluate(_activeKind, t, _transitionDirection, vw);
        }

        if (outFrame.Opacity > 0.004f)
            DrawImage(ds, outgoing.Image, outgoing.Image.Bitmap, Place(outgoing.View, outFrame, vw, vh),
                outgoing.Rotation, outgoing.FlipH, outgoing.FlipV, outFrame.Opacity);

        if (image is null)
            return (_view, 1, true);
        if (_heroActive)
        {
            var e = Motion.Spring(t);
            return (HeroView(image, e), (float)Math.Min(1, t * 5), true);
        }
        return (Place(_view, inFrame, vw, vh), inFrame.Opacity, true);
    }

    private static Viewport Place(Viewport view, TransitionFrame frame, double vw, double vh)
    {
        var placed = Math.Abs(frame.Scale - 1) < 1e-6 ? view : view.ZoomAt(frame.Scale, vw / 2, vh / 2);
        return placed.Pan(frame.OffsetX, 0);
    }

    /// <summary>From the clicked card (image fitted inside it, as the card shows it) to the resting view.</summary>
    private Viewport HeroView(LoadedImage image, double e)
    {
        var (iw, ih) = RotatedSize(image);
        var r = _heroRect;
        var s = Math.Min(r.Width / iw, r.Height / ih);
        var start = new Viewport(s, r.X + (r.Width - iw * s) / 2, r.Y + (r.Height - ih * s) / 2);
        return Lerp(start, _view, e);
    }

    /// <summary>Remembers where the clicked card sits so the image can grow out of it.</summary>
    private void CaptureHero(FilmItem item)
    {
        if (!_uiSettings.AnimationsEnabled || Comparing || _slideshow
            || FilmStrip.ContainerFromItem(item) is not SelectorItem { ContentTemplateRoot: FrameworkElement card })
            return;
        var bounds = card.TransformToVisual(Canvas).TransformBounds(new Rect(0, 0, card.ActualWidth, card.ActualHeight));
        _heroFrom = new Rect(bounds.X * DpiScale, bounds.Y * DpiScale, bounds.Width * DpiScale, bounds.Height * DpiScale);
        _heroStamp = Now;
    }

    // ---------- Swipe ----------

    private bool CanSwipe => _image is not null && _fit && !Comparing && !_cropMode && ViewModel.Film.Count > 1 && _outgoing is null;

    private double SwipeOffset(double now)
    {
        if (_swipeReturnStart < 0)
            return _swipeOffset;
        var t = (now - _swipeReturnStart) / SwipeReturnMs;
        if (t >= 1)
        {
            _swipeReturnStart = -1;
            _swipeOffset = 0;
            return 0;
        }
        _swipeOffset = _swipeReturnFrom * (1 - Motion.Spring(t));
        return _swipeOffset;
    }

    private void DragSwipe(double dxPhysical)
    {
        _swipeReturnStart = -1;
        // Rubber band at the ends of the folder: the image follows at a third of the finger's speed.
        var atEnd = (dxPhysical < 0 && ViewModel.Index >= ViewModel.Film.Count - 1) || (dxPhysical > 0 && ViewModel.Index <= 0);
        _swipeOffset += atEnd ? dxPhysical / 3 : dxPhysical;
        Canvas.Invalidate();
    }

    private void ReturnSwipe()
    {
        if (_swipeOffset == 0)
            return;
        _swipeReturnFrom = _swipeOffset;
        _swipeReturnStart = Now;
        Canvas.Invalidate();
    }

    private async Task FinishSwipeAsync(double velocity)
    {
        _swipeIdleTimer?.Stop();
        if (_swipeOffset == 0 || _swipeReturnStart >= 0)
            return;
        var step = Motion.SwipeOutcome(_swipeOffset, velocity, PaneSize.W);
        var canGo = step > 0 ? ViewModel.Index < ViewModel.Film.Count - 1 : step < 0 && ViewModel.Index > 0;
        if (!canGo)
        {
            ReturnSwipe();
            return;
        }
        _swipeCooldownUntil = Now + SwipeCooldownMs;
        StopSlideshow();
        await (step > 0 ? ViewModel.NextAsync() : ViewModel.PreviousAsync());
        ReturnSwipe();   // the next image failed to load: nothing took the offset over
    }

    /// <summary>Touch: a one-finger horizontal drag on a fitted image turns pages instead of panning. Returns true if it took the delta.</summary>
    private bool SwipeManipulation(ManipulationDeltaRoutedEventArgs e)
    {
        if (!CanSwipe || Math.Abs(e.Cumulative.Scale - 1) > 0.02f)
        {
            if (_swipeOffset != 0 && _swipeReturnStart < 0)
                ReturnSwipe();
            return false;
        }
        StopMotion();
        DragSwipe(e.Delta.Translation.X * DpiScale);
        return true;
    }

    private async void Canvas_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
    {
        if (e.PointerDeviceType == PointerDeviceType.Mouse || _swipeOffset == 0)
            return;
        await FinishSwipeAsync(e.Velocities.Linear.X * DpiScale);
    }

    /// <summary>Precision touchpad: a two-finger horizontal swipe on a fitted image turns pages. Returns true if it took the event.</summary>
    private bool SwipeWheel(double horizontalDeltaPhysical)
    {
        if (Now < _swipeCooldownUntil)
            return true;   // swallow the momentum tail after a committed swipe (it would pan the next image)
        if (!CanSwipe)
            return false;
        DragSwipe(-horizontalDeltaPhysical);
        _swipeIdleTimer?.Stop();
        _swipeIdleTimer?.Start();   // fingers lifted = no more wheel events for a moment
        return true;
    }
}
