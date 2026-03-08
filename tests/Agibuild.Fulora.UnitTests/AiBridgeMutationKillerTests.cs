using System.Runtime.CompilerServices;
using Agibuild.Fulora.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

/// <summary>
/// High-assertion-strength tests designed to kill Stryker mutations in AiBridgeService.
/// Each test captures exact arguments passed to mocks and verifies exact output fields.
/// </summary>
public sealed class AiBridgeMutationKillerTests
{
    #region Complete

    [Fact]
    public async Task Complete_without_system_prompt_sends_only_user_message()
    {
        var (bridge, mock) = CreateBridge();
        await bridge.Complete(new AiChatRequest { Message = "hello" });

        Assert.Single(mock.CapturedMessages);
        Assert.Equal(ChatRole.User, mock.CapturedMessages[0].Role);
        Assert.Equal("hello", mock.CapturedMessages[0].Text);
    }

    [Fact]
    public async Task Complete_with_system_prompt_sends_system_then_user()
    {
        var (bridge, mock) = CreateBridge();
        await bridge.Complete(new AiChatRequest { Message = "hi", SystemPrompt = "be brief" });

        Assert.Equal(2, mock.CapturedMessages.Count);
        Assert.Equal(ChatRole.System, mock.CapturedMessages[0].Role);
        Assert.Equal("be brief", mock.CapturedMessages[0].Text);
        Assert.Equal(ChatRole.User, mock.CapturedMessages[1].Role);
        Assert.Equal("hi", mock.CapturedMessages[1].Text);
    }

    [Fact]
    public async Task Complete_without_model_id_passes_null_options()
    {
        var (bridge, mock) = CreateBridge();
        await bridge.Complete(new AiChatRequest { Message = "test" });

        Assert.Null(mock.CapturedOptions);
    }

    [Fact]
    public async Task Complete_with_model_id_passes_options_with_model()
    {
        var (bridge, mock) = CreateBridge();
        await bridge.Complete(new AiChatRequest { Message = "test", ModelId = "gpt-4" });

        Assert.NotNull(mock.CapturedOptions);
        Assert.Equal("gpt-4", mock.CapturedOptions!.ModelId);
    }

    [Fact]
    public async Task Complete_maps_all_result_fields()
    {
        var usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 20 };
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer"))
        {
            ModelId = "model-x",
            Usage = usage
        };
        var (bridge, _) = CreateBridge(response);
        var result = await bridge.Complete(new AiChatRequest { Message = "q" });

        Assert.Equal("answer", result.Text);
        Assert.Equal("model-x", result.ModelId);
        Assert.Equal(10, result.PromptTokens);
        Assert.Equal(20, result.CompletionTokens);
    }

    [Fact]
    public async Task Complete_null_response_text_maps_to_empty_string()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, (string?)null));
        var (bridge, _) = CreateBridge(response);
        var result = await bridge.Complete(new AiChatRequest { Message = "q" });

        Assert.Equal("", result.Text);
    }

    [Fact]
    public async Task Complete_with_named_provider_resolves_correct_client()
    {
        var mockA = new CapturingMock(new ChatResponse(new ChatMessage(ChatRole.Assistant, "from-a")));
        var mockB = new CapturingMock(new ChatResponse(new ChatMessage(ChatRole.Assistant, "from-b")));

        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("providerA", mockA);
            ai.AddChatClient("providerB", mockB);
            ai.AddConversation();
        });
        using var sp = services.BuildServiceProvider();
        var bridge = sp.GetRequiredService<IAiBridgeService>();

        var resultB = await bridge.Complete(new AiChatRequest { Message = "x", Provider = "providerB" });
        Assert.Equal("from-b", resultB.Text);
        Assert.NotEmpty(mockB.CapturedMessages);
        Assert.Empty(mockA.CapturedMessages);
    }

    #endregion

    #region CompleteTyped

    [Fact]
    public async Task CompleteTyped_passes_schema_in_response_format()
    {
        var (bridge, mock) = CreateBridge();
        await bridge.CompleteTyped(new AiTypedChatRequest
        {
            Message = "extract",
            JsonSchema = """{"type":"object","properties":{"name":{"type":"string"}}}"""
        });

        Assert.NotNull(mock.CapturedOptions);
        Assert.NotNull(mock.CapturedOptions!.ResponseFormat);
    }

    [Fact]
    public async Task CompleteTyped_returns_response_text()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"name":"test"}"""));
        var (bridge, _) = CreateBridge(response);
        var result = await bridge.CompleteTyped(new AiTypedChatRequest
        {
            Message = "extract",
            JsonSchema = """{"type":"object"}"""
        });

        Assert.Equal("""{"name":"test"}""", result);
    }

    #endregion

    #region ListProviders

    [Fact]
    public async Task ListProviders_returns_registered_names()
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("alpha", new CapturingMock());
            ai.AddChatClient("beta", new CapturingMock());
            ai.AddConversation();
        });
        using var sp = services.BuildServiceProvider();
        var bridge = sp.GetRequiredService<IAiBridgeService>();

        var providers = await bridge.ListProviders();
        Assert.Contains("alpha", providers);
        Assert.Contains("beta", providers);
        Assert.Equal(2, providers.Length);
    }

    [Fact]
    public async Task ListProviders_single_provider_returns_array_with_one()
    {
        var (bridge, _) = CreateBridge();
        var providers = await bridge.ListProviders();

        Assert.NotNull(providers);
        Assert.Single(providers);
        Assert.Equal("default", providers[0]);
    }

    #endregion

    #region UploadBlob / FetchBlob

    [Fact]
    public async Task UploadBlob_stores_and_returns_id()
    {
        var (bridge, _) = CreateBridge();
        var data = Convert.ToBase64String([1, 2, 3]);
        var blobId = await bridge.UploadBlob(data, "application/octet-stream", "test.bin");
        Assert.NotNull(blobId);
        Assert.NotEmpty(blobId);
    }

    [Fact]
    public async Task UploadBlob_returned_id_is_fetchable()
    {
        var (bridge, _) = CreateBridge();
        var originalBytes = new byte[] { 10, 20, 30 };
        var blobId = await bridge.UploadBlob(Convert.ToBase64String(originalBytes), "image/png", "img.png");

        Assert.NotNull(blobId);
        var fetched = await bridge.FetchBlob(blobId);
        Assert.NotNull(fetched);
        Assert.Equal(originalBytes, Convert.FromBase64String(fetched!));
    }

    [Fact]
    public async Task FetchBlob_returns_stored_data()
    {
        var (bridge, _) = CreateBridge();
        var originalData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var blobId = await bridge.UploadBlob(Convert.ToBase64String(originalData), "application/octet-stream", null);
        var fetched = await bridge.FetchBlob(blobId);
        Assert.NotNull(fetched);
        Assert.Equal(originalData, Convert.FromBase64String(fetched!));
    }

    [Fact]
    public async Task FetchBlob_returns_null_for_missing()
    {
        var (bridge, _) = CreateBridge();
        var result = await bridge.FetchBlob("nonexistent");
        Assert.Null(result);
    }

    #endregion

    #region StreamCompletion

    [Fact]
    public async Task StreamCompletion_yields_non_empty_tokens_only()
    {
        var updates = new[]
        {
            new ChatResponseUpdate(ChatRole.Assistant, "hello"),
            new ChatResponseUpdate(ChatRole.Assistant, ""),
            new ChatResponseUpdate(ChatRole.Assistant, " world"),
            new ChatResponseUpdate(ChatRole.Assistant, (string?)null),
        };
        var mock = new StreamingCapturingMock(updates);
        var bridge = CreateBridgeFromMock(mock);

        var tokens = new List<string>();
        await foreach (var t in bridge.StreamCompletion(new AiChatRequest { Message = "test" }))
            tokens.Add(t);

        Assert.Equal(2, tokens.Count);
        Assert.Equal("hello", tokens[0]);
        Assert.Equal(" world", tokens[1]);
    }

    [Fact]
    public async Task StreamCompletion_passes_system_prompt_and_model_id()
    {
        var mock = new StreamingCapturingMock([new ChatResponseUpdate(ChatRole.Assistant, "ok")]);
        var bridge = CreateBridgeFromMock(mock);

        await foreach (var _ in bridge.StreamCompletion(
            new AiChatRequest { Message = "hi", SystemPrompt = "sys", ModelId = "m1" }))
        { }

        Assert.Equal(2, mock.CapturedMessages.Count);
        Assert.Equal(ChatRole.System, mock.CapturedMessages[0].Role);
        Assert.Equal("sys", mock.CapturedMessages[0].Text);
        Assert.NotNull(mock.CapturedOptions);
        Assert.Equal("m1", mock.CapturedOptions!.ModelId);
    }

    #endregion

    #region RunWithTools

    [Fact]
    public async Task RunWithTools_passes_tools_in_options()
    {
        var mock = new CapturingMock();
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", mock);
            ai.AddToolCalling();
            ai.AddConversation();
        });
        using var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<IAiToolRegistry>();
        registry.Register(new TestTools());

        var bridge = sp.GetRequiredService<IAiBridgeService>();
        await bridge.RunWithTools(new AiChatRequest { Message = "test" });

        Assert.NotNull(mock.CapturedOptions);
        Assert.NotNull(mock.CapturedOptions!.Tools);
        Assert.NotEmpty(mock.CapturedOptions.Tools!);
    }

    [Fact]
    public async Task RunWithTools_without_tools_sends_empty_tools_options()
    {
        var mock = new CapturingMock();
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", mock);
            ai.AddToolCalling();
            ai.AddConversation();
        });
        using var sp = services.BuildServiceProvider();
        var bridge = sp.GetRequiredService<IAiBridgeService>();

        await bridge.RunWithTools(new AiChatRequest { Message = "test" });

        Assert.NotNull(mock.CapturedOptions);
        Assert.Null(mock.CapturedOptions!.Tools);
    }

    #endregion

    #region StreamWithTools

    [Fact]
    public async Task StreamWithTools_filters_empty_tokens()
    {
        var updates = new[]
        {
            new ChatResponseUpdate(ChatRole.Assistant, "x"),
            new ChatResponseUpdate(ChatRole.Assistant, ""),
            new ChatResponseUpdate(ChatRole.Assistant, "y"),
        };
        var mock = new StreamingCapturingMock(updates);
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", mock);
            ai.AddToolCalling();
            ai.AddConversation();
        });
        using var sp = services.BuildServiceProvider();
        var bridge = sp.GetRequiredService<IAiBridgeService>();

        var tokens = new List<string>();
        await foreach (var t in bridge.StreamWithTools(new AiChatRequest { Message = "test" }))
            tokens.Add(t);

        Assert.Equal(["x", "y"], tokens);
    }

    #endregion

    #region Conversation: CreateConversation

    [Fact]
    public async Task CreateConversation_returns_non_null_non_empty_id()
    {
        var (bridge, _) = CreateBridge();
        var convId = await bridge.CreateConversation(new AiConversationCreateRequest());

        Assert.NotNull(convId);
        Assert.NotEmpty(convId);
        Assert.Equal(12, convId.Length);
    }

    [Fact]
    public async Task CreateConversation_passes_system_prompt_to_manager()
    {
        var (bridge, _) = CreateBridge();
        var convId = await bridge.CreateConversation(
            new AiConversationCreateRequest { SystemPrompt = "you are helpful" });

        var history = await bridge.GetHistory(convId);
        Assert.Single(history.Messages);
        Assert.Equal("system", history.Messages[0].Role);
        Assert.Equal("you are helpful", history.Messages[0].Text);
    }

    [Fact]
    public async Task CreateConversation_null_system_prompt_creates_empty()
    {
        var (bridge, _) = CreateBridge();
        var convId = await bridge.CreateConversation(new AiConversationCreateRequest());

        var history = await bridge.GetHistory(convId);
        Assert.Empty(history.Messages);
    }

    #endregion

    #region Conversation: SendMessage

    [Fact]
    public async Task SendMessage_adds_user_message_before_calling_client()
    {
        var mock = new CapturingMock(new ChatResponse(new ChatMessage(ChatRole.Assistant, "reply")));
        var bridge = CreateBridgeWithConversation(mock);

        var convId = await bridge.CreateConversation(
            new AiConversationCreateRequest { SystemPrompt = "sys" });
        await bridge.SendMessage(new AiConversationMessageRequest
        {
            ConversationId = convId,
            Message = "user-msg"
        });

        // Verify messages passed to client: system + user
        Assert.Equal(2, mock.CapturedMessages.Count);
        Assert.Equal(ChatRole.System, mock.CapturedMessages[0].Role);
        Assert.Equal("sys", mock.CapturedMessages[0].Text);
        Assert.Equal(ChatRole.User, mock.CapturedMessages[1].Role);
        Assert.Equal("user-msg", mock.CapturedMessages[1].Text);
    }

    [Fact]
    public async Task SendMessage_adds_assistant_response_to_history()
    {
        var mock = new CapturingMock(new ChatResponse(new ChatMessage(ChatRole.Assistant, "reply-text")));
        var bridge = CreateBridgeWithConversation(mock);

        var convId = await bridge.CreateConversation(new AiConversationCreateRequest());
        await bridge.SendMessage(new AiConversationMessageRequest
        {
            ConversationId = convId,
            Message = "user-msg"
        });

        var history = await bridge.GetHistory(convId);
        Assert.Equal(2, history.Messages.Length);
        Assert.Equal("user", history.Messages[0].Role);
        Assert.Equal("user-msg", history.Messages[0].Text);
        Assert.Equal("assistant", history.Messages[1].Role);
        Assert.Equal("reply-text", history.Messages[1].Text);
    }

    [Fact]
    public async Task SendMessage_null_response_text_stores_empty_string()
    {
        var mock = new CapturingMock(new ChatResponse(new ChatMessage(ChatRole.Assistant, (string?)null)));
        var bridge = CreateBridgeWithConversation(mock);

        var convId = await bridge.CreateConversation(new AiConversationCreateRequest());
        await bridge.SendMessage(new AiConversationMessageRequest
        {
            ConversationId = convId,
            Message = "msg"
        });

        var history = await bridge.GetHistory(convId);
        Assert.Equal("", history.Messages[1].Text);
    }

    [Fact]
    public async Task SendMessage_UseTools_false_does_not_pass_tools()
    {
        var mock = new CapturingMock(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", mock);
            ai.AddToolCalling();
            ai.AddConversation();
        });
        using var sp = services.BuildServiceProvider();
        var bridge = sp.GetRequiredService<IAiBridgeService>();

        var convId = await bridge.CreateConversation(new AiConversationCreateRequest());
        await bridge.SendMessage(new AiConversationMessageRequest
        {
            ConversationId = convId,
            Message = "test",
            UseTools = false
        });

        Assert.Null(mock.CapturedOptions);
    }

    [Fact]
    public async Task SendMessage_UseTools_true_passes_tool_options()
    {
        var mock = new CapturingMock(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", mock);
            ai.AddToolCalling();
            ai.AddConversation();
        });
        using var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<IAiToolRegistry>();
        registry.Register(new TestTools());
        var bridge = sp.GetRequiredService<IAiBridgeService>();

        var convId = await bridge.CreateConversation(new AiConversationCreateRequest());
        await bridge.SendMessage(new AiConversationMessageRequest
        {
            ConversationId = convId,
            Message = "test",
            UseTools = true
        });

        Assert.NotNull(mock.CapturedOptions);
        Assert.NotNull(mock.CapturedOptions!.Tools);
        Assert.NotEmpty(mock.CapturedOptions.Tools!);
    }

    [Fact]
    public async Task SendMessage_UseTools_true_with_modelId_passes_model_in_tool_options()
    {
        var mock = new CapturingMock(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", mock);
            ai.AddToolCalling();
            ai.AddConversation();
        });
        using var sp = services.BuildServiceProvider();
        var bridge = sp.GetRequiredService<IAiBridgeService>();

        var convId = await bridge.CreateConversation(new AiConversationCreateRequest());
        await bridge.SendMessage(new AiConversationMessageRequest
        {
            ConversationId = convId,
            Message = "test",
            UseTools = true,
            ModelId = "gpt-4"
        });

        Assert.NotNull(mock.CapturedOptions);
        Assert.Equal("gpt-4", mock.CapturedOptions!.ModelId);
    }

    [Fact]
    public async Task SendMessage_UseTools_true_invokes_function_calls()
    {
        var callCount = 0;
        var mock = new FunctionCallCountingMock(() =>
        {
            callCount++;
            if (callCount == 1)
            {
                return new ChatResponse([
                    new ChatMessage(ChatRole.Assistant, [
                        new Microsoft.Extensions.AI.FunctionCallContent("call-1", "Add",
                            new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2 })
                    ])
                ]);
            }
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "result: 3"));
        });

        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", mock);
            ai.AddToolCalling();
            ai.AddConversation();
        });
        using var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<IAiToolRegistry>();
        registry.Register(new TestTools());
        var bridge = sp.GetRequiredService<IAiBridgeService>();

        var convId = await bridge.CreateConversation(new AiConversationCreateRequest());
        var result = await bridge.SendMessage(new AiConversationMessageRequest
        {
            ConversationId = convId,
            Message = "what is 1+2?",
            UseTools = true
        });

        Assert.True(callCount >= 2, $"Expected at least 2 calls (function invocation), got {callCount}");
        Assert.Equal("result: 3", result.Text);
    }

    [Fact]
    public async Task SendMessage_UseTools_false_does_not_invoke_functions()
    {
        var callCount = 0;
        var mock = new FunctionCallCountingMock(() =>
        {
            callCount++;
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "plain response"));
        });

        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", mock);
            ai.AddToolCalling();
            ai.AddConversation();
        });
        using var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<IAiToolRegistry>();
        registry.Register(new TestTools());
        var bridge = sp.GetRequiredService<IAiBridgeService>();

        var convId = await bridge.CreateConversation(new AiConversationCreateRequest());
        await bridge.SendMessage(new AiConversationMessageRequest
        {
            ConversationId = convId,
            Message = "test",
            UseTools = false
        });

        Assert.Equal(1, callCount);
    }

    #endregion

    #region Conversation: StreamMessage

    [Fact]
    public async Task StreamMessage_accumulates_and_stores_full_response()
    {
        var updates = new[]
        {
            new ChatResponseUpdate(ChatRole.Assistant, "part1"),
            new ChatResponseUpdate(ChatRole.Assistant, "part2"),
        };
        var mock = new StreamingCapturingMock(updates);
        var bridge = CreateBridgeWithConversationStreaming(mock);

        var convId = await bridge.CreateConversation(new AiConversationCreateRequest());

        var tokens = new List<string>();
        await foreach (var t in bridge.StreamMessage(new AiConversationMessageRequest
        {
            ConversationId = convId,
            Message = "msg"
        }))
            tokens.Add(t);

        Assert.Equal(["part1", "part2"], tokens);

        var history = await bridge.GetHistory(convId);
        Assert.Equal(2, history.Messages.Length);
        Assert.Equal("user", history.Messages[0].Role);
        Assert.Equal("msg", history.Messages[0].Text);
        Assert.Equal("assistant", history.Messages[1].Role);
        Assert.Equal("part1part2", history.Messages[1].Text);
    }

    [Fact]
    public async Task StreamMessage_filters_empty_tokens_but_stores_full()
    {
        var updates = new[]
        {
            new ChatResponseUpdate(ChatRole.Assistant, "a"),
            new ChatResponseUpdate(ChatRole.Assistant, ""),
            new ChatResponseUpdate(ChatRole.Assistant, "b"),
        };
        var mock = new StreamingCapturingMock(updates);
        var bridge = CreateBridgeWithConversationStreaming(mock);

        var convId = await bridge.CreateConversation(new AiConversationCreateRequest());

        var tokens = new List<string>();
        await foreach (var t in bridge.StreamMessage(new AiConversationMessageRequest
        {
            ConversationId = convId,
            Message = "m"
        }))
            tokens.Add(t);

        Assert.Equal(["a", "b"], tokens);

        var history = await bridge.GetHistory(convId);
        Assert.Equal("ab", history.Messages[1].Text);
    }

    [Fact]
    public async Task StreamMessage_UseTools_true_passes_tool_options()
    {
        var updates = new[] { new ChatResponseUpdate(ChatRole.Assistant, "ok") };
        var mock = new StreamingCapturingMock(updates);
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", mock);
            ai.AddToolCalling();
            ai.AddConversation();
        });
        using var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<IAiToolRegistry>();
        registry.Register(new TestTools());
        var bridge = sp.GetRequiredService<IAiBridgeService>();

        var convId = await bridge.CreateConversation(new AiConversationCreateRequest());
        await foreach (var _ in bridge.StreamMessage(new AiConversationMessageRequest
        {
            ConversationId = convId,
            Message = "test",
            UseTools = true
        }))
        { }

        Assert.NotNull(mock.CapturedOptions);
        Assert.NotNull(mock.CapturedOptions!.Tools);
        Assert.NotEmpty(mock.CapturedOptions.Tools!);
    }

    [Fact]
    public async Task StreamMessage_UseTools_false_passes_null_options()
    {
        var updates = new[] { new ChatResponseUpdate(ChatRole.Assistant, "ok") };
        var mock = new StreamingCapturingMock(updates);
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", mock);
            ai.AddToolCalling();
            ai.AddConversation();
        });
        using var sp = services.BuildServiceProvider();
        var bridge = sp.GetRequiredService<IAiBridgeService>();

        var convId = await bridge.CreateConversation(new AiConversationCreateRequest());
        await foreach (var _ in bridge.StreamMessage(new AiConversationMessageRequest
        {
            ConversationId = convId,
            Message = "test",
            UseTools = false
        }))
        { }

        Assert.Null(mock.CapturedOptions);
    }

    [Fact]
    public async Task StreamMessage_UseTools_true_with_modelId_passes_model_in_options()
    {
        var updates = new[] { new ChatResponseUpdate(ChatRole.Assistant, "ok") };
        var mock = new StreamingCapturingMock(updates);
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", mock);
            ai.AddToolCalling();
            ai.AddConversation();
        });
        using var sp = services.BuildServiceProvider();
        var bridge = sp.GetRequiredService<IAiBridgeService>();

        var convId = await bridge.CreateConversation(new AiConversationCreateRequest());
        await foreach (var _ in bridge.StreamMessage(new AiConversationMessageRequest
        {
            ConversationId = convId,
            Message = "test",
            UseTools = true,
            ModelId = "model-x"
        }))
        { }

        Assert.NotNull(mock.CapturedOptions);
        Assert.Equal("model-x", mock.CapturedOptions!.ModelId);
    }

    [Fact]
    public async Task StreamMessage_UseTools_true_invokes_function_calls()
    {
        var callCount = 0;
        var mock = new FunctionCallCountingMock(() =>
        {
            callCount++;
            if (callCount == 1)
            {
                return new ChatResponse([
                    new ChatMessage(ChatRole.Assistant, [
                        new Microsoft.Extensions.AI.FunctionCallContent("call-1", "Add",
                            new Dictionary<string, object?> { ["a"] = 5, ["b"] = 3 })
                    ])
                ]);
            }
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "8"));
        });

        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", mock);
            ai.AddToolCalling();
            ai.AddConversation();
        });
        using var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<IAiToolRegistry>();
        registry.Register(new TestTools());
        var bridge = sp.GetRequiredService<IAiBridgeService>();

        var convId = await bridge.CreateConversation(new AiConversationCreateRequest());
        var tokens = new List<string>();
        await foreach (var t in bridge.StreamMessage(new AiConversationMessageRequest
        {
            ConversationId = convId,
            Message = "5+3?",
            UseTools = true
        }))
            tokens.Add(t);

        Assert.True(callCount >= 2, $"Expected at least 2 calls (function invocation), got {callCount}");
    }

    [Fact]
    public async Task StreamMessage_UseTools_false_does_not_invoke_functions()
    {
        var callCount = 0;
        var streamUpdates = new[] { new ChatResponseUpdate(ChatRole.Assistant, "plain") };
        var mock = new StreamingCountingMock(streamUpdates, () => callCount++);

        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", mock);
            ai.AddToolCalling();
            ai.AddConversation();
        });
        using var sp = services.BuildServiceProvider();
        var bridge = sp.GetRequiredService<IAiBridgeService>();

        var convId = await bridge.CreateConversation(new AiConversationCreateRequest());
        await foreach (var _ in bridge.StreamMessage(new AiConversationMessageRequest
        {
            ConversationId = convId,
            Message = "test",
            UseTools = false
        }))
        { }

        Assert.Equal(1, callCount);
    }

    #endregion

    #region GetHistory

    [Fact]
    public async Task GetHistory_maps_role_and_text_correctly()
    {
        var mock = new CapturingMock(new ChatResponse(new ChatMessage(ChatRole.Assistant, "resp")));
        var bridge = CreateBridgeWithConversation(mock);

        var convId = await bridge.CreateConversation(
            new AiConversationCreateRequest { SystemPrompt = "sys" });
        await bridge.SendMessage(new AiConversationMessageRequest
        {
            ConversationId = convId,
            Message = "hello"
        });

        var history = await bridge.GetHistory(convId);
        Assert.Equal(convId, history.ConversationId);
        Assert.Equal(3, history.Messages.Length);

        Assert.Equal("system", history.Messages[0].Role);
        Assert.Equal("sys", history.Messages[0].Text);
        Assert.Equal("user", history.Messages[1].Role);
        Assert.Equal("hello", history.Messages[1].Text);
        Assert.Equal("assistant", history.Messages[2].Role);
        Assert.Equal("resp", history.Messages[2].Text);
    }

    [Fact]
    public async Task GetHistory_null_message_text_maps_to_empty()
    {
        var mock = new CapturingMock(new ChatResponse(new ChatMessage(ChatRole.Assistant, (string?)null)));
        var bridge = CreateBridgeWithConversation(mock);

        var convId = await bridge.CreateConversation(new AiConversationCreateRequest());
        await bridge.SendMessage(new AiConversationMessageRequest
        {
            ConversationId = convId,
            Message = "x"
        });

        var history = await bridge.GetHistory(convId);
        Assert.Equal("", history.Messages[^1].Text);
    }

    #endregion

    #region DeleteConversation

    [Fact]
    public async Task DeleteConversation_prevents_further_GetHistory()
    {
        var (bridge, _) = CreateBridge();
        var convId = await bridge.CreateConversation(new AiConversationCreateRequest());
        await bridge.DeleteConversation(convId);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => bridge.GetHistory(convId));
    }

    [Fact]
    public async Task DeleteConversation_nonexistent_throws()
    {
        var (bridge, _) = CreateBridge();
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => bridge.DeleteConversation("nonexistent-id"));
    }

    #endregion

    #region Helpers

    private static (IAiBridgeService bridge, CapturingMock mock) CreateBridge(ChatResponse? response = null)
    {
        var mock = new CapturingMock(response);
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", mock);
            ai.AddConversation();
        });
        var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<IAiBridgeService>(), mock);
    }

    private static IAiBridgeService CreateBridgeFromMock(StreamingCapturingMock mock)
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", mock);
            ai.AddConversation();
        });
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IAiBridgeService>();
    }

    private static IAiBridgeService CreateBridgeWithConversation(CapturingMock mock)
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", mock);
            ai.AddConversation();
        });
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IAiBridgeService>();
    }

    private static IAiBridgeService CreateBridgeWithConversationStreaming(StreamingCapturingMock mock)
    {
        var services = new ServiceCollection();
        services.AddFuloraAi(ai =>
        {
            ai.AddChatClient("default", mock);
            ai.AddConversation();
        });
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IAiBridgeService>();
    }

    private sealed class CapturingMock : IChatClient
    {
        private readonly ChatResponse _response;
        public List<ChatMessage> CapturedMessages { get; } = [];
        public ChatOptions? CapturedOptions { get; private set; }

        public CapturingMock(ChatResponse? response = null) =>
            _response = response ?? new ChatResponse(new ChatMessage(ChatRole.Assistant, "default-response"));

        public void Dispose() { }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
        {
            CapturedMessages.AddRange(messages);
            CapturedOptions = options;
            return Task.FromResult(_response);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default)
            => AsyncEnumerable.Empty<ChatResponseUpdate>();

        public object? GetService(Type t, object? k = null) => null;
    }

    private sealed class StreamingCapturingMock : IChatClient
    {
        private readonly ChatResponseUpdate[] _updates;
        public List<ChatMessage> CapturedMessages { get; } = [];
        public ChatOptions? CapturedOptions { get; private set; }

        public StreamingCapturingMock(ChatResponseUpdate[] updates) => _updates = updates;

        public void Dispose() { }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Concat(_updates.Select(u => u.Text)))));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            CapturedMessages.AddRange(messages);
            CapturedOptions = options;
            foreach (var u in _updates)
            {
                yield return u;
                await Task.Yield();
            }
        }

        public object? GetService(Type t, object? k = null) => null;
    }

    private sealed class FunctionCallCountingMock : IChatClient
    {
        private readonly Func<ChatResponse> _responseFactory;

        public FunctionCallCountingMock(Func<ChatResponse> responseFactory)
            => _responseFactory = responseFactory;

        public void Dispose() { }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(_responseFactory());

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var response = _responseFactory();
            foreach (var msg in response.Messages)
            {
                var update = new ChatResponseUpdate { Role = msg.Role };
                foreach (var content in msg.Contents)
                    update.Contents.Add(content);
                yield return update;
                await Task.Yield();
            }
        }

        public object? GetService(Type t, object? k = null) => null;
    }

    private sealed class StreamingCountingMock : IChatClient
    {
        private readonly ChatResponseUpdate[] _updates;
        private readonly Action _onStreamCall;

        public StreamingCountingMock(ChatResponseUpdate[] updates, Action onStreamCall)
        {
            _updates = updates;
            _onStreamCall = onStreamCall;
        }

        public void Dispose() { }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "default")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            _onStreamCall();
            foreach (var u in _updates)
            {
                yield return u;
                await Task.Yield();
            }
        }

        public object? GetService(Type t, object? k = null) => null;
    }

    private sealed class TestTools
    {
        [AiTool(Description = "Add two numbers")]
        public int Add(int a, int b) => a + b;
    }

    #endregion
}
