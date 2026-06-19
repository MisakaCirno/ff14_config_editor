using System.Windows;

namespace UIMarkerEditor;

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
        if (owner != null)
        {
            dialog.Owner = owner;
        }

        dialog.ShowDialog();
        return dialog.Result;
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
