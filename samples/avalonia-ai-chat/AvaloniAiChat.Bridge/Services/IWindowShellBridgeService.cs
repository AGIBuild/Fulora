using Agibuild.Fulora;

namespace AvaloniAiChat.Bridge.Services;

[JsExport]
public interface IWindowShellBridgeService
{
    Task<WindowShellState> GetWindowShellState();
    Task<WindowShellState> UpdateWindowShellSettings(WindowShellSettings settings);
    IAsyncEnumerable<WindowShellState> StreamWindowShellState(CancellationToken cancellationToken = default);
}
