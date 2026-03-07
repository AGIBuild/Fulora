using Agibuild.Fulora.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class AiProviderRegistryTests
{
    [Fact]
    public void RegisterChatClient_and_resolve_by_name()
    {
        var registry = new AiProviderRegistry();
        var client = new TestChatClient("test-model");
        registry.RegisterChatClient("fast", client);

        var resolved = registry.GetChatClient("fast");

        Assert.Same(client, resolved);
    }

    [Fact]
    public void GetChatClient_resolves_default_as_first_registered()
    {
        var registry = new AiProviderRegistry();
        var first = new TestChatClient("first");
        var second = new TestChatClient("second");
        registry.RegisterChatClient("a", first);
        registry.RegisterChatClient("b", second);

        var resolved = registry.GetChatClient();

        Assert.Same(first, resolved);
    }

    [Fact]
    public void GetChatClient_throws_when_no_providers()
    {
        var registry = new AiProviderRegistry();

        var ex = Assert.Throws<InvalidOperationException>(() => registry.GetChatClient());
        Assert.Contains("No AI chat client", ex.Message);
    }

    [Fact]
    public void GetChatClient_throws_for_unknown_name()
    {
        var registry = new AiProviderRegistry();
        registry.RegisterChatClient("known", new TestChatClient("m"));

        var ex = Assert.Throws<InvalidOperationException>(() => registry.GetChatClient("unknown"));
        Assert.Contains("unknown", ex.Message);
        Assert.Contains("known", ex.Message);
    }

    [Fact]
    public void ChatClientNames_returns_registered_names()
    {
        var registry = new AiProviderRegistry();
        registry.RegisterChatClient("alpha", new TestChatClient("a"));
        registry.RegisterChatClient("beta", new TestChatClient("b"));

        var names = registry.ChatClientNames;

        Assert.Contains("alpha", names);
        Assert.Contains("beta", names);
        Assert.Equal(2, names.Count);
    }

    [Fact]
    public void AddFuloraAi_registers_provider_registry_in_DI()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", new TestChatClient("m"));
        });

        using var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<IAiProviderRegistry>();
        var client = registry.GetChatClient();

        Assert.NotNull(client);
    }

    [Fact]
    public void AddFuloraAi_with_resilience_registers_options()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", new TestChatClient("m"));
            ai.AddResilience(r => r.MaxRetries = 5);
        });

        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<AiResilienceOptions>();

        Assert.Equal(5, options.MaxRetries);
    }

    [Fact]
    public void ContentFilterResult_Allow_has_correct_action()
    {
        var result = ContentFilterResult.Allow;
        Assert.Equal(ContentFilterAction.Allow, result.Action);
        Assert.Null(result.Reason);
        Assert.Null(result.TransformedContent);
    }

    [Fact]
    public void ContentFilterResult_Block_has_reason()
    {
        var result = ContentFilterResult.Block("harmful content");
        Assert.Equal(ContentFilterAction.Block, result.Action);
        Assert.Equal("harmful content", result.Reason);
    }

    [Fact]
    public void ContentFilterResult_Transform_has_content()
    {
        var result = ContentFilterResult.Transform("cleaned text");
        Assert.Equal(ContentFilterAction.Transform, result.Action);
        Assert.Equal("cleaned text", result.TransformedContent);
    }

    [Fact]
    public void AiMediaPayload_stores_data_and_mime()
    {
        var payload = new AiMediaPayload
        {
            Data = [0x89, 0x50, 0x4E, 0x47],
            MimeType = "image/png",
            Name = "test.png"
        };

        Assert.Equal(4, payload.Data.Length);
        Assert.Equal("image/png", payload.MimeType);
        Assert.Equal("test.png", payload.Name);
    }

    [Fact]
    public void AiToolAttribute_properties_default_to_null()
    {
        var attr = new AiToolAttribute();
        Assert.Null(attr.Group);
        Assert.Null(attr.Description);
    }

    private sealed class TestChatClient(string modelId) : IChatClient
    {
        public void Dispose() { }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, $"response from {modelId}")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return AsyncEnumerable.Empty<ChatResponseUpdate>();
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }
}
