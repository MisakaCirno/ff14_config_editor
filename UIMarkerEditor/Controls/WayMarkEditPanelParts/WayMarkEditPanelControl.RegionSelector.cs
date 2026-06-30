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
    public partial class WayMarkEditPanelControl
    {
        private void RefreshRegionOptions(IEnumerable<ushort>? extraRegionIds = null)
        {
            Dictionary<ushort, MapData> options = MapData.GetMapDataDisplayDict();
            if (extraRegionIds != null)
            {
                foreach (ushort regionId in extraRegionIds)
                {
                    if (!options.ContainsKey(regionId))
                    {
                        options[regionId] = new MapData(regionId, MapData.GetName(regionId));
                    }
                }
            }

            regionOptions.Clear();
            foreach (MapData option in options.Values.OrderBy(option => option.Index))
            {
                regionOptions.Add(option);
            }

            regionOptionsView ??= CollectionViewSource.GetDefaultView(regionOptions);
            regionOptionsView.Filter = FilterRegionOption;
            regionOptionsView.Refresh();
            RegionOptions_ListBox.ItemsSource = regionOptionsView;
        }

        private bool FilterRegionOption(object item)
        {
            if (item is not MapData mapData) return false;
            if (string.IsNullOrWhiteSpace(regionFilterText)) return true;

            string[] keywords = regionFilterText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (string keyword in keywords)
            {
                if (!mapData.DisplayName.Contains(keyword, StringComparison.OrdinalIgnoreCase) &&
                    !mapData.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) &&
                    !mapData.Index.ToString(CultureInfo.InvariantCulture).Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private void RegionSearch_TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (suppressRegionTextChanged) return;

            regionFilterText = RegionSearch_TextBox.Text.Trim();
            regionOptionsView?.Refresh();
            OpenRegionPopup();
        }

        private void RegionSearch_TextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            PrepareRegionPopup();
            OpenRegionPopupAfterFocus();
        }

        private void RegionSearch_TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            PrepareRegionPopup();
            OpenRegionPopupAfterFocus();
        }

        private void RegionSearch_TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            PrepareRegionPopup();
            OpenRegionPopupAfterFocus();
        }

        private void PrepareRegionPopup()
        {
            regionFilterText = string.Empty;
            regionOptionsView?.Refresh();
            SetRegionClearButtonVisible(true);
        }

        private void OpenRegionPopupAfterFocus()
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (RegionSearch_TextBox.IsKeyboardFocusWithin)
                {
                    RegionSearch_Popup.IsOpen = true;
                }
            });
        }

        private void RegionSearch_TextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (isClearingRegionText) return;
            if (isSelectingRegionFromPopup) return;
            if (IsFocusInRegionOptions(e.NewFocus as DependencyObject)) return;

            CommitFreeRegionTextOrRestore();
            SetRegionClearButtonVisible(false);
        }

        private void RegionSearch_TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitFreeRegionTextOrRestore();
                RegionSearch_Popup.IsOpen = false;
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                if (currentWayMark != null)
                {
                    SetRegionSearchText(currentWayMark.RegionID);
                }

                regionFilterText = string.Empty;
                regionOptionsView?.Refresh();
                RegionSearch_Popup.IsOpen = false;
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down)
            {
                OpenRegionPopup();
                RegionOptions_ListBox.Focus();
                if (RegionOptions_ListBox.SelectedIndex < 0 && RegionOptions_ListBox.Items.Count > 0)
                {
                    RegionOptions_ListBox.SelectedIndex = 0;
                }
                e.Handled = true;
            }
        }

        private void RegionDropDown_Button_Click(object sender, RoutedEventArgs e)
        {
            if (RegionSearch_Popup.IsOpen)
            {
                RegionSearch_Popup.IsOpen = false;
                return;
            }

            regionFilterText = string.Empty;
            regionOptionsView?.Refresh();
            RegionSearch_TextBox.Focus();
            OpenRegionPopupAfterFocus();
        }

        private void RegionClear_Button_Click(object sender, RoutedEventArgs e)
        {
            RegionSearch_TextBox.Clear();
            regionFilterText = string.Empty;
            regionOptionsView?.Refresh();
            RegionSearch_TextBox.Focus();
            OpenRegionPopupAfterFocus();
            Dispatcher.BeginInvoke(() => isClearingRegionText = false);
        }

        private void RegionClear_Button_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            isClearingRegionText = true;
        }

        private void RegionClear_Button_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            Dispatcher.BeginInvoke(() => isClearingRegionText = false);
        }

        private void RegionOptions_ListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            isSelectingRegionFromPopup = true;
        }

        private void RegionOptions_ListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject)?.DataContext is MapData selectedRegion)
            {
                RegionOptions_ListBox.SelectedItem = selectedRegion;
                CommitSelectedRegionOption(selectedRegion);
                e.Handled = true;
                return;
            }

            CommitSelectedRegionOption();
        }

        private void RegionOptions_ListBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitSelectedRegionOption();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                RegionSearch_Popup.IsOpen = false;
                RegionSearch_TextBox.Focus();
                e.Handled = true;
            }
        }

        private bool IsFocusInRegionOptions(DependencyObject? focusedElement)
        {
            while (focusedElement != null)
            {
                if (focusedElement == RegionOptions_ListBox)
                {
                    return true;
                }

                focusedElement = VisualTreeHelper.GetParent(focusedElement);
            }

            return false;
        }

        private void RegionSearch_Popup_Closed(object sender, EventArgs e)
        {
            if (isClearingRegionText) return;
            if (isSelectingRegionFromPopup)
            {
                isSelectingRegionFromPopup = false;
                return;
            }

            RestoreRegionSearchText();
        }

        private void CloseRegionSearchIfClickedOutside(DependencyObject? clickedElement)
        {
            if (!RegionSearch_TextBox.IsKeyboardFocusWithin && !RegionSearch_Popup.IsOpen) return;
            if (IsElementWithin(clickedElement, RegionSearch_Container) ||
                IsElementWithin(clickedElement, RegionOptions_ListBox))
            {
                return;
            }

            RestoreRegionSearchText();
            RegionSearch_Popup.IsOpen = false;
            SetRegionClearButtonVisible(false);
            Keyboard.ClearFocus();
        }

        private void SetRegionClearButtonVisible(bool visible)
        {
            RegionClear_Button.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private static bool IsElementWithin(DependencyObject? element, DependencyObject container)
        {
            while (element != null)
            {
                if (element == container)
                {
                    return true;
                }

                element = VisualTreeHelper.GetParent(element);
            }

            return false;
        }

        private void OpenRegionPopup()
        {
            if (!RegionSearch_TextBox.IsKeyboardFocusWithin) return;

            RegionSearch_Popup.IsOpen = true;
        }

        private void CommitSelectedRegionOption()
        {
            if (RegionOptions_ListBox.SelectedItem is not MapData selectedRegion) return;
            CommitSelectedRegionOption(selectedRegion);
        }

        private void CommitSelectedRegionOption(MapData selectedRegion)
        {
            if (currentWayMark == null) return;

            currentWayMark.RegionID = selectedRegion.Index;
            SetRegionSearchText(selectedRegion.Index);
            regionFilterText = string.Empty;
            regionOptionsView?.Refresh();
            RegionSearch_Popup.IsOpen = false;
            SetRegionClearButtonVisible(false);
            isSelectingRegionFromPopup = false;
        }

        private void RestoreRegionSearchText()
        {
            if (currentWayMark == null) return;

            regionFilterText = string.Empty;
            regionOptionsView?.Refresh();
            SetRegionSearchText(currentWayMark.RegionID);
        }

        private void CommitFreeRegionTextOrRestore()
        {
            if (unknownMapIdPolicy != UnknownMapIdPolicy.AllowUnknown || currentWayMark == null)
            {
                RestoreRegionSearchText();
                return;
            }

            string text = RegionSearch_TextBox.Text.Trim();
            if (!ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort regionId))
            {
                RestoreRegionSearchText();
                return;
            }

            currentWayMark.RegionID = regionId;
            EnsureRegionOption(regionId);
            SetRegionSearchText(regionId);
            regionFilterText = string.Empty;
            regionOptionsView?.Refresh();
        }

        private void EnsureRegionOption(ushort regionId)
        {
            if (regionOptions.Any(option => option.Index == regionId)) return;

            regionOptions.Add(new MapData(regionId, MapData.GetName(regionId)));
            SortRegionOptions();
        }

        private void SortRegionOptions()
        {
            List<MapData> sortedOptions = [.. regionOptions.OrderBy(option => option.Index)];
            regionOptions.Clear();
            foreach (MapData option in sortedOptions)
            {
                regionOptions.Add(option);
            }
        }

        private void SetRegionSearchText(ushort regionId)
        {
            MapData? selectedMap = regionOptions.FirstOrDefault(option => option.Index == regionId);
            if (selectedMap == null)
            {
                EnsureRegionOption(regionId);
                selectedMap = regionOptions.FirstOrDefault(option => option.Index == regionId);
            }

            try
            {
                suppressRegionTextChanged = true;
                RegionSearch_TextBox.Text = selectedMap?.DisplayName ?? MapData.GetDisplayName(regionId);
                RegionSearch_TextBox.CaretIndex = RegionSearch_TextBox.Text.Length;
            }
            finally
            {
                suppressRegionTextChanged = false;
            }
        }

    }
}
