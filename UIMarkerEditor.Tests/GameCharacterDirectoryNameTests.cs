using UIMarkerEditor;

namespace UIMarkerEditor.Tests;

public sealed class GameCharacterDirectoryNameTests
{
    [Fact]
    public void TryExtractUserID_WhenDirectoryNameIsExact_ReturnsUppercaseUserID()
    {
        bool extracted = GameCharacterDirectoryName.TryExtractUserID(
            "ffxiv_chr001122334455aabb",
            out string? userID);

        Assert.True(extracted);
        Assert.Equal("001122334455AABB", userID);
    }

    [Theory]
    [InlineData("FFXIV_CHR0011223344556677_Manual")]
    [InlineData("FFXIV_CHR001122334455667")]
    [InlineData("FFXIV_CHR00112233445566778")]
    [InlineData("FFXIV_CHR00112233445566ZZ")]
    [InlineData("ManualFiles")]
    public void TryExtractUserID_WhenDirectoryNameIsNotExact_ReturnsFalse(string directoryName)
    {
        bool extracted = GameCharacterDirectoryName.TryExtractUserID(directoryName, out string? userID);

        Assert.False(extracted);
        Assert.Null(userID);
    }
}
