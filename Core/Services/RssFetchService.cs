using System.Net;

namespace RSSMagnetCatcher.Core.Services;

public sealed record RssFetchResult(int HttpStatusCode, string Content);

public sealed class RssFetchException : Exception
{
    public RssFetchException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

public sealed class RssFetchService
{
    private readonly HttpClient _httpClient;

    public RssFetchService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<RssFetchResult> FetchAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new RssFetchException(
                response.StatusCode,
                $"RSS 请求失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        return new RssFetchResult((int)response.StatusCode, content);
    }
}
