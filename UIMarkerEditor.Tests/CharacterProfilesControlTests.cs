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
                    control.Initialize(store, ownerWindow, () => { }, () => { });
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
                try
                {
                    control.Initialize(store, ownerWindow, () => { }, () => { });
                    LoadCharacterProfileIntoDetail(control, profile);

                    TextBox characterNameTextBox = Assert.IsType<TextBox>(control.FindName("CharacterName_TextBox"));
                    characterNameTextBox.Text = "草稿角色";

                    Assert.Equal("初始角色", profile.CharacterName);

                    Assert.True(InvokeTrySaveCharacterProfile(control, showSuccessMessage: false, out string savedUserID));

                    Assert.Equal("0011223344556677", savedUserID);
                    Assert.Equal("草稿角色", profile.CharacterName);
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
                    control.Initialize(store, ownerWindow, () => { }, () => { });
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
}
