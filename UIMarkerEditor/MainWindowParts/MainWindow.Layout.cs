using System.ComponentModel;
using System.Windows;

namespace UIMarkerEditor
{
    public partial class MainWindow
    {
        private void ApplySavedLayoutSettings()
        {
            WindowLayoutSettings layout = appDataStore.Settings.WindowLayout ?? new WindowLayoutSettings();
            ApplyWindowBounds(layout);
            ApplyWindowState(layout);
            WayMarkEditor_Control.ApplyLayoutSettings(layout);
            BackupRestore_Control.ApplyLayoutSettings(layout);
            CharacterProfiles_Control.ApplyLayoutSettings(layout);
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            SaveLayoutSettings();
        }

        private void SaveLayoutSettings()
        {
            WindowLayoutSettings layout = appDataStore.Settings.WindowLayout ?? new WindowLayoutSettings();
            Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;

            if (IsFinitePositive(bounds.Width) && IsFinitePositive(bounds.Height))
            {
                layout.Left = bounds.Left;
                layout.Top = bounds.Top;
                layout.Width = Math.Max(bounds.Width, MinWidth);
                layout.Height = Math.Max(bounds.Height, MinHeight);
            }

            layout.WindowState = WindowState == WindowState.Maximized
                ? nameof(WindowState.Maximized)
                : nameof(WindowState.Normal);

            WayMarkEditor_Control.CaptureLayoutSettings(layout);
            BackupRestore_Control.CaptureLayoutSettings(layout);
            CharacterProfiles_Control.CaptureLayoutSettings(layout);

            appDataStore.Settings.WindowLayout = layout;
            try
            {
                appDataStore.SaveSettings(appDataStore.Settings);
            }
            catch (InvalidOperationException)
            {
                // Keep the corrupted settings file untouched; the startup warning explains the repair path.
            }
        }

        private void ApplyWindowBounds(WindowLayoutSettings layout)
        {
            if (!IsFinitePositive(layout.Width) || !IsFinitePositive(layout.Height)) return;

            Rect savedBounds = new(
                layout.Left,
                layout.Top,
                Math.Max(layout.Width, MinWidth),
                Math.Max(layout.Height, MinHeight));
            if (!IsUsableWindowBounds(savedBounds)) return;

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = savedBounds.Left;
            Top = savedBounds.Top;
            Width = savedBounds.Width;
            Height = savedBounds.Height;
        }

        private void ApplyWindowState(WindowLayoutSettings layout)
        {
            WindowState = string.Equals(layout.WindowState, nameof(WindowState.Maximized), StringComparison.Ordinal)
                ? WindowState.Maximized
                : WindowState.Normal;
        }

        private static bool IsFinitePositive(double value)
        {
            return double.IsFinite(value) && value > 0;
        }

        private static bool IsUsableWindowBounds(Rect bounds)
        {
            if (!double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Top)) return false;

            Rect virtualScreen = new(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);
            return virtualScreen.IntersectsWith(bounds);
        }
    }
}
