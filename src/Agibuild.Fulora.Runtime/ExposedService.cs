namespace Agibuild.Fulora;

internal sealed record ExposedService(
    string ServiceName,
    List<string> RegisteredMethods,
    string JsStub,
    Action<IWebViewRpcService>? GeneratedUnregister = null,
    Action? GeneratedDisconnectEvents = null,
    object? Implementation = null);
