using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FreeCamManager.ViewModels;

namespace FreeCamManager.Views;

public partial class HistoryView : UserControl
{
    public HistoryView() => InitializeComponent();

    private void HeaderGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is HistoryViewModel vm) vm.SetAutoFileColumnWidth(e.NewSize.Width);
    }

    private void FileColumnThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (DataContext is HistoryViewModel vm) vm.AdjustFileColumnWidth(e.HorizontalChange);
        e.Handled = true;
    }

    private void FeatureColumnThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (DataContext is HistoryViewModel vm) vm.AdjustFeatureColumnWidth(e.HorizontalChange);
        e.Handled = true;
    }

    private void StageColumnThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (DataContext is HistoryViewModel vm) vm.AdjustStageColumnWidth(e.HorizontalChange);
        e.Handled = true;
    }

    private async void ColumnThumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (DataContext is HistoryViewModel vm) await vm.PersistColumnWidthsAsync();
        e.Handled = true;
    }
    private void List_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) { if (FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject) is { } row) { row.IsSelected = true; row.Focus(); } }
    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox or ComboBox) return;
        if (DataContext is not HistoryViewModel { SelectedRow: { } row }) return;
        var rating = e.Key switch { Key.D0 or Key.NumPad0 => 0, Key.D1 or Key.NumPad1 => 1, Key.D2 or Key.NumPad2 => 2, Key.D3 or Key.NumPad3 => 3, Key.D4 or Key.NumPad4 => 4, Key.D5 or Key.NumPad5 => 5, _ => -1 };
        if (rating < 0) return; row.Rating = rating; e.Handled = true;
    }
    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject { while (current is not null) { if (current is T wanted) return wanted; current = VisualTreeHelper.GetParent(current); } return null; }
}
