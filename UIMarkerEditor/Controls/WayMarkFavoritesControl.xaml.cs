using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FF14ConfigEditor.UISave;

namespace UIMarkerEditor.Controls;

public partial class WayMarkFavoritesControl : UserControl
{
    private static readonly TimeSpan AutoSaveDelay = TimeSpan.FromMilliseconds(500);
    private readonly ObservableCollection<WayMarkFavorite> favoriteEntries = [];
    private readonly DispatcherTimer autoSaveTimer;
    private AppDataStore? appDataStore;
    private Window? ownerWindow;
    private WayMarkFavorite? loadedFavorite;
    private WayMark? editingWayMark;
    private bool hasUnsavedChanges;
    private bool isUpdatingDetail;
    private bool suppressSelectionChanged;
    private bool isAutoSaveMode;

    public WayMarkFavoritesControl()
    {
        InitializeComponent();
        autoSaveTimer = new DispatcherTimer { Interval = AutoSaveDelay };
        autoSaveTimer.Tick += AutoSaveTimer_Tick;
        Unloaded += (_, _) =>
        {
            autoSaveTimer.Stop();
            StopWatchingFavoritesListDragSuppressionRelease();
        };

        Favorites_ListBox.ItemsSource = favoriteEntries;
        WayMarkEditPanel_Control.WayMarkChanged += (_, _) =>
        {
            if (isUpdatingDetail) return;
            MarkDirty();
            Preview_Control.RefreshPreview();
        };
        UpdateDetail(null);
    }

    public void Initialize(AppDataStore appDataStore, Window ownerWindow)
    {
        this.appDataStore = appDataStore;
        this.ownerWindow = ownerWindow;
        ApplySettings(appDataStore.Settings);
        RefreshFavorites();
    }

    public void ApplySettings(AppSettings settings)
    {
        WayMarkEditPanel_Control.ApplyAppearanceSettings(settings);

        bool nextAutoSaveMode = settings.WayMarkFavoriteSaveMode == WayMarkFavoriteSaveMode.Auto;
        if (isAutoSaveMode == nextAutoSaveMode)
        {
            UpdateButtonState();
            return;
        }

        isAutoSaveMode = nextAutoSaveMode;
        if (isAutoSaveMode && hasUnsavedChanges)
        {
            ScheduleAutoSave();
        }
        else if (!isAutoSaveMode)
        {
            autoSaveTimer.Stop();
        }

        SetAutoSaveStatus(hasUnsavedChanges ? "保存中..." : "已保存", hasUnsavedChanges ? Brushes.DimGray : Brushes.DarkGreen);
        UpdateButtonState();
    }

    public void RefreshFavorites(string? preserveSelectedId = null)
    {
        if (appDataStore == null) return;
        if (!TryHandlePendingChanges()) return;

        RefreshFavoritesCore(preserveSelectedId);
    }

    private void RefreshFavoritesCore(string? preserveSelectedId = null)
    {
        if (appDataStore == null) return;

        string? selectedId = preserveSelectedId ?? loadedFavorite?.Id ?? SelectedFavorite?.Id;
        suppressSelectionChanged = true;
        try
        {
            favoriteEntries.Clear();
            foreach (WayMarkFavorite favorite in appDataStore.GetWayMarkFavoritesSnapshot())
            {
                favoriteEntries.Add(favorite);
            }

            Favorites_ListBox.SelectedItem = favoriteEntries.FirstOrDefault(favorite =>
                string.Equals(favorite.Id, selectedId, StringComparison.OrdinalIgnoreCase));
            if (Favorites_ListBox.SelectedItem == null && favoriteEntries.Count > 0)
            {
                Favorites_ListBox.SelectedIndex = 0;
            }
        }
        finally
        {
            suppressSelectionChanged = false;
        }

        UpdateDetail(SelectedFavorite);
    }

    public void ApplyLayoutSettings(WindowLayoutSettings layout)
    {
        double listRatio = ClampRatio(layout.WayMarkFavoriteListRatio);
        double editorRatio = ClampRatio(layout.WayMarkFavoriteEditorRatio);
        double previewRatio = ClampRatio(layout.WayMarkFavoritePreviewRatio);
        double total = listRatio + editorRatio + previewRatio;
        if (total <= 0) return;

        FavoriteList_Column.Width = new GridLength(listRatio / total, GridUnitType.Star);
        FavoriteEditor_Column.Width = new GridLength(editorRatio / total, GridUnitType.Star);
        FavoritePreview_Column.Width = new GridLength(previewRatio / total, GridUnitType.Star);
    }

    public void CaptureLayoutSettings(WindowLayoutSettings layout)
    {
        double totalWidth = FavoriteList_Column.ActualWidth + FavoriteEditor_Column.ActualWidth + FavoritePreview_Column.ActualWidth;
        if (totalWidth <= 1) return;

        layout.WayMarkFavoriteListRatio = FavoriteList_Column.ActualWidth / totalWidth;
        layout.WayMarkFavoriteEditorRatio = FavoriteEditor_Column.ActualWidth / totalWidth;
        layout.WayMarkFavoritePreviewRatio = FavoritePreview_Column.ActualWidth / totalWidth;
    }

    public bool ConfirmSaveOrDiscardChanges()
    {
        return TryHandlePendingChanges();
    }

    private static double ClampRatio(double value)
    {
        return double.IsFinite(value) && value > 0.05 ? value : 0.05;
    }
    public void RefreshMapDataDisplay()
    {
        Favorites_ListBox.Items.Refresh();
        WayMarkEditPanel_Control.RefreshMapDataDisplay(favoriteEntries.Select(favorite => favorite.RegionID));
    }

    private WayMarkFavorite? SelectedFavorite => Favorites_ListBox.SelectedItem as WayMarkFavorite;

    private void Favorites_ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressSelectionChanged) return;

        WayMarkFavorite? previousFavorite = loadedFavorite;
        WayMarkFavorite? nextFavorite = SelectedFavorite;
        if (previousFavorite != null &&
            !string.Equals(previousFavorite.Id, nextFavorite?.Id, StringComparison.OrdinalIgnoreCase) &&
            !TryHandlePendingChanges())
        {
            suppressSelectionChanged = true;
            Favorites_ListBox.SelectedItem = favoriteEntries.FirstOrDefault(favorite =>
                string.Equals(favorite.Id, previousFavorite.Id, StringComparison.OrdinalIgnoreCase));
            suppressSelectionChanged = false;
            return;
        }

        UpdateDetail(nextFavorite);
    }

    private void Favorites_ListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject) is ListBoxItem item)
        {
            item.IsSelected = true;
            Favorites_ListBox.SelectedItem = item.DataContext;
            return;
        }

        Favorites_ListBox.SelectedItem = null;
    }

    private void Favorites_ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        isFavoritesContextMenuOpen = true;
        ClearFavoriteDragState();
        UpdateButtonState();
    }

    private void Favorites_ContextMenu_Closed(object sender, RoutedEventArgs e)
    {
        isFavoritesContextMenuOpen = false;
        SuppressFavoritesListDragUntilLeftButtonReleased();
    }

    private void RefreshFavorites_MenuItem_Click(object sender, RoutedEventArgs e)
    {
        RefreshFavorites();
    }

    private void AddFavorite_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null) return;
        if (!TryHandlePendingChanges()) return;

        try
        {
            WayMarkFavorite favorite = appDataStore.AddWayMarkFavorite(CreateDefaultFavoriteSnapshot(), "新收藏");
            RefreshFavoritesCore(favorite.Id);
        }
        catch (Exception ex) when (ex is InvalidOperationException or AppDataStoreException)
        {
            AppMessageBox.Show(ownerWindow, $"新增标点收藏失败：{ex.Message}", "标点收藏保存受保护", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SaveFavorite_Button_Click(object sender, RoutedEventArgs e)
    {
        SaveLoadedFavorite(showSuccessMessage: true);
    }

    private void DiscardFavorite_Button_Click(object sender, RoutedEventArgs e)
    {
        if (loadedFavorite == null) return;

        string favoriteId = loadedFavorite.Id;
        autoSaveTimer.Stop();
        hasUnsavedChanges = false;
        RefreshFavoritesCore(favoriteId);
    }

    private void DeleteFavorite_Button_Click(object sender, RoutedEventArgs e)
    {
        if (appDataStore == null || SelectedFavorite == null || loadedFavorite == null) return;

        WayMarkFavorite favorite = SelectedFavorite;
        bool shouldResumeAutoSave = isAutoSaveMode && hasUnsavedChanges;
        autoSaveTimer.Stop();
        if (AppMessageBox.Show(ownerWindow, $"确定要删除收藏“{favorite.DisplayName}”吗？", "确认删除收藏", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            if (shouldResumeAutoSave)
            {
                ScheduleAutoSave();
            }
            return;
        }

        try
        {
            appDataStore.DeleteWayMarkFavorite(favorite.Id);
            hasUnsavedChanges = false;
            loadedFavorite = null;
            SetAutoSaveStatus("已保存", Brushes.DarkGreen);
            RefreshFavoritesCore();
        }
        catch (Exception ex) when (ex is InvalidOperationException or AppDataStoreException)
        {
            if (shouldResumeAutoSave)
            {
                ScheduleAutoSave();
            }
            UpdateButtonState();
            AppMessageBox.Show(ownerWindow, $"删除标点收藏失败：{ex.Message}", "标点收藏保存受保护", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void MoveFavoriteUp_Button_Click(object sender, RoutedEventArgs e)
    {
        MoveLoadedFavorite(-1);
    }

    private void MoveFavoriteDown_Button_Click(object sender, RoutedEventArgs e)
    {
        MoveLoadedFavorite(1);
    }

    private void CommentName_TextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (isUpdatingDetail) return;
        MarkDirty();
    }

    private void AutoSaveTimer_Tick(object? sender, EventArgs e)
    {
        autoSaveTimer.Stop();
        if (hasUnsavedChanges)
        {
            SaveLoadedFavorite(showSuccessMessage: false, showFailureMessage: false);
        }
    }

    private void MoveLoadedFavorite(int offset)
    {
        if (appDataStore == null || SelectedFavorite == null) return;

        int sourceIndex = favoriteEntries.IndexOf(SelectedFavorite);
        MoveFavoriteToIndex(sourceIndex, sourceIndex + offset);
    }

    private void UpdateDetail(WayMarkFavorite? favorite)
    {
        isUpdatingDetail = true;
        try
        {
            autoSaveTimer.Stop();
            loadedFavorite = favorite == null ? null : WayMarkSnapshotConverter.CloneFavorite(favorite);
            editingWayMark = loadedFavorite == null
                ? null
                : WayMarkSnapshotConverter.CreateWayMark(loadedFavorite.Marker);
            CommentName_TextBox.Text = loadedFavorite?.CommentName ?? string.Empty;
            WayMarkEditPanel_Control.SetWayMark(editingWayMark, favoriteEntries.Select(item => item.RegionID));
            Preview_Control.SetWayMark(editingWayMark);
            hasUnsavedChanges = false;
            SetAutoSaveStatus(loadedFavorite == null ? string.Empty : "已保存", Brushes.DarkGreen);
        }
        finally
        {
            isUpdatingDetail = false;
        }

        UpdateButtonState();
    }

    private void MarkDirty()
    {
        if (loadedFavorite == null || editingWayMark == null) return;

        hasUnsavedChanges = true;
        if (isAutoSaveMode)
        {
            SetAutoSaveStatus("保存中...", Brushes.DimGray);
            ScheduleAutoSave();
        }

        UpdateButtonState();
    }

    private void ScheduleAutoSave()
    {
        autoSaveTimer.Stop();
        autoSaveTimer.Start();
    }

    private void UpdateButtonState()
    {
        WayMarkFavorite? selectedFavorite = SelectedFavorite;
        bool hasActiveFavorite = loadedFavorite != null &&
            selectedFavorite != null &&
            string.Equals(loadedFavorite.Id, selectedFavorite.Id, StringComparison.OrdinalIgnoreCase);
        int selectedIndex = Favorites_ListBox.SelectedIndex;
        bool canMoveUp = selectedFavorite != null && selectedIndex > 0;
        bool canMoveDown = selectedFavorite != null && selectedIndex >= 0 && selectedIndex < favoriteEntries.Count - 1;
        bool canSortFavorites = favoriteEntries.Count > 1;

        CommentName_TextBox.IsEnabled = hasActiveFavorite;
        ManualSaveButtons_Panel.Visibility = isAutoSaveMode ? Visibility.Collapsed : Visibility.Visible;
        AutoSaveStatus_TextBlock.Visibility = isAutoSaveMode ? Visibility.Visible : Visibility.Collapsed;
        SaveFavorite_Button.IsEnabled = hasActiveFavorite && hasUnsavedChanges && !isAutoSaveMode;
        DiscardFavorite_Button.IsEnabled = hasActiveFavorite && hasUnsavedChanges && !isAutoSaveMode;
        DeleteFavorite_Button.IsEnabled = hasActiveFavorite;
        MoveFavoriteUp_Button.IsEnabled = canMoveUp;
        MoveFavoriteDown_Button.IsEnabled = canMoveDown;

        AddFavorite_MenuItem.IsEnabled = appDataStore != null;
        DeleteFavorite_MenuItem.IsEnabled = hasActiveFavorite;
        MoveFavoriteUp_MenuItem.IsEnabled = canMoveUp;
        MoveFavoriteDown_MenuItem.IsEnabled = canMoveDown;
        SortFavorites_MenuItem.IsEnabled = canSortFavorites;
        SortFavoritesByRegionAscending_MenuItem.IsEnabled = canSortFavorites;
        SortFavoritesByRegionDescending_MenuItem.IsEnabled = canSortFavorites;
    }

    private bool TryHandlePendingChanges()
    {
        if (!TryCommitPendingFavoriteEdits())
        {
            return false;
        }

        if (!hasUnsavedChanges) return true;

        if (isAutoSaveMode)
        {
            autoSaveTimer.Stop();
            return SaveLoadedFavorite(showSuccessMessage: false);
        }

        MessageBoxResult result = AppMessageBox.Show(
            ownerWindow,
            "当前收藏有未保存修改，是否保存？",
            "保存收藏修改",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        return result switch
        {
            MessageBoxResult.Yes => SaveLoadedFavorite(showSuccessMessage: false),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    private bool SaveLoadedFavorite(bool showSuccessMessage, bool showFailureMessage = true)
    {
        autoSaveTimer.Stop();
        if (appDataStore == null || loadedFavorite == null || editingWayMark == null) return false;
        if (!TryCommitPendingFavoriteEdits()) return false;
        autoSaveTimer.Stop();

        WayMarkFavorite updatedFavorite = WayMarkSnapshotConverter.CloneFavorite(loadedFavorite);
        updatedFavorite.CommentName = CommentName_TextBox.Text.Trim();
        updatedFavorite.Marker = WayMarkSnapshotConverter.CreateSnapshot(editingWayMark);
        try
        {
            appDataStore.UpdateWayMarkFavorite(updatedFavorite);
            hasUnsavedChanges = false;
            SetAutoSaveStatus("已保存", Brushes.DarkGreen);
            RefreshFavoritesCore(updatedFavorite.Id);
            if (showSuccessMessage)
            {
                ToastService.ShowSuccess("收藏修改已保存。");
            }
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or AppDataStoreException)
        {
            SetAutoSaveStatus("保存失败", Brushes.Firebrick, ex.Message);
            UpdateButtonState();
            if (showFailureMessage)
            {
                AppMessageBox.Show(ownerWindow, $"保存标点收藏失败：{ex.Message}", "标点收藏保存受保护", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return false;
        }
    }

    private bool TryCommitPendingFavoriteEdits()
    {
        if (WayMarkEditPanel_Control.CommitPendingEdits())
        {
            return true;
        }

        AppMessageBox.Show(
            ownerWindow,
            "当前坐标输入不完整或超出可保存范围，请修正后再继续。",
            "坐标输入无效",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    private void SetAutoSaveStatus(string text, Brush foreground, string? toolTip = null)
    {
        AutoSaveStatus_TextBlock.Text = text;
        AutoSaveStatus_TextBlock.Foreground = foreground;
        AutoSaveStatus_TextBlock.ToolTip = toolTip ?? text;
    }

    private static WayMarkSnapshot CreateDefaultFavoriteSnapshot()
    {
        return new WayMarkSnapshot
        {
            Timestamp = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
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
