using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FreeCamManager.Controls;

/// <summary>
/// List-local floating "back to top" button. Automatically hides while
/// no scrolling is possible or the list has not moved down far enough.
/// </summary>
public sealed class ScrollToTopButton : Button
{
    private const double RevealOffset = 80;

    public static readonly DependencyProperty TargetListProperty = DependencyProperty.Register(
        nameof(TargetList), typeof(ListView), typeof(ScrollToTopButton),
        new PropertyMetadata(null, OnTargetChanged));

    public ListView? TargetList
    {
        get => (ListView?)GetValue(TargetListProperty);
        set => SetValue(TargetListProperty, value);
    }

    public ScrollToTopButton()
    {
        Content = "↑ 置顶";
        Width = 74;
        Height = 32;
        Padding = new Thickness(9, 0, 9, 0);
        Margin = new Thickness(0, 0, 18, 16);
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Bottom;
        Cursor = Cursors.Hand;
        BorderThickness = new Thickness(1);
        Visibility = Visibility.Collapsed;
        Focusable = false;
        ToolTip = "回到当前列表顶部";
        SetResourceReference(StyleProperty, "RoundedButtonStyle");
        SetResourceReference(BackgroundProperty, "AccentSoftBrush");
        SetResourceReference(BorderBrushProperty, "AccentBrush");

        Click += (_, _) => FindScroller(TargetList)?.ScrollToTop();
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateButton));
    }

    private static void OnTargetChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var button = (ScrollToTopButton)dependencyObject;
        if (args.OldValue is ListView oldList)
        {
            oldList.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(button.OnScrollChanged));
            oldList.SizeChanged -= button.OnListSizeChanged;
        }
        if (args.NewValue is ListView nextList)
        {
            nextList.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(button.OnScrollChanged));
            nextList.SizeChanged += button.OnListSizeChanged;
        }
        button.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(button.UpdateButton));
    }

    private void OnListSizeChanged(object sender, SizeChangedEventArgs e) => UpdateButton();

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Ignore inner ScrollViewers (e.g. controls inside an item template).
        if (ReferenceEquals(e.OriginalSource, FindScroller(TargetList))) UpdateButton();
    }

    private void UpdateButton()
    {
        var viewer = FindScroller(TargetList);
        Visibility = viewer is { ScrollableHeight: > RevealOffset, VerticalOffset: > RevealOffset }
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private static ScrollViewer? FindScroller(DependencyObject? root)
    {
        if (root is null) return null;
        if (root is ScrollViewer viewer) return viewer;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var found = FindScroller(VisualTreeHelper.GetChild(root, i));
            if (found is not null) return found;
        }
        return null;
    }
}
