using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FreeCamManager.Controls;
using FreeCamManager.ViewModels;

namespace FreeCamManager.Views;

public partial class DevelopmentView : UserControl
{
    private Point _dragStart;
    private ArtifactRowViewModel? _dragRow;
    private bool _resultDragStarted;

    public DevelopmentView() => InitializeComponent();


    private void HeaderGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is DevelopmentViewModel vm) vm.SetAutoFileColumnWidth(e.NewSize.Width);
    }

    private void FileColumnThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (DataContext is DevelopmentViewModel vm) vm.AdjustFileColumnWidth(e.HorizontalChange);
        e.Handled = true;
    }

    private void FeatureColumnThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (DataContext is DevelopmentViewModel vm) vm.AdjustFeatureColumnWidth(e.HorizontalChange);
        e.Handled = true;
    }

    private void StageColumnThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (DataContext is DevelopmentViewModel vm) vm.AdjustStageColumnWidth(e.HorizontalChange);
        e.Handled = true;
    }

    private async void ColumnThumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (DataContext is DevelopmentViewModel vm) await vm.PersistColumnWidthsAsync();
        e.Handled = true;
    }

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
        if (FindAncestor<CompactPickerControl>(source) is not null ||
            FindAncestor<StarRatingControl>(source) is not null ||
            FindAncestor<LockToggleControl>(source) is not null ||
            FindAncestor<StatusBadge>(source) is not null ||
            FindAncestor<ButtonBase>(source) is not null) return;
        if (FindAncestor<ListViewItem>(source)?.DataContext is not ArtifactRowViewModel row) return;
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
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var row = _dragRow;
        var path = row.GetDraggableResultPath();
        _resultDragStarted = true;
        _dragRow = null;
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            row.NotifyDragNotFound();
            return;
        }
        var data = new DataObject(DataFormats.FileDrop, new[] { path });
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);
        e.Handled = true;
    }

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox or ComboBox) return;
        if (DataContext is not DevelopmentViewModel { SelectedRow: { } row }) return;
        var rating = e.Key switch
        {
            Key.D0 or Key.NumPad0 => 0, Key.D1 or Key.NumPad1 => 1, Key.D2 or Key.NumPad2 => 2,
            Key.D3 or Key.NumPad3 => 3, Key.D4 or Key.NumPad4 => 4, Key.D5 or Key.NumPad5 => 5, _ => -1
        };
        if (rating < 0) return;
        row.Rating = rating; e.Handled = true;
    }


    private void ResultBadge_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var row = (sender as FrameworkElement)?.DataContext as ArtifactRowViewModel;
        _dragRow = null;
        if (_resultDragStarted)
        {
            _resultDragStarted = false;
            e.Handled = true;
            return;
        }

        if (row is null || !string.Equals(row.TestStatus, "已测试", StringComparison.Ordinal)) return;
        if (row.OpenResultCommand.CanExecute(null)) row.OpenResultCommand.Execute(null);
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
