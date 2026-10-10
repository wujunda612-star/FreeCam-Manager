using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeCamManager.ViewModels;
using FreeCamManager.Controls;

namespace FreeCamManager.Views;

public partial class CustomCategoryView : UserControl
{
    private Point _dragStart;
    private ArtifactRowViewModel? _dragRow;
    private bool _dragStarted;
    public CustomCategoryView() => InitializeComponent();

    private void CustomList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject)?.DataContext
            is not ArtifactRowViewModel row) return;
        if (FindAncestor<StatusBadge>(e.OriginalSource as DependencyObject) is not null) return;
        CustomList.SelectedItem = row;
        if (row.StartTestCommand.CanExecute(null)) row.StartTestCommand.Execute(null);
        e.Handled = true;
    }

    private void CustomList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject) is { } row)
        {
            row.IsSelected = true;
            row.Focus();
        }
    }

    private void ResultBadge_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragRow = (sender as FrameworkElement)?.DataContext as ArtifactRowViewModel;
        _dragStart = e.GetPosition(this);
        _dragStarted = false;
    }
    private void ResultBadge_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragRow is null) return;
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var path = _dragRow.GetDraggableResultPath();
        _dragStarted = true;
        _dragRow = null;
        if (!System.IO.File.Exists(path)) return;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy);
        e.Handled = true;
    }
    private void ResultBadge_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var row = (sender as FrameworkElement)?.DataContext as ArtifactRowViewModel;
        _dragRow = null;
        if (_dragStarted) { _dragStarted = false; e.Handled = true; return; }
        if (row is not null && row.OpenResultCommand.CanExecute(null))
            row.OpenResultCommand.Execute(null);
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T wanted) return wanted;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
