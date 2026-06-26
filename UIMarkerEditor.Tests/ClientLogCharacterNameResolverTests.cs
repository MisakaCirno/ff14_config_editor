using System.Buffers.Binary;
using System.IO;
using System.Text;
using UIMarkerEditor;

namespace UIMarkerEditor.Tests;

public sealed class ClientLogCharacterNameResolverTests : IDisposable
{
    private const string JobChangeMarker = "\u7684\u804C\u4E1A\u8F6C\u6362\u6210\u4E86\u201C";
    private const string JobChangeSuffix = "\u201D\u3002";
    private const string PlayerOne = "\u73A9\u5BB6\u4E00\u53F7";
    private const string OldName = "\u65E7\u6635\u79F0";
    private const string NewName = "\u65B0\u6635\u79F0";
    private const string OfficialName = "\u6B63\u5F0F\u89D2\u8272";
    private const string ManualDirectoryName = "\u624B\u52A8\u76EE\u5F55";
    private const string OlderChatName = "\u8F83\u65E7\u804A\u5929\u6635\u79F0";
    private const string NewerChatName = "\u6700\u65B0\u804A\u5929\u6635\u79F0";
    private const string LinkedPlayerName = "\u5916\u90E8\u73A9\u5BB6";
    private const string Knight = "\u9A91\u58EB";
    private const string Warrior = "\u6218\u58EB";
    private const string WhiteMage = "\u767D\u9B54\u6CD5\u5E08";

    private readonly string testDirectory = Path.Combine(
        Path.GetTempPath(),
        "UIMarkerEditor.Tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    [Fact]
    public void TryParseJobChangeMessage_WhenMessageMatches_ReturnsNameAndJob()
    {
        bool parsed = ClientLogCharacterNameResolver.TryParseJobChangeMessage(
            JobChangeMessage(PlayerOne, WhiteMage),
            out string? characterName,
            out string? jobName);

        Assert.True(parsed);
        Assert.Equal(PlayerOne, characterName);
        Assert.Equal(WhiteMage, jobName);
    }

    [Fact]
    public void FindLatestFromCharacterDirectory_UsesNewestKind57JobChangeMessage()
    {
        string characterDirectory = CreateCharacterDirectory("0011223344556677");
        WriteLog(
            characterDirectory,
            "00000000.log",
            Entry(100, 57, JobChangeMessage(OldName, Knight)),
            Entry(101, 1, JobChangeMessage("\u566A\u97F3", Warrior)),
            Entry(102, 57, JobChangeMessage(NewName, WhiteMage)));

        ClientLogCharacterNameMatch? match = ClientLogCharacterNameResolver.FindLatestFromCharacterDirectory(characterDirectory);

        Assert.NotNull(match);
        Assert.Equal("0011223344556677", match.UserID);
        Assert.Equal(NewName, match.CharacterName);
        Assert.Equal(WhiteMage, match.JobName);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(102), match.Timestamp);
        Assert.Equal(2, match.EntryIndex);
        Assert.Equal(ClientLogCharacterNameSource.JobChange, match.Source);
    }

    [Fact]
    public void FindLatestFromCharacterDirectory_PrefersKind57OverNewerChatSender()
    {
        string characterDirectory = CreateCharacterDirectory("0011223344556677");
        WriteLog(
            characterDirectory,
            "00000000.log",
            Entry(100, 57, JobChangeMessage(OfficialName, Warrior)),
            ChatEntry(200, 15, NewerChatName, "\u6700\u65B0\u804A\u5929\u8BB0\u5F55"));

        ClientLogCharacterNameMatch? match = ClientLogCharacterNameResolver.FindLatestFromCharacterDirectory(characterDirectory);

        Assert.NotNull(match);
        Assert.Equal(OfficialName, match.CharacterName);
        Assert.Equal(Warrior, match.JobName);
        Assert.Equal(0, match.EntryIndex);
        Assert.Equal(ClientLogCharacterNameSource.JobChange, match.Source);
    }

    [Fact]
    public void FindLatestFromCharacterDirectory_WhenNoKind57UsesLatestLocalChatSender()
    {
        string characterDirectory = CreateCharacterDirectory("0011223344556677");
        WriteLog(
            characterDirectory,
            "00000000.log",
            ChatEntry(100, 15, OlderChatName, "\u8F83\u65E7\u804A\u5929\u8BB0\u5F55"),
            ChatEntry(200, 37, "\uE072" + NewerChatName, "\u6700\u65B0\u804A\u5929\u8BB0\u5F55"));

        ClientLogCharacterNameMatch? match = ClientLogCharacterNameResolver.FindLatestFromCharacterDirectory(characterDirectory);

        Assert.NotNull(match);
        Assert.Equal(NewerChatName, match.CharacterName);
        Assert.Equal(string.Empty, match.JobName);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(200), match.Timestamp);
        Assert.Equal(1, match.EntryIndex);
        Assert.Equal(ClientLogCharacterNameSource.ChatSender, match.Source);
    }

    [Fact]
    public void FindLatestFromCharacterDirectory_IgnoresChatSenderWithPlayerLink()
    {
        string characterDirectory = CreateCharacterDirectory("0011223344556677");
        WriteLog(
            characterDirectory,
            "00000000.log",
            ChatEntry(100, 15, OlderChatName, "\u672C\u5730\u804A\u5929\u8BB0\u5F55"),
            ChatEntryWithRawSender(200, 15, BuildPlayerLinkSender(LinkedPlayerName, worldId: 1186), "\u8DE8\u670D\u73A9\u5BB6\u804A\u5929"));

        ClientLogCharacterNameMatch? match = ClientLogCharacterNameResolver.FindLatestFromCharacterDirectory(characterDirectory);

        Assert.NotNull(match);
        Assert.Equal(OlderChatName, match.CharacterName);
        Assert.Equal(0, match.EntryIndex);
        Assert.Equal(ClientLogCharacterNameSource.ChatSender, match.Source);
    }

    [Fact]
    public void FindLatestFromSaveFile_WhenDirectoryUserDoesNotMatch_ReturnsNull()
    {
        string characterDirectory = CreateCharacterDirectory("0011223344556677");
        WriteLog(
            characterDirectory,
            "00000000.log",
            Entry(100, 57, JobChangeMessage(PlayerOne, Knight)));
        string saveFilePath = Path.Combine(characterDirectory, "UISAVE.DAT");
        File.WriteAllText(saveFilePath, string.Empty);

        ClientLogCharacterNameMatch? match = ClientLogCharacterNameResolver.FindLatestFromSaveFile(
            saveFilePath,
            "8899AABBCCDDEEFF");

        Assert.Null(match);
    }

    [Fact]
    public void ScanGameCharacterRootDirectory_SkipsCharacterDirectoryWithSuffix()
    {
        string rootDirectory = Path.Combine(testDirectory, "game", "My Games", "FINAL FANTASY XIV - A Realm Reborn");
        string exactDirectory = Path.Combine(rootDirectory, "FFXIV_CHR0011223344556677");
        string suffixDirectory = Path.Combine(rootDirectory, "FFXIV_CHR8899AABBCCDDEEFF_Manual");
        Directory.CreateDirectory(exactDirectory);
        Directory.CreateDirectory(suffixDirectory);
        WriteLog(
            exactDirectory,
            "00000000.log",
            Entry(100, 57, JobChangeMessage(OfficialName, Warrior)));
        WriteLog(
            suffixDirectory,
            "00000000.log",
            Entry(101, 57, JobChangeMessage(ManualDirectoryName, Warrior)));

        IReadOnlyList<ClientLogCharacterNameMatch> matches =
            ClientLogCharacterNameResolver.ScanGameCharacterRootDirectory(rootDirectory);

        ClientLogCharacterNameMatch match = Assert.Single(matches);
        Assert.Equal("0011223344556677", match.UserID);
        Assert.Equal(OfficialName, match.CharacterName);
    }

    [Fact]
    public void FindLatestFromCharacterDirectory_WhenNoMatchingKind57AndNoLocalChatSender_ReturnsNull()
    {
        string characterDirectory = CreateCharacterDirectory("0011223344556677");
        WriteLog(
            characterDirectory,
            "00000000.log",
            Entry(100, 57, PlayerOne + "\u5207\u6362\u4E86\u804C\u4E1A\u3002"),
            Entry(101, 1, JobChangeMessage(PlayerOne, Knight)),
            ChatEntryWithRawSender(102, 15, BuildPlayerLinkSender(LinkedPlayerName, worldId: 1186), "\u8DE8\u670D\u73A9\u5BB6\u804A\u5929"));

        ClientLogCharacterNameMatch? match = ClientLogCharacterNameResolver.FindLatestFromCharacterDirectory(characterDirectory);

        Assert.Null(match);
    }

    private string CreateCharacterDirectory(string userID)
    {
        string characterDirectory = Path.Combine(testDirectory, $"FFXIV_CHR{userID}");
        Directory.CreateDirectory(characterDirectory);
        return characterDirectory;
    }

    private static void WriteLog(string characterDirectory, string fileName, params LogSource[] entries)
    {
        string logDirectory = Path.Combine(characterDirectory, "log");
        Directory.CreateDirectory(logDirectory);
        File.WriteAllBytes(Path.Combine(logDirectory, fileName), BuildLogFile(entries));
    }

    private static LogSource Entry(uint timestamp, uint kind, string body)
        => new(timestamp, kind, string.Empty, null, body);

    private static LogSource ChatEntry(uint timestamp, uint kind, string sender, string body)
        => new(timestamp, kind, sender, null, body);

    private static LogSource ChatEntryWithRawSender(uint timestamp, uint kind, byte[] senderPayload, string body)
        => new(timestamp, kind, string.Empty, senderPayload, body);

    private static string JobChangeMessage(string name, string job)
        => name + JobChangeMarker + job + JobChangeSuffix;

    private static byte[] BuildLogFile(params LogSource[] entries)
    {
        byte[][] entryBytes = entries.Select(BuildEntry).ToArray();
        int headerLength = 8 + (entries.Length * 4);
        int totalLength = headerLength + entryBytes.Sum(static entry => entry.Length);
        byte[] result = new byte[totalLength];

        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0, 4), 100);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4, 4), (uint)(100 + entries.Length));

        int cumulativeOffset = 0;
        for (int i = 0; i < entryBytes.Length; i++)
        {
            cumulativeOffset += entryBytes[i].Length;
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8 + (i * 4), 4), (uint)cumulativeOffset);
        }

        int writeOffset = headerLength;
        foreach (byte[] entry in entryBytes)
        {
            entry.CopyTo(result.AsSpan(writeOffset));
            writeOffset += entry.Length;
        }

        return result;
    }

    private static byte[] BuildEntry(LogSource source)
    {
        byte[] senderPayload = source.SenderPayload ?? Encoding.UTF8.GetBytes(source.Sender);
        byte[] bodyPayload = Encoding.UTF8.GetBytes(source.Body);
        byte[] payload = new byte[1 + senderPayload.Length + 1 + bodyPayload.Length];
        payload[0] = 0x1F;
        senderPayload.CopyTo(payload.AsSpan(1));
        payload[1 + senderPayload.Length] = 0x1F;
        bodyPayload.CopyTo(payload.AsSpan(1 + senderPayload.Length + 1));

        byte[] entry = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(0, 4), source.Timestamp);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(4, 4), source.Meta);
        payload.CopyTo(entry.AsSpan(8));
        return entry;
    }

    private static byte[] BuildPlayerLinkSender(string playerName, uint worldId)
    {
        byte[] playerNameBytes = Encoding.UTF8.GetBytes(playerName);
        byte[] data = [
            .. EncodeTypedInteger(0),
            .. EncodeTypedInteger(0),
            .. EncodeTypedInteger(worldId),
            .. EncodeTypedInteger(0),
            .. playerNameBytes
        ];
        byte[] token = BuildToken(0x27, data);
        return [.. token, .. playerNameBytes];
    }

    private static byte[] BuildToken(byte tag, byte[] data)
        => [0x02, tag, .. EncodeTypedInteger((uint)data.Length), .. data, 0x03];

    private static byte[] EncodeTypedInteger(uint value)
    {
        if (value < 239)
        {
            return [(byte)(value + 1)];
        }

        if (value <= 0xFF)
        {
            return [240, (byte)value];
        }

        if (value <= 0xFFFF)
        {
            return [242, (byte)(value >> 8), (byte)value];
        }

        if (value <= 0xFFFFFF)
        {
            return [246, (byte)(value >> 16), (byte)(value >> 8), (byte)value];
        }

        return [254, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
    }

    private sealed record LogSource(
        uint Timestamp,
        uint Meta,
        string Sender,
        byte[]? SenderPayload,
        string Body);
}
