using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

/// <summary>
/// Runtime implementation of <see cref="IBridgeService"/> that supports both:
/// <list type="bullet">
/// <item>Source-generated registrations (<see cref="IBridgeServiceRegistration{T}"/>) for AOT compatibility</item>
/// <item>Reflection-based fallback for interfaces without a source generator</item>
/// </list>
/// The generator produces assembly-level <see cref="BridgeRegistrationAttribute"/> and
/// <see cref="BridgeProxyAttribute"/> that this service discovers automatically.
/// </summary>
internal sealed class RuntimeBridgeService : IBridgeService, IDisposable
{
    private readonly IWebViewRpcService _rpc;
    private readonly Func<string, Task<string?>> _invokeScript;
    private readonly ILogger _logger;
    private readonly IBridgeTracer _tracer;
    private readonly IReadOnlyList<IRuntimeBridgeStrategy> _strategies;

    private readonly ConcurrentDictionary<Type, ExposedService> _exportedServices = new();
    private readonly ConcurrentDictionary<Type, object> _importProxies = new();

    private volatile bool _disposed;

    internal RuntimeBridgeService(
        IWebViewRpcService rpc,
        Func<string, Task<string?>> invokeScript,
        ILogger logger,
        IEnumerable<IRuntimeBridgeStrategy> strategies)
        : this(rpc, invokeScript, logger, enableDevTools: false, tracer: null, strategies)
    {
    }

    internal RuntimeBridgeService(
        IWebViewRpcService rpc,
        Func<string, Task<string?>> invokeScript,
        ILogger logger,
        bool enableDevTools = false,
        IBridgeTracer? tracer = null,
        IEnumerable<IRuntimeBridgeStrategy>? strategies = null)
    {
        _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc));
        _invokeScript = invokeScript ?? throw new ArgumentNullException(nameof(invokeScript));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tracer = tracer ?? NullBridgeTracer.Instance;
        _strategies = (strategies ?? RuntimeBridgeStrategyDefaults.Create(_rpc, _invokeScript, _logger, _tracer)).ToArray();
    }

    // ==================== Expose (JsExport) ====================

    public void Expose<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T implementation,
        BridgeOptions? options = null) where T : class
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(implementation);

        var interfaceType = typeof(T);
        ValidateJsExportAttribute(interfaceType);

        if (!_exportedServices.TryAdd(interfaceType, default!))
            throw new InvalidOperationException(
                $"Service '{interfaceType.Name}' has already been exposed. Call Remove<{interfaceType.Name}>() first.");

        try
        {
            foreach (var strategy in _strategies)
            {
                if (!strategy.TryExpose(implementation, options, out var exposedService))
                    continue;

                _exportedServices[interfaceType] = exposedService;
                return;
            }

            throw new InvalidOperationException(
                $"No bridge strategy could expose service interface '{interfaceType.FullName}'.");
        }
        catch
        {
            _exportedServices.TryRemove(interfaceType, out _);
            throw;
        }
    }

    // ==================== GetProxy (JsImport) ====================

    public T GetProxy<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] T>() where T : class
    {
        ThrowIfDisposed();

        var interfaceType = typeof(T);
        ValidateJsImportAttribute(interfaceType);

        return (T)_importProxies.GetOrAdd(interfaceType, _ =>
        {
            foreach (var strategy in _strategies)
            {
                if (!strategy.TryCreateProxy<T>(out var proxy))
                    continue;

                return proxy!;
            }

            throw new InvalidOperationException(
                $"No bridge strategy could create a proxy for service interface '{interfaceType.FullName}'.");
        });
    }

    // ==================== Remove ====================

    public void Remove<T>() where T : class
    {
        ThrowIfDisposed();

        if (_exportedServices.TryRemove(typeof(T), out var service))
        {
            if (service.GeneratedUnregister is not null)
            {
                service.GeneratedUnregister(_rpc);
            }
            else
            {
                foreach (var method in service.RegisteredMethods)
                    _rpc.UnregisterHandler(method);
            }

            service.GeneratedDisconnectEvents?.Invoke();

            CleanupJsStub(service.ServiceName);

            DisposeImplementation(service);

            _tracer.OnServiceRemoved(service.ServiceName);
            _logger.LogDebug("Bridge: removed {Service}", service.ServiceName);
        }
    }

    private void DisposeImplementation(ExposedService service)
    {
        if (service.Implementation is IAsyncDisposable asyncDisposable)
        {
            try
            {
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Bridge: failed to async-dispose implementation for {Service}", service.ServiceName);
            }
        }
        else if (service.Implementation is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Bridge: failed to dispose implementation for {Service}", service.ServiceName);
            }
        }
    }

    private void CleanupJsStub(string serviceName)
    {
        try
        {
            _ = _invokeScript($"delete window.agWebView.bridge.{serviceName};");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Bridge: failed to clean up JS stub for {Service}", serviceName);
        }
    }

    // ==================== Disposal ====================

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var kvp in _exportedServices)
        {
            foreach (var method in kvp.Value.RegisteredMethods)
                _rpc.UnregisterHandler(method);

            DisposeImplementation(kvp.Value);
        }
        _exportedServices.Clear();
        _importProxies.Clear();

        _logger.LogDebug("Bridge: disposed");
    }

    // ==================== Private helpers ====================

    internal static string GetServiceName<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TAttr>(Type interfaceType) where TAttr : Attribute
    {
        var attr = interfaceType.GetCustomAttribute<TAttr>();
        var nameProperty = typeof(TAttr).GetProperty("Name");
        var customName = nameProperty?.GetValue(attr) as string;

        if (!string.IsNullOrEmpty(customName))
            return customName;

        var name = interfaceType.Name;
        return name.StartsWith('I') && name.Length > 1 && char.IsUpper(name[1])
            ? name[1..]
            : name;
    }

    internal static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        if (char.IsLower(name[0])) return name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static void ValidateJsExportAttribute(Type interfaceType)
    {
        if (!interfaceType.IsInterface)
            throw new InvalidOperationException(
                $"Type '{interfaceType.Name}' must be an interface to use with Bridge.Expose<T>().");

        if (interfaceType.GetCustomAttribute<JsExportAttribute>() is null)
            throw new InvalidOperationException(
                $"Interface '{interfaceType.Name}' must be decorated with [JsExport] to use with Bridge.Expose<T>().");
    }

    private static void ValidateJsImportAttribute(Type interfaceType)
    {
        if (!interfaceType.IsInterface)
            throw new InvalidOperationException(
                $"Type '{interfaceType.Name}' must be an interface to use with Bridge.GetProxy<T>().");

        if (interfaceType.GetCustomAttribute<JsImportAttribute>() is null)
            throw new InvalidOperationException(
                $"Interface '{interfaceType.Name}' must be decorated with [JsImport] to use with Bridge.GetProxy<T>().");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(RuntimeBridgeService));
    }

    // ==================== Middleware ====================

    internal static List<IBridgeMiddleware>? BuildMiddlewarePipeline(BridgeOptions? options)
    {
        var middlewares = new List<IBridgeMiddleware>();

        if (options?.RateLimit is not null)
            middlewares.Add(new RateLimitMiddleware(options.RateLimit));

        if (options?.Middleware is { Count: > 0 } custom)
            middlewares.AddRange(custom);

        return middlewares.Count > 0 ? middlewares : null;
    }

    internal static Func<JsonElement?, Task<object?>> WrapWithMiddleware(
        Func<JsonElement?, Task<object?>> handler,
        List<IBridgeMiddleware> middlewares,
        string serviceName,
        string methodName)
    {
        return args =>
        {
            var context = new BridgeCallContext
            {
                ServiceName = serviceName,
                MethodName = methodName,
                Arguments = args,
                CancellationToken = CancellationToken.None,
            };

            BridgeCallHandler terminal = ctx => handler(ctx.Arguments);

            var pipeline = terminal;
            for (int i = middlewares.Count - 1; i >= 0; i--)
            {
                var mw = middlewares[i];
                var next = pipeline;
                pipeline = ctx => mw.InvokeAsync(ctx, next);
            }

            return pipeline(context);
        };
    }

    // ==================== Reinject on navigation ====================

    /// <summary>
    /// Re-invokes the cached JS stub for every currently-exposed service.
    /// Called after page reload / navigation to restore <c>window.agWebView.bridge.*</c>.
    /// </summary>
    internal void ReinjectServiceStubs()
    {
        foreach (var kvp in _exportedServices)
        {
            var svc = kvp.Value;
            _ = _invokeScript(svc.JsStub);
            _logger.LogDebug("Bridge: re-injected JS stub for {Service}", svc.ServiceName);
        }
    }

    internal static Func<JsonElement?, Task<object?>> WrapWithTracing(
        Func<JsonElement?, Task<object?>> handler, IBridgeTracer tracer, string serviceName, string methodName)
    {
        return async args =>
        {
            var paramsJson = args?.GetRawText();
            tracer.OnExportCallStart(serviceName, methodName, paramsJson);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var result = await handler(args).ConfigureAwait(false);
                sw.Stop();
                tracer.OnExportCallEnd(serviceName, methodName, sw.ElapsedMilliseconds, result?.GetType()?.Name);
                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                tracer.OnExportCallError(serviceName, methodName, sw.ElapsedMilliseconds, ex);
                throw;
            }
        };
    }
}
