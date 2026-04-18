namespace Agibuild.Fulora;

/// <summary>
/// Capability: register and remove JavaScript snippets that run at document start
/// on every navigation.
/// </summary>
public interface IWebViewPreloadScripts
{
    /// <summary>
    /// Registers a preload script. The returned opaque ID can be passed to
    /// <c>RemovePreloadScriptAsync</c> to unregister it.
    /// </summary>
    Task<string> AddPreloadScriptAsync(string javaScript);

    /// <summary>Removes a previously registered preload script by ID.</summary>
    Task RemovePreloadScriptAsync(string scriptId);
}
