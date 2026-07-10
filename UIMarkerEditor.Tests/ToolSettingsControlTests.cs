using System.IO;
using System.Windows;
using System.Windows.Controls;
using UIMarkerEditor.Controls;

namespace UIMarkerEditor.Tests;

public sealed class ToolSettingsControlTests
{
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
