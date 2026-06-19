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

namespace UIMarkerEditor.Controls
{
    public partial class WayMarkEditorControl
    {
        private void MoveUp_Button_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedWayMark(-1);
        }

        private void MoveDown_Button_Click(object sender, RoutedEventArgs e)
        {
            MoveSelectedWayMark(1);
        }

        private void MoveSelectedWayMark(int offset)
        {
            if (WayMark_ListBox.SelectedItem is not WayMark selectedMark)
            {
                MessageBox.Show("请先选择一个要移动的标点槽位。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            List<WayMark>? wayMarks = GetWayMarks();
            if (wayMarks == null) return;

            int currentIndex = wayMarks.IndexOf(selectedMark);
            int targetIndex = currentIndex + offset;
            MoveWayMark(currentIndex, targetIndex);
        }

        private void SortWayMarksByRegionAscending_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            SortWayMarksByRegion(ascending: true);
        }

        private void SortWayMarksByRegionDescending_MenuItem_Click(object sender, RoutedEventArgs e)
        {
            SortWayMarksByRegion(ascending: false);
        }

        private void SortWayMarksByRegion(bool ascending)
        {
            List<WayMark>? marks = GetWayMarks();
            if (marks == null || marks.Count <= 1) return;

            WayMark? selectedMark = SelectedWayMark;
            List<WayMark> sortedMarks = ascending
                ? [.. marks
                    .OrderBy(mark => WayMarkRegionSort.GetZeroLastBucket(mark.RegionID))
                    .ThenBy(mark => mark.RegionID)]
                : [.. marks
                    .OrderBy(mark => WayMarkRegionSort.GetZeroLastBucket(mark.RegionID))
                    .ThenByDescending(mark => mark.RegionID)];
            if (marks.SequenceEqual(sortedMarks))
            {
                UpdateMoveButtonState();
                return;
            }

            marks.Clear();
            marks.AddRange(sortedMarks);
            WayMark_ListBox.Items.Refresh();
            if (selectedMark != null && marks.Contains(selectedMark))
            {
                WayMark_ListBox.SelectedItem = selectedMark;
                WayMark_ListBox.ScrollIntoView(selectedMark);
            }

            UpdateMoveButtonState();
            NotifyWayMarksChanged();
        }
        private void MoveWayMark(int sourceIndex, int targetIndex)
        {
            List<WayMark>? wayMarks = GetWayMarks();
            if (wayMarks == null) return;
            if (sourceIndex < 0 || sourceIndex >= wayMarks.Count) return;
            if (targetIndex < 0 || targetIndex >= wayMarks.Count) return;
            if (sourceIndex == targetIndex) return;

            WayMark movedMark = wayMarks[sourceIndex];
            wayMarks.RemoveAt(sourceIndex);
            wayMarks.Insert(targetIndex, movedMark);

            WayMark_ListBox.Items.Refresh();
            WayMark_ListBox.SelectedItem = movedMark;
            WayMark_ListBox.ScrollIntoView(movedMark);
            UpdateMoveButtonState();
            NotifyWayMarksChanged();
        }

        private List<WayMark>? GetWayMarks()
        {
            return wayMarks;
        }

        private IEnumerable<ushort> GetLoadedRegionIds()
        {
            return GetWayMarks()?.Select(mark => mark.RegionID) ?? [];
        }

        private void UpdateMoveButtonState()
        {
            int selectedIndex = WayMark_ListBox.SelectedIndex;
            int itemCount = WayMark_ListBox.Items.Count;
            bool canMoveUp = selectedIndex > 0;
            bool canMoveDown = selectedIndex >= 0 && selectedIndex < itemCount - 1;

            MoveUp_Button.IsEnabled = canMoveUp;
            MoveDown_Button.IsEnabled = canMoveDown;
            MoveWayMarkUp_MenuItem.IsEnabled = canMoveUp;
            MoveWayMarkDown_MenuItem.IsEnabled = canMoveDown;
        }

        private void WayMark_ListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ShouldSuppressWayMarkListDrag())
            {
                ClearWayMarkDragState();
                e.Handled = true;
                return;
            }

            dragStartPoint = e.GetPosition(null);
            draggedWayMark = null;

            ListBoxItem? item = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
            if (item?.DataContext is WayMark wayMark)
            {
                draggedWayMark = wayMark;
                WayMark_ListBox.SelectedItem = wayMark;
            }
        }

        private void WayMark_ListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            EndWayMarkListDragSuppressionIfReleased();
            draggedWayMark = null;
            HideDragVisuals();
        }

        private void WayMark_ListBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (ShouldSuppressWayMarkListDrag())
            {
                ClearWayMarkDragState();
                e.Handled = true;
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndWayMarkListDragSuppressionIfReleased();
                ClearWayMarkDragState();
                return;
            }

            if (draggedWayMark is not WayMark draggedMark) return;

            Point currentPosition = e.GetPosition(null);
            Vector diff = dragStartPoint - currentPosition;
            if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            try
            {
                ShowDragPreview(draggedMark, e.GetPosition(WayMark_ListBox));
                DragDrop.DoDragDrop(WayMark_ListBox, draggedMark, DragDropEffects.Move);
            }
            finally
            {
                ClearWayMarkDragState();
            }
        }

        private void WayMark_ListBox_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(WayMark)) is not WayMark draggedMark)
            {
                e.Effects = DragDropEffects.None;
                HideDragVisuals();
                return;
            }

            List<WayMark>? wayMarks = GetWayMarks();
            if (wayMarks == null)
            {
                e.Effects = DragDropEffects.None;
                HideDragVisuals();
                return;
            }

            Point position = e.GetPosition(WayMark_ListBox);
            currentDropTargetIndex = GetVisualDropTargetIndex(e.OriginalSource as DependencyObject, position, wayMarks.Count);
            UpdateDropIndicator(currentDropTargetIndex);
            ShowDragPreview(draggedMark, position);

            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void WayMark_ListBox_DragLeave(object sender, DragEventArgs e)
        {
            HideDragVisuals();
        }

        private void WayMark_ListBox_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (e.Data.GetData(typeof(WayMark)) is not WayMark draggedMark) return;

                List<WayMark>? wayMarks = GetWayMarks();
                if (wayMarks == null) return;

                int sourceIndex = wayMarks.IndexOf(draggedMark);
                int targetIndex = currentDropTargetIndex >= 0
                    ? currentDropTargetIndex
                    : GetVisualDropTargetIndex(e.OriginalSource as DependencyObject, e.GetPosition(WayMark_ListBox), wayMarks.Count);
                if (sourceIndex < 0 || targetIndex < 0) return;

                if (sourceIndex < targetIndex)
                {
                    targetIndex--;
                }

                MoveWayMark(sourceIndex, targetIndex);
            }
            finally
            {
                ClearWayMarkDragState();
            }
        }

        private int GetVisualDropTargetIndex(DependencyObject? source, Point position, int itemCount)
        {
            ListBoxItem? targetItem = FindVisualParent<ListBoxItem>(source);
            if (targetItem?.DataContext is WayMark targetMark)
            {
                int targetIndex = WayMark_ListBox.Items.IndexOf(targetMark);
                Point itemPosition = position;
                if (source != null)
                {
                    itemPosition = Mouse.GetPosition(targetItem);
                }

                return itemPosition.Y > targetItem.ActualHeight / 2 ? targetIndex + 1 : targetIndex;
            }

            if (position.Y <= 0)
            {
                return 0;
            }

            return itemCount;
        }

        private void UpdateDropIndicator(int insertionIndex)
        {
            double y = GetDropIndicatorY(insertionIndex);
            if (double.IsNaN(y))
            {
                DropIndicator_Line.Visibility = Visibility.Collapsed;
                return;
            }

            DropIndicator_Line.Width = Math.Max(0, WayMark_ListBox.ActualWidth - 8);
            Canvas.SetLeft(DropIndicator_Line, 4);
            Canvas.SetTop(DropIndicator_Line, y);
            DropIndicator_Line.Visibility = Visibility.Visible;
        }

        private double GetDropIndicatorY(int insertionIndex)
        {
            int itemCount = WayMark_ListBox.Items.Count;
            if (itemCount == 0) return 2;

            int containerIndex = Math.Clamp(insertionIndex, 0, itemCount - 1);
            if (WayMark_ListBox.ItemContainerGenerator.ContainerFromIndex(containerIndex) is not ListBoxItem item)
            {
                return double.NaN;
            }

            Point itemTop = item.TranslatePoint(new Point(0, 0), DragOverlay_Canvas);
            return insertionIndex >= itemCount
                ? itemTop.Y + item.ActualHeight - 1
                : itemTop.Y - 1;
        }

        private void ShowDragPreview(WayMark wayMark, Point position)
        {
            DragPreview_TextBlock.Text = $"{MapData.GetName(wayMark.RegionID)}({wayMark.RegionID})";
            DragPreview_Border.Width = Math.Max(0, WayMark_ListBox.ActualWidth - 8);
            DragPreview_Border.Visibility = Visibility.Visible;

            Canvas.SetLeft(DragPreview_Border, 4);
            Canvas.SetTop(DragPreview_Border, Math.Min(position.Y + 12, Math.Max(0, WayMark_ListBox.ActualHeight - 36)));
        }

        private void HideDragVisuals()
        {
            currentDropTargetIndex = -1;
            DropIndicator_Line.Visibility = Visibility.Collapsed;
            DragPreview_Border.Visibility = Visibility.Collapsed;
        }

        private void ClearWayMarkDragState()
        {
            draggedWayMark = null;
            HideDragVisuals();

            if (ReferenceEquals(Mouse.Captured, WayMark_ListBox) || WayMark_ListBox.IsMouseCaptureWithin)
            {
                Mouse.Capture(null);
            }
        }

        private void SuppressWayMarkListDragUntilLeftButtonReleased()
        {
            ClearWayMarkDragState();

            if (Mouse.LeftButton == MouseButtonState.Pressed)
            {
                suppressWayMarkListDragUntilLeftButtonReleased = true;
                StartWatchingWayMarkListDragSuppressionRelease();
                return;
            }

            EndWayMarkListDragSuppression();
        }

        private bool ShouldSuppressWayMarkListDrag()
        {
            if (isWayMarkContextMenuOpen)
            {
                return true;
            }

            if (!suppressWayMarkListDragUntilLeftButtonReleased)
            {
                return false;
            }

            if (Mouse.LeftButton == MouseButtonState.Released)
            {
                EndWayMarkListDragSuppression();
                return false;
            }

            return true;
        }

        private void EndWayMarkListDragSuppressionIfReleased()
        {
            if (Mouse.LeftButton == MouseButtonState.Released)
            {
                EndWayMarkListDragSuppression();
            }
        }

        private void EndWayMarkListDragSuppression()
        {
            suppressWayMarkListDragUntilLeftButtonReleased = false;
            StopWatchingWayMarkListDragSuppressionRelease();
        }

        private void StartWatchingWayMarkListDragSuppressionRelease()
        {
            if (isWatchingWayMarkListDragSuppressionRelease)
            {
                return;
            }

            InputManager.Current.PostProcessInput += WayMarkInputManager_PostProcessInput;
            isWatchingWayMarkListDragSuppressionRelease = true;
        }

        private void StopWatchingWayMarkListDragSuppressionRelease()
        {
            if (!isWatchingWayMarkListDragSuppressionRelease)
            {
                return;
            }

            InputManager.Current.PostProcessInput -= WayMarkInputManager_PostProcessInput;
            isWatchingWayMarkListDragSuppressionRelease = false;
        }

        private void WayMarkInputManager_PostProcessInput(object sender, ProcessInputEventArgs e)
        {
            if (Mouse.LeftButton == MouseButtonState.Released)
            {
                EndWayMarkListDragSuppression();
            }
        }
    }
}
