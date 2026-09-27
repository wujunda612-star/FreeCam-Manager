using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FreeCamManager.Controls;

public partial class LockToggleControl : UserControl
{
    public static readonly DependencyProperty IsLockedProperty = DependencyProperty.Register(
        nameof(IsLocked), typeof(bool), typeof(LockToggleControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnLockedChanged));

    public bool IsLocked { get => (bool)GetValue(IsLockedProperty); set => SetValue(IsLockedProperty, value); }

    public LockToggleControl()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateIcon();
    }

    private void Button_Click(object sender, RoutedEventArgs e) => IsLocked = !IsLocked;
    private static void OnLockedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((LockToggleControl)d).UpdateIcon();

    private void UpdateIcon()
    {
        if (TryFindResource(IsLocked ? "IconLock" : "IconUnlock") is Geometry geometry) LockPath.Data = geometry;
        ToolTip = IsLocked ? "已锁定：禁止删除和归档" : "未锁定";
    }
}
