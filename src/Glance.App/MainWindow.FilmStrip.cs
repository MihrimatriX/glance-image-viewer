using System.Numerics;
using Glance.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Glance.App;

/// <summary>Film strip cards: dock-style emphasis around the current image, centred scrolling, hover lift.</summary>
public sealed partial class MainWindow
{
    private ScrollViewer? _filmScroller;
    private bool _centerQueued;

    private void InitFilmStrip()
    {
        // Items moved or arrived: distances changed for cards that are already on screen.
        ViewModel.Film.CollectionChanged += (_, _) => DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, ApplyCardEmphasis);
    }

    /// <summary>The current image changed: re-weigh the cards and bring the current one to the middle.</summary>
    private void OnCurrentCardChanged()
    {
        ApplyCardEmphasis();
        CenterCurrentCard();
    }

    private void ApplyCardEmphasis()
    {
        if (FilmStrip.ItemsPanelRoot is not { } panel)
            return;
        foreach (var child in panel.Children)
        {
            if (child is SelectorItem container)
                Emphasize(container, FilmStrip.IndexFromContainer(container));
        }
    }

    private void Emphasize(SelectorItem container, int index)
    {
        if (container.ContentTemplateRoot is not UIElement card || index < 0)
            return;
        var (scale, opacity) = Motion.CardEmphasis(ViewModel.Index < 0 ? int.MaxValue : index - ViewModel.Index);
        card.Scale = new Vector3(scale, scale, 1);
        card.Opacity = opacity;
        // The current card is drawn over its neighbours while it grows.
        Microsoft.UI.Xaml.Controls.Canvas.SetZIndex(container, index == ViewModel.Index ? 1 : 0);
    }

    /// <summary>Keeps the current card in the middle of the strip, scrolling smoothly; falls back to ScrollIntoView while it isn't realized.</summary>
    private void CenterCurrentCard()
    {
        var index = ViewModel.Index;
        if (index < 0 || index >= ViewModel.Film.Count)
            return;
        _filmScroller ??= FindChild<ScrollViewer>(FilmStrip);
        if (_filmScroller is not { Content: UIElement content } scroller || FilmStrip.ContainerFromIndex(index) is not FrameworkElement container)
        {
            FilmStrip.ScrollIntoView(ViewModel.Film[index]);
            if (!_centerQueued)
            {
                _centerQueued = true;   // one retry, after layout has realized the container
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
                {
                    CenterCurrentCard();
                    _centerQueued = false;
                });
            }
            return;
        }
        var x = container.TransformToVisual(content).TransformPoint(new Point(0, 0)).X;
        var target = x + container.ActualWidth / 2 - scroller.ViewportWidth / 2;
        scroller.ChangeView(Math.Clamp(target, 0, scroller.ScrollableWidth), null, null, disableAnimation: !_uiSettings.AnimationsEnabled);
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                return match;
            if (FindChild<T>(child) is { } deeper)
                return deeper;
        }
        return null;
    }

    private void Film_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        var item = (FilmItem)args.Item;
        if (args.InRecycleQueue)
        {
            MainViewModel.UnloadThumbnail(item);
            return;
        }
        if (args.ItemContainer is { } container)
            Emphasize(container, args.ItemIndex);
        _ = ViewModel.LoadThumbnailAsync(item);
    }

    // The card's face lifts a little under the pointer (Scale/TranslationTransition in XAML animate it).
    private void Thumb_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        var face = (UIElement)sender;
        face.Scale = new Vector3(1.08f, 1.08f, 1);
        face.Translation = new Vector3(0, -4, 0);
    }

    private void Thumb_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        var face = (UIElement)sender;
        face.Scale = Vector3.One;
        face.Translation = Vector3.Zero;
    }
}
