using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora;

internal static class RuntimeBridgeStrategyDefaults
{
    internal static IReadOnlyList<IRuntimeBridgeStrategy> Create(
        IWebViewRpcService rpc,
        Func<string, Task<string?>> invokeScript,
        ILogger logger,
        IBridgeTracer tracer)
    {
        return
        [
            new RuntimeBridgeGeneratedPath(rpc, invokeScript, logger, tracer),
            new RuntimeBridgeDynamicFallback(rpc, invokeScript, logger, tracer)
        ];
    }
}
