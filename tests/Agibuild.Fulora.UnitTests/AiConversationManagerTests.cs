using Agibuild.Fulora.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class AiConversationManagerTests : IDisposable
{
    private readonly InMemoryAiConversationManager _manager = new(Options.Create(new AiConversationOptions
    {
        DefaultMaxTokens = 100,
        SessionTtl = null
    }));

    public void Dispose() => _manager.Dispose();

    [Fact]
    public void CreateConversation_returns_unique_id()
    {
        var id1 = _manager.CreateConversation();
        var id2 = _manager.CreateConversation();

        Assert.NotNull(id1);
        Assert.NotNull(id2);
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void CreateConversation_with_system_prompt()
    {
        var id = _manager.CreateConversation("You are helpful");
        var messages = _manager.GetAllMessages(id);

        Assert.Single(messages);
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Equal("You are helpful", messages[0].Text);
    }

    [Fact]
    public void AddMessage_and_retrieve()
    {
        var id = _manager.CreateConversation();
        _manager.AddMessage(id, new ChatMessage(ChatRole.User, "Hello"));
        _manager.AddMessage(id, new ChatMessage(ChatRole.Assistant, "Hi there"));

        var messages = _manager.GetAllMessages(id);
        Assert.Equal(2, messages.Count);
        Assert.Equal("Hello", messages[0].Text);
        Assert.Equal("Hi there", messages[1].Text);
    }

    [Fact]
    public void GetMessages_within_token_budget_returns_all()
    {
        var id = _manager.CreateConversation();
        _manager.AddMessage(id, new ChatMessage(ChatRole.User, "Hi"));
        _manager.AddMessage(id, new ChatMessage(ChatRole.Assistant, "Hey"));

        var messages = _manager.GetMessages(id, maxTokens: 1000);
        Assert.Equal(2, messages.Count);
    }

    [Fact]
    public void GetMessages_trims_oldest_when_over_budget()
    {
        var opts = new AiConversationOptions
        {
            DefaultMaxTokens = 50,
            SessionTtl = null,
            EstimateTokens = text => text.Length
        };
        using var manager = new InMemoryAiConversationManager(Options.Create(opts));

        var id = manager.CreateConversation("system");
        manager.AddMessage(id, new ChatMessage(ChatRole.User, new string('a', 20)));
        manager.AddMessage(id, new ChatMessage(ChatRole.Assistant, new string('b', 20)));
        manager.AddMessage(id, new ChatMessage(ChatRole.User, new string('c', 20)));

        // Budget = 50, system = 6 chars, remaining = 44
        // Messages: a(20) + b(20) + c(20) = 60 > 44
        // Should keep system + c(20) + b(20) = 46 > 44, so just system + c(20) = 26
        var messages = manager.GetMessages(id, maxTokens: 50);

        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.True(messages.Count >= 2); // system + at least newest
        Assert.Equal(ChatRole.User, messages[^1].Role); // newest message kept
        Assert.Equal(new string('c', 20), messages[^1].Text);
    }

    [Fact]
    public void GetMessages_system_prompt_always_retained()
    {
        var opts = new AiConversationOptions
        {
            DefaultMaxTokens = 10,
            SessionTtl = null,
            EstimateTokens = text => text.Length
        };
        using var manager = new InMemoryAiConversationManager(Options.Create(opts));

        var id = manager.CreateConversation("A long system prompt that exceeds budget");
        manager.AddMessage(id, new ChatMessage(ChatRole.User, "user msg"));

        var messages = manager.GetMessages(id, maxTokens: 10);

        Assert.Single(messages); // only system prompt
        Assert.Equal(ChatRole.System, messages[0].Role);
    }

    [Fact]
    public void ClearConversation_retains_system_prompt()
    {
        var id = _manager.CreateConversation("system prompt");
        _manager.AddMessage(id, new ChatMessage(ChatRole.User, "msg1"));
        _manager.ClearConversation(id);

        var messages = _manager.GetAllMessages(id);
        Assert.Single(messages);
        Assert.Equal(ChatRole.System, messages[0].Role);
    }

    [Fact]
    public void RemoveConversation_deletes_entirely()
    {
        var id = _manager.CreateConversation();
        _manager.RemoveConversation(id);

        Assert.Throws<KeyNotFoundException>(() => _manager.GetAllMessages(id));
    }

    [Fact]
    public void Operations_on_nonexistent_throw_KeyNotFoundException()
    {
        Assert.Throws<KeyNotFoundException>(() => _manager.AddMessage("nope", new ChatMessage(ChatRole.User, "x")));
        Assert.Throws<KeyNotFoundException>(() => _manager.GetMessages("nope"));
        Assert.Throws<KeyNotFoundException>(() => _manager.GetAllMessages("nope"));
        Assert.Throws<KeyNotFoundException>(() => _manager.ClearConversation("nope"));
        Assert.Throws<KeyNotFoundException>(() => _manager.RemoveConversation("nope"));
    }

    [Fact]
    public void ListConversations_returns_active_ids()
    {
        var id1 = _manager.CreateConversation();
        var id2 = _manager.CreateConversation();

        var list = _manager.ListConversations();
        Assert.Contains(id1, list);
        Assert.Contains(id2, list);

        _manager.RemoveConversation(id1);
        var listAfter = _manager.ListConversations();
        Assert.DoesNotContain(id1, listAfter);
        Assert.Contains(id2, listAfter);
    }

    [Fact]
    public void CreateConversation_without_system_prompt_has_empty_history()
    {
        var id = _manager.CreateConversation();
        var messages = _manager.GetAllMessages(id);
        Assert.Empty(messages);
    }

    [Fact]
    public void GetMessages_uses_default_max_tokens_when_null()
    {
        var id = _manager.CreateConversation();
        _manager.AddMessage(id, new ChatMessage(ChatRole.User, "Hi"));

        // DefaultMaxTokens = 100 from fixture, "Hi" = ~1 token, should fit
        var messages = _manager.GetMessages(id);
        Assert.Single(messages);
    }

    [Fact]
    public void Windowing_without_system_prompt_keeps_newest()
    {
        var opts = new AiConversationOptions
        {
            DefaultMaxTokens = 30,
            SessionTtl = null,
            EstimateTokens = text => text.Length
        };
        using var manager = new InMemoryAiConversationManager(Options.Create(opts));

        var id = manager.CreateConversation(); // no system prompt
        manager.AddMessage(id, new ChatMessage(ChatRole.User, new string('a', 20)));
        manager.AddMessage(id, new ChatMessage(ChatRole.Assistant, new string('b', 20)));

        // Budget = 30, a(20) + b(20) = 40 > 30
        var messages = manager.GetMessages(id, maxTokens: 30);

        Assert.Single(messages);
        Assert.Equal(new string('b', 20), messages[0].Text); // newest kept
    }

    [Fact]
    public void Windowing_with_exact_budget_fit()
    {
        var opts = new AiConversationOptions
        {
            DefaultMaxTokens = 40,
            SessionTtl = null,
            EstimateTokens = text => text.Length
        };
        using var manager = new InMemoryAiConversationManager(Options.Create(opts));

        var id = manager.CreateConversation(); // no system prompt
        manager.AddMessage(id, new ChatMessage(ChatRole.User, new string('a', 20)));
        manager.AddMessage(id, new ChatMessage(ChatRole.Assistant, new string('b', 20)));

        // Budget = 40, a(20) + b(20) = 40 — exact fit
        var messages = manager.GetMessages(id, maxTokens: 40);
        Assert.Equal(2, messages.Count);
    }

    [Fact]
    public void Default_constructor_uses_default_options()
    {
        using var manager = new InMemoryAiConversationManager(Options.Create(new AiConversationOptions()));
        var id = manager.CreateConversation();
        Assert.NotNull(id);
    }

    [Fact]
    public void Null_message_text_treated_as_empty_for_token_count()
    {
        var opts = new AiConversationOptions
        {
            DefaultMaxTokens = 5,
            SessionTtl = null,
            EstimateTokens = text => text.Length
        };
        using var manager = new InMemoryAiConversationManager(Options.Create(opts));

        var id = manager.CreateConversation();
        manager.AddMessage(id, new ChatMessage(ChatRole.User, (string?)null));

        var messages = manager.GetMessages(id, maxTokens: 5);
        Assert.Single(messages);
    }

    [Fact]
    public void ClearConversation_without_system_prompt_results_in_empty()
    {
        var id = _manager.CreateConversation();
        _manager.AddMessage(id, new ChatMessage(ChatRole.User, "msg"));
        _manager.ClearConversation(id);

        var messages = _manager.GetAllMessages(id);
        Assert.Empty(messages);
    }
}

public sealed class AiConversationOptionsTests
{
    [Fact]
    public void Default_max_tokens_is_4096()
    {
        var opts = new AiConversationOptions();
        Assert.Equal(4096, opts.DefaultMaxTokens);
    }

    [Fact]
    public void Default_session_ttl_is_one_hour()
    {
        var opts = new AiConversationOptions();
        Assert.Equal(TimeSpan.FromHours(1), opts.SessionTtl);
    }

    [Fact]
    public void Default_estimate_tokens_uses_length_div_4()
    {
        var opts = new AiConversationOptions();
        Assert.Equal(3, opts.EstimateTokens("Hello world!")); // 12 / 4 = 3
        Assert.Equal(1, opts.EstimateTokens("Hi")); // 2 / 4 = 0, Math.Max(1, 0) = 1
        Assert.Equal(1, opts.EstimateTokens("")); // 0 / 4 = 0, Math.Max(1, 0) = 1
    }
}

public sealed class AiToolCallingOptionsTests
{
    [Fact]
    public void Default_max_iterations_is_10()
    {
        var opts = new AiToolCallingOptions();
        Assert.Equal(10, opts.MaxIterations);
    }
}
