namespace FF14LogParser;

internal static class SeStringIntegerReader
{
    public static SeStringInteger Read(ReadOnlySpan<byte> bytes, int offset, long absoluteBaseOffset = 0)
    {
        EnsureAvailable(bytes, offset, 1, "SEString typed integer 缺少类型字节。", absoluteBaseOffset);

        var typeByte = bytes[offset];
        if (typeByte == 0)
        {
            throw new FF14LogParseException(
                "SEString typed integer 长度编码非法：类型字节 0 无法表示非负长度。",
                absoluteBaseOffset + offset,
                expectedLength: 1,
                remainingLength: bytes.Length - offset);
        }

        if (typeByte < 240)
        {
            return new SeStringInteger((uint)(typeByte - 1), 1);
        }

        return typeByte switch
        {
            240 => Read1(bytes, offset, absoluteBaseOffset, b1 => b1),
            241 => Read1(bytes, offset, absoluteBaseOffset, b1 => (uint)b1 * 256u),
            242 or 244 => Read2(bytes, offset, absoluteBaseOffset, (b1, b2) => ((uint)b1 << 8) | b2),
            243 => Read1(bytes, offset, absoluteBaseOffset, b1 => (uint)b1 << 16),
            245 => Read2(bytes, offset, absoluteBaseOffset, (b1, b2) => ((uint)b1 << 16) | ((uint)b2 << 8)),
            246 or 250 or 252 => Read3(bytes, offset, absoluteBaseOffset, (b1, b2, b3) => ((uint)b1 << 16) | ((uint)b2 << 8) | b3),
            247 => Read1(bytes, offset, absoluteBaseOffset, b1 => (uint)b1 << 24),
            248 => Read2(bytes, offset, absoluteBaseOffset, (b1, b2) => ((uint)b1 << 24) | b2),
            249 => Read2(bytes, offset, absoluteBaseOffset, (b1, b2) => ((uint)b1 << 24) | ((uint)b2 << 8)),
            251 => Read2(bytes, offset, absoluteBaseOffset, (b1, b2) => ((uint)b1 << 24) | ((uint)b2 << 16)),
            253 => Read3(bytes, offset, absoluteBaseOffset, (b1, b2, b3) => ((uint)b1 << 24) | ((uint)b2 << 16) | ((uint)b3 << 8)),
            254 => Read4(bytes, offset, absoluteBaseOffset, (b1, b2, b3, b4) => ((uint)b1 * 16777216u) | ((uint)b2 << 16) | ((uint)b3 << 8) | b4),
            _ => throw new FF14LogParseException(
                $"SEString typed integer 长度编码非法：未知类型字节 {typeByte}。",
                absoluteBaseOffset + offset,
                expectedLength: 1,
                remainingLength: bytes.Length - offset)
        };
    }

    private static SeStringInteger Read1(
        ReadOnlySpan<byte> bytes,
        int offset,
        long absoluteBaseOffset,
        Func<byte, uint> decode)
    {
        EnsureAvailable(bytes, offset, 2, "SEString typed integer 数据不足。", absoluteBaseOffset);

        return new SeStringInteger(decode(bytes[offset + 1]), 2);
    }

    private static SeStringInteger Read2(
        ReadOnlySpan<byte> bytes,
        int offset,
        long absoluteBaseOffset,
        Func<byte, byte, uint> decode)
    {
        EnsureAvailable(bytes, offset, 3, "SEString typed integer 数据不足。", absoluteBaseOffset);

        return new SeStringInteger(decode(bytes[offset + 1], bytes[offset + 2]), 3);
    }

    private static SeStringInteger Read3(
        ReadOnlySpan<byte> bytes,
        int offset,
        long absoluteBaseOffset,
        Func<byte, byte, byte, uint> decode)
    {
        EnsureAvailable(bytes, offset, 4, "SEString typed integer 数据不足。", absoluteBaseOffset);

        return new SeStringInteger(decode(bytes[offset + 1], bytes[offset + 2], bytes[offset + 3]), 4);
    }

    private static SeStringInteger Read4(
        ReadOnlySpan<byte> bytes,
        int offset,
        long absoluteBaseOffset,
        Func<byte, byte, byte, byte, uint> decode)
    {
        EnsureAvailable(bytes, offset, 5, "SEString typed integer 数据不足。", absoluteBaseOffset);

        return new SeStringInteger(decode(bytes[offset + 1], bytes[offset + 2], bytes[offset + 3], bytes[offset + 4]), 5);
    }

    private static void EnsureAvailable(
        ReadOnlySpan<byte> bytes,
        int offset,
        int expectedLength,
        string message,
        long absoluteBaseOffset)
    {
        if (offset < 0 || offset > bytes.Length || bytes.Length - offset < expectedLength)
        {
            throw new FF14LogParseException(
                message,
                absoluteBaseOffset + offset,
                expectedLength,
                Math.Max(0, bytes.Length - offset));
        }
    }
}
