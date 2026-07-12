using System.IO;
using System.Windows;
using System.Windows.Controls;
using UIMarkerEditor.Controls;

namespace UIMarkerEditor.Tests;

public sealed class ToolSettingsControlTests
{
    [Fact]
    public void LoadSettings_WhenGameDirectoryMissing_ShowsFeatureWarnings()
    {
        string testDirectory = CreateTestDirectory();
        try
        {
            Exception? exception = WpfTestHost.Run(() =>
            {
                WpfTestHost.EnsureApplicationResources();
                AppDataStore store = CreateInitializedStore(testDirectory);

                using ToolSettingsControlHost host = CreateHost(store, () => { });
                Border warningBorder = Assert.IsType<Border>(
                    host.Control.FindName("GameInstallDirectoryCapabilityWarning_Border"));
                TextBlock autoBackupWarning = Assert.IsType<TextBlock>(
                    host.Control.FindName("AutoBackupUnavailable_TextBlock"));
                CheckBox startupWarningCheckBox = Assert.IsType<CheckBox>(
                    host.Control.FindName("ShowGameInstallDirectoryDetectionWarning_CheckBox"));

                Assert.Equal(Visibility.Visible, warningBorder.Visibility);
                Assert.Equal(Visibility.Visible, autoBackupWarning.Visibility);
                Assert.True(startupWarningCheckBox.IsChecked);

                startupWarningCheckBox.IsChecked = false;

                Assert.False(store.Settings.ShowGameInstallDirectoryDetectionWarning);
            });

            Assert.Null(exception);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public void LoadSettings_WhenGameCharacterDirectoryExists_HidesFeatureWarnings()
    {
        string testDirectory = CreateTestDirectory();
        try
        {
            Exception? exception = WpfTestHost.Run(() =>
            {
                WpfTestHost.EnsureApplicationResources();
                AppDataStore store = CreateInitializedStore(testDirectory);
                string gameInstallDirectory = CreateGameInstallDirectory(testDirectory, "AvailableGame");
                Directory.CreateDirectory(Path.Combine(
                    gameInstallDirectory,
                    "game",
                    "My Games",
                    "FINAL FANTASY XIV - A Realm Reborn"));
                AppSettings settings = store.CreateSettingsSnapshot();
                settings.GameInstallDirectory = gameInstallDirectory;
                store.SaveSettings(settings);

                using ToolSettingsControlHost host = CreateHost(store, () => { });
                Border warningBorder = Assert.IsType<Border>(
                    host.Control.FindName("GameInstallDirectoryCapabilityWarning_Border"));
                TextBlock autoBackupWarning = Assert.IsType<TextBlock>(
                    host.Control.FindName("AutoBackupUnavailable_TextBlock"));

                Assert.Equal(Visibility.Collapsed, warningBorder.Visibility);
                Assert.Equal(Visibility.Collapsed, autoBackupWarning.Visibility);
            });

            Assert.Null(exception);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public void CommitPendingSettingsEdits_WhenScanDisabled_SavesGameInstallDirectoryWithoutScanning()
    {
        string testDirectory = CreateTestDirectory();
        try
        {
            Exception? exception = WpfTestHost.Run(() =>
            {
                WpfTestHost.EnsureApplicationResources();
                AppDataStore store = CreateInitializedStore(testDirectory);
                string gameInstallDirectory = CreateGameInstallDirectory(testDirectory, "NoScanGame");
                int scanCount = 0;

                using ToolSettingsControlHost host = CreateHost(store, () => scanCount++);
                TextBox textBox = GetGameInstallDirectoryTextBox(host.Control);
                textBox.Text = gameInstallDirectory;

                Assert.True(host.Control.CommitPendingSettingsEdits(scanLocalCharactersAfterGameInstallDirectorySave: false));

                Assert.Equal(Path.GetFullPath(gameInstallDirectory), store.Settings.GameInstallDirectory);
                Assert.Equal(0, scanCount);
            });

            Assert.Null(exception);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public void CommitPendingSettingsEdits_WhenScanEnabled_ScansAfterSavingGameInstallDirectory()
    {
        string testDirectory = CreateTestDirectory();
        try
        {
            Exception? exception = WpfTestHost.Run(() =>
            {
                WpfTestHost.EnsureApplicationResources();
                AppDataStore store = CreateInitializedStore(testDirectory);
                string gameInstallDirectory = CreateGameInstallDirectory(testDirectory, "ScanGame");
                int scanCount = 0;

                using ToolSettingsControlHost host = CreateHost(store, () => scanCount++);
                TextBox textBox = GetGameInstallDirectoryTextBox(host.Control);
                textBox.Text = gameInstallDirectory;

                Assert.True(host.Control.CommitPendingSettingsEdits());

                Assert.Equal(Path.GetFullPath(gameInstallDirectory), store.Settings.GameInstallDirectory);
                Assert.Equal(1, scanCount);
            });

            Assert.Null(exception);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    private static string CreateTestDirectory()
    {
        string testDirectory = Path.Combine(
            Path.GetTempPath(),
            "UIMarkerEditor.ToolSettingsControlTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        return testDirectory;
    }

    private static AppDataStore CreateInitializedStore(string testDirectory)
    {
        AppDataStore store = new(testDirectory);
        store.Initialize();
        return store;
    }

    private static string CreateGameInstallDirectory(string testDirectory, string name)
    {
        string gameInstallDirectory = Path.Combine(testDirectory, name);
        string gameDirectory = Path.Combine(gameInstallDirectory, "game");
        Directory.CreateDirectory(gameDirectory);
        File.WriteAllText(Path.Combine(gameDirectory, "ffxiv_dx11.exe"), string.Empty);
        return gameInstallDirectory;
    }

    private static ToolSettingsControlHost CreateHost(AppDataStore store, Action scanLocalCharacters)
    {
        ToolSettingsControl control = new();
        Window owner = new()
        {
            Content = control
        };
        control.Initialize(
            store,
            owner,
            () => { },
            () => { },
            (_, _) => true,
            (_, _, _) => Task.CompletedTask,
            () => { },
            () => { },
            () => { },
            scanLocalCharacters,
            (_, _) => Task.CompletedTask,
            _ => Task.CompletedTask,
            () => Task.CompletedTask,
            (_, _) => { },
            () => { });
        control.LoadSettingsIntoUi();
        owner.Show();
        control.UpdateLayout();
        return new ToolSettingsControlHost(control, owner);
    }

    private static TextBox GetGameInstallDirectoryTextBox(ToolSettingsControl control)
    {
        return Assert.IsType<TextBox>(control.FindName("GameInstallDirectory_TextBox"));
    }

    private sealed class ToolSettingsControlHost(ToolSettingsControl control, Window owner) : IDisposable
    {
        public ToolSettingsControl Control { get; } = control;

        public void Dispose()
        {
            owner.Close();
        }
    }
}
