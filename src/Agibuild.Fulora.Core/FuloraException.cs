namespace Agibuild.Fulora;

/// <summary>
/// Base exception for all Fulora framework errors.
/// Carries a machine-readable <see cref="ErrorCode"/> that maps to
/// JSON-RPC error codes when crossing the bridge boundary.
/// </summary>
public class FuloraException : Exception
{
    /// <summary>Machine-readable error code from <see cref="FuloraErrorCodes"/>.</summary>
    public string ErrorCode { get; }

    /// <summary>Initializes a new instance.</summary>
    public FuloraException(string errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}

/// <summary>
/// Well-known error codes for <see cref="FuloraException"/>.
/// Organized by domain prefix.
/// </summary>
public static class FuloraErrorCodes
{
    /// <summary>AI content filter blocked the request.</summary>
    public const string AiContentBlocked = "AI_CONTENT_BLOCKED";
    /// <summary>AI token budget exceeded.</summary>
    public const string AiBudgetExceeded = "AI_BUDGET_EXCEEDED";
    /// <summary>AI structured output validation failed.</summary>
    public const string AiStructuredOutputFailed = "AI_STRUCTURED_OUTPUT_FAILED";

    /// <summary>Navigation failed due to a network error.</summary>
    public const string NavigationNetwork = "NAV_NETWORK";
    /// <summary>Navigation failed due to an SSL error.</summary>
    public const string NavigationSsl = "NAV_SSL";
    /// <summary>Navigation timed out.</summary>
    public const string NavigationTimeout = "NAV_TIMEOUT";
    /// <summary>General navigation failure.</summary>
    public const string NavigationFailed = "NAV_FAILED";

    /// <summary>Script execution failed.</summary>
    public const string ScriptError = "SCRIPT_ERROR";

    /// <summary>RPC invocation failed.</summary>
    public const string RpcError = "RPC_ERROR";
}
