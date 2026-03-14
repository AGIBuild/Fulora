using System.Runtime.CompilerServices;
using Agibuild.Fulora;
using Agibuild.Fulora.NativeOverlay;
using Agibuild.Fulora.Plugin.Biometric;
using Agibuild.Fulora.Testing;
using Avalonia;
using Microsoft.Extensions.AI;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

/// <summary>
/// End-to-end integration tests that exercise full feature pipelines through
/// FuloraTestApp, verifying that components integrate correctly.
/// </summary>
public sealed class E2EIntegrationTests
{
    // ==================== AI Streaming E2E ====================

    [Fact]
    public async Task E2E_AiStreaming_EchoClient_streams_through_full_pipeline()
    {
        var client = new EchoChatClientForE2E();
        var tokens = new List<string>();

        await foreach (var token in StreamCompletion(client, "Hello", TestContext.Current.CancellationToken))
        {
            tokens.Add(token);
        }

        var expected = "[Echo] Hello";
        Assert.Equal(expected.Length, tokens.Count);
        Assert.Equal(expected, string.Join("", tokens));
    }

    [Fact]
    public async Task E2E_AiStreaming_cancellation_mid_stream_stops_enumeration()
    {
        var client = new EchoChatClientForE2E();
        using var cts = new CancellationTokenSource();
        var tokens = new List<string>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var token in StreamCompletion(client, "Long message for streaming test", cts.Token))
            {
                tokens.Add(token);
                if (tokens.Count >= 5)
                    cts.Cancel();
            }
        });

        Assert.True(tokens.Count >= 5);
        Assert.True(tokens.Count < "[Echo] Long message for streaming test".Length);
    }

    [Fact]
    public async Task E2E_AiStreaming_multiple_sequential_streams_are_independent()
    {
        var client = new EchoChatClientForE2E();

        var tokens1 = new List<string>();
        await foreach (var t in StreamCompletion(client, "A", TestContext.Current.CancellationToken))
            tokens1.Add(t);

        var tokens2 = new List<string>();
        await foreach (var t in StreamCompletion(client, "B", TestContext.Current.CancellationToken))
            tokens2.Add(t);

        Assert.Equal("[Echo] A", string.Join("", tokens1));
        Assert.Equal("[Echo] B", string.Join("", tokens2));
    }

    // ==================== Drag-Drop E2E ====================

    [Fact]
    public async Task E2E_DragDrop_full_lifecycle_enter_over_drop()
    {
        await using var app = FuloraTestApp.CreateWithDragDrop();
        var dd = (MockWebViewAdapterWithDragDrop)app.Adapter!;
        var core = app.Core!;
        var service = new DragDropBridgeService(core);

        var enteredPayload = new DragDropPayload { Text = "dragging" };
        var enterArgs = new DragEventArgs
        {
            Payload = enteredPayload,
            AllowedEffects = DragDropEffects.Copy,
            Effect = DragDropEffects.Copy,
            X = 10,
            Y = 20
        };

        var enteredFired = false;
        core.DragEntered += (_, e) => enteredFired = true;
        dd.RaiseDragEntered(enterArgs);
        Assert.True(enteredFired);

        var overFired = false;
        core.DragOver += (_, _) => overFired = true;
        dd.RaiseDragOver(new DragEventArgs
        {
            Payload = new DragDropPayload(),
            AllowedEffects = DragDropEffects.Copy,
            X = 15,
            Y = 25
        });
        Assert.True(overFired);

        var dropPayload = new DragDropPayload
        {
            Files = new List<FileDropInfo>
            {
                new("/home/user/doc.pdf", "application/pdf", 10240),
                new("/home/user/photo.jpg", "image/jpeg", 204800)
            },
            Text = "dropped files"
        };
        dd.RaiseDropCompleted(new DropEventArgs
        {
            Payload = dropPayload,
            Effect = DragDropEffects.Copy,
            X = 20,
            Y = 30
        });

        var result = await service.GetLastDropPayloadAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.Equal("dropped files", result!.Text);
        Assert.Equal(2, result.Files!.Count);
        Assert.Equal("/home/user/doc.pdf", result.Files[0].Path);
        Assert.Equal(204800, result.Files[1].Size);
    }

    [Fact]
    public async Task E2E_DragDrop_leave_does_not_affect_service_state()
    {
        await using var app = FuloraTestApp.CreateWithDragDrop();
        var dd = (MockWebViewAdapterWithDragDrop)app.Adapter!;
        var core = app.Core!;
        var service = new DragDropBridgeService(core);

        dd.RaiseDropCompleted(new DropEventArgs
        {
            Payload = new DragDropPayload { Text = "before leave" },
            Effect = DragDropEffects.Copy
        });

        dd.RaiseDragLeft();

        var result = await service.GetLastDropPayloadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("before leave", result?.Text);
    }

    [Fact]
    public async Task E2E_DragDrop_service_support_flag_reflects_adapter()
    {
        await using var appWithDd = FuloraTestApp.CreateWithDragDrop();
        var serviceDd = new DragDropBridgeService(appWithDd.Core!);
        Assert.True(await serviceDd.IsDragDropSupportedAsync(TestContext.Current.CancellationToken));

        await using var appWithout = FuloraTestApp.Create();
        var serviceNoDd = new DragDropBridgeService(appWithout.Core!);
        Assert.False(await serviceNoDd.IsDragDropSupportedAsync(TestContext.Current.CancellationToken));
    }

    // ==================== Biometric E2E ====================

    [Fact]
    public async Task E2E_Biometric_full_auth_flow_available_and_success()
    {
        var provider = new InMemoryBiometricProvider(true, true, "touchid");
        var service = new BiometricService(provider);

        var avail = await service.CheckAvailabilityAsync(TestContext.Current.CancellationToken);
        Assert.True(avail.IsAvailable);
        Assert.Equal("touchid", avail.BiometricType);

        var result = await service.AuthenticateAsync("Confirm purchase", TestContext.Current.CancellationToken);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task E2E_Biometric_full_auth_flow_user_cancels()
    {
        var provider = new InMemoryBiometricProvider(true, false, "faceid");
        var service = new BiometricService(provider);

        var avail = await service.CheckAvailabilityAsync(TestContext.Current.CancellationToken);
        Assert.True(avail.IsAvailable);
        Assert.Equal("faceid", avail.BiometricType);

        var result = await service.AuthenticateAsync("Verify identity", TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.Equal("user_cancelled", result.ErrorCode);
    }

    [Fact]
    public async Task E2E_Biometric_unavailable_platform_graceful_degradation()
    {
        var provider = new InMemoryBiometricProvider(false, false);
        var service = new BiometricService(provider);

        var avail = await service.CheckAvailabilityAsync(TestContext.Current.CancellationToken);
        Assert.False(avail.IsAvailable);
        Assert.Equal("not_available", avail.ErrorReason);

        var result = await service.AuthenticateAsync("Won't work", TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.Equal("not_available", result.ErrorCode);
    }

    [Fact]
    public async Task E2E_Biometric_provider_crash_returns_internal_error()
    {
        var provider = new CrashingBiometricProvider();
        var service = new BiometricService(provider);

        var avail = await service.CheckAvailabilityAsync(TestContext.Current.CancellationToken);
        Assert.False(avail.IsAvailable);
        Assert.Contains("crash", avail.ErrorReason, StringComparison.OrdinalIgnoreCase);

        var result = await service.AuthenticateAsync("test", TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.Equal("internal_error", result.ErrorCode);
    }

    [Fact]
    public void E2E_Biometric_plugin_registration_exposes_service()
    {
        var bridge = new TrackingBridgeService();
        bridge.UsePlugin<BiometricPlugin>();

        Assert.Single(bridge.Exposed);
        Assert.Equal(typeof(IBiometricService), bridge.Exposed[0].InterfaceType);
    }

    // ==================== Overlay E2E ====================

    [Fact]
    public void E2E_Overlay_full_lifecycle_create_position_hittest_dispose()
    {
        var webView = new WebView();
        var host = new WebViewOverlayHost(webView);

        Assert.Null(host.Content);
        Assert.False(host.IsVisible);
        Assert.False(host.HasKeyboardFocus);

        host.Content = new object();
        host.SyncVisibilityWith(true);
        Assert.True(host.IsVisible);

        host.UpdatePosition(new Rect(0, 0, 200, 100), new Point(10, 20), 2.0);
        Assert.Equal(new Rect(10, 20, 400, 200), host.Bounds);
        Assert.Equal(2.0, host.DpiScale);

        Assert.Equal(OverlayHitTestResult.Overlay, host.HitTest(50, 50));
        Assert.Equal(OverlayHitTestResult.Passthrough, host.HitTest(5, 5));

        host.TransferFocusToOverlay();
        Assert.True(host.HasKeyboardFocus);
        host.TransferFocusToWebView();
        Assert.False(host.HasKeyboardFocus);

        host.SyncVisibilityWith(false);
        Assert.False(host.IsVisible);
        Assert.Equal(OverlayHitTestResult.Passthrough, host.HitTest(50, 50));

        host.Dispose();
        Assert.Null(host.Content);
    }

    [Fact]
    public void E2E_Overlay_DPI_change_updates_bounds()
    {
        var webView = new WebView();
        var host = new WebViewOverlayHost(webView);
        host.Content = new object();

        host.UpdatePosition(new Rect(0, 0, 100, 50), new Point(0, 0), 1.0);
        Assert.Equal(new Rect(0, 0, 100, 50), host.Bounds);

        host.UpdatePosition(new Rect(0, 0, 100, 50), new Point(0, 0), 1.5);
        Assert.Equal(new Rect(0, 0, 150, 75), host.Bounds);
        Assert.Equal(1.5, host.DpiScale);

        host.UpdatePosition(new Rect(0, 0, 100, 50), new Point(0, 0), 2.0);
        Assert.Equal(new Rect(0, 0, 200, 100), host.Bounds);
    }

    [Fact]
    public void E2E_Overlay_native_provider_factory_produces_platform_provider()
    {
        var provider = NativeOverlayProviderFactory.Create();
        Assert.NotNull(provider);
        Assert.False(provider.IsVisible);
        Assert.Equal(IntPtr.Zero, provider.OverlayHandle);
        provider.Dispose();
    }

    // ==================== FuloraTestApp lifecycle E2E ====================

    [Fact]
    public async Task E2E_FuloraTestApp_full_lifecycle_with_bridge_tracer()
    {
        await using var app = FuloraTestApp.Create();
        var handle = app.GetWebView();

        app.Tracer.OnExportCallStart("FileService", "upload", """{"name":"test.txt"}""");
        app.Tracer.OnExportCallEnd("FileService", "upload", 150, "string");
        app.Tracer.OnImportCallStart("NotifyService", "show", null);
        app.Tracer.OnImportCallEnd("NotifyService", "show", 5);

        var exportCalls = app.Tracer.GetBridgeCalls("FileService");
        Assert.Single(exportCalls);
        Assert.Equal("upload", exportCalls[0].MethodName);
        Assert.Equal(150, exportCalls[0].ElapsedMs);

        var importCalls = app.Tracer.GetBridgeCalls("NotifyService");
        Assert.Single(importCalls);
        Assert.Equal(BridgeCallDirection.Import, importCalls[0].Direction);

        app.Tracer.Reset();
        Assert.Empty(app.Tracer.GetBridgeCalls());
    }

    [Fact]
    public async Task E2E_FuloraTestApp_with_drag_drop_has_adapter()
    {
        await using var app = FuloraTestApp.CreateWithDragDrop();

        Assert.NotNull(app.Adapter);
        Assert.NotNull(app.Core);
        Assert.True(app.Core!.HasDragDropSupport);
    }

    // ==================== Helpers ====================

    private static async IAsyncEnumerable<string> StreamCompletion(
        IChatClient chatClient, string prompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ChatMessage[] messages = [new(ChatRole.User, prompt)];

        await foreach (var update in chatClient.GetStreamingResponseAsync(messages, cancellationToken: ct))
        {
            if (update.Text is { Length: > 0 } text)
                yield return text;
        }
    }

    private sealed class EchoChatClientForE2E : IChatClient
    {
        public void Dispose() { }
        public ChatClientMetadata Metadata { get; } = new("echo-e2e");

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var lastMessage = chatMessages.LastOrDefault()?.Text ?? "Hello!";
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, $"[Echo] {lastMessage}")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var lastMessage = chatMessages.LastOrDefault()?.Text ?? "Hello!";
            foreach (var ch in $"[Echo] {lastMessage}")
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new ChatResponseUpdate(ChatRole.Assistant, ch.ToString());
                await Task.Yield();
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }

    private sealed class CrashingBiometricProvider : IBiometricPlatformProvider
    {
        public Task<BiometricAvailability> CheckAvailabilityAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("Hardware crash simulation");

        public Task<BiometricResult> AuthenticateAsync(string reason, CancellationToken ct = default)
            => throw new InvalidOperationException("Hardware crash simulation");
    }
}
