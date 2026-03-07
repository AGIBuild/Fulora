using Agibuild.Fulora;
using Agibuild.Fulora.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
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
        var meteringOpts = sp.GetRequiredService<AiMeteringOptions>();
        var resilienceOpts = sp.GetRequiredService<AiResilienceOptions>();

        var client = registry.GetChatClient("test");

        // Build pipeline: ContentGate → Resilience → Metering → Provider
        client = new MeteringChatClient(client, meteringOpts);
        client = new ResilientChatClient(client, resilienceOpts);
        client = new ContentGateChatClient(client, filters);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);

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
