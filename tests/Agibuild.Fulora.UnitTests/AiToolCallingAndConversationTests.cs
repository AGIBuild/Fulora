using System.Runtime.CompilerServices;
using Agibuild.Fulora;
using Agibuild.Fulora.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class AiToolCallingAndConversationTests
{
    [Fact]
    public void AddToolCalling_registers_options_in_DI()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", new NullChatClient());
            ai.AddToolCalling(opts => opts.MaxIterations = 5);
        });
        using var sp = services.BuildServiceProvider();

        var opts = sp.GetRequiredService<IOptions<AiToolCallingOptions>>().Value;
        Assert.Equal(5, opts.MaxIterations);
    }

    [Fact]
    public void AddConversation_registers_manager_in_DI()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", new NullChatClient());
            ai.AddConversation(opts => opts.DefaultMaxTokens = 8192);
        });
        using var sp = services.BuildServiceProvider();

        var manager = sp.GetRequiredService<IAiConversationManager>();
        Assert.NotNull(manager);

        var opts = sp.GetRequiredService<IOptions<AiConversationOptions>>().Value;
        Assert.Equal(8192, opts.DefaultMaxTokens);
    }

    [Fact]
    public async Task RunWithTools_with_no_tools_behaves_like_complete()
    {
        var mockClient = new SimpleMock("hello from tools");
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", mockClient);
            ai.AddToolCalling();
        });
        using var sp = services.BuildServiceProvider();

        var bridge = sp.GetRequiredService<IAiBridgeService>();
        var result = await bridge.RunWithTools(new AiChatRequest { Message = "test" });

        Assert.Equal("hello from tools", result.Text);
    }

    [Fact]
    public async Task RunWithTools_executes_tool_call()
    {
        var mockClient = new ToolCallingMockClient();
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", mockClient);
            ai.AddToolCalling();
        });
        using var sp = services.BuildServiceProvider();

        var toolRegistry = sp.GetRequiredService<IAiToolRegistry>();
        toolRegistry.Register(new TestToolProvider());

        var bridge = sp.GetRequiredService<IAiBridgeService>();
        var result = await bridge.RunWithTools(new AiChatRequest { Message = "call add" });

        Assert.Contains("result", result.Text.ToLower());
    }

    [Fact]
    public async Task SendMessage_accumulates_conversation_history()
    {
        var callCount = 0;
        var mockClient = new CallCountingMock(() => $"response-{++callCount}");
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", mockClient);
            ai.AddConversation();
        });
        using var sp = services.BuildServiceProvider();

        var bridge = sp.GetRequiredService<IAiBridgeService>();
        var convId = await bridge.CreateConversation(
            new AiConversationCreateRequest { SystemPrompt = "You are helpful" });

        var r1 = await bridge.SendMessage(
            new AiConversationMessageRequest { ConversationId = convId, Message = "Hello" });
        Assert.Equal("response-1", r1.Text);

        var r2 = await bridge.SendMessage(
            new AiConversationMessageRequest { ConversationId = convId, Message = "How are you?" });
        Assert.Equal("response-2", r2.Text);

        var history = await bridge.GetHistory(convId);
        // system + user1 + assistant1 + user2 + assistant2 = 5
        Assert.Equal(5, history.Messages.Length);
        Assert.Equal("system", history.Messages[0].Role);
        Assert.Equal("user", history.Messages[1].Role);
        Assert.Equal("assistant", history.Messages[2].Role);
    }

    [Fact]
    public async Task StreamMessage_streams_and_adds_to_history()
    {
        var mockClient = new StreamingTokenMock(["token1", "token2", "token3"]);
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", mockClient);
            ai.AddConversation();
        });
        using var sp = services.BuildServiceProvider();

        var bridge = sp.GetRequiredService<IAiBridgeService>();
        var convId = await bridge.CreateConversation(new AiConversationCreateRequest());

        var tokens = new List<string>();
        await foreach (var t in bridge.StreamMessage(
            new AiConversationMessageRequest { ConversationId = convId, Message = "stream test" }, TestContext.Current.CancellationToken).WithCancellation(TestContext.Current.CancellationToken))
        {
            tokens.Add(t);
        }

        Assert.Equal(["token1", "token2", "token3"], tokens);

        var history = await bridge.GetHistory(convId);
        // user + assistant = 2
        Assert.Equal(2, history.Messages.Length);
        Assert.Equal("token1token2token3", history.Messages[1].Text);
    }

    [Fact]
    public async Task GetHistory_returns_messages()
    {
        var mockClient = new SimpleMock("ok");
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", mockClient);
            ai.AddConversation();
        });
        using var sp = services.BuildServiceProvider();

        var bridge = sp.GetRequiredService<IAiBridgeService>();
        var convId = await bridge.CreateConversation(
            new AiConversationCreateRequest { SystemPrompt = "sys" });

        await bridge.SendMessage(
            new AiConversationMessageRequest { ConversationId = convId, Message = "msg" });

        var history = await bridge.GetHistory(convId);
        Assert.Equal(convId, history.ConversationId);
        Assert.Equal(3, history.Messages.Length); // sys + user + assistant
    }

    [Fact]
    public async Task DeleteConversation_removes_session()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", new NullChatClient());
            ai.AddConversation();
        });
        using var sp = services.BuildServiceProvider();

        var bridge = sp.GetRequiredService<IAiBridgeService>();
        var convId = await bridge.CreateConversation(new AiConversationCreateRequest());

        await bridge.DeleteConversation(convId);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => bridge.GetHistory(convId));
    }

    [Fact]
    public async Task StreamWithTools_streams_tokens()
    {
        var mockClient = new StreamingTokenMock(["alpha", "beta"]);
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", mockClient);
            ai.AddToolCalling();
        });
        using var sp = services.BuildServiceProvider();

        var bridge = sp.GetRequiredService<IAiBridgeService>();
        var tokens = new List<string>();
        await foreach (var t in bridge.StreamWithTools(new AiChatRequest { Message = "stream" }, TestContext.Current.CancellationToken).WithCancellation(TestContext.Current.CancellationToken))
            tokens.Add(t);

        Assert.Equal(["alpha", "beta"], tokens);
    }

    [Fact]
    public async Task SendMessage_with_UseTools_uses_tool_calling_client()
    {
        var mockClient = new SimpleMock("tool-response");
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", mockClient);
            ai.AddToolCalling();
            ai.AddConversation();
        });
        using var sp = services.BuildServiceProvider();

        var bridge = sp.GetRequiredService<IAiBridgeService>();
        var convId = await bridge.CreateConversation(new AiConversationCreateRequest());
        var result = await bridge.SendMessage(new AiConversationMessageRequest
        {
            ConversationId = convId,
            Message = "call tool",
            UseTools = true
        });

        Assert.Equal("tool-response", result.Text);
    }

    [Fact]
    public async Task StreamMessage_with_UseTools_streams_and_records()
    {
        var mockClient = new StreamingTokenMock(["t1", "t2"]);
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", mockClient);
            ai.AddToolCalling();
            ai.AddConversation();
        });
        using var sp = services.BuildServiceProvider();

        var bridge = sp.GetRequiredService<IAiBridgeService>();
        var convId = await bridge.CreateConversation(new AiConversationCreateRequest());

        var tokens = new List<string>();
        await foreach (var t in bridge.StreamMessage(new AiConversationMessageRequest
        {
            ConversationId = convId,
            Message = "stream with tools",
            UseTools = true
        }, TestContext.Current.CancellationToken).WithCancellation(TestContext.Current.CancellationToken))
            tokens.Add(t);

        Assert.Equal(["t1", "t2"], tokens);

        var history = await bridge.GetHistory(convId);
        Assert.Equal(2, history.Messages.Length);
        Assert.Equal("t1t2", history.Messages[1].Text);
    }

    [Fact]
    public async Task CreateConversation_without_system_prompt_returns_id()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", new NullChatClient());
            ai.AddConversation();
        });
        using var sp = services.BuildServiceProvider();

        var bridge = sp.GetRequiredService<IAiBridgeService>();
        var convId = await bridge.CreateConversation(new AiConversationCreateRequest());

        Assert.NotNull(convId);
        Assert.NotEmpty(convId);

        var history = await bridge.GetHistory(convId);
        Assert.Empty(history.Messages);
    }

    [Fact]
    public async Task GetHistory_empty_conversation_with_system_prompt()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", new NullChatClient());
            ai.AddConversation();
        });
        using var sp = services.BuildServiceProvider();

        var bridge = sp.GetRequiredService<IAiBridgeService>();
        var convId = await bridge.CreateConversation(
            new AiConversationCreateRequest { SystemPrompt = "you are helpful" });

        var history = await bridge.GetHistory(convId);
        Assert.Single(history.Messages);
        Assert.Equal("system", history.Messages[0].Role);
        Assert.Equal("you are helpful", history.Messages[0].Text);
    }

    [Fact]
    public void Default_DI_registers_conversation_manager_without_AddConversation()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", new NullChatClient());
        });
        using var sp = services.BuildServiceProvider();

        var manager = sp.GetService<IAiConversationManager>();
        Assert.NotNull(manager);
    }

    [Fact]
    public void AddToolCalling_without_configure_uses_defaults()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", new NullChatClient());
            ai.AddToolCalling();
        });
        using var sp = services.BuildServiceProvider();

        var opts = sp.GetRequiredService<IOptions<AiToolCallingOptions>>().Value;
        Assert.Equal(10, opts.MaxIterations);
    }

    [Fact]
    public async Task RunWithTools_passes_system_prompt()
    {
        var captured = new List<ChatMessage>();
        var mockClient = new MessageCapturingMock(captured, "sys-response");
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", mockClient);
            ai.AddToolCalling();
        });
        using var sp = services.BuildServiceProvider();

        var bridge = sp.GetRequiredService<IAiBridgeService>();
        await bridge.RunWithTools(new AiChatRequest
        {
            Message = "hello",
            SystemPrompt = "be concise"
        });

        Assert.Equal(ChatRole.System, captured[0].Role);
        Assert.Equal("be concise", captured[0].Text);
    }

    [Fact]
    public async Task RunWithTools_passes_model_id()
    {
        ChatOptions? capturedOptions = null;
        var mockClient = new OptionsCapturingMock(opts => capturedOptions = opts, "ok");
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", mockClient);
            ai.AddToolCalling();
        });
        using var sp = services.BuildServiceProvider();

        var bridge = sp.GetRequiredService<IAiBridgeService>();
        await bridge.RunWithTools(new AiChatRequest
        {
            Message = "test",
            ModelId = "gpt-4o"
        });

        Assert.NotNull(capturedOptions);
        Assert.Equal("gpt-4o", capturedOptions!.ModelId);
    }

    // --- Test helpers ---

    private sealed class NullChatClient : IChatClient
    {
        public void Dispose() { }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "")));
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default) =>
            AsyncEnumerable.Empty<ChatResponseUpdate>();
        public object? GetService(Type t, object? k = null) => null;
    }

    private sealed class SimpleMock(string response) : IChatClient
    {
        public void Dispose() { }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default) =>
            AsyncEnumerable.Empty<ChatResponseUpdate>();
        public object? GetService(Type t, object? k = null) => null;
    }

    private sealed class CallCountingMock(Func<string> responseFactory) : IChatClient
    {
        public void Dispose() { }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseFactory())));
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default) =>
            AsyncEnumerable.Empty<ChatResponseUpdate>();
        public object? GetService(Type t, object? k = null) => null;
    }

    private sealed class StreamingTokenMock(string[] tokens) : IChatClient
    {
        public void Dispose() { }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Concat(tokens))));
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> m, ChatOptions? o = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var token in tokens)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, token);
                await Task.Yield();
            }
        }
        public object? GetService(Type t, object? k = null) => null;
    }

    private sealed class ToolCallingMockClient : IChatClient
    {
        private int _callCount;
        public void Dispose() { }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
        {
            var msgs = messages.ToList();
            if (_callCount++ == 0 && options?.Tools?.Count > 0)
            {
                // First call: return a function call
                var fc = new FunctionCallContent("call-1", "Add", new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2 });
                var msg = new ChatMessage(ChatRole.Assistant, [fc]);
                return Task.FromResult(new ChatResponse(msg));
            }

            // Subsequent call (after tool result): return text
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "The result is 3")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default) =>
            AsyncEnumerable.Empty<ChatResponseUpdate>();
        public object? GetService(Type t, object? k = null) => null;
    }

    private sealed class MessageCapturingMock(List<ChatMessage> captured, string response) : IChatClient
    {
        public void Dispose() { }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default)
        {
            captured.AddRange(m);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default) =>
            AsyncEnumerable.Empty<ChatResponseUpdate>();
        public object? GetService(Type t, object? k = null) => null;
    }

    private sealed class OptionsCapturingMock(Action<ChatOptions?> capture, string response) : IChatClient
    {
        public void Dispose() { }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default)
        {
            capture(o);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default) =>
            AsyncEnumerable.Empty<ChatResponseUpdate>();
        public object? GetService(Type t, object? k = null) => null;
    }

    private sealed class TestToolProvider
    {
        [AiTool(Description = "Adds two numbers")]
        public int Add(int a, int b) => a + b;
    }
}
