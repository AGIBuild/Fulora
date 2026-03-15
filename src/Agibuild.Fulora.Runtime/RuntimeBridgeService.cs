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
    private readonly bool _enableDevTools;
    private readonly IBridgeTracer _tracer;

    private readonly ConcurrentDictionary<Type, ExposedService> _exportedServices = new();
    private readonly ConcurrentDictionary<Type, object> _importProxies = new();

    /// <summary>
    /// Shared JSON options: camelCase-insensitive for seamless JS ↔ C# mapping.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private volatile bool _disposed;

    internal RuntimeBridgeService(
        IWebViewRpcService rpc,
        Func<string, Task<string?>> invokeScript,
        ILogger logger,
        bool enableDevTools = false,
        IBridgeTracer? tracer = null)
    {
        _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc));
        _invokeScript = invokeScript ?? throw new ArgumentNullException(nameof(invokeScript));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _enableDevTools = enableDevTools;
        _tracer = tracer ?? NullBridgeTracer.Instance;
    }

    // ==================== Expose (JsExport) ====================

    public void Expose<T>(T implementation, BridgeOptions? options = null) where T : class
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
            // Try source-generated registration first (AOT safe, no reflection).
            var generated = FindGeneratedRegistration<T>();
            if (generated is not null)
            {
                IWebViewRpcService targetRpc = _rpc;
                var pipeline = BuildMiddlewarePipeline(options);
                if (pipeline is not null)
                    targetRpc = new MiddlewareRpcWrapper(targetRpc, pipeline, generated.ServiceName);

                if (_tracer is not NullBridgeTracer)
                    targetRpc = new TracingRpcWrapper(targetRpc, _tracer, generated.ServiceName);

                generated.RegisterHandlers(targetRpc, implementation);

                var jsStub = generated.GetJsStub();
                _ = _invokeScript(jsStub);
                _exportedServices[interfaceType] = new ExposedService(
                    generated.ServiceName,
                    generated.MethodNames.ToList(),
                    jsStub,
                    generated.UnregisterHandlers,
                    () => generated.DisconnectEvents(implementation),
                    implementation);

                _tracer.OnServiceExposed(generated.ServiceName, generated.MethodNames.Count, isSourceGenerated: true);
                _logger.LogDebug("Bridge: exposed {Service} with {Count} methods (source-generated)",
                    generated.ServiceName, generated.MethodNames.Count);
                return;
            }

            // Fallback: reflection-based registration.
            ExposeViaReflection(implementation, interfaceType, options);
        }
        catch
        {
            _exportedServices.TryRemove(interfaceType, out _);
            throw;
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Reflection-based fallback path; source-generated path is preferred for AOT/trim scenarios.")]
    private void ExposeViaReflection<T>(T implementation, Type interfaceType, BridgeOptions? options = null) where T : class
    {
        var serviceName = GetServiceName<JsExportAttribute>(interfaceType);
        var registeredMethods = new List<string>();
        var pipeline = BuildMiddlewarePipeline(options);
        var useTracing = _tracer is not NullBridgeTracer;

        try
        {
            foreach (var method in interfaceType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                var rpcMethodName = $"{serviceName}.{ToCamelCase(method.Name)}";
                var handler = CreateHandler(method, implementation);
                if (pipeline is not null)
                {
                    var methodName = ToCamelCase(method.Name);
                    handler = WrapWithMiddleware(handler, pipeline, serviceName, methodName);
                }
                if (useTracing)
                {
                    var methodName = ToCamelCase(method.Name);
                    handler = WrapWithTracing(handler, _tracer, serviceName, methodName);
                }
                _rpc.Handle(rpcMethodName, handler);
                registeredMethods.Add(rpcMethodName);
            }

            var jsStub = GenerateJsStub(serviceName, interfaceType);
            _ = _invokeScript(jsStub);

            _exportedServices[interfaceType] = new ExposedService(serviceName, registeredMethods, jsStub, Implementation: implementation);

            _tracer.OnServiceExposed(serviceName, registeredMethods.Count, isSourceGenerated: false);
            _logger.LogDebug("Bridge: exposed {Service} with {Count} methods (reflection)",
                serviceName, registeredMethods.Count);
        }
        catch
        {
            foreach (var m in registeredMethods)
                _rpc.UnregisterHandler(m);
            throw;
        }
    }

    // ==================== GetProxy (JsImport) ====================

    public T GetProxy<T>() where T : class
    {
        ThrowIfDisposed();

        var interfaceType = typeof(T);
        ValidateJsImportAttribute(interfaceType);

        return (T)_importProxies.GetOrAdd(interfaceType, _ =>
        {
            // Try source-generated proxy first (AOT safe, no DispatchProxy).
            var generatedProxy = FindAndCreateGeneratedProxy<T>();
            if (generatedProxy is not null)
            {
                _logger.LogDebug("Bridge: using source-generated proxy for {Service}", typeof(T).Name);
                return generatedProxy;
            }

            // Fallback: DispatchProxy-based.
            return CreateImportProxy<T>();
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

    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Task<T>.Result property is guaranteed to exist by the runtime.")]
    private static Func<JsonElement?, Task<object?>> CreateHandler(MethodInfo method, object target)
    {
        var parameters = method.GetParameters();

        return async args =>
        {
            try
            {
                var invokeArgs = DeserializeParameters(parameters, args);
                var result = method.Invoke(target, invokeArgs);

                // Handle Task and Task<T> return types.
                if (result is Task task)
                {
                    await task.ConfigureAwait(false);

                    var taskType = task.GetType();
                    if (taskType.IsGenericType)
                    {
                        // Task<T> — extract the Result.
                        var resultProperty = taskType.GetProperty("Result");
                        return resultProperty?.GetValue(task);
                    }

                    // Task (void) — no return value.
                    return null;
                }

                return result;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                // Unwrap the reflection-induced wrapper.
                throw ex.InnerException;
            }
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Reflection-based fallback; source-generated path is preferred for AOT/trim.")]
    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "Value types always have a parameterless constructor.")]
    private static object?[] DeserializeParameters(ParameterInfo[] parameters, JsonElement? args)
    {
        if (parameters.Length == 0)
            return [];

        if (args is null || args.Value.ValueKind == JsonValueKind.Null || args.Value.ValueKind == JsonValueKind.Undefined)
        {
            // All parameters must be optional or nullable for this to work.
            var defaults = new object?[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                defaults[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
            }
            return defaults;
        }

        // Named parameters (object format).
        if (args.Value.ValueKind == JsonValueKind.Object)
        {
            var result = new object?[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                var camelName = ToCamelCase(p.Name!);

                if (args.Value.TryGetProperty(camelName, out var prop))
                {
                    result[i] = prop.Deserialize(p.ParameterType, JsonOptions);
                }
                else if (args.Value.TryGetProperty(p.Name!, out var exactProp))
                {
                    // Fallback: exact name match.
                    result[i] = exactProp.Deserialize(p.ParameterType, JsonOptions);
                }
                else if (p.HasDefaultValue)
                {
                    result[i] = p.DefaultValue;
                }
                else
                {
                    result[i] = p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
                }
            }
            return result;
        }

        // Positional parameters (array format) — fallback.
        if (args.Value.ValueKind == JsonValueKind.Array)
        {
            var result = new object?[parameters.Length];
            int i = 0;
            foreach (var element in args.Value.EnumerateArray())
            {
                if (i >= parameters.Length) break;
                result[i] = element.Deserialize(parameters[i].ParameterType, JsonOptions);
                i++;
            }
            // Fill remaining with defaults.
            for (; i < parameters.Length; i++)
            {
                result[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
            }
            return result;
        }

        // Single parameter shorthand.
        if (parameters.Length == 1)
        {
            return [args.Value.Deserialize(parameters[0].ParameterType, JsonOptions)];
        }

        return new object?[parameters.Length];
    }

    [UnconditionalSuppressMessage("Trimming", "IL2091",
        Justification = "DispatchProxy fallback; source-generated proxy is preferred for AOT/trim.")]
    private T CreateImportProxy<T>() where T : class
    {
        var interfaceType = typeof(T);
        var serviceName = GetServiceName<JsImportAttribute>(interfaceType);

        IWebViewRpcService rpcForProxy = _rpc;
        if (_tracer is not NullBridgeTracer)
            rpcForProxy = new TracingRpcWrapper(rpcForProxy, _tracer, serviceName);

        var proxy = DispatchProxy.Create<T, BridgeImportProxy>();
        var bridgeProxy = (BridgeImportProxy)(object)proxy;
        bridgeProxy.Initialize(rpcForProxy, serviceName);

        _logger.LogDebug("Bridge: created import proxy for {Service}", serviceName);
        return proxy;
    }

    private static string GenerateJsStub(
        string serviceName,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type interfaceType)
    {
        var methods = interfaceType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var methodLines = new List<string>();

        foreach (var m in methods)
        {
            var camelName = ToCamelCase(m.Name);
            methodLines.Add(
                $"        {camelName}: function(params) {{ return window.agWebView.rpc.invoke('{serviceName}.{camelName}', params); }}");
        }

        return $$"""
            (function() {
                if (!window.agWebView) window.agWebView = {};
                if (!window.agWebView.bridge) window.agWebView.bridge = {};
                window.agWebView.bridge.{{serviceName}} = {
            {{string.Join(",\n", methodLines)}}
                };
            })();
            """;
    }

    private static string GetServiceName<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TAttr>(Type interfaceType) where TAttr : Attribute
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

    // ==================== Source-generated code discovery ====================

    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "RegistrationType is a source-generated type known to have a parameterless constructor.")]
    private static IBridgeServiceRegistration<T>? FindGeneratedRegistration<T>() where T : class
    {
        var interfaceType = typeof(T);

        // Scan calling assembly for [assembly: BridgeRegistration(typeof(T), typeof(Reg))]
        foreach (var assembly in new[] { interfaceType.Assembly, Assembly.GetCallingAssembly() })
        {
            foreach (var attr in assembly.GetCustomAttributes<BridgeRegistrationAttribute>())
            {
                if (attr.InterfaceType == interfaceType)
                {
                    return (IBridgeServiceRegistration<T>)Activator.CreateInstance(attr.RegistrationType)!;
                }
            }
        }

        return null;
    }

    private static T? FindGeneratedProxy<T>() where T : class
    {
        var interfaceType = typeof(T);

        foreach (var assembly in new[] { interfaceType.Assembly, Assembly.GetCallingAssembly() })
        {
            foreach (var attr in assembly.GetCustomAttributes<BridgeProxyAttribute>())
            {
                if (attr.InterfaceType == interfaceType)
                {
                    // Generated proxy has a constructor taking IWebViewRpcService.
                    // But we don't have the RPC reference here — need to pass it in.
                    return null; // Handled separately below.
                }
            }
        }

        return null;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "ProxyType is a source-generated type known to have a constructor taking IWebViewRpcService.")]
    private T? FindAndCreateGeneratedProxy<T>() where T : class
    {
        var interfaceType = typeof(T);
        IWebViewRpcService rpcForProxy = _rpc;
        if (_tracer is not NullBridgeTracer)
        {
            var serviceName = GetServiceName<JsImportAttribute>(interfaceType);
            rpcForProxy = new TracingRpcWrapper(rpcForProxy, _tracer, serviceName);
        }

        foreach (var assembly in new[] { interfaceType.Assembly, Assembly.GetCallingAssembly() })
        {
            foreach (var attr in assembly.GetCustomAttributes<BridgeProxyAttribute>())
            {
                if (attr.InterfaceType == interfaceType)
                {
                    return (T)Activator.CreateInstance(attr.ProxyType, rpcForProxy)!;
                }
            }
        }

        return null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(RuntimeBridgeService));
    }

    // ==================== Middleware ====================

    private static List<IBridgeMiddleware>? BuildMiddlewarePipeline(BridgeOptions? options)
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

/// <summary>
/// <see cref="DispatchProxy"/> implementation for <see cref="JsImportAttribute"/> interfaces.
/// Routes every method call to the RPC service.
/// </summary>
public class BridgeImportProxy : DispatchProxy
{
    private IWebViewRpcService? _rpc;
    private string _serviceName = "";

    internal void Initialize(IWebViewRpcService rpc, string serviceName)
    {
        _rpc = rpc;
        _serviceName = serviceName;
    }

    /// <inheritdoc />
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (_rpc is null || targetMethod is null)
            throw new InvalidOperationException("Bridge proxy has not been initialized.");

        var methodName = $"{_serviceName}.{RuntimeBridgeService.ToCamelCase(targetMethod.Name)}";
        var parameters = targetMethod.GetParameters();

        // Build named params object.
        Dictionary<string, object?>? namedParams = null;
        if (args is not null && args.Length > 0)
        {
            namedParams = new Dictionary<string, object?>(args.Length);
            for (int i = 0; i < args.Length && i < parameters.Length; i++)
            {
                namedParams[RuntimeBridgeService.ToCamelCase(parameters[i].Name!)] = args[i];
            }
        }

        var returnType = targetMethod.ReturnType;

        // Task (void)
        if (returnType == typeof(Task))
        {
            return _rpc.InvokeAsync(methodName, namedParams);
        }

        // Task<T>
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var invokeMethod = typeof(IWebViewRpcService)
                .GetMethod(nameof(IWebViewRpcService.InvokeAsync), 1, [typeof(string), typeof(object)])!
                .MakeGenericMethod(resultType);
            return invokeMethod.Invoke(_rpc, [methodName, namedParams]);
        }

        throw new NotSupportedException(
            $"[JsImport] method '{targetMethod.DeclaringType?.Name}.{targetMethod.Name}' must return Task or Task<T>. " +
            "Synchronous return types are not supported.");
    }
}
