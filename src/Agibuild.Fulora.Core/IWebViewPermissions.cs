namespace Agibuild.Fulora;

/// <summary>
/// Capability: observe runtime permission prompts (camera, microphone, geolocation, etc.).
/// </summary>
public interface IWebViewPermissions
{
    /// <summary>
    /// Raised when page content requests a platform permission. Handlers must grant
    /// or deny via <see cref="PermissionRequestedEventArgs"/> before the handler returns.
    /// </summary>
    event EventHandler<PermissionRequestedEventArgs>? PermissionRequested;
}
