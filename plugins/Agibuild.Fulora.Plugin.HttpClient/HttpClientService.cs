using System.Net.Http;
using System.Text;

namespace Agibuild.Fulora.Plugin.HttpClient;

/// <summary>
/// Implementation of <see cref="IHttpClientService"/> wrapping System.Net.Http.HttpClient.
/// Resolves relative URLs against BaseUrl, applies default and per-request headers,
/// runs the interceptor pipeline, and returns <see cref="HttpBridgeResponse"/>.
/// </summary>
public sealed class HttpClientService : IHttpClientService
{
    private readonly global::System.Net.Http.HttpClient _httpClient;
    private readonly HttpClientOptions _options;

    /// <summary>
    /// Creates a new HTTP client service with the given options and handler.
    /// </summary>
    /// <param name="options">Configuration options. When null, uses defaults.</param>
    /// <param name="handler">Optional message handler for testing (e.g., mock). When null, uses default.</param>
    public HttpClientService(HttpClientOptions? options = null, HttpMessageHandler? handler = null)
    {
        _options = options ?? new HttpClientOptions();
        _httpClient = handler != null
            ? new global::System.Net.Http.HttpClient(handler, disposeHandler: false)
            : new global::System.Net.Http.HttpClient();

        _httpClient.Timeout = _options.Timeout;

        if (!string.IsNullOrEmpty(_options.BaseUrl))
        {
            var baseUrl = _options.BaseUrl;
            if (!baseUrl.EndsWith('/'))
                baseUrl += "/";
            _httpClient.BaseAddress = new Uri(baseUrl);
        }
    }

    private static Uri ResolveUrl(string? baseUrl, string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute)
            && absolute.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return absolute;

        return new Uri(url.TrimStart('/'), UriKind.Relative);
    }

    private Dictionary<string, string> MergeHeaders(Dictionary<string, string>? perRequest)
    {
        var merged = new Dictionary<string, string>(_options.DefaultHeaders, StringComparer.OrdinalIgnoreCase);
        if (perRequest != null)
        {
            foreach (var (k, v) in perRequest)
                merged[k] = v;
        }
        return merged;
    }

    private async Task<HttpRequestMessage> RunInterceptorsAsync(HttpRequestMessage request)
    {
        foreach (var interceptor in _options.Interceptors)
        {
            request = await interceptor.InterceptAsync(request).ConfigureAwait(false);
        }
        return request;
    }

    private async Task<HttpBridgeResponse> SendAsync(HttpRequestMessage request, Dictionary<string, string>? headers)
    {
        foreach (var (key, value) in MergeHeaders(headers))
        {
            request.Headers.TryAddWithoutValidation(key, value);
        }

        request = await RunInterceptorsAsync(request).ConfigureAwait(false);

        using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        var responseHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, values) in response.Headers)
        {
            foreach (var v in values)
                responseHeaders[key] = v;
        }
        foreach (var (key, values) in response.Content.Headers)
        {
            foreach (var v in values)
                responseHeaders[key] = v;
        }

        return new HttpBridgeResponse
        {
            StatusCode = (int)response.StatusCode,
            Body = body,
            Headers = responseHeaders,
            IsSuccess = response.IsSuccessStatusCode
        };
    }

    /// <summary>Sends an HTTP GET request to the specified URL.</summary>
    public Task<HttpBridgeResponse> GetAsync(string url, Dictionary<string, string>? headers = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, ResolveUrl(_options.BaseUrl, url));
        return SendAsync(request, headers);
    }

    /// <summary>Sends an HTTP POST request to the specified URL.</summary>
    public Task<HttpBridgeResponse> Post(string url, string? body = null, Dictionary<string, string>? headers = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ResolveUrl(_options.BaseUrl, url));
        if (!string.IsNullOrEmpty(body))
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return SendAsync(request, headers);
    }

    /// <summary>Sends an HTTP PUT request to the specified URL.</summary>
    public Task<HttpBridgeResponse> Put(string url, string? body = null, Dictionary<string, string>? headers = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, ResolveUrl(_options.BaseUrl, url));
        if (!string.IsNullOrEmpty(body))
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return SendAsync(request, headers);
    }

    /// <summary>Sends an HTTP DELETE request to the specified URL.</summary>
    public Task<HttpBridgeResponse> Delete(string url, Dictionary<string, string>? headers = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, ResolveUrl(_options.BaseUrl, url));
        return SendAsync(request, headers);
    }

    /// <summary>Sends an HTTP PATCH request to the specified URL.</summary>
    public Task<HttpBridgeResponse> Patch(string url, string? body = null, Dictionary<string, string>? headers = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, ResolveUrl(_options.BaseUrl, url));
        if (!string.IsNullOrEmpty(body))
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return SendAsync(request, headers);
    }
}
