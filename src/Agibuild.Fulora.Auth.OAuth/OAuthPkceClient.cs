using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agibuild.Fulora.Auth;

/// <summary>
/// OAuth 2.0 PKCE client for desktop/mobile hybrid apps.
/// Handles authorization URL building, token exchange, and token refresh.
/// The app is responsible for presenting the authorization URL and capturing the redirect.
/// </summary>
public sealed class OAuthPkceClient
{
    private readonly HttpClient _httpClient;
    private readonly OAuthPkceOptions _options;

    /// <summary>
    /// Initializes a new <see cref="OAuthPkceClient"/> with the specified HTTP client and options.
    /// </summary>
    public OAuthPkceClient(HttpClient httpClient, OAuthPkceOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _httpClient = httpClient;
        _options = options;
    }

    /// <summary>
    /// Builds the authorization URL for the PKCE flow.
    /// The app should navigate to this URL (system browser or embedded WebView).
    /// </summary>
    public string BuildAuthorizationUrl(string codeChallenge, string? state = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codeChallenge);

        var endpoint = _options.GetAuthorizationEndpoint();
        var scope = string.Join(" ", _options.Scopes);

        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = _options.RedirectUri,
            ["scope"] = scope,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        };

        if (!string.IsNullOrEmpty(state))
            query["state"] = state;

        var queryString = string.Join("&", query.Select(kvp =>
            $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        return $"{endpoint}?{queryString}";
    }

    /// <summary>
    /// Exchanges an authorization code for tokens using the PKCE flow.
    /// </summary>
    public async Task<OAuthTokenResponse> ExchangeCodeAsync(
        string code, string codeVerifier, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeVerifier);

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _options.RedirectUri,
            ["client_id"] = _options.ClientId,
            ["code_verifier"] = codeVerifier,
        };

        return await PostTokenRequestAsync(form, ct);
    }

    /// <summary>
    /// Refreshes an access token using a refresh token.
    /// </summary>
    public async Task<OAuthTokenResponse> RefreshTokenAsync(
        string refreshToken, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = _options.ClientId,
        };

        if (_options.Scopes.Length > 0)
            form["scope"] = string.Join(" ", _options.Scopes);

        return await PostTokenRequestAsync(form, ct);
    }

    private async Task<OAuthTokenResponse> PostTokenRequestAsync(
        Dictionary<string, string> form, CancellationToken ct)
    {
        var tokenEndpoint = _options.GetTokenEndpoint();
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(tokenEndpoint, content, ct);

        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            try
            {
                var error = JsonSerializer.Deserialize<OAuthErrorResponse>(json);
                throw new OAuthException(error?.Error, error?.ErrorDescription);
            }
            catch (JsonException)
            {
                throw new OAuthException($"Token endpoint returned {(int)response.StatusCode}: {json}");
            }
        }

        var tokenResponse = JsonSerializer.Deserialize<OAuthTokenResponse>(json);
        return tokenResponse ?? throw new OAuthException("Empty response from token endpoint.");
    }

    private sealed class OAuthErrorResponse
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; set; }
    }
}
