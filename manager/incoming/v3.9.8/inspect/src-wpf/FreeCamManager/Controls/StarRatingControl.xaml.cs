using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FreeCamManager.Controls;

public partial class StarRatingControl : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(StarRatingControl),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged, CoerceValue));

    public int Value { get => (int)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    public StarRatingControl()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateSegments();
    }

    private static object CoerceValue(DependencyObject d, object baseValue) => Math.Clamp((int)baseValue, 0, 5);
    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((StarRatingControl)d).UpdateSegments();

    private void Segment_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border segment || !int.TryParse(segment.Tag?.ToString(), out var rating)) return;
        Value = Value == rating ? 0 : rating;
        e.Handled = true;
    }

    private void UpdateSegments()
    {
        var segments = new (Border Segment, int Rating)[]
        {
            (Segment5, 5), (Segment4, 4), (Segment3, 3), (Segment2, 2), (Segment1, 1)
        };
        foreach (var (segment, rating) in segments)
            segment.SetResourceReference(Border.BackgroundProperty, Value >= rating ? "StarActiveBrush" : "StarInactiveBrush");
    }
}
