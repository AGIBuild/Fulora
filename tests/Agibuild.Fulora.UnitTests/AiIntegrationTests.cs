using Agibuild.Fulora;
using Agibuild.Fulora.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class AiIntegrationTests
{
    [Fact]
    public async Task Full_pipeline_complete_flows_through_middleware()
    {
        var callLog = new List<string>();
        var mockClient = new LoggingMockChatClient(callLog);

        var services = new ServiceCollection();
        services.AddSingleton<IAiContentFilter>(new LoggingContentFilter(callLog));
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("test", mockClient);
            ai.AddResilience(r => r.MaxRetries = 1);
            ai.AddMetering(m =>
            {
                m.ModelPricing["test-model"] = new ModelPricing { PromptPer1K = 0.01m, CompletionPer1K = 0.02m };
            });
        });

        using var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<IAiProviderRegistry>();
        var filters = sp.GetServices<IAiContentFilter>().ToList();
        var meteringOpts = sp.GetRequiredService<IOptions<AiMeteringOptions>>().Value;
        var resilienceOpts = sp.GetRequiredService<IOptions<AiResilienceOptions>>().Value;

        var client = registry.GetChatClient("test");

        // Build pipeline: ContentGate → Resilience → Metering → Provider
        client = new MeteringChatClient(client, meteringOpts);
        client = new ResilientChatClient(client, resilienceOpts);
        client = new ContentGateChatClient(client, filters);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("mock response", response.Text);
        Assert.Contains("input-filter", callLog);
        Assert.Contains("output-filter", callLog);
        Assert.Contains("mock-called", callLog);
    }

    [Fact]
    public async Task AiBridgeService_complete_returns_result()
    {
        var mockClient = new SimpleMockChatClient("test response", 10, 5);
        var services = new ServiceCollection();
        services.AddFuloraAi(ai => ai.AddChatClient("default", mockClient));

        using var sp = services.BuildServiceProvider();
        var bridgeService = sp.GetRequiredService<IAiBridgeService>();

        var result = await bridgeService.Complete(new AiChatRequest { Message = "hi" });

        Assert.Equal("test response", result.Text);
    }

    [Fact]
    public async Task AiBridgeService_list_providers_returns_names()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("openai", new SimpleMockChatClient("a", 0, 0));
            ai.AddChatClient("anthropic", new SimpleMockChatClient("b", 0, 0));
        });

        using var sp = services.BuildServiceProvider();
        var bridgeService = sp.GetRequiredService<IAiBridgeService>();

        var providers = await bridgeService.ListProviders();

        Assert.Contains("openai", providers);
        Assert.Contains("anthropic", providers);
    }

    [Fact]
    public async Task AiBridgeService_upload_and_fetch_blob()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai => ai.AddChatClient("default", new SimpleMockChatClient("x", 0, 0)));

        using var sp = services.BuildServiceProvider();
        var bridgeService = sp.GetRequiredService<IAiBridgeService>();

        var base64 = Convert.ToBase64String([1, 2, 3, 4]);
        var blobId = await bridgeService.UploadBlob(base64, "application/octet-stream", "test.bin");
        Assert.NotNull(blobId);

        var fetched = await bridgeService.FetchBlob(blobId);
        Assert.NotNull(fetched);
        Assert.Equal(base64, fetched);
    }

    [Fact]
    public async Task AiBridgeService_fetch_nonexistent_returns_null()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai => ai.AddChatClient("default", new SimpleMockChatClient("x", 0, 0)));

        using var sp = services.BuildServiceProvider();
        var bridgeService = sp.GetRequiredService<IAiBridgeService>();

        var result = await bridgeService.FetchBlob("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public void DI_registers_all_ai_services()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai => ai.AddChatClient("default", new SimpleMockChatClient("x", 0, 0)));

        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetRequiredService<IAiProviderRegistry>());
        Assert.NotNull(sp.GetRequiredService<IAiPayloadStore>());
        Assert.NotNull(sp.GetRequiredService<IAiToolRegistry>());
        Assert.NotNull(sp.GetRequiredService<IAiBridgeService>());
    }

    [Fact]
    public async Task Pipeline_ordering_content_gate_runs_before_resilience()
    {
        var callOrder = new List<string>();
        var blockingFilter = new OrderTrackingFilter(callOrder, blockInput: true);
        var inner = new OrderTrackingClient(callOrder);

        IChatClient pipeline = inner;
        pipeline = new MeteringChatClient(pipeline, new AiMeteringOptions());
        pipeline = new ResilientChatClient(pipeline, new AiResilienceOptions { MaxRetries = 2, RetryBaseDelay = TimeSpan.FromMilliseconds(1) });
        pipeline = new ContentGateChatClient(pipeline, [blockingFilter]);

        await Assert.ThrowsAsync<AiContentBlockedException>(
            () => pipeline.GetResponseAsync([new ChatMessage(ChatRole.User, "blocked")], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("filter-input", callOrder);
        Assert.DoesNotContain("provider-called", callOrder);
    }

    [Fact]
    public async Task Resilience_retry_accumulates_metering_tokens()
    {
        var callCount = 0;
        var failOnce = new FailOnceThenSucceedClient(() =>
        {
            callCount++;
            if (callCount < 2)
                throw new HttpRequestException("transient", null, System.Net.HttpStatusCode.ServiceUnavailable);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
            {
                ModelId = "gpt-4",
                Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 50 }
            };
        });

        var meteringOpts = new AiMeteringOptions
        {
            ModelPricing = new() { ["gpt-4"] = new ModelPricing { PromptPer1K = 0.03m, CompletionPer1K = 0.06m } }
        };

        IChatClient pipeline = failOnce;
        var metering = new MeteringChatClient(pipeline, meteringOpts);
        pipeline = new ResilientChatClient(metering, new AiResilienceOptions
        {
            MaxRetries = 3,
            RetryBaseDelay = TimeSpan.FromMilliseconds(5),
            Timeout = TimeSpan.FromSeconds(10)
        });

        var response = await pipeline.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("ok", response.Text);
        Assert.Equal(150, metering.PeriodTokensUsed);
    }

    [Fact]
    public async Task Structured_output_with_content_filter_filters_input_before_schema_call()
    {
        var callLog = new List<string>();
        var jsonClient = new JsonResponseClient("""{"name":"Test","age":42}""");
        var filter = new LoggingContentFilter(callLog);

        IChatClient pipeline = jsonClient;
        pipeline = new ContentGateChatClient(pipeline, [filter]);

        var result = await pipeline.CompleteAsync<PersonDto>(
            [new ChatMessage(ChatRole.User, "give me a person")],
            maxRetries: 1,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("Test", result.Name);
        Assert.Equal(42, result.Age);
        Assert.Contains("input-filter", callLog);
        Assert.Contains("output-filter", callLog);
    }

    [Fact]
    public async Task Tool_registry_integrated_with_bridge_service()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai => ai.AddChatClient("default", new SimpleMockChatClient("x", 0, 0)));

        using var sp = services.BuildServiceProvider();
        var toolRegistry = sp.GetRequiredService<IAiToolRegistry>();
        var toolService = new ToolServiceImpl();
        toolRegistry.Register(toolService);

        var tool = toolRegistry.FindTool("Add");
        Assert.NotNull(tool);

        var result = await tool.InvokeAsync(new Microsoft.Extensions.AI.AIFunctionArguments(
            new Dictionary<string, object?> { ["a"] = 3, ["b"] = 7 }), TestContext.Current.CancellationToken);
        var jsonResult = (System.Text.Json.JsonElement)result!;
        Assert.Equal(10, jsonResult.GetInt32());
    }

    [Fact]
    public async Task Payload_store_lifecycle_through_bridge_upload_fetch_expiry()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai => ai.AddChatClient("default", new SimpleMockChatClient("x", 0, 0)));

        using var sp = services.BuildServiceProvider();
        var bridgeService = sp.GetRequiredService<IAiBridgeService>();
        var payloadStore = sp.GetRequiredService<IAiPayloadStore>();

        var imageData = new byte[64];
        Random.Shared.NextBytes(imageData);
        var base64 = Convert.ToBase64String(imageData);

        var blobId = await bridgeService.UploadBlob(base64, "image/png", "test.png");

        var directFetch = payloadStore.Fetch(blobId);
        Assert.NotNull(directFetch);
        Assert.Equal("image/png", directFetch.MimeType);
        Assert.Equal(imageData, directFetch.Data);

        var bridgeFetch = await bridgeService.FetchBlob(blobId);
        Assert.Equal(base64, bridgeFetch);

        payloadStore.Remove(blobId);
        var afterRemove = await bridgeService.FetchBlob(blobId);
        Assert.Null(afterRemove);
    }

    [Fact]
    public async Task Multiple_content_filters_chain_in_order()
    {
        var callOrder = new List<string>();
        var filter1 = new NamedFilter("F1", callOrder);
        var filter2 = new NamedFilter("F2", callOrder);
        var inner = new SimpleMockChatClient("response", 0, 0);

        var pipeline = new ContentGateChatClient(inner, [filter1, filter2]);
        await pipeline.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["F1-input", "F2-input", "F1-output", "F2-output"], callOrder);
    }

    [Fact]
    public async Task Content_filter_transform_propagates_to_provider()
    {
        string? receivedMessage = null;
        var captureClient = new CaptureInputClient(msg => receivedMessage = msg);
        var transformFilter = new TransformAllFilter("sanitized input");

        var pipeline = new ContentGateChatClient(captureClient, [transformFilter]);
        await pipeline.GetResponseAsync([new ChatMessage(ChatRole.User, "original input")], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("sanitized input", receivedMessage);
    }

    [Fact]
    public async Task Metering_budget_blocks_second_call_in_pipeline()
    {
        var client = new SimpleMockChatClient("ok", 100, 50);
        var meteringOpts = new AiMeteringOptions
        {
            PeriodBudgetTokens = 200,
            BudgetPeriod = TimeSpan.FromHours(1)
        };

        var metering = new MeteringChatClient(client, meteringOpts);

        await metering.GetResponseAsync([new ChatMessage(ChatRole.User, "first")], cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(150, metering.PeriodTokensUsed);

        await Assert.ThrowsAsync<AiBudgetExceededException>(
            () => metering.GetResponseAsync([new ChatMessage(ChatRole.User, "second")], cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Payload_router_round_trips_through_store()
    {
        using var store = new InMemoryAiPayloadStore();
        var router = new AiPayloadRouter(store, thresholdBytes: 50);

        var smallPayload = new AiMediaPayload { Data = new byte[30], MimeType = "text/plain" };
        var smallRef = router.Route(smallPayload);
        Assert.True(smallRef.IsInline);
        Assert.StartsWith("data:text/plain;base64,", smallRef.Value);

        var largeData = new byte[100];
        Random.Shared.NextBytes(largeData);
        var largePayload = new AiMediaPayload { Data = largeData, MimeType = "image/jpeg" };
        var largeRef = router.Route(largePayload);
        Assert.False(largeRef.IsInline);
        Assert.NotNull(largeRef.BlobId);

        var retrieved = store.Fetch(largeRef.BlobId!);
        Assert.NotNull(retrieved);
        Assert.Equal(largeData, retrieved.Data);
        Assert.Equal("image/jpeg", retrieved.MimeType);
    }

    [Fact]
    public async Task BridgeService_complete_with_provider_selection()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("fast", new SimpleMockChatClient("fast response", 5, 3));
            ai.AddChatClient("smart", new SimpleMockChatClient("smart response", 50, 30));
        });

        using var sp = services.BuildServiceProvider();
        var bridge = sp.GetRequiredService<IAiBridgeService>();

        var fastResult = await bridge.Complete(new AiChatRequest { Message = "test", Provider = "fast" });
        Assert.Equal("fast response", fastResult.Text);

        var smartResult = await bridge.Complete(new AiChatRequest { Message = "test", Provider = "smart" });
        Assert.Equal("smart response", smartResult.Text);
    }

    public sealed record PersonDto
    {
        public string Name { get; init; } = "";
        public int Age { get; init; }
    }

    [AiTool]
    public interface ICalcToolService
    {
        int Add(int a, int b);
    }

    private sealed class ToolServiceImpl : ICalcToolService
    {
        public int Add(int a, int b) => a + b;
    }

    private sealed class OrderTrackingFilter(List<string> order, bool blockInput = false) : IAiContentFilter
    {
        public Task<ContentFilterResult> FilterInputAsync(string content, CancellationToken ct)
        {
            order.Add("filter-input");
            return Task.FromResult(blockInput
                ? ContentFilterResult.Block("blocked by test")
                : ContentFilterResult.Allow);
        }

        public Task<ContentFilterResult> FilterOutputAsync(string content, CancellationToken ct)
        {
            order.Add("filter-output");
            return Task.FromResult(ContentFilterResult.Allow);
        }
    }

    private sealed class OrderTrackingClient(List<string> order) : IChatClient
    {
        public void Dispose() { }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default)
        {
            order.Add("provider-called");
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default) => AsyncEnumerable.Empty<ChatResponseUpdate>();
        public object? GetService(Type t, object? k = null) => null;
    }

    private sealed class FailOnceThenSucceedClient(Func<ChatResponse> handler) : IChatClient
    {
        public void Dispose() { }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default)
            => Task.FromResult(handler());
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default)
            => AsyncEnumerable.Empty<ChatResponseUpdate>();
        public object? GetService(Type t, object? k = null) => null;
    }

    private sealed class JsonResponseClient(string json) : IChatClient
    {
        public void Dispose() { }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, json)));
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default)
            => AsyncEnumerable.Empty<ChatResponseUpdate>();
        public object? GetService(Type t, object? k = null) => null;
    }

    private sealed class NamedFilter(string name, List<string> order) : IAiContentFilter
    {
        public Task<ContentFilterResult> FilterInputAsync(string content, CancellationToken ct)
        {
            order.Add($"{name}-input");
            return Task.FromResult(ContentFilterResult.Allow);
        }
        public Task<ContentFilterResult> FilterOutputAsync(string content, CancellationToken ct)
        {
            order.Add($"{name}-output");
            return Task.FromResult(ContentFilterResult.Allow);
        }
    }

    private sealed class TransformAllFilter(string replacement) : IAiContentFilter
    {
        public Task<ContentFilterResult> FilterInputAsync(string content, CancellationToken ct)
            => Task.FromResult(ContentFilterResult.Transform(replacement));
        public Task<ContentFilterResult> FilterOutputAsync(string content, CancellationToken ct)
            => Task.FromResult(ContentFilterResult.Allow);
    }

    private sealed class CaptureInputClient(Action<string> capture) : IChatClient
    {
        public void Dispose() { }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? o = null, CancellationToken ct = default)
        {
            var userMsg = messages.FirstOrDefault(m => m.Role == ChatRole.User)?.Text;
            if (userMsg is not null) capture(userMsg);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "echo")));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default)
            => AsyncEnumerable.Empty<ChatResponseUpdate>();
        public object? GetService(Type t, object? k = null) => null;
    }

    private sealed class LoggingMockChatClient(List<string> log) : IChatClient
    {
        public void Dispose() { }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            log.Add("mock-called");
            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, "mock response"))
            {
                ModelId = "test-model",
                Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 }
            });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            AsyncEnumerable.Empty<ChatResponseUpdate>();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }

    private sealed class LoggingContentFilter(List<string> log) : IAiContentFilter
    {
        public Task<ContentFilterResult> FilterInputAsync(string content, CancellationToken ct)
        {
            log.Add("input-filter");
            return Task.FromResult(ContentFilterResult.Allow);
        }

        public Task<ContentFilterResult> FilterOutputAsync(string content, CancellationToken ct)
        {
            log.Add("output-filter");
            return Task.FromResult(ContentFilterResult.Allow);
        }
    }

    private sealed class SimpleMockChatClient(string response, int promptTokens, int completionTokens) : IChatClient
    {
        public void Dispose() { }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, response))
            {
                Usage = new UsageDetails { InputTokenCount = promptTokens, OutputTokenCount = completionTokens }
            });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            AsyncEnumerable.Empty<ChatResponseUpdate>();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }
}
