using Agibuild.Fulora;

namespace Agibuild.Fulora.Plugin.HttpClient;

/// <summary>
/// Bridge service for host-routed HTTP requests.
/// Wraps System.Net.Http.HttpClient with base URL, timeout, and interceptor pipeline.
/// </summary>
[JsExport]
public interface IHttpClientService
{
    /// <summary>Sends an HTTP GET request to the specified URL.</summary>
    Task<HttpBridgeResponse> GetAsync(string url, Dictionary<string, string>? headers = null);
    /// <summary>Sends an HTTP POST request to the specified URL.</summary>
    Task<HttpBridgeResponse> Post(string url, string? body = null, Dictionary<string, string>? headers = null);
    /// <summary>Sends an HTTP PUT request to the specified URL.</summary>
    Task<HttpBridgeResponse> Put(string url, string? body = null, Dictionary<string, string>? headers = null);
    /// <summary>Sends an HTTP DELETE request to the specified URL.</summary>
    Task<HttpBridgeResponse> Delete(string url, Dictionary<string, string>? headers = null);
    /// <summary>Sends an HTTP PATCH request to the specified URL.</summary>
    Task<HttpBridgeResponse> Patch(string url, string? body = null, Dictionary<string, string>? headers = null);
}
