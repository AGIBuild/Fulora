using Agibuild.Fulora.Adapters.Abstractions;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

/// <summary>
/// Runtime wrapper around <see cref="ICookieAdapter"/> that adds lifecycle guards and dispatcher marshaling.
/// </summary>
internal sealed class RuntimeCookieManager : ICookieManager
{
    private readonly ICookieAdapter _cookieAdapter;
    private readonly WebViewCoreContext _context;

    public RuntimeCookieManager(ICookieAdapter cookieAdapter, WebViewCoreContext context)
    {
        _cookieAdapter = cookieAdapter ?? throw new ArgumentNullException(nameof(cookieAdapter));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<IReadOnlyList<WebViewCookie>> GetCookiesAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        _context.ThrowIfDisposed();
        _context.Logger.LogDebug("CookieManager.GetCookiesAsync: {Uri}", uri);
        return _context.Operations.EnqueueAsync("Cookie.GetCookiesAsync", () => _cookieAdapter.GetCookiesAsync(uri));
    }

    public Task SetCookieAsync(WebViewCookie cookie)
    {
        ArgumentNullException.ThrowIfNull(cookie);
        _context.ThrowIfDisposed();
        _context.Logger.LogDebug("CookieManager.SetCookieAsync: {Name}@{Domain}", cookie.Name, cookie.Domain);
        return _context.Operations.EnqueueAsync("Cookie.SetCookieAsync", () => _cookieAdapter.SetCookieAsync(cookie));
    }

    public Task DeleteCookieAsync(WebViewCookie cookie)
    {
        ArgumentNullException.ThrowIfNull(cookie);
        _context.ThrowIfDisposed();
        _context.Logger.LogDebug("CookieManager.DeleteCookieAsync: {Name}@{Domain}", cookie.Name, cookie.Domain);
        return _context.Operations.EnqueueAsync("Cookie.DeleteCookieAsync", () => _cookieAdapter.DeleteCookieAsync(cookie));
    }

    public Task ClearAllCookiesAsync()
    {
        _context.ThrowIfDisposed();
        _context.Logger.LogDebug("CookieManager.ClearAllCookiesAsync");
        return _context.Operations.EnqueueAsync("Cookie.ClearAllCookiesAsync", () => _cookieAdapter.ClearAllCookiesAsync());
    }
}
