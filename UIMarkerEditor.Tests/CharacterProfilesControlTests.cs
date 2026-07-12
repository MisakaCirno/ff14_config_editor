using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using UIMarkerEditor.Controls;

namespace UIMarkerEditor.Tests;

public sealed class CharacterProfilesControlTests
{
    [Fact]
    public void TryRefreshCharacterListFromExternalChange_WhenDetailIsDirty_SkipsReload()
    {
        Exception? exception = WpfTestHost.Run(() =>
        {
            WpfTestHost.EnsureApplicationResources();
            string testDirectory = Path.Combine(
                Path.GetTempPath(),
                "UIMarkerEditor.CharacterProfilesTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDirectory);
            try
            {
                AppDataStore store = new(testDirectory);
                store.Initialize();
                store.Characters.Add(new CharacterProfile
                {
                    UserID = "0011223344556677",
                    CharacterName = "初始角色"
                });

                CharacterProfilesControl control = new();
                System.Windows.Window ownerWindow = new();
                try
                {
                    control.Initialize(store, ownerWindow, () => { }, () => { }, () => { });
                    control.RefreshCharacterList();

                    DataGrid characterGrid = Assert.IsType<DataGrid>(control.FindName("Character_DataGrid"));
                    Assert.Single(characterGrid.Items);

                    store.Characters.Add(new CharacterProfile
                    {
                        UserID = "8899AABBCCDDEEFF",
                        CharacterName = "外部新增"
                    });
                    SetPrivateBoolean(control, "isCharacterDetailDirty", true);

                    Assert.False(control.TryRefreshCharacterListFromExternalChange());
                    Assert.Single(characterGrid.Items);

                    SetPrivateBoolean(control, "isCharacterDetailDirty", false);

                    Assert.True(control.TryRefreshCharacterListFromExternalChange());
                    Assert.Equal(2, characterGrid.Items.Count);
                }
                finally
                {
                    ownerWindow.Close();
                }
            }
            finally
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        });

        Assert.Null(exception);
    }

    [Fact]
    public void CharacterDetailEdits_DoNotMutateStoreUntilSaved()
    {
        Exception? exception = WpfTestHost.Run(() =>
        {
            WpfTestHost.EnsureApplicationResources();
            string testDirectory = Path.Combine(
                Path.GetTempPath(),
                "UIMarkerEditor.CharacterProfilesTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDirectory);
            try
            {
                AppDataStore store = new(testDirectory);
                store.Initialize();
                CharacterProfile profile = new()
                {
                    UserID = "0011223344556677",
                    CharacterName = "初始角色"
                };
                store.Characters.Add(profile);

                CharacterProfilesControl control = new();
                Window ownerWindow = new();
                int recentFilesRefreshCount = 0;
                try
                {
                    control.Initialize(store, ownerWindow, () => { }, () => { }, () => recentFilesRefreshCount++);
                    LoadCharacterProfileIntoDetail(control, profile);

                    TextBox characterNameTextBox = Assert.IsType<TextBox>(control.FindName("CharacterName_TextBox"));
                    characterNameTextBox.Text = "草稿角色";

                    Assert.Equal("初始角色", profile.CharacterName);

                    Assert.True(InvokeTrySaveCharacterProfile(control, showSuccessMessage: false, out string savedUserID));

                    Assert.Equal("0011223344556677", savedUserID);
                    Assert.Equal("草稿角色", profile.CharacterName);
                    Assert.Equal(1, recentFilesRefreshCount);
                }
                finally
                {
                    ownerWindow.Close();
                }
            }
            finally
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        });

        Assert.Null(exception);
    }

    [Fact]
    public void SaveCharacter_AppliesPendingExternalRefresh()
    {
        Exception? exception = WpfTestHost.Run(() =>
        {
            WpfTestHost.EnsureApplicationResources();
            string testDirectory = Path.Combine(
                Path.GetTempPath(),
                "UIMarkerEditor.CharacterProfilesTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDirectory);
            try
            {
                AppDataStore store = new(testDirectory);
                store.Initialize();
                CharacterProfile profile = new()
                {
                    UserID = "0011223344556677",
                    CharacterName = "初始角色"
                };
                store.Characters.Add(profile);

                CharacterProfilesControl control = new();
                Window ownerWindow = new();
                try
                {
                    control.Initialize(store, ownerWindow, () => { }, () => { }, () => { });
                    control.RefreshCharacterList();
                    LoadCharacterProfileIntoDetail(control, profile);

                    DataGrid characterGrid = Assert.IsType<DataGrid>(control.FindName("Character_DataGrid"));
                    TextBox characterNameTextBox = Assert.IsType<TextBox>(control.FindName("CharacterName_TextBox"));
                    characterNameTextBox.Text = "已保存角色";

                    store.Characters.Add(new CharacterProfile
                    {
                        UserID = "8899AABBCCDDEEFF",
                        CharacterName = "外部新增"
                    });

                    Assert.False(control.TryRefreshCharacterListFromExternalChange());
                    Assert.Single(characterGrid.Items);

                    InvokeSaveCharacterButton(control);

                    Assert.Equal("已保存角色", profile.CharacterName);
                    Assert.Equal(2, characterGrid.Items.Count);
                }
                finally
                {
                    ownerWindow.Close();
                }
            }
            finally
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        });

        Assert.Null(exception);
    }

    [Fact]
    public void RestoreCharacterSelectionWithoutReload_PreservesDraftFields()
    {
        Exception? exception = WpfTestHost.Run(() =>
        {
            WpfTestHost.EnsureApplicationResources();
            string testDirectory = Path.Combine(
                Path.GetTempPath(),
                "UIMarkerEditor.CharacterProfilesTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDirectory);
            try
            {
                AppDataStore store = new(testDirectory);
                store.Initialize();
                CharacterProfile firstProfile = new()
                {
                    UserID = "0011223344556677",
                    CharacterName = "初始角色"
                };
                CharacterProfile secondProfile = new()
                {
                    UserID = "8899AABBCCDDEEFF",
                    CharacterName = "其他角色"
                };
                store.Characters.Add(firstProfile);
                store.Characters.Add(secondProfile);

                CharacterProfilesControl control = new();
                Window ownerWindow = new();
                try
                {
                    control.Initialize(store, ownerWindow, () => { }, () => { }, () => { });
                    control.RefreshCharacterList();
                    LoadCharacterProfileIntoDetail(control, firstProfile);

                    DataGrid characterGrid = Assert.IsType<DataGrid>(control.FindName("Character_DataGrid"));
                    TextBox characterNameTextBox = Assert.IsType<TextBox>(control.FindName("CharacterName_TextBox"));
                    characterNameTextBox.Text = "草稿角色";

                    SetPrivateBoolean(control, "suppressCharacterSelectionChanged", true);
                    try
                    {
                        characterGrid.SelectedItem = secondProfile;
                    }
                    finally
                    {
                        SetPrivateBoolean(control, "suppressCharacterSelectionChanged", false);
                    }

                    InvokeRestoreCharacterSelectionWithoutReload(control, firstProfile);

                    Assert.Same(firstProfile, characterGrid.SelectedItem);
                    Assert.Equal("草稿角色", characterNameTextBox.Text);
                    Assert.Equal("初始角色", firstProfile.CharacterName);
                }
                finally
                {
                    ownerWindow.Close();
                }
            }
            finally
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        });

        Assert.Null(exception);
    }

    private static void SetPrivateBoolean(object target, string fieldName, bool value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field not found: {fieldName}");
        field.SetValue(target, value);
    }

    private static void LoadCharacterProfileIntoDetail(CharacterProfilesControl control, CharacterProfile profile)
    {
        MethodInfo method = typeof(CharacterProfilesControl).GetMethod("LoadCharacterProfileIntoDetail", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Method not found: LoadCharacterProfileIntoDetail");
        method.Invoke(control, [profile]);
    }

    private static bool InvokeTrySaveCharacterProfile(
        CharacterProfilesControl control,
        bool showSuccessMessage,
        out string savedUserID)
    {
        MethodInfo method = typeof(CharacterProfilesControl).GetMethod("TrySaveCharacterProfile", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Method not found: TrySaveCharacterProfile");
        object?[] parameters = [showSuccessMessage, string.Empty];
        bool result = (bool)method.Invoke(control, parameters)!;
        savedUserID = (string)parameters[1]!;
        return result;
    }

    private static void InvokeSaveCharacterButton(CharacterProfilesControl control)
    {
        MethodInfo method = typeof(CharacterProfilesControl).GetMethod("SaveCharacter_Button_Click", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Method not found: SaveCharacter_Button_Click");
        method.Invoke(control, [control, new RoutedEventArgs()]);
    }

    private static void InvokeRestoreCharacterSelectionWithoutReload(
        CharacterProfilesControl control,
        CharacterProfile profile)
    {
        MethodInfo method = typeof(CharacterProfilesControl).GetMethod("RestoreCharacterSelectionWithoutReload", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Method not found: RestoreCharacterSelectionWithoutReload");
        method.Invoke(control, [profile]);
    }
}
