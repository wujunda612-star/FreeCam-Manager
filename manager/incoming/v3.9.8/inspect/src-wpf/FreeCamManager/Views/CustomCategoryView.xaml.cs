using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FreeCamManager.Controls;
using FreeCamManager.ViewModels;

namespace FreeCamManager.Views;

public partial class CustomCategoryView : UserControl
{
    private Point _dragStart;
    private ArtifactRowViewModel? _dragRow;
    private bool _resultDragStarted;

    public CustomCategoryView() => InitializeComponent();

    private void List_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject) is { } row)
        {
            row.IsSelected = true;
            row.Focus();
        }
    }

    private void VersionList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        var source = e.OriginalSource as DependencyObject;
        if (FindAncestor<StatusBadge>(source) is not null ||
            FindAncestor<ButtonBase>(source) is not null) return;
        if (FindAncestor<ListViewItem>(source)?.DataContext is not ArtifactRowViewModel row || !row.CanStartTest)
            return;
        VersionList.SelectedItem = row;
        if (row.StartTestCommand.CanExecute(null)) row.StartTestCommand.Execute(null);
        e.Handled = true;
    }

    private void ResultBadge_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _dragRow = (sender as FrameworkElement)?.DataContext as ArtifactRowViewModel;
        _resultDragStarted = false;
    }

    private void ResultBadge_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragRow is null) return;
        var here = e.GetPosition(this);
        if (Math.Abs(here.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(here.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var row = _dragRow;
        _resultDragStarted = true;
        _dragRow = null;
        var path = row.GetDraggableResultPath();
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            row.NotifyDragNotFound();
            return;
        }
        DragDrop.DoDragDrop((DependencyObject)sender,
            new DataObject(DataFormats.FileDrop, new[] { path }), DragDropEffects.Copy);
        e.Handled = true;
    }

    private void ResultBadge_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var row = (sender as FrameworkElement)?.DataContext as ArtifactRowViewModel;
        _dragRow = null;
        if (_resultDragStarted) { _resultDragStarted = false; e.Handled = true; return; }
        if (row is null || row.TestStatus != "已测试") return;
        if (row.OpenResultCommand.CanExecute(null)) row.OpenResultCommand.Execute(null);
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? obj) where T : DependencyObject
    {
        while (obj is not null)
        {
            if (obj is T result) return result;
            obj = VisualTreeHelper.GetParent(obj);
        }
        return null;
    }
}
