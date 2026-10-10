using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace FreeCamManager.Controls;

public partial class CompactPickerControl : UserControl
{
    private static CompactPickerControl? _openPicker;
    private Window? _ownerWindow;
    public static readonly DependencyProperty SelectedValueProperty = DependencyProperty.Register(
        nameof(SelectedValue), typeof(string), typeof(CompactPickerControl),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedValueChanged));

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(CompactPickerControl),
        new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty DimUnmarkedProperty = DependencyProperty.Register(
        nameof(DimUnmarked), typeof(bool), typeof(CompactPickerControl),
        new PropertyMetadata(false, OnDimUnmarkedChanged));

    public string SelectedValue
    {
        get => (string)GetValue(SelectedValueProperty);
        set => SetValue(SelectedValueProperty, value);
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public bool DimUnmarked
    {
        get => (bool)GetValue(DimUnmarkedProperty);
        set => SetValue(DimUnmarkedProperty, value);
    }

    public CompactPickerControl()
    {
        InitializeComponent();
        Loaded += (_, _) => { RebuildItems(); UpdateText(); };
        Unloaded += (_, _) => ClosePicker();
        PickerPopup.Closed += (_, _) => { if (ReferenceEquals(_openPicker, this)) _openPicker = null; DetachOwnerHandlers(); };
    }

    private static void OnSelectedValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((CompactPickerControl)d).UpdateText();

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((CompactPickerControl)d).RebuildItems();

    private static void OnDimUnmarkedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (CompactPickerControl)d;
        control.UpdateText();
        control.RebuildItems();
    }

    private void PickerBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (PickerPopup.IsOpen) ClosePicker();
        else OpenPicker();
        e.Handled = true;
    }

    private void OpenPicker()
    {
        if (_openPicker is not null && !ReferenceEquals(_openPicker, this))
            _openPicker.ClosePicker();

        _openPicker = this;
        _ownerWindow = Window.GetWindow(this);
        if (_ownerWindow is not null)
        {
            _ownerWindow.PreviewMouseDown -= OwnerWindow_PreviewMouseDown;
            _ownerWindow.PreviewMouseDown += OwnerWindow_PreviewMouseDown;
            _ownerWindow.PreviewKeyDown -= OwnerWindow_PreviewKeyDown;
            _ownerWindow.PreviewKeyDown += OwnerWindow_PreviewKeyDown;
        }
        PickerPopup.IsOpen = true;
    }

    private void ClosePicker()
    {
        PickerPopup.IsOpen = false;
        if (ReferenceEquals(_openPicker, this)) _openPicker = null;
        DetachOwnerHandlers();
    }

    private void DetachOwnerHandlers()
    {
        if (_ownerWindow is null) return;
        _ownerWindow.PreviewMouseDown -= OwnerWindow_PreviewMouseDown;
        _ownerWindow.PreviewKeyDown -= OwnerWindow_PreviewKeyDown;
        _ownerWindow = null;
    }

    private void OwnerWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideThisControl(e.OriginalSource as DependencyObject)) return;
        ClosePicker();
    }

    private void OwnerWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        ClosePicker();
        e.Handled = true;
    }

    private bool IsInsideThisControl(DependencyObject? source)
    {
        for (var current = source; current is not null;)
        {
            if (ReferenceEquals(current, this)) return true;
            current = current is Visual or Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return false;
    }

    private void RebuildItems()
    {
        if (ItemsHost is null) return;
        ItemsHost.Children.Clear();
        if (ItemsSource is null) return;

        foreach (var item in ItemsSource)
        {
            var text = item?.ToString() ?? "";
            var label = new TextBlock
            {
                Text = text,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            label.SetResourceReference(TextBlock.ForegroundProperty,
                DimUnmarked && text == "未标记" ? "TextMutedBrush" : "TextPrimaryBrush");
            var row = new Border
            {
                Tag = text,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(9, 7, 9, 7),
                Margin = new Thickness(1),
                Child = label,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand
            };
            row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "HoverBrush");
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
            row.PreviewMouseLeftButtonDown += Option_PreviewMouseLeftButtonDown;
            row.PreviewMouseLeftButtonUp += Option_PreviewMouseLeftButtonUp;
            ItemsHost.Children.Add(row);
        }
    }

    private void Option_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: string value } row) return;
        SelectedValue = value;
        row.CaptureMouse();
        e.Handled = true;
    }

    private void Option_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border row) return;
        e.Handled = true;
        if (row.IsMouseCaptured) row.ReleaseMouseCapture();
        ClosePicker();
    }

    private void UpdateText()
    {
        if (ValueText is null) return;
        ValueText.Text = string.IsNullOrWhiteSpace(SelectedValue) ? "未标记" : SelectedValue;
        ValueText.SetResourceReference(TextBlock.ForegroundProperty,
            DimUnmarked && ValueText.Text == "未标记" ? "TextMutedBrush" : "TextPrimaryBrush");
    }
}
