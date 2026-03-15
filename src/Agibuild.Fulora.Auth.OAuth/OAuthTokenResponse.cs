using System.Text.Json.Serialization;

namespace Agibuild.Fulora.Auth;

/// <summary>
/// Represents the response from an OAuth token endpoint.
/// </summary>
public sealed class OAuthTokenResponse
{
    /// <summary>The access token issued by the authorization server.</summary>
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = "";

    /// <summary>The type of the token (typically "Bearer").</summary>
    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "Bearer";

    /// <summary>The lifetime in seconds of the access token.</summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    /// <summary>The refresh token, if issued.</summary>
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    /// <summary>The scope of the access token.</summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    /// <summary>The OpenID Connect ID token, if requested.</summary>
    [JsonPropertyName("id_token")]
    public string? IdToken { get; set; }
}
