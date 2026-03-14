using System.Collections.Concurrent;

namespace Agibuild.Fulora;

/// <summary>
/// Thread-safe implementation of <see cref="IWebViewMessageBus"/> for cross-WebView communication.
/// Uses <see cref="ConcurrentDictionary{TKey,TValue}"/> for topic→subscribers and dispatches to all matching handlers.
/// </summary>
public sealed class WebViewMessageBus : IWebViewMessageBus
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<string, ConcurrentBag<Subscription>> _subscriptions = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string?>> _pendingRequests = new();
    private readonly ConcurrentDictionary<string, ResponderEntry> _responders = new();

    /// <inheritdoc />
    public void Publish(string topic, string? payloadJson = null, string? sourceWebViewId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        if (!_subscriptions.TryGetValue(topic, out var bag))
            return;

        var message = new WebViewMessage(topic, payloadJson, sourceWebViewId, DateTimeOffset.UtcNow);
        foreach (var sub in bag)
        {
            if (sub.IsDisposed)
                continue;

            if (sub.TargetWebViewId != null && sub.TargetWebViewId != sourceWebViewId)
                continue;

            try
            {
                sub.Handler(message);
            }
            catch
            {
                // Subscriber exception must not break other subscribers
            }
        }
    }

    /// <inheritdoc />
    public IDisposable Subscribe(string topic, Action<WebViewMessage> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(handler);

        return SubscribeCore(topic, null, handler);
    }

    /// <inheritdoc />
    public IDisposable Subscribe(string topic, string targetWebViewId, Action<WebViewMessage> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetWebViewId);
        ArgumentNullException.ThrowIfNull(handler);

        return SubscribeCore(topic, targetWebViewId, handler);
    }

    private DisposableSubscription SubscribeCore(string topic, string? targetWebViewId, Action<WebViewMessage> handler)
    {
        var bag = _subscriptions.GetOrAdd(topic, _ => new ConcurrentBag<Subscription>());
        var sub = new Subscription(handler, targetWebViewId);
        bag.Add(sub);
        return new DisposableSubscription(sub);
    }

    /// <inheritdoc />
    public async Task<string?> RequestAsync(string topic, string? payloadJson = null, string? targetWebViewId = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        var correlationId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[correlationId] = tcs;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DefaultRequestTimeout);

        try
        {
            var message = new WebViewMessage(topic, payloadJson, targetWebViewId, DateTimeOffset.UtcNow);

            if (_responders.TryGetValue(topic, out var entry) && !entry.IsDisposed)
            {
                try
                {
                    var result = await entry.Handler(message).ConfigureAwait(false);
                    if (_pendingRequests.TryRemove(correlationId, out var removed))
                        removed.TrySetResult(result);
                    return result;
                }
                catch (Exception ex)
                {
                    if (_pendingRequests.TryRemove(correlationId, out var removed))
                        removed.TrySetException(ex);
                    throw;
                }
            }

            using var reg = cts.Token.Register(() =>
            {
                if (_pendingRequests.TryRemove(correlationId, out var removed))
                    removed.TrySetCanceled(cts.Token);
            });

            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _pendingRequests.TryRemove(correlationId, out _);
        }
    }

    /// <inheritdoc />
    public IDisposable Respond(string topic, Func<WebViewMessage, Task<string?>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(handler);

        var entry = new ResponderEntry(handler);
        _responders[topic] = entry;
        return new DisposableResponder(_responders, topic, entry);
    }

    private sealed class Subscription
    {
        internal readonly Action<WebViewMessage> Handler;
        internal readonly string? TargetWebViewId;
        private int _disposed;

        internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        internal void MarkDisposed() => Volatile.Write(ref _disposed, 1);

        public Subscription(Action<WebViewMessage> handler, string? targetWebViewId)
        {
            Handler = handler;
            TargetWebViewId = targetWebViewId;
        }
    }

    private sealed class DisposableSubscription : IDisposable
    {
        private readonly Subscription _inner;
        private int _disposed;

        public DisposableSubscription(Subscription inner) => _inner = inner;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _inner.MarkDisposed();
        }
    }

    private sealed class ResponderEntry
    {
        internal readonly Func<WebViewMessage, Task<string?>> Handler;
        private int _disposed;

        internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        internal void MarkDisposed() => Volatile.Write(ref _disposed, 1);

        internal ResponderEntry(Func<WebViewMessage, Task<string?>> handler) => Handler = handler;
    }

    private sealed class DisposableResponder : IDisposable
    {
        private readonly ConcurrentDictionary<string, ResponderEntry> _responders;
        private readonly string _topic;
        private readonly ResponderEntry _entry;
        private int _disposed;

        public DisposableResponder(ConcurrentDictionary<string, ResponderEntry> responders, string topic, ResponderEntry entry)
        {
            _responders = responders;
            _topic = topic;
            _entry = entry;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _entry.MarkDisposed();
                if (_responders.TryGetValue(_topic, out var current) && current == _entry)
                    _responders.TryRemove(_topic, out _);
            }
        }
    }
}
