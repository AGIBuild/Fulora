using Agibuild.Fulora.AI;
using Microsoft.Extensions.AI;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class MeteringChatClientTests
{
    private static AiMeteringOptions DefaultOptions => new()
    {
        ModelPricing = new()
        {
            ["gpt-4"] = new ModelPricing { PromptPer1K = 0.03m, CompletionPer1K = 0.06m }
        }
    };

    [Fact]
    public async Task Extracts_token_usage_from_response()
    {
        var inner = new UsageTrackingClient(promptTokens: 100, completionTokens: 50);
        var client = new MeteringChatClient(inner, DefaultOptions);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal(150, client.PeriodTokensUsed);
    }

    [Fact]
    public void EstimateCost_calculates_correctly()
    {
        var inner = new UsageTrackingClient(0, 0);
        var client = new MeteringChatClient(inner, DefaultOptions);

        var cost = client.EstimateCost(1000, 500, "gpt-4");

        Assert.Equal(0.03m + 0.03m, cost); // 1000/1000*0.03 + 500/1000*0.06
    }

    [Fact]
    public void EstimateCost_returns_zero_for_unknown_model()
    {
        var inner = new UsageTrackingClient(0, 0);
        var client = new MeteringChatClient(inner, DefaultOptions);

        var cost = client.EstimateCost(1000, 500, "unknown-model");

        Assert.Equal(0m, cost);
    }

    [Fact]
    public async Task Throws_when_single_call_limit_exceeded()
    {
        var options = DefaultOptions;
        options.SingleCallTokenLimit = 100;
        var inner = new UsageTrackingClient(promptTokens: 80, completionTokens: 30);
        var client = new MeteringChatClient(inner, options);

        await Assert.ThrowsAsync<AiBudgetExceededException>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]));
    }

    [Fact]
    public async Task Throws_when_period_budget_exceeded()
    {
        var options = DefaultOptions;
        options.PeriodBudgetTokens = 200;
        options.BudgetPeriod = TimeSpan.FromHours(1);
        var inner = new UsageTrackingClient(promptTokens: 100, completionTokens: 50);
        var client = new MeteringChatClient(inner, options);

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "first")]);
        Assert.Equal(150, client.PeriodTokensUsed);

        await Assert.ThrowsAsync<AiBudgetExceededException>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "second")]));
    }

    [Fact]
    public async Task No_usage_does_not_throw()
    {
        var inner = new UsageTrackingClient(promptTokens: 0, completionTokens: 0, includeUsage: false);
        var client = new MeteringChatClient(inner, DefaultOptions);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.NotNull(response);
        Assert.Equal(0, client.PeriodTokensUsed);
    }

    private sealed class UsageTrackingClient(int promptTokens, int completionTokens, bool includeUsage = true) : IChatClient
    {
        public void Dispose() { }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "response"))
            {
                ModelId = "gpt-4",
                Usage = includeUsage
                    ? new UsageDetails { InputTokenCount = promptTokens, OutputTokenCount = completionTokens }
                    : null
            };
            return Task.FromResult(response);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            AsyncEnumerable.Empty<ChatResponseUpdate>();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }
}
