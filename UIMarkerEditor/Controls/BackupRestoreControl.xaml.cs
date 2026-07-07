using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FF14ConfigEditor;

namespace UIMarkerEditor.Controls;

public partial class BackupRestoreControl : UserControl
{
    private readonly ObservableCollection<BackupMetadata> backupEntries = [];
    private AppDataStore? appDataStore;
    private Window? ownerWindow;
    private Func<string> getCurrentFilePath = () => string.Empty;
    private Action<string> loadConfigFile = _ => { };
    private Func<bool> confirmSaveOrDiscardWayMarkChanges = () => true;
    private Func<bool> confirmSaveOrDiscardCharacterChanges = () => true;
    private Func<Task> syncServerListForCharacterEditing = () => Task.CompletedTask;
    private Action refreshCharacterList = () => { };

    public BackupRestoreControl()
    {
        InitializeComponent();
        Backup_DataGrid.ItemsSource = backupEntries;
        UpdateBackupDetail(null);
    }

    public void Initialize(
        AppDataStore appDataStore,
        Window ownerWindow,
        Func<string> getCurrentFilePath,
        Action<string> loadConfigFile,
        Func<bool> confirmSaveOrDiscardWayMarkChanges,
        Func<bool> confirmSaveOrDiscardCharacterChanges,
        Func<Task> syncServerListForCharacterEditing,
        Action refreshCharacterList)
    {
        this.appDataStore = appDataStore;
        this.ownerWindow = ownerWindow;
        this.getCurrentFilePath = getCurrentFilePath;
        this.loadConfigFile = loadConfigFile;
        this.confirmSaveOrDiscardWayMarkChanges = confirmSaveOrDiscardWayMarkChanges;
        this.confirmSaveOrDiscardCharacterChanges = confirmSaveOrDiscardCharacterChanges;
        this.syncServerListForCharacterEditing = syncServerListForCharacterEditing;
        this.refreshCharacterList = refreshCharacterList;
    }

    public void RefreshBackupList()
    {
        if (appDataStore == null) return;

        backupEntries.Clear();
        foreach (BackupMetadata backup in appDataStore.LoadBackups())
        {
            FillBackupDisplayFields(backup);
            backupEntries.Add(backup);
        }

        UpdateBackupDetail(null);
    }

    public void ApplyLayoutSettings(WindowLayoutSettings layout)
    {
        double listRatio = ClampRatio(layout.BackupListRatio);
        BackupList_Column.Width = new GridLength(listRatio, GridUnitType.Star);
        BackupDetail_Column.Width = new GridLength(1 - listRatio, GridUnitType.Star);
    }

    public void CaptureLayoutSettings(WindowLayoutSettings layout)
    {
        double totalWidth = BackupList_Column.ActualWidth + BackupDetail_Column.ActualWidth;
        if (totalWidth <= 1) return;

        layout.BackupListRatio = BackupList_Column.ActualWidth / totalWidth;
    }

    private static double ClampRatio(double value)
    {
        return double.IsFinite(value) ? Math.Clamp(value, 0.15, 0.85) : 0.4;
    }

    private void FillBackupDisplayFields(BackupMetadata backup)
    {
        if (appDataStore == null) return;

        CharacterProfile? profile = appDataStore.Characters.FirstOrDefault(character =>
            string.Equals(character.UserID, backup.EffectiveUserID, StringComparison.OrdinalIgnoreCase));

        backup.CharacterDisplayName = profile?.DisplayName ?? DisplayOptionalText(backup.EffectiveUserID);
        backup.CharacterNameDisplay = profile != null && !string.IsNullOrWhiteSpace(profile.CharacterName)
            ? profile.CharacterName
            : DisplayOptionalText(backup.EffectiveUserID);
        backup.ServerDisplayName = profile == null
            ? "无"
            : DisplayOptionalText(string.Join(" / ", new[] { profile.DataCenter, profile.World }
                .Where(part => !string.IsNullOrWhiteSpace(part))));
    }

    private void Backup_DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateBackupDetail(Backup_DataGrid.SelectedItem as BackupMetadata);
    }

    private void Backup_DataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject) is DataGridRow row)
        {
            row.IsSelected = true;
            Backup_DataGrid.SelectedItem = row.Item;
            return;
        }

        Backup_DataGrid.SelectedItem = null;
    }

    private async void Backup_DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject) is not DataGridRow row ||
            row.Item is not BackupMetadata backup)
        {
            return;
        }

        Backup_DataGrid.SelectedItem = backup;
        e.Handled = true;
        await OpenCharacterProfileForBackupAsync(backup);
    }

    private void Backup_ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        BackupMetadata? backup = Backup_DataGrid.SelectedItem as BackupMetadata;
        bool hasBackup = backup != null;
        bool hasBackupDirectory = backup != null && Directory.Exists(backup.BackupDirectory);
        bool hasValidUserID = backup != null && IsValidUserID(backup.EffectiveUserID);
        bool alreadyHasCharacterProfile = hasValidUserID && HasCharacterProfile(backup!.EffectiveUserID);

        RestoreBackup_MenuItem.IsEnabled = hasBackup;
        RestoreBackupAs_MenuItem.IsEnabled = hasBackup;
        DeleteBackup_MenuItem.IsEnabled = hasBackup;
        OpenBackupDirectory_MenuItem.IsEnabled = hasBackupDirectory;
        OpenCharacterProfileFromBackup_MenuItem.IsEnabled = hasValidUserID;
        OpenCharacterProfileFromBackup_MenuItem.Header = backup == null
            ? "为此备份创建角色备注..."
            : alreadyHasCharacterProfile
                ? "编辑角色备注..."
                : hasValidUserID
                    ? "为此备份创建角色备注..."
                    : "无法创建角色备注";
    }

    private async void OpenCharacterProfileFromBackup_MenuItem_Click(object sender, RoutedEventArgs e)
    {
        await OpenCharacterProfileForBackupAsync(Backup_DataGrid.SelectedItem as BackupMetadata);
    }

    private async Task OpenCharacterProfileForBackupAsync(BackupMetadata? backup)
    {
        if (appDataStore == null || !confirmSaveOrDiscardCharacterChanges()) return;

        if (backup == null)
        {
            AppMessageBox.Show(ownerWindow, "请先选择一个备份。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string userID = backup.EffectiveUserID;
        if (!IsValidUserID(userID))
        {
            AppMessageBox.Show(ownerWindow, "这个备份没有可用于创建角色备注的 16 位 User ID。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await syncServerListForCharacterEditing();
        CharacterProfile? existingProfile = appDataStore.Characters.FirstOrDefault(character =>
            string.Equals(character.UserID, userID, StringComparison.OrdinalIgnoreCase));
        BackupCharacterProfileDialog dialog = existingProfile != null && HasCharacterRemark(existingProfile)
            ? new BackupCharacterProfileDialog(existingProfile, appDataStore.ServerList.Groups)
            : new BackupCharacterProfileDialog(userID, appDataStore.ServerList.Groups, existingProfile);
        DialogOwnerHelper.ConfigureOwnedDialog(dialog, ownerWindow ?? Window.GetWindow(this));

        if (dialog.ShowDialog() != true) return;

        SaveCharacterProfileForBackup(backup, dialog);
    }

    private void SaveCharacterProfileForBackup(BackupMetadata backup, BackupCharacterProfileDialog dialog)
    {
        if (appDataStore == null) return;

        string userID = backup.EffectiveUserID;
        bool isNewProfile = !appDataStore.Characters.Any(character =>
            string.Equals(character.UserID, userID, StringComparison.OrdinalIgnoreCase));
        CharacterProfile profile = appDataStore.GetOrCreateCharacter(userID);
        string previousCharacterName = profile.CharacterName;
        string previousDataCenter = profile.DataCenter;
        string previousWorld = profile.World;
        string previousNote = profile.Note;
        DateTime previousUpdatedAt = profile.UpdatedAt;

        profile.CharacterName = dialog.CharacterName;
        profile.DataCenter = dialog.DataCenter;
        profile.World = dialog.World;
        profile.Note = dialog.Note;
        profile.UpdatedAt = DateTime.Now;
        try
        {
            appDataStore.SaveCharacters();
        }
        catch (Exception ex) when (ex is InvalidOperationException or AppDataStoreException)
        {
            if (isNewProfile)
            {
                appDataStore.Characters.Remove(profile);
            }
            else
            {
                profile.CharacterName = previousCharacterName;
                profile.DataCenter = previousDataCenter;
                profile.World = previousWorld;
                profile.Note = previousNote;
                profile.UpdatedAt = previousUpdatedAt;
            }

            AppMessageBox.Show(ownerWindow, $"保存角色备注失败：{ex.Message}", "角色备注保存受保护", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string selectedBackupId = backup.Id;
        refreshCharacterList();
        RefreshBackupList();
        Backup_DataGrid.SelectedItem = backupEntries.FirstOrDefault(entry => entry.Id == selectedBackupId);
        ToastService.ShowSuccess("角色备注已保存。");
    }

    private void UpdateBackupDetail(BackupMetadata? backup)
    {
        UpdateBackupActionButtons(backup);
        if (backup == null)
        {
            ClearBackupDetailFields();
            BackupSnapshot_TextBox.Text = string.Empty;
            BackupEmpty_Panel.Visibility = Visibility.Visible;
            BackupDetail_ScrollViewer.Visibility = Visibility.Collapsed;
            return;
        }

        BackupEmpty_Panel.Visibility = Visibility.Collapsed;
        BackupDetail_ScrollViewer.Visibility = Visibility.Visible;
        BackupDetail_BackupTime_TextBox.Text = backup.BackupTime.ToString("yyyy-MM-dd HH:mm:ss");
        BackupDetail_Character_TextBox.Text = backup.CharacterDisplayName;
        BackupDetail_CreationTrigger_TextBox.Text = backup.CreationTriggerDisplay;
        BackupDetail_OriginalPath_TextBox.Text = backup.OriginalFilePath;
        BackupDetail_FolderUserID_TextBox.Text = DisplayOptionalText(backup.FolderUserID);
        BackupDetail_FileUserID_TextBox.Text = DisplayOptionalText(backup.FileUserID);
        BackupDetail_SourceFileSize_TextBox.Text = $"{backup.SourceFileSize:N0} 字节";
        BackupDetail_SourceSha256_TextBox.Text = backup.SourceFileSha256;
        BackupDetail_BackupFile_TextBox.Text = backup.BackupFilePath;
        BackupSnapshot_TextBox.Text = backup.MarkerSnapshots.Count == 0
            ? "无"
            : string.Join(Environment.NewLine, backup.MarkerSnapshots.Select(snapshot => snapshot.DisplayText));
    }

    private void UpdateBackupActionButtons(BackupMetadata? backup)
    {
        bool hasBackup = backup != null;
        RestoreBackup_Button.IsEnabled = hasBackup;
        RestoreBackupAs_Button.IsEnabled = hasBackup;
        DeleteBackup_Button.IsEnabled = hasBackup;
        OpenBackupDirectory_Button.IsEnabled = backup != null && Directory.Exists(backup.BackupDirectory);
    }

    private void ClearBackupDetailFields()
    {
        BackupDetail_BackupTime_TextBox.Text = string.Empty;
        BackupDetail_Character_TextBox.Text = string.Empty;
        BackupDetail_CreationTrigger_TextBox.Text = string.Empty;
        BackupDetail_OriginalPath_TextBox.Text = string.Empty;
        BackupDetail_FolderUserID_TextBox.Text = string.Empty;
        BackupDetail_FileUserID_TextBox.Text = string.Empty;
        BackupDetail_SourceFileSize_TextBox.Text = string.Empty;
        BackupDetail_SourceSha256_TextBox.Text = string.Empty;
        BackupDetail_BackupFile_TextBox.Text = string.Empty;
        BackupSnapshot_TextBox.Text = string.Empty;
    }

    private void RefreshBackups_Button_Click(object sender, RoutedEventArgs e)
    {
        RefreshBackupList();
    }

    private void RestoreBackup_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null) return;

        if (Backup_DataGrid.SelectedItem is not BackupMetadata backup)
        {
            AppMessageBox.Show(ownerWindow, "请先选择一个备份。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (IsCurrentFile(backup.OriginalFilePath) && !confirmSaveOrDiscardWayMarkChanges())
        {
            return;
        }

        bool willCreateSafetyBackup = ShouldCreateSafetyBackupBeforeRestore(backup.OriginalFilePath);
        string warning = BuildRestoreWarning(backup, backup.OriginalFilePath, willCreateSafetyBackup);
        if (AppMessageBox.Show(ownerWindow, warning, "确认还原备份", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (!TryCreateSafetyBackupBeforeRestore(backup.OriginalFilePath, willCreateSafetyBackup))
            {
                return;
            }

            appDataStore.RestoreBackup(backup, backup.OriginalFilePath);
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(ownerWindow, $"还原失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            RefreshBackupList();
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(ownerWindow, $"备份已还原到原文件路径，但刷新备份列表失败：{ex.Message}", "还原已完成", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        string currentFilePath = getCurrentFilePath();
        if (string.Equals(currentFilePath, backup.OriginalFilePath, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                loadConfigFile(currentFilePath);
            }
            catch (Exception ex)
            {
                AppMessageBox.Show(ownerWindow, $"备份已还原到原文件路径，但重新加载当前文件失败：{ex.Message}", "还原已完成", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        ToastService.ShowSuccess("备份已还原到原文件路径。");
    }

    private void RestoreBackupAs_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null) return;

        if (Backup_DataGrid.SelectedItem is not BackupMetadata backup)
        {
            AppMessageBox.Show(ownerWindow, "请先选择一个备份。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Microsoft.Win32.SaveFileDialog saveFileDialog = new()
        {
            Title = "还原 UISAVE.DAT 到...",
            FileName = "UISAVE.DAT",
            Filter = "UISAVE.dat 文件 (UISAVE.dat)|UISAVE.dat|所有文件 (*.*)|*.*",
            InitialDirectory = Directory.Exists(backup.OriginalDirectory) ? backup.OriginalDirectory : null
        };

        if (DialogOwnerHelper.ShowCommonDialog(saveFileDialog, ownerWindow ?? Window.GetWindow(this)) != true) return;

        string targetFilePath = saveFileDialog.FileName;
        bool targetIsCurrentFile = IsCurrentFile(targetFilePath);
        bool willCreateSafetyBackup = ShouldCreateSafetyBackupBeforeRestore(targetFilePath);
        if (targetIsCurrentFile && !confirmSaveOrDiscardWayMarkChanges())
        {
            return;
        }

        RestoreBackupAsTargetConfirmation targetConfirmation =
            RestoreBackupAsTargetConfirmation.Evaluate(targetFilePath, File.Exists(targetFilePath), willCreateSafetyBackup);
        if (targetConfirmation.RequiresConfirmation &&
            AppMessageBox.Show(
                ownerWindow,
                targetConfirmation.Message,
                "确认还原到指定位置",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (!TryCreateSafetyBackupBeforeRestore(targetFilePath, willCreateSafetyBackup))
            {
                return;
            }

            appDataStore.RestoreBackup(backup, targetFilePath);
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(ownerWindow, $"还原失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (willCreateSafetyBackup)
        {
            try
            {
                RefreshBackupList();
            }
            catch (Exception ex)
            {
                AppMessageBox.Show(ownerWindow, $"备份已还原到指定位置，但刷新备份列表失败：{ex.Message}", "还原已完成", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        if (targetIsCurrentFile)
        {
            try
            {
                loadConfigFile(getCurrentFilePath());
            }
            catch (Exception ex)
            {
                AppMessageBox.Show(ownerWindow, $"备份已还原到指定位置，但重新加载当前文件失败：{ex.Message}", "还原已完成", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        ToastService.ShowSuccess("备份已还原到指定位置。");
    }

    private bool ShouldCreateSafetyBackupBeforeRestore(string targetFilePath)
    {
        return appDataStore != null &&
            appDataStore.Settings.AutoBackupBeforeRestore &&
            File.Exists(targetFilePath) &&
            appDataStore.IsTrustedGameCharacterSaveFile(targetFilePath);
    }

    private bool TryCreateSafetyBackupBeforeRestore(string targetFilePath, bool willCreateSafetyBackup)
    {
        if (!willCreateSafetyBackup || appDataStore == null)
        {
            return true;
        }

        try
        {
            appDataStore.CreateBackup(
                targetFilePath,
                cleanupAfterCreate: false,
                creationTrigger: BackupCreationTriggers.BeforeRestore);
            return true;
        }
        catch (Exception ex)
        {
            AppMessageBox.Show(ownerWindow, $"还原前安全备份失败，已取消还原：{ex.Message}", "还原前安全备份失败", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private bool IsCurrentFile(string filePath)
    {
        try
        {
            string currentFilePath = getCurrentFilePath();
            return !string.IsNullOrWhiteSpace(currentFilePath) &&
                string.Equals(
                    Path.GetFullPath(currentFilePath),
                    Path.GetFullPath(filePath),
                    StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void DeleteBackup_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null) return;

        if (Backup_DataGrid.SelectedItem is not BackupMetadata backup)
        {
            AppMessageBox.Show(ownerWindow, "请先选择一个备份。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (AppMessageBox.Show(ownerWindow, "确定要删除这个备份吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            appDataStore.DeleteBackup(backup);
        }
        catch (Exception ex)
        {
            AppLogger.Warning(AppLogCategory.IO, $"删除备份失败：{backup.BackupDirectory}", ex);
            AppMessageBox.Show(ownerWindow, $"删除备份失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            RefreshBackupList();
        }
        catch (Exception ex)
        {
            AppLogger.Warning(AppLogCategory.IO, "备份已删除，但刷新备份列表失败", ex);
            AppMessageBox.Show(ownerWindow, $"备份已删除，但刷新备份列表失败：{ex.Message}", "删除已完成", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenBackupDirectory_Button_Click(object sender, RoutedEventArgs e)
    {
        if (Backup_DataGrid.SelectedItem is BackupMetadata backup && Directory.Exists(backup.BackupDirectory))
        {
            OpenDirectory(backup.BackupDirectory);
        }
    }

    private bool HasCharacterProfile(string userID)
    {
        return appDataStore != null && appDataStore.Characters.Any(character =>
            string.Equals(character.UserID, userID, StringComparison.OrdinalIgnoreCase) &&
            HasCharacterRemark(character));
    }

    private static bool HasCharacterRemark(CharacterProfile profile)
    {
        return !string.IsNullOrWhiteSpace(profile.CharacterName) ||
            !string.IsNullOrWhiteSpace(profile.DataCenter) ||
            !string.IsNullOrWhiteSpace(profile.World) ||
            !string.IsNullOrWhiteSpace(profile.Note);
    }

    private static string BuildRestoreWarning(
        BackupMetadata backup,
        string targetFilePath,
        bool willCreateSafetyBackup)
    {
        string? targetFolderUserID = AppDataStore.GetUserIDFromCharacterFolder(targetFilePath);
        string userIDWarning = !string.IsNullOrWhiteSpace(targetFolderUserID) &&
            !string.IsNullOrWhiteSpace(backup.EffectiveUserID) &&
            !string.Equals(targetFolderUserID, backup.EffectiveUserID, StringComparison.OrdinalIgnoreCase)
                ? $"\n\n警告：目标目录 User ID 为 {targetFolderUserID}，备份归属 User ID 为 {backup.EffectiveUserID}。"
                : string.Empty;
        string safetyBackupText = willCreateSafetyBackup
            ? "覆盖前会自动备份当前目标文件。"
            : "当前不会创建还原前安全备份，覆盖后目标文件当前状态将无法从工具备份中恢复。";

        return
            "将把下面的备份文件还原到原文件路径，并覆盖目标文件。\n\n" +
            $"备份时间：{backup.BackupTime:yyyy-MM-dd HH:mm:ss}\n" +
            $"角色备注：{DisplayOptionalText(backup.CharacterDisplayName)}\n" +
            $"目录 User ID：{DisplayOptionalText(backup.FolderUserID)}\n" +
            $"文件 User ID：{DisplayOptionalText(backup.FileUserID)}\n\n" +
            $"原文件路径：\n{targetFilePath}\n\n" +
            $"备份文件路径：\n{backup.BackupFilePath}\n\n" +
            $"{safetyBackupText}确定继续吗？{userIDWarning}";
    }

    private static string DisplayOptionalText(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? "无" : text;
    }

    private static bool IsValidUserID(string userID)
    {
        return userID.Length == 16 && userID.All(Uri.IsHexDigit);
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

    private static T? FindVisualParent<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source != null)
        {
            if (source is T matched)
            {
                return matched;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }
}
