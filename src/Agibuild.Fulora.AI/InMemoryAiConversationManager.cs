using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Agibuild.Fulora.AI;

/// <summary>
/// In-memory implementation of <see cref="IAiConversationManager"/> with
/// token-aware sliding window and optional session TTL.
/// </summary>
public sealed class InMemoryAiConversationManager : IAiConversationManager, IDisposable
{
    private readonly ConcurrentDictionary<string, ConversationState> _conversations = new();
    private readonly AiConversationOptions _options;
    private readonly Timer? _evictionTimer;

    /// <summary>Initializes a new instance.</summary>
    public InMemoryAiConversationManager(IOptions<AiConversationOptions> options)
    {
        _options = options.Value;
        if (_options.SessionTtl.HasValue)
        {
            _evictionTimer = new Timer(_ => EvictExpired(), null,
                TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }
    }

    /// <inheritdoc />
    public string CreateConversation(string? systemPrompt = null)
    {
        var id = Guid.NewGuid().ToString("N")[..12];
        var state = new ConversationState(systemPrompt);
        _conversations[id] = state;
        return id;
    }

    /// <inheritdoc />
    public void AddMessage(string conversationId, ChatMessage message)
    {
        var state = GetState(conversationId);
        lock (state.Lock)
        {
            state.Messages.Add(message);
            state.LastAccessedUtc = DateTime.UtcNow;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ChatMessage> GetMessages(string conversationId, int? maxTokens = null)
    {
        var state = GetState(conversationId);
        var budget = maxTokens ?? _options.DefaultMaxTokens;

        lock (state.Lock)
        {
            state.LastAccessedUtc = DateTime.UtcNow;
            return WindowMessages(state, budget);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ChatMessage> GetAllMessages(string conversationId)
    {
        var state = GetState(conversationId);
        lock (state.Lock)
        {
            state.LastAccessedUtc = DateTime.UtcNow;
            var result = new List<ChatMessage>();
            if (state.SystemPrompt is not null)
                result.Add(new ChatMessage(ChatRole.System, state.SystemPrompt));
            result.AddRange(state.Messages);
            return result;
        }
    }

    /// <inheritdoc />
    public void ClearConversation(string conversationId)
    {
        var state = GetState(conversationId);
        lock (state.Lock)
        {
            state.Messages.Clear();
            state.LastAccessedUtc = DateTime.UtcNow;
        }
    }

    /// <inheritdoc />
    public void RemoveConversation(string conversationId)
    {
        if (!_conversations.TryRemove(conversationId, out _))
            throw new KeyNotFoundException($"Conversation '{conversationId}' not found.");
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListConversations()
    {
        return [.. _conversations.Keys];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _evictionTimer?.Dispose();
    }

    private ConversationState GetState(string conversationId)
    {
        if (!_conversations.TryGetValue(conversationId, out var state))
            throw new KeyNotFoundException($"Conversation '{conversationId}' not found.");
        return state;
    }

    private List<ChatMessage> WindowMessages(ConversationState state, int tokenBudget)
    {
        var result = new List<ChatMessage>();
        var remaining = tokenBudget;

        if (state.SystemPrompt is not null)
        {
            var systemMessage = new ChatMessage(ChatRole.System, state.SystemPrompt);
            var systemTokens = _options.EstimateTokens(state.SystemPrompt);
            result.Add(systemMessage);
            remaining -= systemTokens;

            if (remaining <= 0)
                return result;
        }

        // Collect messages from newest to oldest that fit the budget
        var fittingMessages = new List<ChatMessage>();
        for (var i = state.Messages.Count - 1; i >= 0; i--)
        {
            var msg = state.Messages[i];
            var msgTokens = _options.EstimateTokens(msg.Text ?? "");
            if (remaining - msgTokens < 0)
                break;
            remaining -= msgTokens;
            fittingMessages.Add(msg);
        }

        fittingMessages.Reverse();
        result.AddRange(fittingMessages);
        return result;
    }

    private void EvictExpired()
    {
        if (_options.SessionTtl is not { } ttl) return;
        var cutoff = DateTime.UtcNow - ttl;
        foreach (var kvp in _conversations)
        {
            if (kvp.Value.LastAccessedUtc < cutoff)
                _conversations.TryRemove(kvp.Key, out _);
        }
    }

    private sealed class ConversationState(string? systemPrompt)
    {
        public string? SystemPrompt { get; } = systemPrompt;
        public List<ChatMessage> Messages { get; } = [];
        public object Lock { get; } = new();
        public DateTime LastAccessedUtc { get; set; } = DateTime.UtcNow;
    }
}
