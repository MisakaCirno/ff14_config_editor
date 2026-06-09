using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.ComponentModel;
using System.Text.Json;
using System.Globalization;
using System.Collections.ObjectModel;
using System.IO;
using FF14ConfigEditor;
using FF14ConfigEditor.UISave;

namespace UIMarkerEditor
{
    public partial class MainWindow
    {
        private void LoadSettingsIntoUi()
        {
            DataDirectory_TextBox.Text = appDataStore.DataDirectory;
            MaxBackupCount_TextBox.Text = appDataStore.Settings.MaxBackupCount.ToString(CultureInfo.InvariantCulture);
            MaxBackupDays_TextBox.Text = appDataStore.Settings.MaxBackupDays.ToString(CultureInfo.InvariantCulture);
            AutoBackup_CheckBox.IsChecked = appDataStore.Settings.AutoBackupBeforeSave;
        }

        private void BrowseDataDirectory_Button_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFolderDialog dialog = new()
            {
                Title = "选择数据目录",
                InitialDirectory = Directory.Exists(DataDirectory_TextBox.Text) ? DataDirectory_TextBox.Text : appDataStore.DataDirectory
            };

            if (dialog.ShowDialog() == true)
            {
                DataDirectory_TextBox.Text = dialog.FolderName;
            }
        }

        private void SaveSettings_Button_Click(object sender, RoutedEventArgs e)
        {
            if (!TryReadPositiveInt(MaxBackupCount_TextBox, "最多保留备份数量", out int maxBackupCount) ||
                !TryReadPositiveInt(MaxBackupDays_TextBox, "最多保留天数", out int maxBackupDays))
            {
                return;
            }

            try
            {
                string requestedDataDirectory = DataDirectory_TextBox.Text.Trim();
                if (!string.Equals(requestedDataDirectory, appDataStore.DataDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBoxResult migrateResult = MessageBox.Show(
                        "是否将现有配置、角色备注和备份迁移到新目录？\n选择“否”将只切换目录，不复制旧数据。",
                        "迁移数据目录",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);
                    if (migrateResult == MessageBoxResult.Cancel) return;

                    appDataStore.ChangeDataDirectory(requestedDataDirectory, migrateResult == MessageBoxResult.Yes);
                }

                appDataStore.SaveSettings(new AppSettings
                {
                    MaxBackupCount = maxBackupCount,
                    MaxBackupDays = maxBackupDays,
                    AutoBackupBeforeSave = AutoBackup_CheckBox.IsChecked == true
                });
                appDataStore.CleanupBackups();
                LoadSettingsIntoUi();
                RefreshBackupList();
                RefreshCharacterList();
                MessageBox.Show("设置已保存。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存设置失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResetDataDirectory_Button_Click(object sender, RoutedEventArgs e)
        {
            DataDirectory_TextBox.Text = appDataStore.DefaultDataDirectory;
        }

        private void OpenDataDirectory_Button_Click(object sender, RoutedEventArgs e)
        {
            OpenDirectory(appDataStore.DataDirectory);
        }

        private static bool TryReadPositiveInt(TextBox textBox, string displayName, out int value)
        {
            if (int.TryParse(textBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0)
            {
                return true;
            }

            MessageBox.Show($"{displayName} 必须是大于 0 的整数。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

    }
}