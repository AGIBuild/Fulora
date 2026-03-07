using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Agibuild.Fulora.AI;

/// <summary>
/// Delegating <see cref="IChatClient"/> that runs an ordered pipeline of
/// <see cref="IAiContentFilter"/> instances on input and output.
/// </summary>
public sealed class ContentGateChatClient : IChatClient
{
    private readonly IChatClient _inner;
    private readonly IReadOnlyList<IAiContentFilter> _filters;

    public ContentGateChatClient(IChatClient inner, IEnumerable<IAiContentFilter> filters)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(filters);
        _inner = inner;
        _filters = filters.ToList().AsReadOnly();
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var filtered = await FilterInputAsync(messages, cancellationToken);

        var response = await _inner.GetResponseAsync(filtered, options, cancellationToken);

        return await FilterOutputAsync(response, cancellationToken);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var filtered = await FilterInputAsync(messages, cancellationToken);

        await foreach (var update in _inner.GetStreamingResponseAsync(filtered, options, cancellationToken))
        {
            var filteredUpdate = await FilterStreamChunkAsync(update, cancellationToken);
            if (filteredUpdate is not null)
                yield return filteredUpdate;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(ContentGateChatClient))
            return this;
        return _inner.GetService(serviceType, serviceKey);
    }

    public void Dispose() => _inner.Dispose();

    private async Task<IList<ChatMessage>> FilterInputAsync(
        IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var result = messages.ToList();

        foreach (var filter in _filters)
        {
            for (var i = 0; i < result.Count; i++)
            {
                var msg = result[i];
                if (msg.Role != ChatRole.User) continue;
                var text = msg.Text;
                if (string.IsNullOrEmpty(text)) continue;

                var filterResult = await filter.FilterInputAsync(text, cancellationToken);
                switch (filterResult.Action)
                {
                    case ContentFilterAction.Block:
                        throw new AiContentBlockedException(filterResult.Reason ?? "Content blocked by filter.");
                    case ContentFilterAction.Transform:
                        result[i] = new ChatMessage(msg.Role, filterResult.TransformedContent ?? text);
                        break;
                }
            }
        }

        return result;
    }

    private async Task<ChatResponse> FilterOutputAsync(
        ChatResponse response,
        CancellationToken cancellationToken)
    {
        foreach (var filter in _filters)
        {
            var text = response.Text;
            if (string.IsNullOrEmpty(text)) continue;

            var filterResult = await filter.FilterOutputAsync(text, cancellationToken);
            switch (filterResult.Action)
            {
                case ContentFilterAction.Block:
                    throw new AiContentBlockedException(filterResult.Reason ?? "Output blocked by filter.");
                case ContentFilterAction.Transform:
                    return new ChatResponse(new ChatMessage(ChatRole.Assistant, filterResult.TransformedContent ?? text))
                    {
                        ModelId = response.ModelId,
                        Usage = response.Usage,
                        ResponseId = response.ResponseId,
                    };
            }
        }

        return response;
    }

    private async Task<ChatResponseUpdate?> FilterStreamChunkAsync(
        ChatResponseUpdate update,
        CancellationToken cancellationToken)
    {
        var text = update.Text;
        if (string.IsNullOrEmpty(text))
            return update;

        foreach (var filter in _filters)
        {
            var filterResult = await filter.FilterOutputAsync(text, cancellationToken);
            switch (filterResult.Action)
            {
                case ContentFilterAction.Block:
                    return null; // omit this chunk
                case ContentFilterAction.Transform:
                    text = filterResult.TransformedContent ?? text;
                    break;
            }
        }

        if (text == update.Text)
            return update;

        return new ChatResponseUpdate
        {
            Role = update.Role,
            Contents = [new TextContent(text)],
            ModelId = update.ModelId,
            ResponseId = update.ResponseId,
            FinishReason = update.FinishReason,
        };
    }
}
