using System;
using System.Collections.Generic;
using System.Linq;

namespace UIMarkerEditor;

public sealed partial class AppDataStore
{
    public void SaveCharacters()
    {
        if (charactersFileInvalid)
        {
            throw new InvalidOperationException("characters.json 本次启动读取失败。为避免覆盖损坏文件，请先备份、删除或修复该文件后重启工具。");
        }

        EnsureDataDirectory();
        WriteJson(CharactersFilePath, Characters.OrderBy(c => c.UserID, StringComparer.OrdinalIgnoreCase).ToList());
    }

    public CharacterProfile GetOrCreateCharacter(string? userID)
    {
        string normalizedUserID = string.IsNullOrWhiteSpace(userID) ? "UNKNOWN" : userID.ToUpperInvariant();
        CharacterProfile? existing = Characters.FirstOrDefault(c =>
            string.Equals(c.UserID, normalizedUserID, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;

        CharacterProfile profile = new()
        {
            UserID = normalizedUserID,
            UpdatedAt = DateTime.Now
        };
        Characters.Add(profile);

        return profile;
    }

    public string GetCharacterDisplayName(string? userID)
    {
        if (string.IsNullOrWhiteSpace(userID)) return "未知角色";

        CharacterProfile? profile = Characters.FirstOrDefault(c =>
            string.Equals(c.UserID, userID, StringComparison.OrdinalIgnoreCase));
        return profile?.DisplayName ?? userID;
    }

    private void LoadCharacters()
    {
        Characters.Clear();
        charactersFileInvalid = false;
        JsonFileReadResult<List<CharacterProfile>> charactersResult = ReadJsonFile<List<CharacterProfile>>(CharactersFilePath);
        if (charactersResult.Status == JsonFileReadStatus.Invalid)
        {
            charactersFileInvalid = true;
            AddJsonReadWarning(
                CharactersFilePath,
                "角色备注无法读取，列表已留空。为避免覆盖损坏文件，本次运行会阻止保存角色备注。",
                charactersResult.Error);
            return;
        }

        foreach (CharacterProfile? profile in charactersResult.Value ?? [])
        {
            if (profile != null)
            {
                Characters.Add(profile);
            }
        }
    }
}
