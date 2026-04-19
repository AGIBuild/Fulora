using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Agibuild.Fulora.Rpc;

/// <summary>
/// Tracks server-side iterators that back the JS <c>_createAsyncIterable</c>
/// helper. Each registration:
/// <list type="bullet">
///   <item>Stores the move-next/dispose delegates keyed by the JS-supplied
///         token.</item>
///   <item>Wires a per-token <c>$/enumerator/next/{token}</c> handler into the
///         supplied <see cref="RpcHandlerRegistry"/>.</item>
///   <item>Arms an inactivity timer that disposes the enumerator if no
///         <c>next</c> arrives within <see cref="InactivityTimeout"/>.</item>
/// </list>
/// JS abort notifications come in via
/// <c>$/enumerator/abort</c> and are forwarded through
/// <see cref="DisposeAsync"/>.
/// </summary>
internal sealed class RpcEnumeratorRegistry
{
    public static readonly TimeSpan InactivityTimeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, ActiveEnumerator> _activeEnumerators = new();
    private readonly RpcHandlerRegistry _handlers;
    private readonly ILogger _logger;

    public RpcEnumeratorRegistry(RpcHandlerRegistry handlers, ILogger logger)
    {
        _handlers = handlers;
        _logger = logger;
    }

    public void Register(string token, Func<Task<(object? Value, bool Finished)>> moveNext, Func<Task> dispose)
    {
        var enumerator = new ActiveEnumerator(moveNext, dispose);
        _activeEnumerators[token] = enumerator;
        enumerator.StartInactivityTimer(InactivityTimeout, () => DisposeAsync(token));

        _handlers.RegisterNonCancellable($"$/enumerator/next/{token}", async _ =>
        {
            if (_activeEnumerators.TryGetValue(token, out var e))
            {
                e.ResetInactivityTimer(InactivityTimeout, () => DisposeAsync(token));

                var (value, finished) = await e.MoveNext();
                if (finished)
                {
                    await DisposeAsync(token);
                }
                return new EnumeratorNextResult { Values = finished ? [] : [value], Finished = finished };
            }
            return new EnumeratorNextResult { Values = [], Finished = true };
        });
    }

    public async Task DisposeAsync(string token)
    {
        if (_activeEnumerators.TryRemove(token, out var enumerator))
        {
            enumerator.Dispose();
            _handlers.UnregisterNonCancellable($"$/enumerator/next/{token}");
            try
            {
                await enumerator.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogEnumeratorDisposeFailed(ex, token);
            }
        }
    }

    private sealed class ActiveEnumerator(
        Func<Task<(object? Value, bool Finished)>> moveNext,
        Func<Task> dispose) : IDisposable
    {
        private CancellationTokenSource? _inactivityCts;

        public Func<Task<(object? Value, bool Finished)>> MoveNext { get; } = moveNext;
        public Func<Task> DisposeAsync { get; } = dispose;

        public void StartInactivityTimer(TimeSpan timeout, Func<Task> onTimeout)
        {
            _inactivityCts?.Dispose();
            _inactivityCts = new CancellationTokenSource(timeout);
            _inactivityCts.Token.Register(() => _ = onTimeout(), useSynchronizationContext: false);
        }

        public void ResetInactivityTimer(TimeSpan timeout, Func<Task> onTimeout)
        {
            StartInactivityTimer(timeout, onTimeout);
        }

        public void Dispose()
        {
            _inactivityCts?.Dispose();
            _inactivityCts = null;
        }
    }
}
