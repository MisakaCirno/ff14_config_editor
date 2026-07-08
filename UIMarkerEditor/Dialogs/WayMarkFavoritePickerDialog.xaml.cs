using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace UIMarkerEditor;

public partial class WayMarkFavoritePickerDialog : Window
{
    private readonly ObservableCollection<WayMarkFavorite> favorites = [];

    public WayMarkFavorite? SelectedFavorite => Favorites_DataGrid.SelectedItem as WayMarkFavorite;

    public WayMarkFavoritePickerDialog(IEnumerable<WayMarkFavorite> favorites)
    {
        InitializeComponent();
        Favorites_DataGrid.ItemsSource = this.favorites;
        foreach (WayMarkFavorite favorite in favorites)
        {
            this.favorites.Add(WayMarkSnapshotConverter.CloneFavorite(favorite));
        }

        if (this.favorites.Count > 0)
        {
            Favorites_DataGrid.SelectedIndex = 0;
        }

        UpdateSelectionState();
    }

    public void ApplyLayoutSettings(WindowLayoutSettings layout)
    {
        double listRatio = ClampRatio(layout.WayMarkFavoritePickerListRatio, 0.6);
        FavoritePickerList_Column.Width = new GridLength(listRatio, GridUnitType.Star);
        FavoritePickerPreview_Column.Width = new GridLength(1 - listRatio, GridUnitType.Star);

        if (!IsFinitePositive(layout.WayMarkFavoritePickerWidth) ||
            !IsFinitePositive(layout.WayMarkFavoritePickerHeight))
        {
            return;
        }

        Rect savedBounds = new(
            layout.WayMarkFavoritePickerLeft,
            layout.WayMarkFavoritePickerTop,
            Math.Max(layout.WayMarkFavoritePickerWidth, MinWidth),
            Math.Max(layout.WayMarkFavoritePickerHeight, MinHeight));
        Width = savedBounds.Width;
        Height = savedBounds.Height;
        if (IsUsableWindowBounds(savedBounds))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = savedBounds.Left;
            Top = savedBounds.Top;
        }
    }

    public void CaptureLayoutSettings(WindowLayoutSettings layout)
    {
        Rect bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (IsFinitePositive(bounds.Width) && IsFinitePositive(bounds.Height))
        {
            layout.WayMarkFavoritePickerLeft = bounds.Left;
            layout.WayMarkFavoritePickerTop = bounds.Top;
            layout.WayMarkFavoritePickerWidth = Math.Max(bounds.Width, MinWidth);
            layout.WayMarkFavoritePickerHeight = Math.Max(bounds.Height, MinHeight);
        }

        double totalWidth = FavoritePickerList_Column.ActualWidth + FavoritePickerPreview_Column.ActualWidth;
        if (totalWidth > 1)
        {
            layout.WayMarkFavoritePickerListRatio = FavoritePickerList_Column.ActualWidth / totalWidth;
        }
    }

    private static double ClampRatio(double value, double defaultValue)
    {
        return double.IsFinite(value) ? Math.Clamp(value, 0.2, 0.8) : defaultValue;
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

    private void Favorites_DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectionState();
    }

    private void Favorites_DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is not WayMarkFavorite)
        {
            return;
        }

        if (SelectedFavorite != null)
        {
            e.Handled = true;
            DialogResult = true;
        }
    }

    private void Ok_Button_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedFavorite == null)
        {
            AppMessageBox.Show(this, "\u8BF7\u5148\u9009\u62E9\u4E00\u4E2A\u6536\u85CF\u6807\u70B9\u3002", "\u63D0\u793A", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    private void UpdateSelectionState()
    {
        WayMarkFavorite? favorite = SelectedFavorite;
        Ok_Button.IsEnabled = favorite != null;
        Preview_Control.SetWayMark(favorite == null
            ? null
            : WayMarkSnapshotConverter.CreateWayMark(favorite.Marker));
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
