using System.Windows;

namespace UIMarkerEditor;

public sealed record AppMessageBoxCheckBoxResult(MessageBoxResult Result, bool IsChecked);

public static class AppMessageBox
{
    public static MessageBoxResult Show(
        Window? owner,
        string messageBoxText,
        string caption,
        MessageBoxButton button,
        MessageBoxImage icon = MessageBoxImage.None)
    {
        AppMessageBoxDialog dialog = new(messageBoxText, caption, button, icon);
        DialogOwnerHelper.ConfigureOwnedDialog(dialog, owner);
        dialog.ShowDialog();
        return dialog.Result;
    }

    public static AppMessageBoxCheckBoxResult ShowWithCheckBox(
        Window? owner,
        string messageBoxText,
        string caption,
        MessageBoxButton button,
        MessageBoxImage icon,
        string checkBoxText,
        bool isChecked = false)
    {
        AppMessageBoxDialog dialog = new(messageBoxText, caption, button, icon, checkBoxText, isChecked);
        DialogOwnerHelper.ConfigureOwnedDialog(dialog, owner);
        dialog.ShowDialog();
        return new AppMessageBoxCheckBoxResult(dialog.Result, dialog.IsOptionChecked);
    }

    public static MessageBoxResult Show(
        string messageBoxText,
        string caption,
        MessageBoxButton button,
        MessageBoxImage icon = MessageBoxImage.None)
    {
        return Show(null, messageBoxText, caption, button, icon);
    }
}
