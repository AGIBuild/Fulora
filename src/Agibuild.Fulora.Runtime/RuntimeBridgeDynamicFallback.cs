using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

/// <summary>
/// Houses the dynamic/reflection-based bridge fallback so the primary runtime path can stay
/// focused on source-generated registrations and proxies.
/// </summary>
internal sealed class RuntimeBridgeDynamicFallback
    : IRuntimeBridgeStrategy
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IWebViewRpcService _rpc;
    private readonly Func<string, Task<string?>> _invokeScript;
    private readonly ILogger _logger;
    private readonly IBridgeTracer _tracer;

    internal RuntimeBridgeDynamicFallback(
        IWebViewRpcService rpc,
        Func<string, Task<string?>> invokeScript,
        ILogger logger,
        IBridgeTracer tracer)
    {
        _rpc = rpc;
        _invokeScript = invokeScript;
        _logger = logger;
        _tracer = tracer;
    }

    [RequiresDynamicCode("Reflection-based bridge exposure fallback is not supported under Native AOT. Use source-generated bridge registrations.")]
    [RequiresUnreferencedCode("Reflection-based bridge exposure fallback is not trimming-safe. Use source-generated bridge registrations.")]
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Reflection-based fallback path; source-generated path is preferred for AOT/trim scenarios.")]
    public bool TryExpose<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T implementation,
        BridgeOptions? options,
        out ExposedService exposedService) where T : class
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
            throw new NotSupportedException(
                $"Native AOT requires source-generated bridge metadata to expose service interface '{typeof(T).FullName}'.");

        var interfaceType = typeof(T);
        var serviceName = RuntimeBridgeService.GetServiceName<JsExportAttribute>(interfaceType);
        var registeredMethods = new List<string>();
        var pipeline = RuntimeBridgeService.BuildMiddlewarePipeline(options);
        var useTracing = _tracer is not NullBridgeTracer;

        try
        {
            foreach (var method in interfaceType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                var rpcMethodName = $"{serviceName}.{RuntimeBridgeService.ToCamelCase(method.Name)}";
                var handler = CreateHandler(method, implementation);
                if (pipeline is not null)
                {
                    var methodName = RuntimeBridgeService.ToCamelCase(method.Name);
                    handler = RuntimeBridgeService.WrapWithMiddleware(handler, pipeline, serviceName, methodName);
                }

                if (useTracing)
                {
                    var methodName = RuntimeBridgeService.ToCamelCase(method.Name);
                    handler = RuntimeBridgeService.WrapWithTracing(handler, _tracer, serviceName, methodName);
                }

                _rpc.Handle(rpcMethodName, handler);
                registeredMethods.Add(rpcMethodName);
            }

            var jsStub = GenerateJsStub(serviceName, interfaceType);
            _ = _invokeScript(jsStub);

            _tracer.OnServiceExposed(serviceName, registeredMethods.Count, isSourceGenerated: false);
            _logger.LogDebug("Bridge: exposed {Service} with {Count} methods (reflection)",
                serviceName, registeredMethods.Count);

            exposedService = new ExposedService(serviceName, registeredMethods, jsStub, Implementation: implementation);
            return true;
        }
        catch
        {
            foreach (var methodName in registeredMethods)
                _rpc.UnregisterHandler(methodName);

            exposedService = default!;
            throw;
        }
    }

    [RequiresDynamicCode("DispatchProxy-based bridge import fallback is not supported under Native AOT. Use source-generated bridge proxies.")]
    [RequiresUnreferencedCode("DispatchProxy-based bridge import fallback is not trimming-safe. Use source-generated bridge proxies.")]
    [UnconditionalSuppressMessage("Trimming", "IL2091",
        Justification = "DispatchProxy fallback; source-generated proxy is preferred for AOT/trim.")]
    public bool TryCreateProxy<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        out T? proxy) where T : class
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
            throw new NotSupportedException(
                $"Native AOT requires source-generated bridge metadata to create proxy for service interface '{typeof(T).FullName}'.");

        var interfaceType = typeof(T);
        var serviceName = RuntimeBridgeService.GetServiceName<JsImportAttribute>(interfaceType);

        IWebViewRpcService rpcForProxy = _rpc;
        if (_tracer is not NullBridgeTracer)
            rpcForProxy = new TracingRpcWrapper(rpcForProxy, _tracer, serviceName);

        proxy = DispatchProxy.Create<T, BridgeImportProxy>();
        var bridgeProxy = (BridgeImportProxy)(object)proxy;
        bridgeProxy.Initialize(rpcForProxy, serviceName);

        _logger.LogDebug("Bridge: created import proxy for {Service}", serviceName);
        return true;
    }

    [RequiresDynamicCode("Reflection-based bridge invocation fallback is not supported under Native AOT. Use source-generated bridge registrations.")]
    [RequiresUnreferencedCode("Reflection-based bridge invocation fallback is not trimming-safe. Use source-generated bridge registrations.")]
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

                if (result is Task task)
                {
                    await task.ConfigureAwait(false);

                    var taskType = task.GetType();
                    if (taskType.IsGenericType)
                    {
                        var resultProperty = taskType.GetProperty("Result");
                        return resultProperty?.GetValue(task);
                    }

                    return null;
                }

                return result;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                throw ex.InnerException;
            }
        };
    }

    [RequiresDynamicCode("Reflection-based parameter deserialization fallback is not supported under Native AOT. Use source-generated bridge registrations.")]
    [RequiresUnreferencedCode("Reflection-based parameter deserialization fallback is not trimming-safe. Use source-generated bridge registrations.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Reflection-based fallback; source-generated path is preferred for AOT/trim.")]
    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "Value types always have a parameterless constructor.")]
    internal static object?[] DeserializeParameters(ParameterInfo[] parameters, JsonElement? args)
    {
        if (parameters.Length == 0)
            return [];

        if (args is null || args.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            var defaults = new object?[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
                defaults[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
            return defaults;
        }

        if (args.Value.ValueKind == JsonValueKind.Object)
        {
            var result = new object?[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                var camelName = RuntimeBridgeService.ToCamelCase(parameter.Name!);

                if (args.Value.TryGetProperty(camelName, out var prop))
                {
                    result[i] = prop.Deserialize(parameter.ParameterType, JsonOptions);
                }
                else if (args.Value.TryGetProperty(parameter.Name!, out var exactProp))
                {
                    result[i] = exactProp.Deserialize(parameter.ParameterType, JsonOptions);
                }
                else if (parameter.HasDefaultValue)
                {
                    result[i] = parameter.DefaultValue;
                }
                else
                {
                    result[i] = parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null;
                }
            }

            return result;
        }

        if (args.Value.ValueKind == JsonValueKind.Array)
        {
            var result = new object?[parameters.Length];
            var i = 0;
            foreach (var element in args.Value.EnumerateArray())
            {
                if (i >= parameters.Length)
                    break;

                result[i] = element.Deserialize(parameters[i].ParameterType, JsonOptions);
                i++;
            }

            for (; i < parameters.Length; i++)
                result[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;

            return result;
        }

        if (parameters.Length == 1)
            return [args.Value.Deserialize(parameters[0].ParameterType, JsonOptions)];

        return new object?[parameters.Length];
    }

    private static string GenerateJsStub(
        string serviceName,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type interfaceType)
    {
        var methodLines = interfaceType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(method =>
            {
                var camelName = RuntimeBridgeService.ToCamelCase(method.Name);
                return $"        {camelName}: function(params) {{ return window.agWebView.rpc.invoke('{serviceName}.{camelName}', params); }}";
            });

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
}
