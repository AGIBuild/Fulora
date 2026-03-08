using Agibuild.Fulora.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

/// <summary>
/// Mutation-killing tests for AiProviderRegistry, AiPayloadRouter, InMemoryAiPayloadStore,
/// AiToolRegistry, and FuloraAiServiceCollectionExtensions.
/// </summary>
public sealed class AiCoreMutationKillerTests
{
    #region AiProviderRegistry

    [Fact]
    public void Registry_first_registered_becomes_default()
    {
        var registry = new AiProviderRegistry();
        var clientA = new DummyChatClient("A");
        var clientB = new DummyChatClient("B");

        registry.RegisterChatClient("first", clientA);
        registry.RegisterChatClient("second", clientB);

        var resolved = registry.GetChatClient();
        Assert.Same(clientA, resolved);
    }

    [Fact]
    public void Registry_named_lookup_returns_exact_client()
    {
        var registry = new AiProviderRegistry();
        var clientA = new DummyChatClient("A");
        var clientB = new DummyChatClient("B");

        registry.RegisterChatClient("provA", clientA);
        registry.RegisterChatClient("provB", clientB);

        Assert.Same(clientB, registry.GetChatClient("provB"));
        Assert.Same(clientA, registry.GetChatClient("provA"));
    }

    [Fact]
    public void Registry_case_insensitive_lookup()
    {
        var registry = new AiProviderRegistry();
        var client = new DummyChatClient("X");
        registry.RegisterChatClient("MyProvider", client);

        Assert.Same(client, registry.GetChatClient("myprovider"));
        Assert.Same(client, registry.GetChatClient("MYPROVIDER"));
    }

    [Fact]
    public void Registry_unknown_name_throws_InvalidOperationException()
    {
        var registry = new AiProviderRegistry();
        registry.RegisterChatClient("known", new DummyChatClient("K"));

        var ex = Assert.Throws<InvalidOperationException>(() => registry.GetChatClient("unknown"));
        Assert.Contains("unknown", ex.Message);
        Assert.Contains("known", ex.Message);
    }

    [Fact]
    public void Registry_no_providers_throws_on_default()
    {
        var registry = new AiProviderRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.GetChatClient());
    }

    [Fact]
    public void Registry_ChatClientNames_returns_all()
    {
        var registry = new AiProviderRegistry();
        registry.RegisterChatClient("alpha", new DummyChatClient("A"));
        registry.RegisterChatClient("beta", new DummyChatClient("B"));

        var names = registry.ChatClientNames;
        Assert.Equal(2, names.Count);
        Assert.Contains("alpha", names);
        Assert.Contains("beta", names);
    }

    [Fact]
    public void Registry_RegisterChatClient_null_name_throws()
    {
        var registry = new AiProviderRegistry();
        Assert.Throws<ArgumentException>(() => registry.RegisterChatClient("", new DummyChatClient("X")));
        Assert.Throws<ArgumentException>(() => registry.RegisterChatClient("  ", new DummyChatClient("X")));
    }

    [Fact]
    public void Registry_RegisterChatClient_null_client_throws()
    {
        var registry = new AiProviderRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.RegisterChatClient("name", null!));
    }

    [Fact]
    public void Registry_overwrite_same_name_uses_latest()
    {
        var registry = new AiProviderRegistry();
        var clientOld = new DummyChatClient("old");
        var clientNew = new DummyChatClient("new");

        registry.RegisterChatClient("name", clientOld);
        registry.RegisterChatClient("name", clientNew);

        Assert.Same(clientNew, registry.GetChatClient("name"));
    }

    #endregion

    #region AiProviderRegistry - EmbeddingGenerator

    [Fact]
    public void Registry_first_registered_embedding_becomes_default()
    {
        var registry = new AiProviderRegistry();
        var genA = new DummyEmbeddingGenerator("A");
        var genB = new DummyEmbeddingGenerator("B");

        registry.RegisterEmbeddingGenerator("first", genA);
        registry.RegisterEmbeddingGenerator("second", genB);

        var resolved = registry.GetEmbeddingGenerator();
        Assert.Same(genA, resolved);
    }

    [Fact]
    public void Registry_named_embedding_lookup_returns_exact()
    {
        var registry = new AiProviderRegistry();
        var genA = new DummyEmbeddingGenerator("A");
        var genB = new DummyEmbeddingGenerator("B");

        registry.RegisterEmbeddingGenerator("embA", genA);
        registry.RegisterEmbeddingGenerator("embB", genB);

        Assert.Same(genB, registry.GetEmbeddingGenerator("embB"));
        Assert.Same(genA, registry.GetEmbeddingGenerator("embA"));
    }

    [Fact]
    public void Registry_embedding_case_insensitive()
    {
        var registry = new AiProviderRegistry();
        var gen = new DummyEmbeddingGenerator("X");
        registry.RegisterEmbeddingGenerator("MyEmbed", gen);

        Assert.Same(gen, registry.GetEmbeddingGenerator("myembed"));
        Assert.Same(gen, registry.GetEmbeddingGenerator("MYEMBED"));
    }

    [Fact]
    public void Registry_embedding_unknown_name_throws()
    {
        var registry = new AiProviderRegistry();
        registry.RegisterEmbeddingGenerator("known", new DummyEmbeddingGenerator("K"));

        var ex = Assert.Throws<InvalidOperationException>(() => registry.GetEmbeddingGenerator("unknown"));
        Assert.Contains("unknown", ex.Message);
        Assert.Contains("known", ex.Message);
    }

    [Fact]
    public void Registry_no_embedding_providers_throws_on_default()
    {
        var registry = new AiProviderRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.GetEmbeddingGenerator());
    }

    [Fact]
    public void Registry_RegisterEmbeddingGenerator_null_name_throws()
    {
        var registry = new AiProviderRegistry();
        Assert.Throws<ArgumentException>(() => registry.RegisterEmbeddingGenerator("", new DummyEmbeddingGenerator("X")));
        Assert.Throws<ArgumentException>(() => registry.RegisterEmbeddingGenerator("  ", new DummyEmbeddingGenerator("X")));
    }

    [Fact]
    public void Registry_RegisterEmbeddingGenerator_null_generator_throws()
    {
        var registry = new AiProviderRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.RegisterEmbeddingGenerator("name", null!));
    }

    [Fact]
    public void Registry_EmbeddingGeneratorNames_returns_all()
    {
        var registry = new AiProviderRegistry();
        registry.RegisterEmbeddingGenerator("alpha", new DummyEmbeddingGenerator("A"));
        registry.RegisterEmbeddingGenerator("beta", new DummyEmbeddingGenerator("B"));

        var names = registry.EmbeddingGeneratorNames;
        Assert.Equal(2, names.Count);
        Assert.Contains("alpha", names);
        Assert.Contains("beta", names);
    }

    [Fact]
    public void Registry_embedding_coalesce_assignment_preserves_first_default()
    {
        var registry = new AiProviderRegistry();
        var genA = new DummyEmbeddingGenerator("A");
        var genB = new DummyEmbeddingGenerator("B");

        registry.RegisterEmbeddingGenerator("first", genA);
        registry.RegisterEmbeddingGenerator("second", genB);

        // Default should still be "first", not overwritten by "second"
        Assert.Same(genA, registry.GetEmbeddingGenerator());
        Assert.Same(genB, registry.GetEmbeddingGenerator("second"));
    }

    [Fact]
    public void Registry_embedding_overwrite_same_name_uses_latest()
    {
        var registry = new AiProviderRegistry();
        var genOld = new DummyEmbeddingGenerator("old");
        var genNew = new DummyEmbeddingGenerator("new");

        registry.RegisterEmbeddingGenerator("name", genOld);
        registry.RegisterEmbeddingGenerator("name", genNew);

        Assert.Same(genNew, registry.GetEmbeddingGenerator("name"));
    }

    #endregion

    #region AiPayloadRouter

    [Fact]
    public void Router_small_payload_returns_inline()
    {
        var store = new InMemoryAiPayloadStore();
        var router = new AiPayloadRouter(store, thresholdBytes: 100);

        var payload = new AiMediaPayload { Data = new byte[50], MimeType = "image/png" };
        var result = router.Route(payload);

        Assert.True(result.IsInline);
        Assert.StartsWith("data:image/png;base64,", result.Value);
        Assert.Null(result.BlobId);
    }

    [Fact]
    public void Router_at_threshold_returns_inline()
    {
        var store = new InMemoryAiPayloadStore();
        var router = new AiPayloadRouter(store, thresholdBytes: 100);

        var payload = new AiMediaPayload { Data = new byte[100], MimeType = "image/png" };
        var result = router.Route(payload);

        Assert.True(result.IsInline);
    }

    [Fact]
    public void Router_over_threshold_returns_blob_reference()
    {
        var store = new InMemoryAiPayloadStore();
        var router = new AiPayloadRouter(store, thresholdBytes: 100);

        var payload = new AiMediaPayload { Data = new byte[101], MimeType = "image/png" };
        var result = router.Route(payload);

        Assert.False(result.IsInline);
        Assert.StartsWith("app://ai/blob/", result.Value);
        Assert.NotNull(result.BlobId);
        Assert.Contains(result.BlobId!, result.Value);
    }

    [Fact]
    public void Router_inline_base64_is_decodable()
    {
        var store = new InMemoryAiPayloadStore();
        var router = new AiPayloadRouter(store, thresholdBytes: 100);

        var data = new byte[] { 1, 2, 3, 4, 5 };
        var payload = new AiMediaPayload { Data = data, MimeType = "application/octet-stream" };
        var result = router.Route(payload);

        var base64Part = result.Value.Split(",")[1];
        Assert.Equal(data, Convert.FromBase64String(base64Part));
    }

    [Fact]
    public void Router_blob_reference_is_retrievable()
    {
        var store = new InMemoryAiPayloadStore();
        var router = new AiPayloadRouter(store, thresholdBytes: 10);

        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
        var payload = new AiMediaPayload { Data = data, MimeType = "application/octet-stream" };
        var result = router.Route(payload);

        Assert.NotNull(result.BlobId);
        var fetched = store.Fetch(result.BlobId!);
        Assert.NotNull(fetched);
        Assert.Equal(data, fetched!.Data);
    }

    [Fact]
    public void Router_null_payload_throws()
    {
        var store = new InMemoryAiPayloadStore();
        var router = new AiPayloadRouter(store);
        Assert.Throws<ArgumentNullException>(() => router.Route(null!));
    }

    [Fact]
    public void Router_null_store_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AiPayloadRouter(null!));
    }

    #endregion

    #region InMemoryAiPayloadStore

    [Fact]
    public void Store_returns_unique_ids()
    {
        using var store = new InMemoryAiPayloadStore();
        var payload = new AiMediaPayload { Data = [1], MimeType = "x" };

        var id1 = store.Store(payload);
        var id2 = store.Store(payload);

        Assert.NotEqual(id1, id2);
        Assert.NotEmpty(id1);
        Assert.NotEmpty(id2);
    }

    [Fact]
    public void Fetch_returns_stored_payload()
    {
        using var store = new InMemoryAiPayloadStore();
        var data = new byte[] { 0xCA, 0xFE };
        var payload = new AiMediaPayload { Data = data, MimeType = "app/bin", Name = "test" };

        var id = store.Store(payload);
        var fetched = store.Fetch(id);

        Assert.NotNull(fetched);
        Assert.Equal(data, fetched!.Data);
        Assert.Equal("app/bin", fetched.MimeType);
        Assert.Equal("test", fetched.Name);
    }

    [Fact]
    public void Fetch_nonexistent_returns_null()
    {
        using var store = new InMemoryAiPayloadStore();
        Assert.Null(store.Fetch("nonexistent"));
    }

    [Fact]
    public void Remove_deletes_entry()
    {
        using var store = new InMemoryAiPayloadStore();
        var id = store.Store(new AiMediaPayload { Data = [1], MimeType = "x" });

        Assert.True(store.Remove(id));
        Assert.Null(store.Fetch(id));
    }

    [Fact]
    public void Remove_nonexistent_returns_false()
    {
        using var store = new InMemoryAiPayloadStore();
        Assert.False(store.Remove("nope"));
    }

    [Fact]
    public void Store_null_payload_throws()
    {
        using var store = new InMemoryAiPayloadStore();
        Assert.Throws<ArgumentNullException>(() => store.Store(null!));
    }

    #endregion

    #region AiToolRegistry

    [Fact]
    public void ToolRegistry_register_discovers_AiTool_methods()
    {
        var registry = new AiToolRegistry();
        registry.Register(new ToolProvider());

        Assert.Single(registry.Tools);
        Assert.Equal("Multiply", registry.Tools[0].Name);
    }

    [Fact]
    public void ToolRegistry_FindTool_returns_registered_tool()
    {
        var registry = new AiToolRegistry();
        registry.Register(new ToolProvider());

        var tool = registry.FindTool("Multiply");
        Assert.NotNull(tool);
        Assert.Equal("Multiply", tool!.Name);
    }

    [Fact]
    public void ToolRegistry_FindTool_case_insensitive()
    {
        var registry = new AiToolRegistry();
        registry.Register(new ToolProvider());

        Assert.NotNull(registry.FindTool("multiply"));
        Assert.NotNull(registry.FindTool("MULTIPLY"));
    }

    [Fact]
    public void ToolRegistry_FindTool_unknown_returns_null()
    {
        var registry = new AiToolRegistry();
        Assert.Null(registry.FindTool("nonexistent"));
    }

    [Fact]
    public void ToolRegistry_Register_null_throws()
    {
        var registry = new AiToolRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
    }

    [Fact]
    public void ToolRegistry_multiple_providers_accumulate()
    {
        var registry = new AiToolRegistry();
        registry.Register(new ToolProvider());
        registry.Register(new AnotherToolProvider());

        Assert.Equal(2, registry.Tools.Count);
    }

    #endregion

    #region FuloraAiServiceCollectionExtensions

    [Fact]
    public void AddFuloraAi_registers_all_core_services()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai => ai.AddChatClient("default", new DummyChatClient("X")));
        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<IAiProviderRegistry>());
        Assert.NotNull(sp.GetService<IAiPayloadStore>());
        Assert.NotNull(sp.GetService<IAiToolRegistry>());
        Assert.NotNull(sp.GetService<IAiBridgeService>());
        Assert.NotNull(sp.GetService<IAiConversationManager>());
    }

    [Fact]
    public void AddFuloraAi_null_configure_throws()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() => services.AddFuloraAi(null!));
    }

    [Fact]
    public void AddFuloraAi_resilience_enabled_registers_options()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", new DummyChatClient("X"));
            ai.AddResilience(opts => opts.MaxRetries = 5);
        });
        using var sp = services.BuildServiceProvider();

        var opts = sp.GetRequiredService<AiResilienceOptions>();
        Assert.Equal(5, opts.MaxRetries);
    }

    [Fact]
    public void AddFuloraAi_metering_enabled_registers_options()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", new DummyChatClient("X"));
            ai.AddMetering(opts => opts.PeriodBudgetTokens = 999);
        });
        using var sp = services.BuildServiceProvider();

        var opts = sp.GetRequiredService<AiMeteringOptions>();
        Assert.Equal(999, opts.PeriodBudgetTokens);
    }

    [Fact]
    public void AddFuloraAi_conversation_enabled_registers_custom_options()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", new DummyChatClient("X"));
            ai.AddConversation(opts => opts.DefaultMaxTokens = 2048);
        });
        using var sp = services.BuildServiceProvider();

        var opts = sp.GetRequiredService<AiConversationOptions>();
        Assert.Equal(2048, opts.DefaultMaxTokens);
    }

    [Fact]
    public void AddFuloraAi_without_conversation_still_registers_default_manager()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai => ai.AddChatClient("default", new DummyChatClient("X")));
        using var sp = services.BuildServiceProvider();

        var manager = sp.GetService<IAiConversationManager>();
        Assert.NotNull(manager);
    }

    [Fact]
    public void AddFuloraAi_tool_calling_enabled_registers_options()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", new DummyChatClient("X"));
            ai.AddToolCalling(opts => opts.MaxIterations = 3);
        });
        using var sp = services.BuildServiceProvider();

        var opts = sp.GetRequiredService<AiToolCallingOptions>();
        Assert.Equal(3, opts.MaxIterations);
    }

    [Fact]
    public void AddFuloraAi_without_tool_calling_does_not_register_options()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai => ai.AddChatClient("default", new DummyChatClient("X")));
        using var sp = services.BuildServiceProvider();

        var opts = sp.GetService<AiToolCallingOptions>();
        Assert.Null(opts);
    }

    [Fact]
    public void AddFuloraAi_bridge_is_transient()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", new DummyChatClient("X"));
            ai.AddConversation();
        });
        using var sp = services.BuildServiceProvider();

        var b1 = sp.GetRequiredService<IAiBridgeService>();
        var b2 = sp.GetRequiredService<IAiBridgeService>();
        Assert.NotSame(b1, b2);
    }

    #endregion

    #region Helpers

    private sealed class DummyChatClient(string id) : IChatClient
    {
        public string Id => id;
        public void Dispose() { }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, $"response-from-{Id}")));
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default)
            => AsyncEnumerable.Empty<ChatResponseUpdate>();
        public object? GetService(Type t, object? k = null) => null;
    }

    private sealed class DummyEmbeddingGenerator(string id) : IEmbeddingGenerator<string, Embedding<float>>
    {
        public string Id => id;
        public void Dispose() { }
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new GeneratedEmbeddings<Embedding<float>>());
        public object? GetService(Type t, object? k = null) => null;
        public EmbeddingGeneratorMetadata Metadata => new(Id);
    }

    private sealed class ToolProvider
    {
        [AiTool(Description = "Multiply two numbers")]
        public int Multiply(int a, int b) => a * b;
    }

    private sealed class AnotherToolProvider
    {
        [AiTool(Description = "Subtract two numbers")]
        public int Subtract(int a, int b) => a - b;
    }

    #endregion
}
