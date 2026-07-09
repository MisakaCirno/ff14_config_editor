using System.ComponentModel;
using System.Windows;
using FF14ConfigEditor;

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
            WayMarkFavorites_Control.ApplyLayoutSettings(layout);
            BackupRestore_Control.ApplyLayoutSettings(layout);
            CharacterProfiles_Control.ApplyLayoutSettings(layout);
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            if (isWayMarkFileLoading)
            {
                AppMessageBox.Show(
                    this,
                    "标点文件正在读取中，请稍候完成后再关闭工具。",
                    "正在读取标点文件",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                e.Cancel = true;
                return;
            }

            if (!TryPrepareWindowCloseChanges(
                    out bool shouldSaveWayMarks,
                    out bool shouldSaveFavorite,
                    out bool shouldSaveCharacter) ||
                !TryApplyWindowCloseChanges(shouldSaveWayMarks, shouldSaveFavorite, shouldSaveCharacter))
            {
                WayMarkFavorites_Control.ResumeAutoSaveIfNeeded();
                e.Cancel = true;
                return;
            }

            StopCurrentFileChangeMonitor();
            SaveLayoutSettings();
        }

        private bool TryPrepareWindowCloseChanges(
            out bool shouldSaveWayMarks,
            out bool shouldSaveFavorite,
            out bool shouldSaveCharacter)
        {
            shouldSaveWayMarks = false;
            shouldSaveFavorite = false;
            shouldSaveCharacter = false;

            if (!TryPrepareCloseWayMarkChanges(out shouldSaveWayMarks))
            {
                return false;
            }

            if (!WayMarkFavorites_Control.TryPrepareCloseChanges(out shouldSaveFavorite))
            {
                return false;
            }

            if (!CharacterProfiles_Control.TryPrepareCloseChanges(out shouldSaveCharacter))
            {
                return false;
            }

            return true;
        }

        private bool TryApplyWindowCloseChanges(
            bool shouldSaveWayMarks,
            bool shouldSaveFavorite,
            bool shouldSaveCharacter)
        {
            if (shouldSaveWayMarks && !SaveWayMarkFile(showSuccessMessage: false))
            {
                return false;
            }

            if (shouldSaveFavorite && !WayMarkFavorites_Control.SavePreparedCloseChanges())
            {
                return false;
            }

            if (shouldSaveCharacter && !CharacterProfiles_Control.SavePreparedCloseChanges())
            {
                return false;
            }

            return true;
        }

        private void SaveLayoutSettings()
        {
            AppSettings settings = appDataStore.CreateSettingsSnapshot();
            WindowLayoutSettings layout = settings.WindowLayout ?? new WindowLayoutSettings();
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
            WayMarkFavorites_Control.CaptureLayoutSettings(layout);
            BackupRestore_Control.CaptureLayoutSettings(layout);
            CharacterProfiles_Control.CaptureLayoutSettings(layout);

            settings.WindowLayout = layout;
            try
            {
                appDataStore.SaveSettings(settings);
            }
            catch (InvalidOperationException ex)
            {
                // 保留损坏的设置文件不覆盖，启动提示会说明修复方式。
                AppLogger.Warning(AppLogCategory.IO, "保存窗口布局设置失败", ex);
            }
            catch (AppDataStoreException ex)
            {
                AppLogger.Warning(AppLogCategory.IO, "保存窗口布局设置失败", ex);
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
            Rect visibleBounds = Rect.Intersect(virtualScreen, bounds);
            if (visibleBounds.IsEmpty)
            {
                return false;
            }

            double requiredVisibleWidth = Math.Min(200, bounds.Width * 0.25);
            double requiredVisibleHeight = Math.Min(120, bounds.Height * 0.25);
            return visibleBounds.Width >= requiredVisibleWidth &&
                visibleBounds.Height >= requiredVisibleHeight;
        }
    }
}
