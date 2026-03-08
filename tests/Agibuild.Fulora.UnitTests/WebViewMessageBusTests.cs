using System.Collections.Concurrent;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public class WebViewMessageBusTests
{
    [Fact]
    public void Publish_with_no_subscribers_does_not_throw()
    {
        var bus = new WebViewMessageBus();
        bus.Publish("topic");
        bus.Publish("topic", "payload");
        bus.Publish("topic", "payload", "wv1");
    }

    [Fact]
    public void Subscribe_receives_published_messages()
    {
        var bus = new WebViewMessageBus();
        var received = new List<WebViewMessage>();
        using var sub = bus.Subscribe("events", m => received.Add(m));

        bus.Publish("events", """{"x":1}""", "wv1");

        Assert.Single(received);
        Assert.Equal("events", received[0].Topic);
        Assert.Equal("""{"x":1}""", received[0].PayloadJson);
        Assert.Equal("wv1", received[0].SourceWebViewId);
    }

    [Fact]
    public void Subscribe_with_targetWebViewId_filter_only_receives_matching_messages()
    {
        var bus = new WebViewMessageBus();
        var received = new List<WebViewMessage>();
        using var sub = bus.Subscribe("events", "wv1", m => received.Add(m));

        bus.Publish("events", "a", "wv1");
        bus.Publish("events", "b", "wv2");
        bus.Publish("events", "c", "wv1");

        Assert.Equal(2, received.Count);
        Assert.Equal("a", received[0].PayloadJson);
        Assert.Equal("c", received[1].PayloadJson);
    }

    [Fact]
    public void Subscribe_with_targetWebViewId_does_not_receive_when_source_is_null()
    {
        var bus = new WebViewMessageBus();
        var received = new List<WebViewMessage>();
        using var sub = bus.Subscribe("events", "wv1", m => received.Add(m));

        bus.Publish("events", "payload", sourceWebViewId: null);

        Assert.Empty(received);
    }

    [Fact]
    public void Dispose_subscription_stops_callbacks()
    {
        var bus = new WebViewMessageBus();
        var received = new List<WebViewMessage>();
        var sub = bus.Subscribe("events", m => received.Add(m));
        sub.Dispose();

        bus.Publish("events", "payload");

        Assert.Empty(received);
    }

    [Fact]
    public void Multiple_topics_are_independent()
    {
        var bus = new WebViewMessageBus();
        var topicA = new List<WebViewMessage>();
        var topicB = new List<WebViewMessage>();
        using var subA = bus.Subscribe("A", m => topicA.Add(m));
        using var subB = bus.Subscribe("B", m => topicB.Add(m));

        bus.Publish("A", "a");
        bus.Publish("B", "b");
        bus.Publish("A", "a2");

        Assert.Equal(2, topicA.Count);
        Assert.Equal("a", topicA[0].PayloadJson);
        Assert.Equal("a2", topicA[1].PayloadJson);
        Assert.Single(topicB);
        Assert.Equal("b", topicB[0].PayloadJson);
    }

    [Fact]
    public void Subscriber_exception_does_not_break_other_subscribers()
    {
        var bus = new WebViewMessageBus();
        var received = new List<WebViewMessage>();
        using var bad = bus.Subscribe("events", _ => throw new InvalidOperationException("boom"));
        using var good = bus.Subscribe("events", m => received.Add(m));

        bus.Publish("events", "payload");

        Assert.Single(received);
        Assert.Equal("payload", received[0].PayloadJson);
    }

    [Fact]
    public void Thread_safety_concurrent_publish_and_subscribe()
    {
        var bus = new WebViewMessageBus();
        var received = new ConcurrentBag<WebViewMessage>();

        using var sub = bus.Subscribe("shared-topic", m => received.Add(m));

        var tasks = Enumerable.Range(0, 50).Select(i => Task.Run(() =>
        {
            bus.Publish("shared-topic", $"payload{i}", $"wv{i}");
        })).ToArray();

        Task.WaitAll(tasks, TestContext.Current.CancellationToken);

        Assert.Equal(50, received.Count);
    }

    [Fact]
    public void Publish_throws_on_null_or_whitespace_topic()
    {
        var bus = new WebViewMessageBus();
        Assert.ThrowsAny<ArgumentException>(() => bus.Publish(null!));
        Assert.ThrowsAny<ArgumentException>(() => bus.Publish(""));
        Assert.ThrowsAny<ArgumentException>(() => bus.Publish("   "));
    }

    [Fact]
    public void Subscribe_throws_on_null_or_whitespace_topic()
    {
        var bus = new WebViewMessageBus();
        Assert.ThrowsAny<ArgumentException>(() => bus.Subscribe(null!, _ => { }));
        Assert.ThrowsAny<ArgumentException>(() => bus.Subscribe("", _ => { }));
        Assert.ThrowsAny<ArgumentException>(() => bus.Subscribe("   ", _ => { }));
    }

    [Fact]
    public void Subscribe_throws_on_null_handler()
    {
        var bus = new WebViewMessageBus();
        Assert.Throws<ArgumentNullException>(() => bus.Subscribe("topic", null!));
    }

    [Fact]
    public void Subscribe_with_target_throws_on_null_or_whitespace_targetWebViewId()
    {
        var bus = new WebViewMessageBus();
        Assert.ThrowsAny<ArgumentException>(() => bus.Subscribe("topic", null!, _ => { }));
        Assert.ThrowsAny<ArgumentException>(() => bus.Subscribe("topic", "", _ => { }));
        Assert.ThrowsAny<ArgumentException>(() => bus.Subscribe("topic", "   ", _ => { }));
    }

    [Fact]
    public async Task Request_gets_response_from_Respond_handler()
    {
        var bus = new WebViewMessageBus();
        using var resp = bus.Respond("rpc", m => Task.FromResult<string?>(m.PayloadJson + "-ok"));

        var result = await bus.RequestAsync("rpc", "hello", null, TestContext.Current.CancellationToken);

        Assert.Equal("hello-ok", result);
    }

    [Fact]
    public async Task Request_times_out_if_no_responder()
    {
        var bus = new WebViewMessageBus();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await bus.RequestAsync("no-responder", null, null, cts.Token));
    }

    [Fact]
    public async Task Respond_can_be_disposed_to_stop_handling()
    {
        var bus = new WebViewMessageBus();
        var resp = bus.Respond("rpc", m => Task.FromResult<string?>("handled"));
        resp.Dispose();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await bus.RequestAsync("rpc", null, null, cts.Token));
    }

    [Fact]
    public async Task Multiple_responders_for_different_topics_work_independently()
    {
        var bus = new WebViewMessageBus();
        using var respA = bus.Respond("topicA", m => Task.FromResult<string?>("A"));
        using var respB = bus.Respond("topicB", m => Task.FromResult<string?>("B"));

        var resultA = await bus.RequestAsync("topicA", null, null, TestContext.Current.CancellationToken);
        var resultB = await bus.RequestAsync("topicB", null, null, TestContext.Current.CancellationToken);

        Assert.Equal("A", resultA);
        Assert.Equal("B", resultB);
    }

    [Fact]
    public async Task RequestAsync_throws_on_null_or_whitespace_topic()
    {
        var bus = new WebViewMessageBus();
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await bus.RequestAsync(null!, null, null, TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await bus.RequestAsync("", null, null, TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<ArgumentException>(async () => await bus.RequestAsync("   ", null, null, TestContext.Current.CancellationToken));
    }
}
