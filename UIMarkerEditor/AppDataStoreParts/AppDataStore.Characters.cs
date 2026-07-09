using System;
using System.Collections.Generic;
using System.Linq;

namespace UIMarkerEditor;

public sealed partial class AppDataStore
{
    private int GetCharactersRevision()
    {
        return System.Threading.Volatile.Read(ref charactersRevision);
    }

    private void AdvanceCharactersRevision()
    {
        System.Threading.Interlocked.Increment(ref charactersRevision);
    }

    public void SaveCharacters()
    {
        if (charactersFileInvalid)
        {
            throw new InvalidOperationException("characters.json 本次启动读取失败。为避免覆盖损坏文件，请先备份、删除或修复该文件后重启工具。");
        }

        EnsureDataDirectory();
        WriteJson(
            CharactersFilePath,
            Characters
                .Select(NormalizeCharacterProfile)
                .OrderBy(c => c.UserID, StringComparer.OrdinalIgnoreCase)
                .ToList());
        AdvanceCharactersRevision();
    }

    public CharacterProfile GetOrCreateCharacter(string? userID)
    {
        string normalizedUserID = NormalizeCharacterUserID(userID);
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
            AdvanceCharactersRevision();
            return;
        }

        foreach (CharacterProfile profile in NormalizeCharacterProfiles(charactersResult.Value ?? []))
        {
            Characters.Add(profile);
        }

        AdvanceCharactersRevision();
    }

    private static List<CharacterProfile> NormalizeCharacterProfiles(IEnumerable<CharacterProfile?> profiles)
    {
        return profiles
            .Where(static profile => profile != null)
            .Select(static profile => NormalizeCharacterProfile(profile!))
            .ToList();
    }

    private static CharacterProfile NormalizeCharacterProfile(CharacterProfile profile)
    {
        return new CharacterProfile
        {
            UserID = NormalizeCharacterUserID(profile.UserID),
            CharacterName = NormalizeCharacterText(profile.CharacterName),
            DataCenter = NormalizeCharacterText(profile.DataCenter),
            World = NormalizeCharacterText(profile.World),
            Note = NormalizeCharacterText(profile.Note),
            UpdatedAt = profile.UpdatedAt == default ? DateTime.Now : profile.UpdatedAt
        };
    }

    private static string NormalizeCharacterUserID(string? userID)
    {
        string normalizedUserID = NormalizeCharacterText(userID).ToUpperInvariant();
        return string.IsNullOrWhiteSpace(normalizedUserID) ? "UNKNOWN" : normalizedUserID;
    }

    private static string NormalizeCharacterText(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }
}
