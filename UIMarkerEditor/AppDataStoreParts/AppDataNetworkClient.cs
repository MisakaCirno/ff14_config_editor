using System.Net.Http;
using System.Text;

namespace UIMarkerEditor;

internal interface IAppDataNetworkClient
{
    Task<string> GetStringAsync(
        string url,
        TimeSpan timeout,
        IReadOnlyDictionary<string, string>? headers = null);
}

internal sealed class HttpAppDataNetworkClient : IAppDataNetworkClient
{
    public async Task<string> GetStringAsync(
        string url,
        TimeSpan timeout,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        using HttpClient httpClient = new()
        {
            Timeout = timeout
        };
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        if (headers != null)
        {
            foreach (KeyValuePair<string, string> header in headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        using HttpResponseMessage response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        byte[] bytes = await response.Content.ReadAsByteArrayAsync();
        return Encoding.UTF8.GetString(bytes);
    }
}
