using System.Net.Http;
using System.IO;
using System.Text;

namespace UIMarkerEditor;

internal interface IAppDataNetworkClient
{
    Task<string> GetStringAsync(
        string url,
        TimeSpan timeout,
        int maxResponseBytes,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default);
}

internal sealed class HttpAppDataNetworkClient : IAppDataNetworkClient
{
    public async Task<string> GetStringAsync(
        string url,
        TimeSpan timeout,
        int maxResponseBytes,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxResponseBytes);

        using HttpClient httpClient = new()
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        if (headers != null)
        {
            foreach (KeyValuePair<string, string> header in headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            timeoutSource.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long contentLength && contentLength > maxResponseBytes)
        {
            throw new InvalidDataException($"网络响应超过允许的 {maxResponseBytes:N0} 字节。\n请求：{url}");
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(timeoutSource.Token);
        return await ReadUtf8ResponseAsync(stream, maxResponseBytes, timeoutSource.Token);
    }

    internal static async Task<string> ReadUtf8ResponseAsync(
        Stream stream,
        int maxResponseBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxResponseBytes);

        using MemoryStream buffer = new(Math.Min(maxResponseBytes, 81920));
        byte[] chunk = new byte[Math.Min(maxResponseBytes, 81920)];
        int totalBytes = 0;
        while (true)
        {
            int bytesRead = await stream.ReadAsync(chunk, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            if (totalBytes > maxResponseBytes - bytesRead)
            {
                throw new InvalidDataException($"网络响应超过允许的 {maxResponseBytes:N0} 字节。");
            }

            buffer.Write(chunk, 0, bytesRead);
            totalBytes += bytesRead;
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, totalBytes);
    }
}
