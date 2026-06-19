using Microsoft.Win32;
using System.Windows;

namespace UIMarkerEditor;

internal static class DialogOwnerHelper
{
    public static Window? Resolve(Window? preferredOwner = null)
    {
        if (IsUsableOwner(preferredOwner))
        {
            return preferredOwner;
        }

        Window? mainWindow = Application.Current?.MainWindow;
        if (IsUsableOwner(mainWindow))
        {
            return mainWindow;
        }

        return Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(IsUsableOwner);
    }

    public static void ConfigureOwnedDialog(Window dialog, Window? preferredOwner = null)
    {
        Window? owner = Resolve(preferredOwner);
        if (owner != null && !ReferenceEquals(dialog, owner))
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            return;
        }

        dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    public static bool? ShowCommonDialog(CommonDialog dialog, Window? preferredOwner = null)
    {
        Window? owner = Resolve(preferredOwner);
        return owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }

    private static bool IsUsableOwner(Window? window)
    {
        return window is { IsLoaded: true, IsVisible: true } &&
            window.WindowState != WindowState.Minimized;
    }
}