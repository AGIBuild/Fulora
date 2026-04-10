using System.Reflection;

namespace Agibuild.Fulora;

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

        Dictionary<string, object?>? namedParams = null;
        if (args is not null && args.Length > 0)
        {
            namedParams = new Dictionary<string, object?>(args.Length);
            for (var i = 0; i < args.Length && i < parameters.Length; i++)
                namedParams[RuntimeBridgeService.ToCamelCase(parameters[i].Name!)] = args[i];
        }

        var returnType = targetMethod.ReturnType;
        if (returnType == typeof(Task))
            return _rpc.InvokeAsync(methodName, namedParams);

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

