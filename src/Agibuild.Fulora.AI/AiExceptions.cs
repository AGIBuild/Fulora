namespace Agibuild.Fulora.AI;

/// <summary>
/// Thrown when an AI content filter blocks input or output.
/// </summary>
public sealed class AiContentBlockedException(string reason)
    : InvalidOperationException($"AI content was blocked: {reason}")
{
    /// <summary>The reason the content was blocked.</summary>
    public string Reason { get; } = reason;
}

/// <summary>
/// Thrown when an AI call exceeds the configured token budget.
/// </summary>
public sealed class AiBudgetExceededException(string message)
    : InvalidOperationException(message);

/// <summary>
/// Thrown when structured output validation fails after all retry attempts.
/// </summary>
public sealed class AiStructuredOutputException : InvalidOperationException
{
    /// <summary>The raw LLM response that failed validation.</summary>
    public string? RawResponse { get; }

    /// <summary>The last validation error.</summary>
    public string? ValidationError { get; }

    public AiStructuredOutputException(string message, string? rawResponse, string? validationError)
        : base(message)
    {
        RawResponse = rawResponse;
        ValidationError = validationError;
    }
}
