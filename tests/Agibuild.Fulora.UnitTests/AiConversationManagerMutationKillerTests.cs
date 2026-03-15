using Agibuild.Fulora.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

/// <summary>
/// High-assertion-strength tests to kill Stryker mutations in InMemoryAiConversationManager.
/// </summary>
public sealed class AiConversationManagerMutationKillerTests
{
    #region CreateConversation

    [Fact]
    public void CreateConversation_returns_12_char_hex_string()
    {
        using var manager = new InMemoryAiConversationManager(Options.Create(new AiConversationOptions { SessionTtl = null }));
        var id = manager.CreateConversation();

        Assert.Equal(12, id.Length);
        Assert.Matches("^[0-9a-f]{12}$", id);
    }

    [Fact]
    public void CreateConversation_stores_system_prompt_exactly()
    {
        using var manager = new InMemoryAiConversationManager(Options.Create(new AiConversationOptions { SessionTtl = null }));
        var id = manager.CreateConversation("exact-prompt");

        var msgs = manager.GetAllMessages(id);
        Assert.Single(msgs);
        Assert.Equal(ChatRole.System, msgs[0].Role);
        Assert.Equal("exact-prompt", msgs[0].Text);
    }

    [Fact]
    public void CreateConversation_null_prompt_has_no_system_message()
    {
        using var manager = new InMemoryAiConversationManager(Options.Create(new AiConversationOptions { SessionTtl = null }));
        var id = manager.CreateConversation(null);
        Assert.Empty(manager.GetAllMessages(id));
    }

    #endregion

    #region AddMessage + GetAllMessages

    [Fact]
    public void AddMessage_preserves_role_and_text()
    {
        using var manager = new InMemoryAiConversationManager(Options.Create(new AiConversationOptions { SessionTtl = null }));
        var id = manager.CreateConversation();

        manager.AddMessage(id, new ChatMessage(ChatRole.User, "hello"));
        manager.AddMessage(id, new ChatMessage(ChatRole.Assistant, "hi"));

        var msgs = manager.GetAllMessages(id);
        Assert.Equal(2, msgs.Count);
        Assert.Equal(ChatRole.User, msgs[0].Role);
        Assert.Equal("hello", msgs[0].Text);
        Assert.Equal(ChatRole.Assistant, msgs[1].Role);
        Assert.Equal("hi", msgs[1].Text);
    }

    [Fact]
    public void GetAllMessages_includes_system_then_messages_in_order()
    {
        using var manager = new InMemoryAiConversationManager(Options.Create(new AiConversationOptions { SessionTtl = null }));
        var id = manager.CreateConversation("sys");

        manager.AddMessage(id, new ChatMessage(ChatRole.User, "a"));
        manager.AddMessage(id, new ChatMessage(ChatRole.Assistant, "b"));
        manager.AddMessage(id, new ChatMessage(ChatRole.User, "c"));

        var msgs = manager.GetAllMessages(id);
        Assert.Equal(4, msgs.Count);
        Assert.Equal(ChatRole.System, msgs[0].Role);
        Assert.Equal("sys", msgs[0].Text);
        Assert.Equal("a", msgs[1].Text);
        Assert.Equal("b", msgs[2].Text);
        Assert.Equal("c", msgs[3].Text);
    }

    #endregion

    #region GetMessages (windowing)

    [Fact]
    public void GetMessages_all_fit_returns_all_in_order()
    {
        var opts = new AiConversationOptions
        {
            DefaultMaxTokens = 1000,
            SessionTtl = null,
            EstimateTokens = text => text.Length
        };
        using var manager = new InMemoryAiConversationManager(Options.Create(opts));
        var id = manager.CreateConversation("sys");

        manager.AddMessage(id, new ChatMessage(ChatRole.User, "aa"));
        manager.AddMessage(id, new ChatMessage(ChatRole.Assistant, "bb"));

        var msgs = manager.GetMessages(id);
        Assert.Equal(3, msgs.Count);
        Assert.Equal(ChatRole.System, msgs[0].Role);
        Assert.Equal("sys", msgs[0].Text);
        Assert.Equal("aa", msgs[1].Text);
        Assert.Equal("bb", msgs[2].Text);
    }

    [Fact]
    public void GetMessages_trims_oldest_first_keeps_newest()
    {
        var opts = new AiConversationOptions
        {
            DefaultMaxTokens = 15,
            SessionTtl = null,
            EstimateTokens = text => text.Length
        };
        using var manager = new InMemoryAiConversationManager(Options.Create(opts));
        var id = manager.CreateConversation("sys"); // 3 tokens

        manager.AddMessage(id, new ChatMessage(ChatRole.User, "aaaaaa"));     // 6
        manager.AddMessage(id, new ChatMessage(ChatRole.Assistant, "bbbbbb")); // 6
        manager.AddMessage(id, new ChatMessage(ChatRole.User, "cccc"));        // 4

        // Budget = 15, sys(3) = remaining 12
        // From newest: cccc(4) → remaining 8, bbbbbb(6) → remaining 2, aaaaaa(6) → 2-6 < 0 → stop
        var msgs = manager.GetMessages(id);
        Assert.Equal(3, msgs.Count);
        Assert.Equal(ChatRole.System, msgs[0].Role);
        Assert.Equal("bbbbbb", msgs[1].Text); // older of the two that fit
        Assert.Equal("cccc", msgs[2].Text);   // newest
    }

    [Fact]
    public void GetMessages_preserves_chronological_order_after_windowing()
    {
        var opts = new AiConversationOptions
        {
            DefaultMaxTokens = 12,
            SessionTtl = null,
            EstimateTokens = text => text.Length
        };
        using var manager = new InMemoryAiConversationManager(Options.Create(opts));
        var id = manager.CreateConversation(); // no system prompt

        manager.AddMessage(id, new ChatMessage(ChatRole.User, "aaaa"));      // 4
        manager.AddMessage(id, new ChatMessage(ChatRole.Assistant, "bbbb")); // 4
        manager.AddMessage(id, new ChatMessage(ChatRole.User, "cccc"));      // 4

        // Budget 12: all fit (4+4+4=12)
        var msgs = manager.GetMessages(id);
        Assert.Equal(3, msgs.Count);
        Assert.Equal("aaaa", msgs[0].Text);
        Assert.Equal("bbbb", msgs[1].Text);
        Assert.Equal("cccc", msgs[2].Text);
    }

    [Fact]
    public void GetMessages_boundary_exactly_at_budget_includes_message()
    {
        var opts = new AiConversationOptions
        {
            DefaultMaxTokens = 8,
            SessionTtl = null,
            EstimateTokens = text => text.Length
        };
        using var manager = new InMemoryAiConversationManager(Options.Create(opts));
        var id = manager.CreateConversation(); // no system prompt

        manager.AddMessage(id, new ChatMessage(ChatRole.User, "aaaa"));      // 4
        manager.AddMessage(id, new ChatMessage(ChatRole.Assistant, "bbbb")); // 4

        // Budget 8: 4+4=8, exactly fits
        var msgs = manager.GetMessages(id);
        Assert.Equal(2, msgs.Count);
    }

    [Fact]
    public void GetMessages_one_over_budget_excludes_oldest()
    {
        var opts = new AiConversationOptions
        {
            DefaultMaxTokens = 7,
            SessionTtl = null,
            EstimateTokens = text => text.Length
        };
        using var manager = new InMemoryAiConversationManager(Options.Create(opts));
        var id = manager.CreateConversation(); // no system prompt

        manager.AddMessage(id, new ChatMessage(ChatRole.User, "aaaa"));      // 4
        manager.AddMessage(id, new ChatMessage(ChatRole.Assistant, "bbbb")); // 4

        // Budget 7: from newest bbbb(4) → remaining 3, aaaa(4) → 3-4 < 0 → break
        var msgs = manager.GetMessages(id);
        Assert.Single(msgs);
        Assert.Equal("bbbb", msgs[0].Text);
    }

    [Fact]
    public void GetMessages_system_prompt_alone_when_remaining_zero()
    {
        var opts = new AiConversationOptions
        {
            DefaultMaxTokens = 5,
            SessionTtl = null,
            EstimateTokens = text => text.Length
        };
        using var manager = new InMemoryAiConversationManager(Options.Create(opts));
        var id = manager.CreateConversation("abcde"); // exactly 5 tokens

        manager.AddMessage(id, new ChatMessage(ChatRole.User, "x")); // 1

        // Budget 5, sys(5) → remaining 0 → return [system] only
        var msgs = manager.GetMessages(id);
        Assert.Single(msgs);
        Assert.Equal(ChatRole.System, msgs[0].Role);
    }

    [Fact]
    public void GetMessages_system_prompt_negative_remaining_still_returned()
    {
        var opts = new AiConversationOptions
        {
            DefaultMaxTokens = 3,
            SessionTtl = null,
            EstimateTokens = text => text.Length
        };
        using var manager = new InMemoryAiConversationManager(Options.Create(opts));
        var id = manager.CreateConversation("abcdef"); // 6 tokens, exceeds budget

        manager.AddMessage(id, new ChatMessage(ChatRole.User, "x"));

        // Budget 3, sys(6) → remaining -3 ≤ 0 → return [system] only
        var msgs = manager.GetMessages(id);
        Assert.Single(msgs);
        Assert.Equal(ChatRole.System, msgs[0].Role);
    }

    [Fact]
    public void GetMessages_explicit_maxTokens_overrides_default()
    {
        var opts = new AiConversationOptions
        {
            DefaultMaxTokens = 1000,
            SessionTtl = null,
            EstimateTokens = text => text.Length
        };
        using var manager = new InMemoryAiConversationManager(Options.Create(opts));
        var id = manager.CreateConversation();

        manager.AddMessage(id, new ChatMessage(ChatRole.User, "aaaa")); // 4
        manager.AddMessage(id, new ChatMessage(ChatRole.User, "bbbb")); // 4

        // Default would fit all (1000), but explicit 4 → only newest
        var msgs = manager.GetMessages(id, maxTokens: 4);
        Assert.Single(msgs);
        Assert.Equal("bbbb", msgs[0].Text);
    }

    [Fact]
    public void GetMessages_null_text_treated_as_empty_string_for_tokens()
    {
        var opts = new AiConversationOptions
        {
            DefaultMaxTokens = 100,
            SessionTtl = null,
            EstimateTokens = text => text.Length
        };
        using var manager = new InMemoryAiConversationManager(Options.Create(opts));
        var id = manager.CreateConversation();

        manager.AddMessage(id, new ChatMessage(ChatRole.User, (string?)null));

        // null text → "" → 0 tokens → fits
        var msgs = manager.GetMessages(id);
        Assert.Single(msgs);
    }

    #endregion

    #region ClearConversation

    [Fact]
    public void ClearConversation_keeps_system_prompt_removes_messages()
    {
        using var manager = new InMemoryAiConversationManager(Options.Create(new AiConversationOptions { SessionTtl = null }));
        var id = manager.CreateConversation("prompt");
        manager.AddMessage(id, new ChatMessage(ChatRole.User, "msg1"));
        manager.AddMessage(id, new ChatMessage(ChatRole.Assistant, "msg2"));

        manager.ClearConversation(id);

        var msgs = manager.GetAllMessages(id);
        Assert.Single(msgs);
        Assert.Equal(ChatRole.System, msgs[0].Role);
        Assert.Equal("prompt", msgs[0].Text);
    }

    [Fact]
    public void ClearConversation_no_system_prompt_results_empty()
    {
        using var manager = new InMemoryAiConversationManager(Options.Create(new AiConversationOptions { SessionTtl = null }));
        var id = manager.CreateConversation();
        manager.AddMessage(id, new ChatMessage(ChatRole.User, "msg"));
        manager.ClearConversation(id);

        Assert.Empty(manager.GetAllMessages(id));
    }

    #endregion

    #region RemoveConversation

    [Fact]
    public void RemoveConversation_makes_all_ops_throw()
    {
        using var manager = new InMemoryAiConversationManager(Options.Create(new AiConversationOptions { SessionTtl = null }));
        var id = manager.CreateConversation();
        manager.RemoveConversation(id);

        Assert.Throws<KeyNotFoundException>(() => manager.AddMessage(id, new ChatMessage(ChatRole.User, "x")));
        Assert.Throws<KeyNotFoundException>(() => manager.GetMessages(id));
        Assert.Throws<KeyNotFoundException>(() => manager.GetAllMessages(id));
        Assert.Throws<KeyNotFoundException>(() => manager.ClearConversation(id));
    }

    [Fact]
    public void RemoveConversation_nonexistent_throws_exact_type()
    {
        using var manager = new InMemoryAiConversationManager(Options.Create(new AiConversationOptions { SessionTtl = null }));
        var ex = Assert.Throws<KeyNotFoundException>(() => manager.RemoveConversation("bogus"));
        Assert.Contains("bogus", ex.Message);
    }

    #endregion

    #region ListConversations

    [Fact]
    public void ListConversations_empty_initially()
    {
        using var manager = new InMemoryAiConversationManager(Options.Create(new AiConversationOptions { SessionTtl = null }));
        Assert.Empty(manager.ListConversations());
    }

    [Fact]
    public void ListConversations_reflects_creates_and_removes()
    {
        using var manager = new InMemoryAiConversationManager(Options.Create(new AiConversationOptions { SessionTtl = null }));
        var id1 = manager.CreateConversation();
        var id2 = manager.CreateConversation();

        var list = manager.ListConversations();
        Assert.Equal(2, list.Count);
        Assert.Contains(id1, list);
        Assert.Contains(id2, list);

        manager.RemoveConversation(id1);
        var listAfter = manager.ListConversations();
        Assert.Single(listAfter);
        Assert.DoesNotContain(id1, listAfter);
        Assert.Contains(id2, listAfter);
    }

    #endregion

    #region Error cases

    [Fact]
    public void AddMessage_nonexistent_throws()
    {
        using var manager = new InMemoryAiConversationManager(Options.Create(new AiConversationOptions { SessionTtl = null }));
        var ex = Assert.Throws<KeyNotFoundException>(
            () => manager.AddMessage("nope", new ChatMessage(ChatRole.User, "x")));
        Assert.Contains("nope", ex.Message);
    }

    [Fact]
    public void GetMessages_nonexistent_throws()
    {
        using var manager = new InMemoryAiConversationManager(Options.Create(new AiConversationOptions { SessionTtl = null }));
        Assert.Throws<KeyNotFoundException>(() => manager.GetMessages("nope"));
    }

    [Fact]
    public void GetAllMessages_nonexistent_throws()
    {
        using var manager = new InMemoryAiConversationManager(Options.Create(new AiConversationOptions { SessionTtl = null }));
        Assert.Throws<KeyNotFoundException>(() => manager.GetAllMessages("nope"));
    }

    #endregion
}
