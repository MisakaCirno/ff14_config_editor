using System;
using System.Windows;

namespace UIMarkerEditor
{
    public partial class MainWindow
    {
        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SystemCommands.CloseWindow(this);
        }

        private void MinimizeWindow_Button_Click(object sender, RoutedEventArgs e)
        {
            SystemCommands.MinimizeWindow(this);
        }

        private void MaximizeRestoreWindow_Button_Click(object sender, RoutedEventArgs e)
        {
            ToggleWindowMaximizeRestore();
        }

        private void CloseWindow_Button_Click(object sender, RoutedEventArgs e)
        {
            SystemCommands.CloseWindow(this);
        }

        private void Window_StateChanged(object? sender, EventArgs e)
        {
            UpdateMaximizeRestoreButton();
        }

        private void ToggleWindowMaximizeRestore()
        {
            if (WindowState == WindowState.Maximized)
            {
                SystemCommands.RestoreWindow(this);
            }
            else
            {
                SystemCommands.MaximizeWindow(this);
            }
        }

        private void UpdateMaximizeRestoreButton()
        {
            if (!IsInitialized) return;

            bool isMaximized = WindowState == WindowState.Maximized;
            MaximizeIcon_Rectangle.Visibility = isMaximized ? Visibility.Collapsed : Visibility.Visible;
            RestoreIcon_Path.Visibility = isMaximized ? Visibility.Visible : Visibility.Collapsed;
            MaximizeRestoreWindow_Button.ToolTip = isMaximized ? "还原" : "最大化";
        }
    }
}
