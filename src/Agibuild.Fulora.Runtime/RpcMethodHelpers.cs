namespace Agibuild.Fulora;

/// <summary>
/// Shared helpers for parsing RPC method names (e.g. "ServiceName.MethodName").
/// </summary>
internal static class RpcMethodHelpers
{
    /// <summary>
    /// Splits an RPC method string into service and method parts at the last dot.
    /// </summary>
    /// <param name="rpcMethod">The full RPC method string (e.g. "ServiceName.MethodName").</param>
    /// <returns>A tuple of (serviceName, methodName). When no dot is found, returns (rpcMethod, "").</returns>
    internal static (string ServiceName, string MethodName) SplitRpcMethod(string rpcMethod)
    {
        var dot = rpcMethod.LastIndexOf('.');
        return dot >= 0 ? (rpcMethod[..dot], rpcMethod[(dot + 1)..]) : (rpcMethod, "");
    }

    /// <summary>
    /// Splits an RPC method string when no-dot input should be treated as method-only.
    /// When no dot is found, returns ("", rpcMethod) instead of (rpcMethod, "").
    /// </summary>
    internal static (string ServiceName, string MethodName) SplitRpcMethodAsMethod(string rpcMethod)
    {
        var dot = rpcMethod.LastIndexOf('.');
        return dot >= 0 ? (rpcMethod[..dot], rpcMethod[(dot + 1)..]) : ("", rpcMethod);
    }

    /// <summary>
    /// Extracts just the method name from an RPC method string (the part after the last dot).
    /// </summary>
    /// <param name="rpcMethod">The full RPC method string (e.g. "ServiceName.MethodName").</param>
    /// <returns>The method name, or the full string if no dot is found.</returns>
    internal static string ExtractMethodName(string rpcMethod)
    {
        var dot = rpcMethod.LastIndexOf('.');
        return dot >= 0 ? rpcMethod[(dot + 1)..] : rpcMethod;
    }
}
