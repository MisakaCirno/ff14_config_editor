using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace UIMarkerEditor.Controls;

public partial class ToolSettingsControl : UserControl
{
    private AppDataStore? appDataStore;
    private Window? ownerWindow;
    private Action refreshBackupList = () => { };
    private Action refreshCharacterList = () => { };

    public ToolSettingsControl()
    {
        InitializeComponent();
    }

    public void Initialize(
        AppDataStore appDataStore,
        Window ownerWindow,
        Action refreshBackupList,
        Action refreshCharacterList)
    {
        this.appDataStore = appDataStore;
        this.ownerWindow = ownerWindow;
        this.refreshBackupList = refreshBackupList;
        this.refreshCharacterList = refreshCharacterList;
    }

    public void LoadSettingsIntoUi()
    {
        if (appDataStore == null) return;

        DataDirectory_TextBox.Text = appDataStore.DataDirectory;
        MaxBackupCount_TextBox.Text = appDataStore.Settings.MaxBackupCount.ToString(CultureInfo.InvariantCulture);
        MaxBackupDays_TextBox.Text = appDataStore.Settings.MaxBackupDays.ToString(CultureInfo.InvariantCulture);
        AutoBackup_CheckBox.IsChecked = appDataStore.Settings.AutoBackupBeforeSave;
    }

    private void BrowseDataDirectory_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null) return;

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
        if (appDataStore == null) return;

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
                    ownerWindow,
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
            refreshBackupList();
            refreshCharacterList();
            MessageBox.Show(ownerWindow, "设置已保存。", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ownerWindow, $"保存设置失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ResetDataDirectory_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null) return;

        DataDirectory_TextBox.Text = appDataStore.DefaultDataDirectory;
    }

    private void OpenDataDirectory_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null) return;

        OpenDirectory(appDataStore.DataDirectory);
    }

    private void TryReadPositiveIntFailed(string displayName)
    {
        MessageBox.Show(ownerWindow, $"{displayName} 必须是大于 0 的整数。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private bool TryReadPositiveInt(TextBox textBox, string displayName, out int value)
    {
        if (int.TryParse(textBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0)
        {
            return true;
        }

        TryReadPositiveIntFailed(displayName);
        return false;
    }

    private static void OpenDirectory(string directory)
    {
        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
        using Process? _ = Process.Start(new ProcessStartInfo
        {
            FileName = directory,
            UseShellExecute = true
        });
    }
}
