using System.Windows;
using System.Windows.Controls;

namespace FreeCamManager.Controls;

public partial class StatusBadge : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(StatusBadge), new PropertyMetadata("", OnTextChanged));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public StatusBadge()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateBadge();
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((StatusBadge)d).UpdateBadge();

    private void UpdateBadge()
    {
        if (BadgeText is null || BadgeBorder is null) return;
        BadgeText.Text = string.IsNullOrWhiteSpace(Text) ? "—" : Text;
        var key = Text == "已测试" ? "BadgeTestedBrush" : Text == "待测试" ? "BadgePendingBrush" : Text == "测试中" ? "AccentSoftBrush" : "PanelElevatedBrush";
        BadgeBorder.SetResourceReference(Border.BackgroundProperty, key);
    }
}
