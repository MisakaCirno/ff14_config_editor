using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FF14ConfigEditor;
using FF14ConfigEditor.UISave;

namespace UIMarkerEditor.Controls;

public partial class WayMarkEditorControl
{
    private AppDataStore? appDataStore;
    private Window? ownerWindow;
    private Action refreshWayMarkFavorites = () => { };
    private WayMarkSnapshot? toolClipboardSnapshot;

    public WayMark? SelectedWayMark => WayMark_ListBox.SelectedItem as WayMark;

    public void Initialize(AppDataStore appDataStore, Window ownerWindow, Action refreshWayMarkFavorites)
    {
        this.appDataStore = appDataStore;
        this.ownerWindow = ownerWindow;
        this.refreshWayMarkFavorites = refreshWayMarkFavorites;
    }

    public bool ImportSnapshotToSelectedWayMark(WayMarkSnapshot snapshot)
    {
        if (SelectedWayMark is not WayMark selectedMark)
        {
            return false;
        }

        ApplySnapshotToWayMark(selectedMark, snapshot);
        return true;
    }

    private void WayMark_ListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is not WayMark wayMark)
        {
            return;
        }

        WayMark_ListBox.SelectedItem = wayMark;
        ImportWayMarkFromFavorites();
    }
    private void WayMark_ListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject) is ListBoxItem item)
        {
            item.IsSelected = true;
            WayMark_ListBox.SelectedItem = item.DataContext;
            return;
        }

        WayMark_ListBox.SelectedItem = null;
    }

    private void WayMarkContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        isWayMarkContextMenuOpen = true;
        ClearWayMarkDragState();
        UpdateMoveButtonState();

        bool hasSelection = SelectedWayMark != null;
        bool hasFavorites = (appDataStore?.WayMarkFavorites.Count ?? 0) > 0;
        bool canSortWayMarks = (GetWayMarks()?.Count ?? 0) > 1;
        CopyWayMark_MenuItem.IsEnabled = hasSelection;
        PasteWayMark_MenuItem.IsEnabled = hasSelection && toolClipboardSnapshot != null;
        FavoriteWayMark_MenuItem.IsEnabled = hasSelection && appDataStore != null;
        ImportFavoriteWayMark_MenuItem.IsEnabled = hasSelection && appDataStore != null && hasFavorites;
        ImportFavoriteWayMark_MenuItem.Header = hasFavorites
            ? "\u4ECE\u6536\u85CF\u5BFC\u5165..."
            : "\u4ECE\u6536\u85CF\u5BFC\u5165...\uFF08\u65E0\u6536\u85CF\uFF09";
        ImportFavoriteWayMark_MenuItem.ToolTip = hasFavorites
            ? null
            : "\u5F53\u524D\u8FD8\u6CA1\u6709\u6807\u70B9\u6536\u85CF\u3002";
        SortWayMarks_MenuItem.IsEnabled = canSortWayMarks;
        SortWayMarksByRegionAscending_MenuItem.IsEnabled = canSortWayMarks;
        SortWayMarksByRegionDescending_MenuItem.IsEnabled = canSortWayMarks;
    }

    private void WayMarkContextMenu_Closed(object sender, RoutedEventArgs e)
    {
        isWayMarkContextMenuOpen = false;
        SuppressWayMarkListDragUntilLeftButtonReleased();
    }

    private void CopyWayMark_MenuItem_Click(object sender, RoutedEventArgs e)
    {
        CopySelectedWayMark(showMessage: true);
    }

    private void PasteWayMark_MenuItem_Click(object sender, RoutedEventArgs e)
    {
        PasteCopiedWayMark(showMessage: true);
    }

    private void FavoriteWayMark_MenuItem_Click(object sender, RoutedEventArgs e)
    {
        SaveSelectedWayMarkToFavorites();
    }

    private void ImportFavoriteWayMark_MenuItem_Click(object sender, RoutedEventArgs e)
    {
        ImportWayMarkFromFavorites();
    }

    private bool HandleWayMarkClipboardShortcut(KeyEventArgs e)
    {
        if (!WayMark_ListBox.IsKeyboardFocusWithin || IsTextInputFocused())
        {
            return false;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return false;
        }

        if (e.Key == Key.C)
        {
            e.Handled = true;
            CopySelectedWayMark(showMessage: false);
            return true;
        }

        if (e.Key == Key.V)
        {
            e.Handled = true;
            PasteCopiedWayMark(showMessage: false);
            return true;
        }

        return false;
    }

    private void CopySelectedWayMark(bool showMessage)
    {
        if (SelectedWayMark is not WayMark selectedMark)
        {
            if (showMessage)
            {
                AppMessageBox.Show(ownerWindow, "请先选择一个要复制的标点。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return;
        }

        toolClipboardSnapshot = WayMarkSnapshotConverter.CreateSnapshot(selectedMark);
        if (showMessage)
        {
            ToastService.ShowSuccess("标点已复制，可粘贴到工具内其它槽位。");
        }
    }

    private void PasteCopiedWayMark(bool showMessage)
    {
        if (SelectedWayMark is not WayMark selectedMark)
        {
            if (showMessage)
            {
                AppMessageBox.Show(ownerWindow, "请先选择一个粘贴目标标点。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return;
        }

        if (toolClipboardSnapshot == null)
        {
            if (showMessage)
            {
                AppMessageBox.Show(ownerWindow, "工具内还没有复制的标点。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return;
        }

        ApplySnapshotToWayMark(selectedMark, toolClipboardSnapshot);
    }

    private void SaveSelectedWayMarkToFavorites()
    {
        if (appDataStore == null) return;

        if (SelectedWayMark is not WayMark selectedMark)
        {
            AppMessageBox.Show(ownerWindow, "请先选择一个要收藏的标点。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        WayMarkSnapshot snapshot = WayMarkSnapshotConverter.CreateSnapshot(selectedMark);
        string regionDisplayName = MapData.GetDisplayName(snapshot.RegionID);
        WayMarkFavoriteNameDialog dialog = new(regionDisplayName, MapData.GetName(snapshot.RegionID))
        {
            Owner = DialogOwnerHelper.Resolve(ownerWindow ?? Window.GetWindow(this))
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            appDataStore.AddWayMarkFavorite(snapshot, dialog.CommentName);
            refreshWayMarkFavorites();
            ToastService.ShowSuccess("标点已加入收藏。");
        }
        catch (Exception ex) when (ex is InvalidOperationException or AppDataStoreException)
        {
            AppMessageBox.Show(ownerWindow, $"保存标点收藏失败：{ex.Message}", "标点收藏保存受保护", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportWayMarkFromFavorites()
    {
        if (appDataStore == null) return;

        if (SelectedWayMark == null)
        {
            AppMessageBox.Show(ownerWindow, "请先选择一个导入目标标点。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IReadOnlyList<WayMarkFavorite> favorites = appDataStore.GetWayMarkFavoritesSnapshot();
        if (favorites.Count == 0)
        {
            AppMessageBox.Show(ownerWindow, "当前还没有标点收藏。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        WayMarkFavoritePickerDialog dialog = new(favorites)
        {
            Owner = DialogOwnerHelper.Resolve(ownerWindow ?? Window.GetWindow(this))
        };
        dialog.ApplyLayoutSettings(appDataStore.Settings.WindowLayout);
        bool? dialogResult = dialog.ShowDialog();
        SaveFavoritePickerLayout(dialog);
        if (dialogResult != true || dialog.SelectedFavorite == null) return;

        ApplySnapshotToWayMark(SelectedWayMark, dialog.SelectedFavorite.Marker);
        ToastService.ShowSuccess("收藏标点已导入到当前标点。");
    }

    private void SaveFavoritePickerLayout(WayMarkFavoritePickerDialog dialog)
    {
        if (appDataStore == null) return;

        AppSettings settings = appDataStore.CreateSettingsSnapshot();
        WindowLayoutSettings layout = settings.WindowLayout ?? new WindowLayoutSettings();
        dialog.CaptureLayoutSettings(layout);
        settings.WindowLayout = layout;
        try
        {
            appDataStore.SaveSettings(settings);
        }
        catch (Exception ex) when (ex is InvalidOperationException or AppDataStoreException)
        {
            AppLogger.Warning(AppLogCategory.IO, "保存收藏导入窗口布局设置失败", ex);
        }
    }
    private void ApplySnapshotToWayMark(WayMark targetMark, WayMarkSnapshot snapshot)
    {
        WayMarkSnapshotConverter.ApplyToWayMark(targetMark, snapshot, updateTimestamp: true);
        WayMarkEditPanel_Control.RefreshMapDataDisplay(GetLoadedRegionIds());
        WayMarkEditPanel_Control.SetWayMark(targetMark, GetLoadedRegionIds());
        WayMark_ListBox.Items.Refresh();
        WayMark_ListBox.SelectedItem = targetMark;
        WayMarkPreview_Control.SetWayMark(targetMark);
        UpdatePreview();
        NotifyWayMarksChanged();
    }

    private static bool IsTextInputFocused()
    {
        return Keyboard.FocusedElement is DependencyObject focusedElement &&
            FindVisualParent<TextBox>(focusedElement) != null;
    }
}
