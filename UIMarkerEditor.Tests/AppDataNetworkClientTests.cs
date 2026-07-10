using System.Text;
using System.IO;
namespace UIMarkerEditor.Tests;

public sealed class AppDataNetworkClientTests
{
    [Fact]
    public async Task ReadUtf8ResponseAsync_WhenContentFitsLimit_ReturnsText()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("测试 response");
        using MemoryStream stream = new(bytes);

        string result = await HttpAppDataNetworkClient.ReadUtf8ResponseAsync(stream, bytes.Length);

        Assert.Equal("测试 response", result);
    }

    [Fact]
    public async Task ReadUtf8ResponseAsync_WhenContentExceedsLimit_Throws()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("123456789");
        using MemoryStream stream = new(bytes);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            HttpAppDataNetworkClient.ReadUtf8ResponseAsync(stream, bytes.Length - 1));

        Assert.Contains("超过允许", exception.Message);
    }
}
