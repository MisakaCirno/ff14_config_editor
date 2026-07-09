using System.IO;
using System.Windows;
using FF14ConfigEditor;

namespace UIMarkerEditor
{
    public partial class MainWindow
    {
        private void LoadSettingsIntoUi()
        {
            ToolSettings_Control.LoadSettingsIntoUi();
            RefreshAppearanceSettings();
        }

        private void RefreshAppearanceSettings()
        {
            WayMarkEditor_Control.ApplyAppearanceSettings(appDataStore.Settings);
            WayMarkFavorites_Control.ApplySettings(appDataStore.Settings);
            RefreshMapDataSourceMenu();
        }

        private void RefreshServerListConsumers()
        {
            CharacterProfiles_Control.RefreshServerPicker();
            RefreshCharacterListFromExternalChange();
        }

        private void RefreshMapDataConsumers()
        {
            UpdateDataVersionText();
            WayMarkEditor_Control.RefreshMapDataDisplay();
            WayMarkFavorites_Control.RefreshMapDataDisplay();
            RefreshBackupList();
            RefreshMapDataSourceMenu();
        }

        private void QuickSettings_MenuItem_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            RefreshMapDataSourceMenu();
        }

        private async void OnlineReferenceMapDataSource_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            await ChangeMapDataSelectionAsync(MapDataTableMode.Automatic, MapDataSource.OnlineReference);
        }

        private async void LocalGameMapDataSource_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            await ChangeMapDataSelectionAsync(MapDataTableMode.Automatic, MapDataSource.LocalGame);
        }

        private async void ManualMapDataTableMode_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            await ChangeMapDataSelectionAsync(MapDataTableMode.Manual, appDataStore.Settings.MapDataSource);
        }

        private async void EditUserMapData_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            await OpenUserMapDataEditorAsync();
        }

        private void RejectUnknownMapId_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            ChangeUnknownMapIdPolicyFromMenu(UnknownMapIdPolicy.RejectUnknown);
        }

        private void AllowUnknownMapId_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            ChangeUnknownMapIdPolicyFromMenu(UnknownMapIdPolicy.AllowUnknown);
        }

        private async Task ChangeMapDataSelectionAsync(MapDataTableMode nextTableMode, MapDataSource nextSource)
        {
            if (IsCurrentMapDataSelection(nextTableMode, nextSource))
            {
                RefreshMapDataSourceMenu();
                return;
            }

            if (nextTableMode == MapDataTableMode.Automatic &&
                nextSource == MapDataSource.LocalGame &&
                !ConfirmLocalGameMapDataAccess())
            {
                RefreshMapDataSourceMenu();
                return;
            }

            ShowMapDataSourceSwitchOverlay(nextTableMode, nextSource);
            SetMapDataSourceMenuEnabled(false);
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
            try
            {
                AppSettings settings = appDataStore.CreateSettingsSnapshot();
                settings.MapDataTableMode = nextTableMode;
                settings.MapDataTableModeInitialized = true;
                if (nextTableMode == MapDataTableMode.Automatic)
                {
                    settings.MapDataSource = nextSource;
                    settings.MapDataSourceInitialized = true;
                }

                try
                {
                    appDataStore.SaveSettings(settings);
                }
                catch (Exception ex) when (ex is InvalidOperationException or AppDataStoreException or IOException or UnauthorizedAccessException)
                {
                    RefreshMapDataSourceMenu();
                    ToolSettings_Control.LoadSettingsIntoUi();
                    HideMapDataSourceSwitchOverlay();
                    AppMessageBox.Show(this, $"切换地图数据来源失败：{ex.Message}", "设置保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ToolSettings_Control.LoadSettingsIntoUi();
                RefreshAppearanceSettings();

                try
                {
                    MapDataLoadResult result = await appDataStore.ForceRefreshMapDataAsync();
                    RefreshMapDataConsumers();
                    ToolSettings_Control.RefreshOnlineDataStatus();

                    if (await PromptToRepairUserMapDataAsync(result))
                    {
                        return;
                    }

                    if (!result.Success)
                    {
                        HideMapDataSourceSwitchOverlay();
                        AppMessageBox.Show(
                            this,
                            BuildMapDataSourceMenuRefreshFailureMessage(result),
                            "地图数据",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    string versionText = string.IsNullOrWhiteSpace(result.Version) ? "未知版本" : result.Version;
                    if (result.UsedCache)
                    {
                        HideMapDataSourceSwitchOverlay();
                        AppMessageBox.Show(
                            this,
                            BuildMapDataSourceMenuCacheFallbackMessage(result),
                            "地图数据",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    string selectionText = GetMapDataSelectionDisplayText(nextTableMode, nextSource);
                    string message = result.Updated
                        ? $"已切换到{selectionText}，地图快照已更新到：{versionText}"
                        : $"已切换到{selectionText}。当前地图快照标识：{versionText}";
                    ToastService.ShowSuccess(message);
                }
                finally
                {
                    ToolSettings_Control.RefreshOnlineDataStatus();
                }
            }
            finally
            {
                HideMapDataSourceSwitchOverlay();
                SetMapDataSourceMenuEnabled(true);
                RefreshMapDataSourceMenu();
            }
        }

        private async Task ChangeMapDataOnlineSourceAsync(MapDataOnlineSourceKind nextOnlineSource)
        {
            if (appDataStore.Settings.MapDataOnlineSource == nextOnlineSource)
            {
                ToolSettings_Control.LoadSettingsIntoUi();
                return;
            }

            bool refreshCurrentMapData =
                appDataStore.Settings.MapDataTableMode == MapDataTableMode.Automatic &&
                appDataStore.Settings.MapDataSource == MapDataSource.OnlineReference;

            if (refreshCurrentMapData)
            {
                ShowMapDataSourceSwitchOverlay(MapDataTableMode.Automatic, MapDataSource.OnlineReference);
                SetMapDataSourceMenuEnabled(false);
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
            }

            try
            {
                AppSettings settings = appDataStore.CreateSettingsSnapshot();
                settings.MapDataOnlineSource = nextOnlineSource;
                try
                {
                    appDataStore.SaveSettings(settings);
                }
                catch (Exception ex) when (ex is InvalidOperationException or AppDataStoreException or IOException or UnauthorizedAccessException)
                {
                    ToolSettings_Control.LoadSettingsIntoUi();
                    AppMessageBox.Show(this, $"切换在线地图数据来源失败：{ex.Message}", "设置保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ToolSettings_Control.LoadSettingsIntoUi();
                if (!refreshCurrentMapData)
                {
                    ToastService.ShowSuccess($"已将在线地图数据来源切换为{GetMapDataOnlineSourceDisplayText(nextOnlineSource)}。");
                    return;
                }

                MapDataLoadResult result = await appDataStore.ForceRefreshMapDataAsync();
                RefreshMapDataConsumers();
                ToolSettings_Control.RefreshOnlineDataStatus();
                if (await PromptToRepairUserMapDataAsync(result))
                {
                    return;
                }

                if (!result.Success)
                {
                    AppMessageBox.Show(
                        this,
                        BuildMapDataSourceMenuRefreshFailureMessage(result),
                        "地图数据",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                string versionText = string.IsNullOrWhiteSpace(result.Version) ? "未知版本" : result.Version;
                if (result.UsedCache)
                {
                    AppMessageBox.Show(
                        this,
                        BuildMapDataSourceMenuCacheFallbackMessage(result),
                        "地图数据",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                ToastService.ShowSuccess(result.Updated
                    ? $"已切换到{GetMapDataOnlineSourceDisplayText(nextOnlineSource)}，地图快照已更新到：{versionText}"
                    : $"已切换到{GetMapDataOnlineSourceDisplayText(nextOnlineSource)}。当前地图快照标识：{versionText}");
            }
            finally
            {
                if (refreshCurrentMapData)
                {
                    HideMapDataSourceSwitchOverlay();
                    SetMapDataSourceMenuEnabled(true);
                }
            }
        }

        private void ChangeUnknownMapIdPolicyFromMenu(UnknownMapIdPolicy nextPolicy)
        {
            if (appDataStore.Settings.UnknownMapIdPolicy == nextPolicy)
            {
                RefreshMapDataSourceMenu();
                return;
            }

            if (!ConfirmUnknownMapIdPolicyChange(nextPolicy, out bool disableFutureConfirmation))
            {
                RefreshMapDataSourceMenu();
                return;
            }

            AppSettings settings = appDataStore.CreateSettingsSnapshot();
            settings.UnknownMapIdPolicy = nextPolicy;
            if (disableFutureConfirmation)
            {
                settings.ShowAllowUnknownMapIdPolicyWarning = false;
            }

            try
            {
                appDataStore.SaveSettings(settings);
            }
            catch (Exception ex) when (ex is InvalidOperationException or AppDataStoreException or IOException or UnauthorizedAccessException)
            {
                RefreshMapDataSourceMenu();
                ToolSettings_Control.LoadSettingsIntoUi();
                AppMessageBox.Show(this, $"切换未知地图 ID 策略失败：{ex.Message}", "设置保存失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ToolSettings_Control.LoadSettingsIntoUi();
            RefreshAppearanceSettings();
            RefreshMapDataConsumers();
            ToastService.ShowSuccess($"已切换到{GetUnknownMapIdPolicyDisplayText(nextPolicy)}。");
        }

        private async Task OpenUserMapDataEditorAsync()
        {
            UserMapDataEditorDialog dialog = new(appDataStore.UserMapDataFilePath)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            if (appDataStore.Settings.MapDataTableMode != MapDataTableMode.Manual)
            {
                ToolSettings_Control.RefreshOnlineDataStatus();
                ToastService.ShowSuccess("用户填写数据已保存。切换到用户填写数据后会读取这个文件。");
                return;
            }

            ShowMapDataSourceSwitchOverlay(MapDataTableMode.Manual, appDataStore.Settings.MapDataSource);
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
            try
            {
                MapDataLoadResult result = await appDataStore.ForceRefreshMapDataAsync();
                RefreshMapDataConsumers();
                ToolSettings_Control.RefreshOnlineDataStatus();
                if (result.RequiresUserMapDataRepair)
                {
                    AppMessageBox.Show(
                        this,
                        BuildMapDataSourceMenuRefreshFailureMessage(result),
                        "地图数据",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (!result.Success)
                {
                    AppMessageBox.Show(
                        this,
                        BuildMapDataSourceMenuRefreshFailureMessage(result),
                        "地图数据",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                string versionText = string.IsNullOrWhiteSpace(result.Version) ? "未知版本" : result.Version;
                ToastService.ShowSuccess(result.Updated
                    ? $"用户填写数据已保存，地图快照已更新到：{versionText}"
                    : $"用户填写数据已保存。当前地图快照标识：{versionText}");
            }
            finally
            {
                HideMapDataSourceSwitchOverlay();
            }
        }

        internal async Task<bool> PromptToRepairUserMapDataAsync(MapDataLoadResult result)
        {
            if (!result.RequiresUserMapDataRepair)
            {
                return false;
            }

            try
            {
                HideMapDataSourceSwitchOverlay();
                if (AppMessageBox.Show(
                    this,
                    UserMapDataRepairPrompt.BuildMessage(result),
                    "用户地图数据需要修复",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    await OpenUserMapDataEditorAsync();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warning(AppLogCategory.UI, "打开用户地图数据修复编辑器失败", ex);
                AppMessageBox.Show(this, $"打开用户地图数据编辑器失败：{ex.Message}", "用户地图数据需要修复", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return true;
        }

        private void RefreshMapDataSourceMenu()
        {
            OnlineReferenceMapDataSource_MenuItem.IsChecked =
                appDataStore.Settings.MapDataTableMode == MapDataTableMode.Automatic &&
                appDataStore.Settings.MapDataSource == MapDataSource.OnlineReference;
            LocalGameMapDataSource_MenuItem.IsChecked =
                appDataStore.Settings.MapDataTableMode == MapDataTableMode.Automatic &&
                appDataStore.Settings.MapDataSource == MapDataSource.LocalGame;
            ManualMapDataTableMode_MenuItem.IsChecked = appDataStore.Settings.MapDataTableMode == MapDataTableMode.Manual;
            RejectUnknownMapId_MenuItem.IsChecked = appDataStore.Settings.UnknownMapIdPolicy == UnknownMapIdPolicy.RejectUnknown;
            AllowUnknownMapId_MenuItem.IsChecked = appDataStore.Settings.UnknownMapIdPolicy == UnknownMapIdPolicy.AllowUnknown;
        }

        private void SetMapDataSourceMenuEnabled(bool isEnabled)
        {
            QuickSettings_MenuItem.IsEnabled = isEnabled;
            MapDataSource_MenuItem.IsEnabled = isEnabled;
            OnlineReferenceMapDataSource_MenuItem.IsEnabled = isEnabled;
            LocalGameMapDataSource_MenuItem.IsEnabled = isEnabled;
            ManualMapDataTableMode_MenuItem.IsEnabled = isEnabled;
            EditUserMapData_MenuItem.IsEnabled = isEnabled;
            UnknownMapIdPolicy_MenuItem.IsEnabled = isEnabled;
            RejectUnknownMapId_MenuItem.IsEnabled = isEnabled;
            AllowUnknownMapId_MenuItem.IsEnabled = isEnabled;
        }

        private bool IsCurrentMapDataSelection(MapDataTableMode tableMode, MapDataSource source)
        {
            if (appDataStore.Settings.MapDataTableMode != tableMode)
            {
                return false;
            }

            return tableMode == MapDataTableMode.Manual ||
                appDataStore.Settings.MapDataSource == source;
        }

        private static string GetMapDataSelectionDisplayText(AppSettings settings)
        {
            return GetMapDataSelectionDisplayText(settings.MapDataTableMode, settings.MapDataSource);
        }

        private static string GetMapDataSelectionDisplayText(MapDataTableMode tableMode, MapDataSource source)
        {
            return tableMode == MapDataTableMode.Manual
                ? "用户填写数据"
                : GetMapDataSourceDisplayText(source);
        }

        private static string GetMapDataSourceDisplayText(MapDataSource source)
        {
            return source == MapDataSource.LocalGame ? "本地游戏数据" : "在线数据";
        }

        private static string GetUnknownMapIdPolicyDisplayText(UnknownMapIdPolicy policy)
        {
            return policy == UnknownMapIdPolicy.AllowUnknown ? "允许未知地图 ID" : "不允许未知地图 ID";
        }

        private static string GetMapDataOnlineSourceDisplayText(MapDataOnlineSourceKind source)
        {
            return source == MapDataOnlineSourceKind.DiemoeMatcha
                ? "diemoe MatchaData"
                : "GitHub ffxiv-datamining-cn";
        }

        private bool ConfirmLocalGameMapDataAccess()
        {
            return AppMessageBox.Show(
                this,
                "切换到本地游戏数据后，工具会读取 FFXIV 安装目录下的 game\\sqpack 数据文件，用于解析地图名称和地图 ID 列表。此操作不会修改游戏文件，但属于稍有敏感的本地游戏资源文件读取行为。\n\n是否继续？",
                "确认读取游戏文件",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }

        private bool ConfirmUnknownMapIdPolicyChange(
            UnknownMapIdPolicy nextPolicy,
            out bool disableFutureConfirmation)
        {
            disableFutureConfirmation = false;
            UnknownMapIdPolicyChangeConfirmation confirmation =
                UnknownMapIdPolicyChangeConfirmation.Evaluate(
                    appDataStore.Settings.UnknownMapIdPolicy,
                    nextPolicy,
                    appDataStore.Settings.ShowAllowUnknownMapIdPolicyWarning);
            if (!confirmation.RequiresConfirmation)
            {
                return true;
            }

            AppMessageBoxCheckBoxResult result = AppMessageBox.ShowWithCheckBox(
                this,
                UnknownMapIdPolicyChangeConfirmation.Message,
                UnknownMapIdPolicyChangeConfirmation.Title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                UnknownMapIdPolicyChangeConfirmation.DoNotShowAgainText);
            bool confirmed = result.Result == MessageBoxResult.Yes;
            disableFutureConfirmation =
                UnknownMapIdPolicyChangeConfirmation.ShouldDisableFutureConfirmation(confirmed, result.IsChecked);
            return confirmed;
        }

        private void ShowMapDataSourceSwitchOverlay(MapDataTableMode nextTableMode, MapDataSource nextSource)
        {
            string title = $"正在切换到{GetMapDataSelectionDisplayText(nextTableMode, nextSource)}...";
            string message = nextTableMode == MapDataTableMode.Manual
                ? "正在读取用户填写数据 mapdata_user.csv，请稍候。"
                : nextSource == MapDataSource.LocalGame
                    ? "正在读取本地游戏 sqpack 并刷新地图数据，请稍候。"
                    : "正在获取在线数据，请稍候。";
            ShowMapDataOperationOverlay(title, message);
        }

        private void HideMapDataSourceSwitchOverlay()
        {
            HideMapDataOperationOverlay();
        }

        private void ShowMapDataOperationOverlay(string title, string message)
        {
            MapDataOperationOverlay_Control.Show(title, message);
        }

        private void HideMapDataOperationOverlay()
        {
            MapDataOperationOverlay_Control.Hide();
        }

        private static string BuildMapDataSourceMenuRefreshFailureMessage(MapDataLoadResult result)
        {
            return
                $"已切换地图数据设置，但地图数据读取失败：{FormatMapDataSourceMenuFailure(result)}{Environment.NewLine}{Environment.NewLine}" +
                "当前没有可用地图数据快照，区域选择和导入校验会受限。可重新读取地图数据、编辑手动 CSV，或开启“允许未知地图 ID”后自行确认地图 ID。";
        }

        private static string BuildMapDataSourceMenuCacheFallbackMessage(MapDataLoadResult result)
        {
            return
                $"已切换地图数据设置，但当前来源读取失败，已继续使用同来源的缓存快照。{Environment.NewLine}{Environment.NewLine}" +
                $"原因：{FormatMapDataSourceMenuFailure(result)}{Environment.NewLine}{Environment.NewLine}" +
                $"区域列表可能不是最新，未覆盖的地图名称会显示为“{MapData.UnavailableRegionName}”。";
        }

        private static string FormatMapDataSourceMenuFailure(MapDataLoadResult result)
        {
            string reason = string.IsNullOrWhiteSpace(result.FailureReason)
                ? "未知原因。"
                : result.FailureReason;
            return string.IsNullOrWhiteSpace(result.FailureStage)
                ? reason
                : $"{result.FailureStage}失败：{reason}";
        }
    }
}
