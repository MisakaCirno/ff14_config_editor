using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace UIMarkerEditor;

public partial class LocalGameCharacterPickerDialog : Window
{
    private readonly ObservableCollection<LocalGameCharacter> characters = [];

    internal LocalGameCharacter? SelectedCharacter => Characters_DataGrid.SelectedItem as LocalGameCharacter;

    internal LocalGameCharacterPickerDialog(IEnumerable<LocalGameCharacter> characters)
    {
        InitializeComponent();
        Characters_DataGrid.ItemsSource = this.characters;
        foreach (LocalGameCharacter character in characters)
        {
            this.characters.Add(character);
        }

        if (this.characters.Count > 0)
        {
            Characters_DataGrid.SelectedIndex = 0;
        }

        UpdateSelectionState();
    }

    private void Characters_DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectionState();
    }

    private void Characters_DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject)?.Item is not LocalGameCharacter)
        {
            return;
        }

        if (SelectedCharacter != null)
        {
            e.Handled = true;
            DialogResult = true;
        }
    }

    private void Ok_Button_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedCharacter == null)
        {
            AppMessageBox.Show(this, "请先选择一个本地角色。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    private void UpdateSelectionState()
    {
        Ok_Button.IsEnabled = SelectedCharacter != null;
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
