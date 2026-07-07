using System.Text;

namespace FF14LogParser.Tests;

public sealed class FF14SeStringTests
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    [Fact]
    public void ExtractPlainText_ReturnsUtf8TextAndSkipsTokens()
    {
        var payload = Concat(
            Utf8.GetBytes("前"),
            [0x02, 0x12, 0x02, 0x03],
            Utf8.GetBytes("后"));

        var text = FF14SeString.ExtractPlainText(payload);

        Assert.Equal("前后", text);
    }

    [Fact]
    public void ExtractPlainText_RejectsInvalidUtf8()
    {
        var exception = Assert.Throws<FF14LogParseException>(() => FF14SeString.ExtractPlainText([0xFF]));

        Assert.Equal(0, exception.Offset);
    }

    [Fact]
    public void ExtractPlainText_RejectsTruncatedToken()
    {
        var exception = Assert.Throws<FF14LogParseException>(() => FF14SeString.ExtractPlainText([0x02, 0x12]));

        Assert.Equal(0, exception.Offset);
        Assert.Equal(3, exception.ExpectedLength);
        Assert.Equal(2, exception.RemainingLength);
    }

    [Fact]
    public void ExtractPlainText_RejectsTokenWithoutEndByte()
    {
        var exception = Assert.Throws<FF14LogParseException>(() => FF14SeString.ExtractPlainText([0x02, 0x12, 0x02, 0x99]));

        Assert.Equal(0, exception.Offset);
    }

    [Fact]
    public void ExtractPlainText_RejectsTokenLengthThatWouldOverflowPosition()
    {
        byte[] payload = [0x02, 0x12, 0xFE, 0x7F, 0xFF, 0xFF, 0xFF];

        var exception = Assert.Throws<FF14LogParseException>(() => FF14SeString.ExtractPlainText(payload));

        Assert.Equal(0, exception.Offset);
        Assert.Contains("长度越界", exception.Message);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(static p => p.Length)];
        var position = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result.AsSpan(position));
            position += part.Length;
        }

        return result;
    }
}
