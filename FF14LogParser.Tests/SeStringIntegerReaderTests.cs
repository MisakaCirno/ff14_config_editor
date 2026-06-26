namespace FF14LogParser.Tests;

public sealed class SeStringIntegerReaderTests
{
    public static TheoryData<byte[], uint, int> EncodedValues => new()
    {
        { [0x01], 0u, 1 },
        { [0xEF], 238u, 1 },
        { [0xF0, 0x12], 0x12u, 2 },
        { [0xF1, 0x12], 0x1200u, 2 },
        { [0xF2, 0x12, 0x34], 0x1234u, 3 },
        { [0xF3, 0x12], 0x120000u, 2 },
        { [0xF4, 0x12, 0x34], 0x1234u, 3 },
        { [0xF5, 0x12, 0x34], 0x123400u, 3 },
        { [0xF6, 0x12, 0x34, 0x56], 0x123456u, 4 },
        { [0xF7, 0x12], 0x12000000u, 2 },
        { [0xF8, 0x12, 0x34], 0x12000034u, 3 },
        { [0xF9, 0x12, 0x34], 0x12003400u, 3 },
        { [0xFA, 0x12, 0x34, 0x56], 0x123456u, 4 },
        { [0xFB, 0x12, 0x34], 0x12340000u, 3 },
        { [0xFC, 0x12, 0x34, 0x56], 0x123456u, 4 },
        { [0xFD, 0x12, 0x34, 0x56], 0x12345600u, 4 },
        { [0xFE, 0x12, 0x34, 0x56, 0x78], 0x12345678u, 5 },
    };

    [Theory]
    [MemberData(nameof(EncodedValues))]
    public void Read_DecodesTypedInteger(byte[] bytes, uint expectedValue, int expectedBytesRead)
    {
        var value = SeStringIntegerReader.Read(bytes, 0, 100);

        Assert.Equal(expectedValue, value.Value);
        Assert.Equal(expectedBytesRead, value.BytesRead);
    }

    [Fact]
    public void Read_RejectsZeroTypeByte()
    {
        var exception = Assert.Throws<FF14LogParseException>(() => SeStringIntegerReader.Read([0x00], 0, 100));

        Assert.Equal(100, exception.Offset);
    }

    [Fact]
    public void Read_RejectsUnknownTypeByte()
    {
        var exception = Assert.Throws<FF14LogParseException>(() => SeStringIntegerReader.Read([0xFF], 0, 100));

        Assert.Equal(100, exception.Offset);
    }

    [Fact]
    public void Read_RejectsTruncatedMultiByteValue()
    {
        var exception = Assert.Throws<FF14LogParseException>(() => SeStringIntegerReader.Read([0xFE, 0x12], 0, 100));

        Assert.Equal(100, exception.Offset);
        Assert.Equal(5, exception.ExpectedLength);
        Assert.Equal(2, exception.RemainingLength);
    }
}
