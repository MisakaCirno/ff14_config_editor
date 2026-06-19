using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace UIMarkerEditor.Controls;

public partial class WayMarkFavoritesControl
{
    private Point favoriteDragStartPoint;
    private WayMarkFavorite? draggedFavorite;
    private int currentFavoriteDropTargetIndex = -1;
    private bool suppressFavoritesListDragUntilLeftButtonReleased;
    private bool isFavoritesContextMenuOpen;
    private bool isWatchingFavoritesListDragSuppressionRelease;

    private void SortFavoritesByRegionAscending_MenuItem_Click(object sender, RoutedEventArgs e)
    {
        SortFavoritesByRegion(ascending: true);
    }

    private void SortFavoritesByRegionDescending_MenuItem_Click(object sender, RoutedEventArgs e)
    {
        SortFavoritesByRegion(ascending: false);
    }

    private void SortFavoritesByRegion(bool ascending)
    {
        if (appDataStore == null || favoriteEntries.Count <= 1) return;

        string? selectedId = SelectedFavorite?.Id ?? loadedFavorite?.Id;
        try
        {
            if (!appDataStore.SortWayMarkFavoritesByRegion(ascending))
            {
                UpdateButtonState();
                return;
            }

            IReadOnlyList<WayMarkFavorite> sortedFavorites = appDataStore.GetWayMarkFavoritesSnapshot();
            suppressSelectionChanged = true;
            try
            {
                favoriteEntries.Clear();
                foreach (WayMarkFavorite favorite in sortedFavorites)
                {
                    favoriteEntries.Add(favorite);
                }

                Favorites_ListBox.SelectedItem = favoriteEntries.FirstOrDefault(favorite =>
                    string.Equals(favorite.Id, selectedId, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                suppressSelectionChanged = false;
            }

            if (Favorites_ListBox.SelectedItem != null)
            {
                Favorites_ListBox.ScrollIntoView(Favorites_ListBox.SelectedItem);
            }

            MarkFavoriteOrderSaved();
        }
        catch (Exception ex) when (ex is InvalidOperationException or AppDataStoreException)
        {
            AppMessageBox.Show(ownerWindow, $"\u6392\u5E8F\u6807\u70B9\u6536\u85CF\u5931\u8D25\uFF1A{ex.Message}", "\u6807\u70B9\u6536\u85CF\u4FDD\u5B58\u53D7\u4FDD\u62A4", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Favorites_ListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ShouldSuppressFavoritesListDrag())
        {
            ClearFavoriteDragState();
            e.Handled = true;
            return;
        }

        favoriteDragStartPoint = e.GetPosition(null);
        draggedFavorite = null;

        if (FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is WayMarkFavorite favorite)
        {
            if (!ReferenceEquals(SelectedFavorite, favorite))
            {
                Favorites_ListBox.SelectedItem = favorite;
                if (!ReferenceEquals(SelectedFavorite, favorite))
                {
                    return;
                }
            }

            draggedFavorite = favorite;
        }
    }

    private void Favorites_ListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndFavoritesListDragSuppressionIfReleased();
        draggedFavorite = null;
        HideFavoriteDragVisuals();
    }

    private void Favorites_ListBox_MouseMove(object sender, MouseEventArgs e)
    {
        if (ShouldSuppressFavoritesListDrag())
        {
            ClearFavoriteDragState();
            e.Handled = true;
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndFavoritesListDragSuppressionIfReleased();
            ClearFavoriteDragState();
            return;
        }

        if (draggedFavorite is not WayMarkFavorite favorite) return;

        Point currentPosition = e.GetPosition(null);
        Vector diff = favoriteDragStartPoint - currentPosition;
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        try
        {
            ShowFavoriteDragPreview(favorite, e.GetPosition(Favorites_ListBox));
            DragDrop.DoDragDrop(Favorites_ListBox, favorite, DragDropEffects.Move);
        }
        finally
        {
            ClearFavoriteDragState();
        }
    }

    private void Favorites_ListBox_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(WayMarkFavorite)) is not WayMarkFavorite favorite)
        {
            e.Effects = DragDropEffects.None;
            HideFavoriteDragVisuals();
            return;
        }

        Point position = e.GetPosition(Favorites_ListBox);
        currentFavoriteDropTargetIndex = GetFavoriteVisualDropTargetIndex(e.OriginalSource as DependencyObject, position, favoriteEntries.Count);
        UpdateFavoriteDropIndicator(currentFavoriteDropTargetIndex);
        ShowFavoriteDragPreview(favorite, position);

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void Favorites_ListBox_DragLeave(object sender, DragEventArgs e)
    {
        HideFavoriteDragVisuals();
    }

    private void Favorites_ListBox_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (e.Data.GetData(typeof(WayMarkFavorite)) is not WayMarkFavorite favorite) return;

            int sourceIndex = favoriteEntries.IndexOf(favorite);
            int targetIndex = currentFavoriteDropTargetIndex >= 0
                ? currentFavoriteDropTargetIndex
                : GetFavoriteVisualDropTargetIndex(e.OriginalSource as DependencyObject, e.GetPosition(Favorites_ListBox), favoriteEntries.Count);
            if (sourceIndex < 0 || targetIndex < 0) return;

            if (sourceIndex < targetIndex)
            {
                targetIndex--;
            }

            MoveFavoriteToIndex(sourceIndex, targetIndex);
        }
        finally
        {
            ClearFavoriteDragState();
        }
    }

    private bool MoveFavoriteToIndex(int sourceIndex, int targetIndex)
    {
        if (appDataStore == null) return false;
        if (sourceIndex < 0 || sourceIndex >= favoriteEntries.Count) return false;
        if (targetIndex < 0 || targetIndex >= favoriteEntries.Count) return false;
        if (sourceIndex == targetIndex)
        {
            UpdateButtonState();
            return false;
        }

        WayMarkFavorite movedFavorite = favoriteEntries[sourceIndex];
        string? selectedId = SelectedFavorite?.Id ?? loadedFavorite?.Id ?? movedFavorite.Id;
        try
        {
            if (!appDataStore.MoveWayMarkFavorite(movedFavorite.Id, targetIndex - sourceIndex))
            {
                UpdateButtonState();
                return false;
            }

            suppressSelectionChanged = true;
            try
            {
                favoriteEntries.RemoveAt(sourceIndex);
                favoriteEntries.Insert(targetIndex, movedFavorite);
                Favorites_ListBox.SelectedItem = favoriteEntries.FirstOrDefault(favorite =>
                    string.Equals(favorite.Id, selectedId, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                suppressSelectionChanged = false;
            }

            if (Favorites_ListBox.SelectedItem != null)
            {
                Favorites_ListBox.ScrollIntoView(Favorites_ListBox.SelectedItem);
            }

            MarkFavoriteOrderSaved();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or AppDataStoreException)
        {
            AppMessageBox.Show(ownerWindow, $"\u8C03\u6574\u6807\u70B9\u6536\u85CF\u987A\u5E8F\u5931\u8D25\uFF1A{ex.Message}", "\u6807\u70B9\u6536\u85CF\u4FDD\u5B58\u53D7\u4FDD\u62A4", MessageBoxButton.OK, MessageBoxImage.Warning);
            UpdateButtonState();
            return false;
        }
    }

    private void MarkFavoriteOrderSaved()
    {
        if (!hasUnsavedChanges)
        {
            SetAutoSaveStatus(loadedFavorite == null ? string.Empty : "\u5DF2\u4FDD\u5B58", Brushes.DarkGreen);
        }
        else if (isAutoSaveMode)
        {
            SetAutoSaveStatus("\u4FDD\u5B58\u4E2D...", Brushes.DimGray);
        }

        UpdateButtonState();
    }

    private int GetFavoriteVisualDropTargetIndex(DependencyObject? source, Point position, int itemCount)
    {
        ListBoxItem? targetItem = FindVisualParent<ListBoxItem>(source);
        if (targetItem?.DataContext is WayMarkFavorite targetFavorite)
        {
            int targetIndex = Favorites_ListBox.Items.IndexOf(targetFavorite);
            Point itemPosition = source != null ? Mouse.GetPosition(targetItem) : position;
            return itemPosition.Y > targetItem.ActualHeight / 2 ? targetIndex + 1 : targetIndex;
        }

        if (position.Y <= 0)
        {
            return 0;
        }

        return itemCount;
    }

    private void UpdateFavoriteDropIndicator(int insertionIndex)
    {
        double y = GetFavoriteDropIndicatorY(insertionIndex);
        if (double.IsNaN(y))
        {
            FavoriteDropIndicator_Line.Visibility = Visibility.Collapsed;
            return;
        }

        FavoriteDropIndicator_Line.Width = Math.Max(0, Favorites_ListBox.ActualWidth - 8);
        Canvas.SetLeft(FavoriteDropIndicator_Line, 4);
        Canvas.SetTop(FavoriteDropIndicator_Line, y);
        FavoriteDropIndicator_Line.Visibility = Visibility.Visible;
    }

    private double GetFavoriteDropIndicatorY(int insertionIndex)
    {
        int itemCount = Favorites_ListBox.Items.Count;
        if (itemCount == 0) return 2;

        int containerIndex = Math.Clamp(insertionIndex, 0, itemCount - 1);
        if (Favorites_ListBox.ItemContainerGenerator.ContainerFromIndex(containerIndex) is not ListBoxItem item)
        {
            return double.NaN;
        }

        Point itemTop = item.TranslatePoint(new Point(0, 0), FavoriteDragOverlay_Canvas);
        return insertionIndex >= itemCount
            ? itemTop.Y + item.ActualHeight - 1
            : itemTop.Y - 1;
    }

    private void ShowFavoriteDragPreview(WayMarkFavorite favorite, Point position)
    {
        FavoriteDragPreview_TextBlock.Text = favorite.DisplayName;
        FavoriteDragPreview_Border.Width = Math.Max(0, Favorites_ListBox.ActualWidth - 8);
        FavoriteDragPreview_Border.Visibility = Visibility.Visible;

        Canvas.SetLeft(FavoriteDragPreview_Border, 4);
        Canvas.SetTop(FavoriteDragPreview_Border, Math.Min(position.Y + 12, Math.Max(0, Favorites_ListBox.ActualHeight - 36)));
    }

    private void HideFavoriteDragVisuals()
    {
        currentFavoriteDropTargetIndex = -1;
        FavoriteDropIndicator_Line.Visibility = Visibility.Collapsed;
        FavoriteDragPreview_Border.Visibility = Visibility.Collapsed;
    }

    private void ClearFavoriteDragState()
    {
        draggedFavorite = null;
        HideFavoriteDragVisuals();

        if (ReferenceEquals(Mouse.Captured, Favorites_ListBox) || Favorites_ListBox.IsMouseCaptureWithin)
        {
            Mouse.Capture(null);
        }
    }

    private void SuppressFavoritesListDragUntilLeftButtonReleased()
    {
        ClearFavoriteDragState();

        if (Mouse.LeftButton == MouseButtonState.Pressed)
        {
            suppressFavoritesListDragUntilLeftButtonReleased = true;
            StartWatchingFavoritesListDragSuppressionRelease();
            return;
        }

        EndFavoritesListDragSuppression();
    }

    private bool ShouldSuppressFavoritesListDrag()
    {
        if (isFavoritesContextMenuOpen)
        {
            return true;
        }

        if (!suppressFavoritesListDragUntilLeftButtonReleased)
        {
            return false;
        }

        if (Mouse.LeftButton == MouseButtonState.Released)
        {
            EndFavoritesListDragSuppression();
            return false;
        }

        return true;
    }

    private void EndFavoritesListDragSuppressionIfReleased()
    {
        if (Mouse.LeftButton == MouseButtonState.Released)
        {
            EndFavoritesListDragSuppression();
        }
    }

    private void EndFavoritesListDragSuppression()
    {
        suppressFavoritesListDragUntilLeftButtonReleased = false;
        StopWatchingFavoritesListDragSuppressionRelease();
    }

    private void StartWatchingFavoritesListDragSuppressionRelease()
    {
        if (isWatchingFavoritesListDragSuppressionRelease)
        {
            return;
        }

        InputManager.Current.PostProcessInput += FavoriteInputManager_PostProcessInput;
        isWatchingFavoritesListDragSuppressionRelease = true;
    }

    private void StopWatchingFavoritesListDragSuppressionRelease()
    {
        if (!isWatchingFavoritesListDragSuppressionRelease)
        {
            return;
        }

        InputManager.Current.PostProcessInput -= FavoriteInputManager_PostProcessInput;
        isWatchingFavoritesListDragSuppressionRelease = false;
    }

    private void FavoriteInputManager_PostProcessInput(object sender, ProcessInputEventArgs e)
    {
        if (Mouse.LeftButton == MouseButtonState.Released)
        {
            EndFavoritesListDragSuppression();
        }
    }
}
