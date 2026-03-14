using System.Collections.Concurrent;
using System.Text.Json;

namespace Agibuild.Fulora;

/// <summary>
/// A mock implementation of <see cref="IBridgeService"/> for unit testing.
/// Allows testing ViewModels and services that depend on Bridge without a real WebView.
/// <para>
/// Deliverable 1.5: MockBridge for consumer unit testing.
/// </para>
/// </summary>
/// <example>
/// <code>
/// var mock = new MockBridgeService();
/// mock.SetupExpose&lt;IAppService&gt;(); // Records that Expose was called
/// var proxy = mock.SetupProxy&lt;IUiController&gt;(new FakeUiController());
///
/// var vm = new MyViewModel(mock);
/// await vm.Initialize();
///
/// Assert.True(mock.WasExposed&lt;IAppService&gt;());
/// </code>
/// </example>
public sealed class MockBridgeService : IBridgeService
{
    private readonly ConcurrentDictionary<Type, object> _exposedServices = new();
    private readonly ConcurrentDictionary<Type, object> _proxies = new();
    private bool _disposed;

    /// <summary>
    /// Registers a service as if <see cref="Expose{T}"/> was called.
    /// The implementation is recorded for later assertions.
    /// </summary>
    public void Expose<T>(T implementation, BridgeOptions? options = null) where T : class
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(implementation);
        _exposedServices[typeof(T)] = implementation;
    }

    /// <summary>
    /// Returns a proxy for the given interface. Must have been set up via <see cref="SetupProxy{T}"/> first.
    /// </summary>
    public T GetProxy<T>() where T : class
    {
        ThrowIfDisposed();
        if (_proxies.TryGetValue(typeof(T), out var proxy))
            return (T)proxy;
        throw new InvalidOperationException(
            $"No mock proxy configured for {typeof(T).Name}. Call MockBridgeService.SetupProxy<{typeof(T).Name}>() first.");
    }

    /// <summary>
    /// Removes an exposed service.
    /// </summary>
    public void Remove<T>() where T : class
    {
        ThrowIfDisposed();
        _exposedServices.TryRemove(typeof(T), out _);
    }

    // ==================== Setup helpers ====================

    /// <summary>
    /// Configures a mock proxy that will be returned by <see cref="GetProxy{T}"/>.
    /// </summary>
    public void SetupProxy<T>(T proxy) where T : class
    {
        ArgumentNullException.ThrowIfNull(proxy);
        _proxies[typeof(T)] = proxy;
    }

    // ==================== Assertion helpers ====================

    /// <summary>Returns whether <see cref="Expose{T}"/> was called for the given interface.</summary>
    public bool WasExposed<T>() where T : class => _exposedServices.ContainsKey(typeof(T));

    /// <summary>Returns the implementation passed to <see cref="Expose{T}"/>, or null.</summary>
    public T? GetExposedImplementation<T>() where T : class
        => _exposedServices.TryGetValue(typeof(T), out var impl) ? (T)impl : null;

    /// <summary>Returns the number of services exposed.</summary>
    public int ExposedCount => _exposedServices.Count;

    /// <summary>Resets all state.</summary>
    public void Reset()
    {
        _exposedServices.Clear();
        _proxies.Clear();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(MockBridgeService));
    }

    /// <summary>Marks the mock as disposed. Subsequent operations throw.</summary>
    public void Dispose()
    {
        _disposed = true;
        _exposedServices.Clear();
        _proxies.Clear();
    }
}
