using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Agibuild.Fulora;

/// <summary>
/// Framework-level shell-window service contract. Provides snapshot, update, and stream
/// access to the global shell-window state (theme, transparency, chrome metrics).
/// </summary>
[JsExport]
public interface IWindowShellService
{
    /// <summary>Return the current shell-window state snapshot.</summary>
    Task<WindowShellState> GetWindowShellState();

    /// <summary>Apply new settings and return the resulting state.</summary>
    Task<WindowShellState> UpdateWindowShellSettings(WindowShellSettings settings);

    /// <summary>Stream state updates with signature-based deduplication.</summary>
    IAsyncEnumerable<WindowShellState> StreamWindowShellState(CancellationToken cancellationToken = default);
}
