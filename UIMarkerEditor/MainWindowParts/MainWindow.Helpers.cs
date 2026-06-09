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

namespace UIMarkerEditor
{
    public partial class MainWindow
    {
        private void UpdateDataVersionText()
        {
            WayMarkEditor_Control.UpdateDataVersionText(appDataStore.MapDataVersion);
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

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject source) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(source); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(source, i);
                if (child is T matched)
                {
                    yield return matched;
                }

                foreach (T descendant in FindVisualChildren<T>(child))
                {
                    yield return descendant;
                }
            }
        }

    }
}
