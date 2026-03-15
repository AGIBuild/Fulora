namespace Agibuild.Fulora.AI;

/// <summary>
/// Thrown when an AI content filter blocks input or output.
/// </summary>
public sealed class AiContentBlockedException : FuloraException
{
    /// <summary>The reason the content was blocked.</summary>
    public string Reason { get; }

    /// <summary>Initializes a new instance.</summary>
    public AiContentBlockedException(string reason)
        : base(FuloraErrorCodes.AiContentBlocked, $"AI content was blocked: {reason}")
    {
        Reason = reason;
    }
}

/// <summary>
/// Thrown when an AI call exceeds the configured token budget.
/// </summary>
public sealed class AiBudgetExceededException(string message)
    : FuloraException(FuloraErrorCodes.AiBudgetExceeded, message);

/// <summary>
/// Thrown when structured output validation fails after all retry attempts.
/// </summary>
public sealed class AiStructuredOutputException : FuloraException
{
    /// <summary>The raw LLM response that failed validation.</summary>
    public string? RawResponse { get; }

    /// <summary>The last validation error.</summary>
    public string? ValidationError { get; }

    /// <summary>Initializes a new instance.</summary>
    public AiStructuredOutputException(string message, string? rawResponse, string? validationError)
        : base(FuloraErrorCodes.AiStructuredOutputFailed, message)
    {
        RawResponse = rawResponse;
        ValidationError = validationError;
    }
}
