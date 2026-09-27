using System.Windows;
using System.Windows.Input;
using FreeCamManager.Services;

namespace FreeCamManager.Dialogs;

public partial class TextEditDialog : Window
{
    public string Value => Editor.Text;

    public TextEditDialog(string title, string label, string value, bool multiline)
    {
        InitializeComponent();
        Title = title;
        LabelText.Text = label;
        Editor.Text = value;
        Editor.AcceptsReturn = multiline;
        Editor.TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap;
        Editor.MinHeight = multiline ? 120 : 38;
        SourceInitialized += (_, _) => WindowChromeService.TryEnableRoundedCorners(this);
        Loaded += (_, _) => { Editor.Focus(); Editor.CaretIndex = Editor.Text.Length; };
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Ok_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
}
