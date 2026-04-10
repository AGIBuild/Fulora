using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

/// <summary>
/// Encapsulates the source-generated bridge path so runtime orchestration can stay separate
/// from the dynamic/reflection fallback lane.
/// </summary>
internal sealed class RuntimeBridgeGeneratedPath
    : IRuntimeBridgeStrategy
{
    private readonly IWebViewRpcService _rpc;
    private readonly Func<string, Task<string?>> _invokeScript;
    private readonly ILogger _logger;
    private readonly IBridgeTracer _tracer;

    internal RuntimeBridgeGeneratedPath(
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

    public bool TryExpose<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T implementation,
        BridgeOptions? options,
        out ExposedService exposedService) where T : class
    {
        var generated = FindRegistration<T>();
        if (generated is null)
        {
            exposedService = default!;
            return false;
        }

        IWebViewRpcService targetRpc = _rpc;
        var pipeline = RuntimeBridgeService.BuildMiddlewarePipeline(options);
        if (pipeline is not null)
            targetRpc = new MiddlewareRpcWrapper(targetRpc, pipeline, generated.ServiceName);

        if (_tracer is not NullBridgeTracer)
            targetRpc = new TracingRpcWrapper(targetRpc, _tracer, generated.ServiceName);

        generated.RegisterHandlers(targetRpc, implementation);

        var jsStub = generated.GetJsStub();
        _ = _invokeScript(jsStub);

        _tracer.OnServiceExposed(generated.ServiceName, generated.MethodNames.Count, isSourceGenerated: true);
        _logger.LogDebug("Bridge: exposed {Service} with {Count} methods (source-generated)",
            generated.ServiceName, generated.MethodNames.Count);

        exposedService = new ExposedService(
            generated.ServiceName,
            generated.MethodNames.ToList(),
            jsStub,
            generated.UnregisterHandlers,
            () => generated.DisconnectEvents(implementation),
            implementation);
        return true;
    }

    public bool TryCreateProxy<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        out T? proxy) where T : class
    {
        var interfaceType = typeof(T);
        IWebViewRpcService rpcForProxy = _rpc;
        if (_tracer is not NullBridgeTracer)
        {
            var serviceName = RuntimeBridgeService.GetServiceName<JsImportAttribute>(interfaceType);
            rpcForProxy = new TracingRpcWrapper(rpcForProxy, _tracer, serviceName);
        }

        foreach (var assembly in CandidateAssemblies(interfaceType))
        {
            foreach (var attr in assembly.GetCustomAttributes<BridgeProxyAttribute>())
            {
                if (attr.InterfaceType == interfaceType)
                {
                    proxy = (T)Activator.CreateInstance(attr.ProxyType, rpcForProxy)!;
                    return true;
                }
            }
        }

        proxy = null;
        return false;
    }

    internal static IEnumerable<Assembly> CandidateAssemblies(Type interfaceType)
        => [interfaceType.Assembly, Assembly.GetCallingAssembly()];

    [UnconditionalSuppressMessage("Trimming", "IL2072",
        Justification = "RegistrationType is a source-generated type known to have a parameterless constructor.")]
    internal static IBridgeServiceRegistration<T>? FindRegistration<T>() where T : class
        {
            var interfaceType = typeof(T);

        foreach (var assembly in CandidateAssemblies(interfaceType))
        {
            foreach (var attr in assembly.GetCustomAttributes<BridgeRegistrationAttribute>())
            {
                if (attr.InterfaceType == interfaceType)
                    return (IBridgeServiceRegistration<T>)Activator.CreateInstance(attr.RegistrationType)!;
            }
        }

        return null;
    }

    internal static bool HasGeneratedProxy<T>() where T : class
    {
        var interfaceType = typeof(T);
        foreach (var assembly in CandidateAssemblies(interfaceType))
        {
            foreach (var attr in assembly.GetCustomAttributes<BridgeProxyAttribute>())
            {
                if (attr.InterfaceType == interfaceType)
                    return true;
            }
        }

        return false;
    }
}
