using System.IO;
using System.Reflection;
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

    private static void SetPrivateBoolean(object target, string fieldName, bool value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field not found: {fieldName}");
        field.SetValue(target, value);
    }
}
