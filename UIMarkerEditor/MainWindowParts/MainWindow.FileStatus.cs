using System;
using System.Linq;
using FF14ConfigEditor;
using FF14ConfigEditor.UISave;

namespace UIMarkerEditor
{
    public partial class MainWindow
    {
        private void UpdateCurrentFileStatus(string filePath)
        {
            string fullPath = System.IO.Path.GetFullPath(filePath);
            string displayText = BuildFileDisplayText(fullPath);
            CurrentFileStatus_TextBlock.Text = displayText;
            CurrentFileStatus_TextBlock.ToolTip = displayText;
        }

        private void ResetCurrentFileStatus()
        {
            const string displayText = "未加载 UISAVE 文件";
            CurrentFileStatus_TextBlock.Text = displayText;
            CurrentFileStatus_TextBlock.ToolTip = displayText;
        }

        private string BuildFileDisplayText(string filePath)
        {
            string fullPath = System.IO.Path.GetFullPath(filePath);
            string? folderUserID = AppDataStore.GetUserIDFromCharacterFolder(fullPath);
            if (string.IsNullOrWhiteSpace(folderUserID)) return fullPath;

            string characterName = BuildCharacterCompactName(folderUserID);
            return string.Equals(characterName, folderUserID, StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : $"{characterName} - {fullPath}";
        }

        private string BuildCharacterCompactName(string userID)
        {
            CharacterProfile? profile = appDataStore.Characters.FirstOrDefault(character =>
                string.Equals(character.UserID, userID, StringComparison.OrdinalIgnoreCase));
            if (profile == null || string.IsNullOrWhiteSpace(profile.CharacterName)) return userID;

            string dataCenter = profile.DataCenter.Trim();
            string world = profile.World.Trim();
            DataCenterAbbreviations.TryGetValue(dataCenter, out string? abbreviation);
            string dataCenterDisplay = abbreviation ?? dataCenter;
            string serverFirstChar = string.IsNullOrWhiteSpace(world) ? string.Empty : world[..1];
            string serverDisplay = string.Join("-", new[] { dataCenterDisplay, serverFirstChar }
                .Where(value => !string.IsNullOrWhiteSpace(value)));

            return string.IsNullOrWhiteSpace(serverDisplay)
                ? profile.CharacterName
                : $"{profile.CharacterName}（{serverDisplay}）";
        }

        private void RegisterLoadedCharacter(ConfigUISave loadedConfig, string filePath)
        {
            string userID = !string.IsNullOrWhiteSpace(loadedConfig.UserIDHex)
                ? loadedConfig.UserIDHex
                : AppDataStore.GetUserIDFromCharacterFolder(filePath) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(userID)) return;

            bool isNewProfile = !appDataStore.Characters.Any(character =>
                string.Equals(character.UserID, userID, StringComparison.OrdinalIgnoreCase));
            CharacterProfile profile = appDataStore.GetOrCreateCharacter(userID);
            string previousCharacterName = profile.CharacterName;
            DateTime previousUpdatedAt = profile.UpdatedAt;
            bool filledCharacterNameFromLog = false;
            bool savedCharacterProfile = false;
            if (string.IsNullOrWhiteSpace(profile.CharacterName))
            {
                ClientLogCharacterNameMatch? logNameMatch = ClientLogCharacterNameResolver.FindLatestFromSaveFile(
                    filePath,
                    userID);
                if (logNameMatch != null)
                {
                    profile.CharacterName = logNameMatch.CharacterName;
                    profile.UpdatedAt = DateTime.Now;
                    filledCharacterNameFromLog = true;
                }
            }

            if (isNewProfile || filledCharacterNameFromLog)
            {
                try
                {
                    appDataStore.SaveCharacters();
                    savedCharacterProfile = true;
                    if (filledCharacterNameFromLog)
                    {
                        ToastService.ShowSuccess($"已从客户端日志识别角色昵称：{profile.CharacterName}");
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or AppDataStoreException)
                {
                    if (isNewProfile)
                    {
                        appDataStore.Characters.Remove(profile);
                    }
                    else
                    {
                        profile.CharacterName = previousCharacterName;
                        profile.UpdatedAt = previousUpdatedAt;
                    }

                    AppLogger.Warning(AppLogCategory.IO, $"自动保存角色备注失败：{userID}", ex);
                }
            }

            if (savedCharacterProfile)
            {
                RefreshCharacterListFromExternalChange();
            }
        }

    }
}
