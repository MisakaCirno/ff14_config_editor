using System.Text;

namespace FF14LogParser;

public static class FF14SeString
{
    private const byte TokenStart = 0x02;
    private const byte TokenEnd = 0x03;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static string ExtractPlainText(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return ExtractPlainText(payload.AsSpan());
    }

    public static string ExtractPlainText(ReadOnlySpan<byte> payload)
        => ExtractPlainText(payload, 0);

    internal static string ExtractPlainText(ReadOnlySpan<byte> payload, long absoluteOffset)
    {
        var builder = new StringBuilder(payload.Length);
        var position = 0;
        while (position < payload.Length)
        {
            if (payload[position] == TokenStart)
            {
                SkipToken(payload, ref position, absoluteOffset);
                continue;
            }

            var textStart = position;
            while (position < payload.Length && payload[position] != TokenStart)
            {
                position++;
            }

            AppendUtf8(builder, payload[textStart..position], absoluteOffset + textStart);
        }

        return builder.ToString();
    }

    private static void SkipToken(ReadOnlySpan<byte> payload, ref int position, long absoluteOffset)
    {
        var tokenOffset = absoluteOffset + position;
        if (payload.Length - position < 3)
        {
            throw new FF14LogParseException(
                "SEString token 头部不完整。",
                tokenOffset,
                expectedLength: 3,
                remainingLength: payload.Length - position);
        }

        var cursor = position + 1;
        _ = payload[cursor++]; // tag

        var length = SeStringIntegerReader.Read(payload, cursor, absoluteOffset);
        cursor += length.BytesRead;
        if (length.Value > int.MaxValue)
        {
            throw new FF14LogParseException(
                $"SEString token 长度过大：{length.Value}。",
                absoluteOffset + cursor,
                expectedLength: null,
                remainingLength: payload.Length - cursor);
        }

        var dataLength = (int)length.Value;
        var inclusiveEnd = cursor + dataLength - 1;
        if (dataLength > 0 && inclusiveEnd < payload.Length && payload[inclusiveEnd] == TokenEnd)
        {
            position = inclusiveEnd + 1;
            return;
        }

        var exclusiveEnd = cursor + dataLength;
        if (exclusiveEnd < payload.Length && payload[exclusiveEnd] == TokenEnd)
        {
            position = exclusiveEnd + 1;
            return;
        }

        throw new FF14LogParseException(
            "SEString token 长度越界或缺少结束符 0x03。",
            tokenOffset,
            expectedLength: dataLength + 1,
            remainingLength: Math.Max(0, payload.Length - cursor));
    }

    private static void AppendUtf8(StringBuilder builder, ReadOnlySpan<byte> bytes, long absoluteOffset)
    {
        if (bytes.IsEmpty)
        {
            return;
        }

        try
        {
            builder.Append(Utf8.GetString(bytes));
        }
        catch (DecoderFallbackException ex)
        {
            throw new FF14LogParseException(
                "SEString 文本不是合法的 UTF-8。",
                absoluteOffset,
                expectedLength: null,
                remainingLength: bytes.Length,
                innerException: ex);
        }
    }
}
