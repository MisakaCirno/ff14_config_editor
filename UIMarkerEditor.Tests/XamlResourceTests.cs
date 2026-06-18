using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UIMarkerEditor;

namespace UIMarkerEditor.Tests;

public sealed class XamlResourceTests
{
    [Fact]
    public void MainWindow_CanInitializeWithThemeResources()
    {
        Exception? exception = RunOnStaThread(() =>
        {
            EnsureApplicationResources();
            AssertButtonPadding();
            AssertColoredButtonForegroundPassesIntoTemplate();

            string testDirectory = Path.Combine(
                Path.GetTempPath(),
                "UIMarkerEditor.XamlTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDirectory);
            try
            {
                MainWindow window = new(new AppDataStore(testDirectory));
                window.Close();
            }
            finally
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        });

        Assert.Null(exception);
    }

    private static void AssertColoredButtonForegroundPassesIntoTemplate()
    {
        (string styleKey, string foregroundKey)[] buttonStyles =
        [
            ("PrimaryButtonStyle", "AppPrimaryButtonForegroundBrush"),
            ("SuccessButtonStyle", "AppSuccessButtonForegroundBrush"),
            ("WarningButtonStyle", "AppWarningButtonForegroundBrush"),
            ("DangerButtonStyle", "AppDangerButtonForegroundBrush"),
            ("SecondaryButtonStyle", "AppSecondaryButtonForegroundBrush"),
            ("InfoButtonStyle", "AppInfoButtonForegroundBrush"),
            ("LightButtonStyle", "AppLightButtonForegroundBrush"),
            ("DarkButtonStyle", "AppDarkButtonForegroundBrush"),
            ("OrangeButtonStyle", "AppOrangeButtonForegroundBrush"),
            ("PurpleButtonStyle", "AppPurpleButtonForegroundBrush"),
            ("TealButtonStyle", "AppTealButtonForegroundBrush"),
            ("PinkButtonStyle", "AppPinkButtonForegroundBrush")
        ];

        foreach ((string styleKey, string foregroundKey) in buttonStyles)
        {
            AssertButtonTextForeground(styleKey, foregroundKey);
        }
    }

    private static void AssertButtonPadding()
    {
        Thickness expectedPadding = new(20, 5, 20, 5);
        Button defaultButton = new()
        {
            Content = "Default",
            Style = (Style)Application.Current.FindResource(typeof(Button))
        };

        Assert.Equal(expectedPadding, defaultButton.Padding);

        foreach (string styleKey in GetColoredButtonStyleKeys())
        {
            Button button = new()
            {
                Content = "Check",
                Style = (Style)Application.Current.FindResource(styleKey)
            };

            Assert.Equal(expectedPadding, button.Padding);
        }
    }

    private static void AssertButtonTextForeground(string buttonStyleKey, string foregroundBrushKey)
    {
        Button button = new()
        {
            Content = "Check",
            Style = (Style)Application.Current.FindResource(buttonStyleKey)
        };

        button.ApplyTemplate();
        button.Measure(new Size(120, 32));
        button.Arrange(new Rect(0, 0, 120, 32));
        button.UpdateLayout();

        SolidColorBrush expectedBrush = Assert.IsType<SolidColorBrush>(
            Application.Current.FindResource(foregroundBrushKey));
        SolidColorBrush buttonBrush = Assert.IsType<SolidColorBrush>(button.Foreground);
        Brush? actualTextBrush = FindVisualChild<AccessText>(button)?.Foreground
            ?? FindVisualChild<TextBlock>(button)?.Foreground;
        SolidColorBrush actualBrush = Assert.IsType<SolidColorBrush>(actualTextBrush);

        Assert.Equal(expectedBrush.Color, buttonBrush.Color);
        Assert.Equal(expectedBrush.Color, actualBrush.Color);
    }

    private static string[] GetColoredButtonStyleKeys()
    {
        return
        [
            "PrimaryButtonStyle",
            "SuccessButtonStyle",
            "WarningButtonStyle",
            "DangerButtonStyle",
            "SecondaryButtonStyle",
            "InfoButtonStyle",
            "LightButtonStyle",
            "DarkButtonStyle",
            "OrangeButtonStyle",
            "PurpleButtonStyle",
            "TealButtonStyle",
            "PinkButtonStyle"
        ];
    }

    private static void EnsureApplicationResources()
    {
        Application application = Application.Current ?? new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };

        application.Resources.MergedDictionaries.Clear();
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/UIMarkerEditor;component/Styles/Theme.xaml", UriKind.Absolute)
        });
    }

    private static Exception? RunOnStaThread(Action action)
    {
        Exception? exception = null;
        Thread thread = new(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                Application.Current?.Shutdown();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return exception;
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }

            T? descendant = FindVisualChild<T>(child);
            if (descendant != null)
            {
                return descendant;
            }
        }

        return null;
    }
}
