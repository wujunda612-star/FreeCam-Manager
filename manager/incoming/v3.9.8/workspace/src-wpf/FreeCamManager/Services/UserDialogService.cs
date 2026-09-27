using System.Windows;
using FreeCamManager.Dialogs;

namespace FreeCamManager.Services;

public sealed class UserDialogService
{
    public bool Confirm(string title, string message) => MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    public void Info(string title, string message) => MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    public void Error(string title, string message) => MessageBox.Show(Application.Current.MainWindow, message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public string? EditText(string title, string label, string value, bool multiline)
    {
        var dialog = new TextEditDialog(title, label, value, multiline) { Owner = Application.Current.MainWindow };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

}
