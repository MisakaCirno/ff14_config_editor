using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using FF14LogParser;

namespace UIMarkerEditor;

internal static class ClientLogCharacterNameResolver
{
    private const int EntryHeaderLength = 8;
    private const int FileBufferSize = 4096;
    private const int MaxOffsetTableLength = 4 * 1024 * 1024;
    private const int JobChangeLogKind = 57;
    private const int PartyChatLogKind = 14;
    private const int AllianceChatLogKind = 15;
    private const int CrossWorldLinkshellChatLogKind = 37;
    private const int DefaultMaxEntriesPerCharacter = 20000;
    private const byte SeStringTokenStart = 0x02;
    private const byte SeStringTokenEnd = 0x03;
    private const byte InteractableTokenTag = 0x27;
    private const uint PlayerLinkInteractableType = 0;
    private const string JobChangeMarker = "\u7684\u804C\u4E1A\u8F6C\u6362\u6210\u4E86\u201C";
    private const string JobChangeSuffix = "\u201D\u3002";

    public static bool TryResolveGameCharacterRootDirectory(
        string? gameInstallDirectory,
        [NotNullWhen(true)]
        out string? gameCharacterRootDirectory)
    {
        return WayMarkOpenDirectoryResolver.TryResolveGameCharacterRootDirectory(
            gameInstallDirectory,
            out gameCharacterRootDirectory);
    }

    public static ClientLogCharacterNameMatch? FindLatestFromSaveFile(
        string saveFilePath,
        string userID,
        ICollection<ClientLogCharacterNameScanError>? errors = null,
        int maxEntriesPerCharacter = DefaultMaxEntriesPerCharacter)
    {
        if (string.IsNullOrWhiteSpace(saveFilePath) || !TryNormalizeUserID(userID, out string? normalizedUserID))
        {
            return null;
        }

        string? characterDirectory;
        try
        {
            characterDirectory = Path.GetDirectoryName(Path.GetFullPath(saveFilePath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            errors?.Add(new ClientLogCharacterNameScanError(string.Empty, saveFilePath, ex.Message));
            return null;
        }

        if (!GameCharacterDirectoryName.TryGetUserIDFromDirectoryPath(characterDirectory, out string? directoryUserID) ||
            !string.Equals(directoryUserID, normalizedUserID, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return FindLatestFromCharacterDirectory(characterDirectory, errors, maxEntriesPerCharacter);
    }

    public static IReadOnlyList<ClientLogCharacterNameMatch> ScanGameCharacterRootDirectory(
        string gameCharacterRootDirectory,
        ICollection<ClientLogCharacterNameScanError>? errors = null,
        int maxEntriesPerCharacter = DefaultMaxEntriesPerCharacter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameCharacterRootDirectory);

        List<ClientLogCharacterNameMatch> matches = [];
        string[] characterDirectories;
        try
        {
            characterDirectories = Directory.EnumerateDirectories(
                gameCharacterRootDirectory,
                "FFXIV_CHR*",
                SearchOption.TopDirectoryOnly).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            errors?.Add(new ClientLogCharacterNameScanError(string.Empty, gameCharacterRootDirectory, ex.Message));
            return matches;
        }

        foreach (string characterDirectory in characterDirectories.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (!GameCharacterDirectoryName.TryGetUserIDFromDirectoryPath(characterDirectory, out _))
            {
                continue;
            }

            ClientLogCharacterNameMatch? match = FindLatestFromCharacterDirectory(
                characterDirectory,
                errors,
                maxEntriesPerCharacter);
            if (match != null)
            {
                matches.Add(match);
            }
        }

        return matches;
    }

    internal static ClientLogCharacterNameMatch? FindLatestFromCharacterDirectory(
        string? characterDirectory,
        ICollection<ClientLogCharacterNameScanError>? errors = null,
        int maxEntriesPerCharacter = DefaultMaxEntriesPerCharacter)
    {
        if (!GameCharacterDirectoryName.TryGetUserIDFromDirectoryPath(characterDirectory, out string? userID))
        {
            return null;
        }

        string logDirectory = Path.Combine(characterDirectory!, "log");
        if (!Directory.Exists(logDirectory) || maxEntriesPerCharacter <= 0)
        {
            return null;
        }

        return FindLatestFromLogDirectory(logDirectory, userID, errors, maxEntriesPerCharacter);
    }

    internal static bool TryParseJobChangeMessage(
        string? message,
        out string? characterName,
        out string? jobName)
    {
        characterName = null;
        jobName = null;
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        string trimmed = message.Trim();
        int markerIndex = trimmed.IndexOf(JobChangeMarker, StringComparison.Ordinal);
        if (markerIndex <= 0 || !trimmed.EndsWith(JobChangeSuffix, StringComparison.Ordinal))
        {
            return false;
        }

        string name = trimmed[..markerIndex].Trim();
        int jobStart = markerIndex + JobChangeMarker.Length;
        string job = trimmed[jobStart..^JobChangeSuffix.Length].Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(job))
        {
            return false;
        }

        characterName = name;
        jobName = job;
        return true;
    }

    private static ClientLogCharacterNameMatch? FindLatestFromLogDirectory(
        string logDirectory,
        string userID,
        ICollection<ClientLogCharacterNameScanError>? errors,
        int maxEntriesPerCharacter)
    {
        ClientLogCharacterNameMatch? chatSenderCandidate = null;
        int scannedEntries = 0;

        foreach (string filePath in EnumerateLogFilesNewestFirst(logDirectory))
        {
            try
            {
                using FileStream stream = OpenReadOnly(filePath, FileOptions.RandomAccess);
                RawLogFileIndex index = ReadRawLogFileIndex(stream, filePath);
                for (int entryIndex = index.EntryCount - 1; entryIndex >= 0; entryIndex--)
                {
                    if (scannedEntries >= maxEntriesPerCharacter)
                    {
                        return chatSenderCandidate;
                    }

                    scannedEntries++;
                    RawEntryHeader header = ReadRawEntryHeader(stream, index, entryIndex);
                    int kind = (int)(header.Meta & 0x7Fu);
                    if (kind == JobChangeLogKind)
                    {
                        byte[] payload = ReadRawEntryPayload(stream, index, entryIndex);
                        if (!TryExtractBody(payload, out string body) ||
                            !TryParseJobChangeMessage(body, out string? characterName, out string? jobName))
                        {
                            continue;
                        }

                        return new ClientLogCharacterNameMatch(
                            userID,
                            characterName!,
                            jobName!,
                            DateTimeOffset.FromUnixTimeSeconds(header.TimestampUnixSeconds),
                            filePath,
                            entryIndex,
                            ClientLogCharacterNameSource.JobChange);
                    }

                    if (chatSenderCandidate != null || !IsChatLogKind(kind))
                    {
                        continue;
                    }

                    byte[] chatPayload = ReadRawEntryPayload(stream, index, entryIndex);
                    if (!TryExtractLocalChatSender(chatPayload, out string? sender))
                    {
                        continue;
                    }

                    chatSenderCandidate = new ClientLogCharacterNameMatch(
                        userID,
                        sender!,
                        string.Empty,
                        DateTimeOffset.FromUnixTimeSeconds(header.TimestampUnixSeconds),
                        filePath,
                        entryIndex,
                        ClientLogCharacterNameSource.ChatSender);
                }
            }
            catch (Exception ex) when (IsLogReadException(ex))
            {
                errors?.Add(new ClientLogCharacterNameScanError(userID, filePath, ex.Message));
            }
        }

        return chatSenderCandidate;
    }

    private static IEnumerable<string> EnumerateLogFilesNewestFirst(string logDirectory)
        => Directory
            .EnumerateFiles(logDirectory, "*.log", SearchOption.TopDirectoryOnly)
            .Select(static path => new FileInfo(path))
            .OrderByDescending(static file => file.LastWriteTimeUtc)
            .ThenByDescending(static file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static file => file.FullName);

    private static FileStream OpenReadOnly(string path, FileOptions options)
        => new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            FileBufferSize,
            options);

    private static RawLogFileIndex ReadRawLogFileIndex(FileStream stream, string filePath)
    {
        long fileLength = stream.Length;
        byte[] header = ReadExactly(stream, 8, 0);
        uint begin = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
        uint end = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4));
        if (end < begin)
        {
            throw new FF14LogParseException(
                $"日志文件头非法：end({end}) 小于 begin({begin})。",
                4,
                filePath: filePath);
        }

        long entryCountLong = (long)end - begin;
        if (entryCountLong > int.MaxValue)
        {
            throw new FF14LogParseException(
                $"日志条目数量过大：{entryCountLong}。",
                0,
                filePath: filePath);
        }

        int entryCount = (int)entryCountLong;
        long offsetTableLength = (long)entryCount * sizeof(uint);
        long bodyBase = 8 + offsetTableLength;
        if (offsetTableLength > MaxOffsetTableLength)
        {
            throw new FF14LogParseException(
                $"日志 offset table 过大：{offsetTableLength} 字节，超过 offset table 上限 {MaxOffsetTableLength} 字节（4 MiB）。",
                8,
                expectedLength: MaxOffsetTableLength,
                remainingLength: checked((int)Math.Min(Math.Max(0, fileLength - 8), int.MaxValue)),
                filePath: filePath);
        }

        if (bodyBase > fileLength)
        {
            throw new FF14LogParseException(
                "日志 offset table 超出文件长度。",
                8,
                expectedLength: (int)offsetTableLength,
                remainingLength: checked((int)Math.Min(Math.Max(0, fileLength - 8), int.MaxValue)),
                filePath: filePath);
        }

        byte[] offsetTable = ReadExactly(stream, (int)offsetTableLength, 8);
        uint[] offsets = new uint[entryCount + 1];
        for (int i = 0; i < entryCount; i++)
        {
            uint currentOffset = BinaryPrimitives.ReadUInt32LittleEndian(offsetTable.AsSpan(i * sizeof(uint), sizeof(uint)));
            if (currentOffset < offsets[i])
            {
                throw new FF14LogParseException(
                    $"日志 offset table 非法：第 {i} 项小于上一项。",
                    8 + (i * sizeof(uint)),
                    filePath: filePath);
            }

            if (bodyBase + currentOffset > fileLength)
            {
                throw new FF14LogParseException(
                    $"日志 offset table 非法：第 {i} 项指向文件末尾之外。",
                    8 + (i * sizeof(uint)),
                    filePath: filePath);
            }

            offsets[i + 1] = currentOffset;
        }

        return new RawLogFileIndex(entryCount, bodyBase, offsets, fileLength);
    }

    private static RawEntryHeader ReadRawEntryHeader(FileStream stream, RawLogFileIndex index, int entryIndex)
    {
        (long start, long stop) = GetEntryRange(index, entryIndex);
        long entryLength = stop - start;
        if (entryLength < EntryHeaderLength)
        {
            throw new FF14LogParseException(
                $"第 {entryIndex} 条日志长度不足 8 字节。",
                start,
                expectedLength: EntryHeaderLength,
                remainingLength: checked((int)Math.Max(0, entryLength)),
                entryIndex: entryIndex);
        }

        byte[] header = ReadExactly(stream, EntryHeaderLength, start);
        return new RawEntryHeader(
            BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4)));
    }

    private static byte[] ReadRawEntryPayload(FileStream stream, RawLogFileIndex index, int entryIndex)
    {
        (long start, long stop) = GetEntryRange(index, entryIndex);
        long payloadLengthLong = stop - start - EntryHeaderLength;
        if (payloadLengthLong < 0 || payloadLengthLong > int.MaxValue)
        {
            throw new FF14LogParseException(
                $"第 {entryIndex} 条日志 payload 长度非法：{payloadLengthLong}。",
                start + EntryHeaderLength,
                entryIndex: entryIndex);
        }

        return ReadExactly(stream, (int)payloadLengthLong, start + EntryHeaderLength);
    }

    private static (long Start, long Stop) GetEntryRange(RawLogFileIndex index, int entryIndex)
        => (
            index.BodyBase + index.Offsets[entryIndex],
            index.BodyBase + index.Offsets[entryIndex + 1]);

    private static byte[] ReadExactly(FileStream stream, int length, long offset)
    {
        byte[] buffer = new byte[length];
        stream.Seek(offset, SeekOrigin.Begin);
        stream.ReadExactly(buffer);
        return buffer;
    }

    private static bool TryExtractBody(byte[] payload, out string body)
    {
        body = string.Empty;
        if (!TrySplitPayload(payload, out _, out ReadOnlySpan<byte> bodySegment))
        {
            return false;
        }

        body = CleanPlainText(FF14SeString.ExtractPlainText(bodySegment));
        return true;
    }

    private static bool TryExtractLocalChatSender(byte[] payload, out string? sender)
    {
        sender = null;
        if (!TrySplitPayload(payload, out ReadOnlySpan<byte> senderSegment, out _) ||
            senderSegment.IsEmpty ||
            ContainsPlayerLink(senderSegment))
        {
            return false;
        }

        string candidate = CleanSenderText(FF14SeString.ExtractPlainText(senderSegment));
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        sender = candidate;
        return true;
    }

    private static bool TrySplitPayload(
        ReadOnlySpan<byte> payload,
        out ReadOnlySpan<byte> senderSegment,
        out ReadOnlySpan<byte> bodySegment)
    {
        senderSegment = ReadOnlySpan<byte>.Empty;
        bodySegment = ReadOnlySpan<byte>.Empty;
        if (payload.IsEmpty)
        {
            return false;
        }

        byte delimiter = payload[0];
        int secondDelimiter = FindDelimiterOutsideTokens(payload, delimiter, 1);
        if (secondDelimiter < 0)
        {
            bodySegment = payload;
            return true;
        }

        senderSegment = payload.Slice(1, secondDelimiter - 1);
        bodySegment = payload[(secondDelimiter + 1)..];
        return true;
    }

    private static int FindDelimiterOutsideTokens(ReadOnlySpan<byte> bytes, byte delimiter, int start)
    {
        int position = start;
        while (position < bytes.Length)
        {
            if (bytes[position] == delimiter)
            {
                return position;
            }

            if (bytes[position] == SeStringTokenStart && TryReadToken(bytes, position, out _, out _, out int nextPosition))
            {
                position = nextPosition;
                continue;
            }

            position++;
        }

        return -1;
    }

    private static bool ContainsPlayerLink(ReadOnlySpan<byte> bytes)
    {
        int position = 0;
        while (position < bytes.Length)
        {
            if (bytes[position] != SeStringTokenStart)
            {
                position++;
                continue;
            }

            if (!TryReadToken(bytes, position, out byte tag, out ReadOnlySpan<byte> data, out int nextPosition))
            {
                position++;
                continue;
            }

            if (tag == InteractableTokenTag &&
                TryReadTypedInteger(data, 0, out uint interactableType, out _) &&
                interactableType == PlayerLinkInteractableType)
            {
                return true;
            }

            position = nextPosition;
        }

        return false;
    }

    private static bool TryReadToken(
        ReadOnlySpan<byte> bytes,
        int position,
        out byte tag,
        out ReadOnlySpan<byte> data,
        out int nextPosition)
    {
        tag = 0;
        data = ReadOnlySpan<byte>.Empty;
        nextPosition = position + 1;
        if (position < 0 || position >= bytes.Length || bytes[position] != SeStringTokenStart || bytes.Length - position < 3)
        {
            return false;
        }

        int cursor = position + 1;
        tag = bytes[cursor++];
        if (!TryReadTypedInteger(bytes, cursor, out uint length, out int lengthBytes) || length > int.MaxValue)
        {
            return false;
        }

        cursor += lengthBytes;
        int dataLength = (int)length;
        long inclusiveEnd = (long)cursor + dataLength - 1;
        if (dataLength > 0 && inclusiveEnd < bytes.Length && bytes[(int)inclusiveEnd] == SeStringTokenEnd)
        {
            data = bytes.Slice(cursor, dataLength - 1);
            nextPosition = (int)inclusiveEnd + 1;
            return true;
        }

        long exclusiveEnd = (long)cursor + dataLength;
        if (exclusiveEnd < bytes.Length && bytes[(int)exclusiveEnd] == SeStringTokenEnd)
        {
            data = bytes.Slice(cursor, dataLength);
            nextPosition = (int)exclusiveEnd + 1;
            return true;
        }

        return false;
    }

    private static bool TryReadTypedInteger(
        ReadOnlySpan<byte> bytes,
        int offset,
        out uint value,
        out int bytesRead)
    {
        value = 0;
        bytesRead = 0;
        if (offset < 0 || offset >= bytes.Length)
        {
            return false;
        }

        byte typeByte = bytes[offset];
        if (typeByte == 0)
        {
            return false;
        }

        if (typeByte < 240)
        {
            value = (uint)(typeByte - 1);
            bytesRead = 1;
            return true;
        }

        return typeByte switch
        {
            240 => TryRead1(bytes, offset, out value, out bytesRead, static b1 => b1),
            241 => TryRead1(bytes, offset, out value, out bytesRead, static b1 => (uint)b1 * 256u),
            242 or 244 => TryRead2(bytes, offset, out value, out bytesRead, static (b1, b2) => ((uint)b1 << 8) | b2),
            243 => TryRead1(bytes, offset, out value, out bytesRead, static b1 => (uint)b1 << 16),
            245 => TryRead2(bytes, offset, out value, out bytesRead, static (b1, b2) => ((uint)b1 << 16) | ((uint)b2 << 8)),
            246 or 250 or 252 => TryRead3(bytes, offset, out value, out bytesRead, static (b1, b2, b3) => ((uint)b1 << 16) | ((uint)b2 << 8) | b3),
            247 => TryRead1(bytes, offset, out value, out bytesRead, static b1 => (uint)b1 << 24),
            248 => TryRead2(bytes, offset, out value, out bytesRead, static (b1, b2) => ((uint)b1 << 24) | b2),
            249 => TryRead2(bytes, offset, out value, out bytesRead, static (b1, b2) => ((uint)b1 << 24) | ((uint)b2 << 8)),
            251 => TryRead2(bytes, offset, out value, out bytesRead, static (b1, b2) => ((uint)b1 << 24) | ((uint)b2 << 16)),
            253 => TryRead3(bytes, offset, out value, out bytesRead, static (b1, b2, b3) => ((uint)b1 << 24) | ((uint)b2 << 16) | ((uint)b3 << 8)),
            254 => TryRead4(bytes, offset, out value, out bytesRead, static (b1, b2, b3, b4) => ((uint)b1 * 16777216u) | ((uint)b2 << 16) | ((uint)b3 << 8) | b4),
            _ => false
        };
    }

    private static bool TryRead1(
        ReadOnlySpan<byte> bytes,
        int offset,
        out uint value,
        out int bytesRead,
        Func<byte, uint> decode)
    {
        value = 0;
        bytesRead = 0;
        if (bytes.Length - offset < 2)
        {
            return false;
        }

        value = decode(bytes[offset + 1]);
        bytesRead = 2;
        return true;
    }

    private static bool TryRead2(
        ReadOnlySpan<byte> bytes,
        int offset,
        out uint value,
        out int bytesRead,
        Func<byte, byte, uint> decode)
    {
        value = 0;
        bytesRead = 0;
        if (bytes.Length - offset < 3)
        {
            return false;
        }

        value = decode(bytes[offset + 1], bytes[offset + 2]);
        bytesRead = 3;
        return true;
    }

    private static bool TryRead3(
        ReadOnlySpan<byte> bytes,
        int offset,
        out uint value,
        out int bytesRead,
        Func<byte, byte, byte, uint> decode)
    {
        value = 0;
        bytesRead = 0;
        if (bytes.Length - offset < 4)
        {
            return false;
        }

        value = decode(bytes[offset + 1], bytes[offset + 2], bytes[offset + 3]);
        bytesRead = 4;
        return true;
    }

    private static bool TryRead4(
        ReadOnlySpan<byte> bytes,
        int offset,
        out uint value,
        out int bytesRead,
        Func<byte, byte, byte, byte, uint> decode)
    {
        value = 0;
        bytesRead = 0;
        if (bytes.Length - offset < 5)
        {
            return false;
        }

        value = decode(bytes[offset + 1], bytes[offset + 2], bytes[offset + 3], bytes[offset + 4]);
        bytesRead = 5;
        return true;
    }

    private static string CleanPlainText(string value)
        => value
            .Replace("\u001F", string.Empty, StringComparison.Ordinal)
            .Replace("\uFFFC", string.Empty, StringComparison.Ordinal);

    private static string CleanSenderText(string value)
    {
        value = CleanPlainText(value);
        StringBuilder builder = new(value.Length);
        foreach (char c in value)
        {
            if (c is >= '\uE000' and <= '\uF8FF' || char.IsControl(c))
            {
                continue;
            }

            builder.Append(c);
        }

        return builder.ToString().Trim();
    }

    private static bool IsChatLogKind(int kind)
        => kind is PartyChatLogKind or AllianceChatLogKind or CrossWorldLinkshellChatLogKind;

    private static bool IsLogReadException(Exception ex)
        => ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException
            or FF14LogParseException
            or DecoderFallbackException;

    private static bool TryNormalizeUserID(string userID, out string? normalizedUserID)
    {
        normalizedUserID = null;
        userID = userID.Trim();
        if (userID.Length != 16 || !userID.All(Uri.IsHexDigit))
        {
            return false;
        }

        normalizedUserID = userID.ToUpperInvariant();
        return true;
    }

    private sealed record RawLogFileIndex(
        int EntryCount,
        long BodyBase,
        uint[] Offsets,
        long FileLength);

    private sealed record RawEntryHeader(uint TimestampUnixSeconds, uint Meta);
}

public enum ClientLogCharacterNameSource
{
    JobChange,
    ChatSender
}

internal sealed record ClientLogCharacterNameMatch(
    string UserID,
    string CharacterName,
    string JobName,
    DateTimeOffset Timestamp,
    string LogFilePath,
    int EntryIndex,
    ClientLogCharacterNameSource Source = ClientLogCharacterNameSource.JobChange);

internal sealed record ClientLogCharacterNameScanError(
    string UserID,
    string Path,
    string Message);
