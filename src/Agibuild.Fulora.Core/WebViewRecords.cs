using System.Diagnostics.CodeAnalysis;

namespace Agibuild.Fulora;

#pragma warning disable CS1591

public static class WebViewOperationFailure
{
    private const string CategoryDataKey = "Agibuild.WebView.OperationFailureCategory";

    public static void SetCategory(Exception exception, WebViewOperationFailureCategory category)
    {
        ArgumentNullException.ThrowIfNull(exception);
        exception.Data[CategoryDataKey] = category;
    }

    public static bool TryGetCategory(Exception exception, out WebViewOperationFailureCategory category)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception.Data[CategoryDataKey] is WebViewOperationFailureCategory typed)
        {
            category = typed;
            return true;
        }

        if (exception.Data[CategoryDataKey] is string text &&
            Enum.TryParse(text, ignoreCase: true, out WebViewOperationFailureCategory parsed))
        {
            category = parsed;
            return true;
        }

        category = default;
        return false;
    }
}

internal readonly record struct NativeNavigationStartingInfo(
    Guid CorrelationId,
    Uri RequestUri,
    bool IsMainFrame);

internal readonly record struct NativeNavigationStartingDecision(
    bool IsAllowed,
    Guid NavigationId);

public readonly record struct WebMessageEnvelope(
    string Body,
    string Origin,
    Guid ChannelId,
    int ProtocolVersion);

public readonly record struct WebMessagePolicyDecision(bool IsAllowed, WebMessageDropReason? DropReason)
{
    public static WebMessagePolicyDecision Allow() => new(true, null);
    public static WebMessagePolicyDecision Deny(WebMessageDropReason reason) => new(false, reason);
}

public readonly record struct WebMessageDropDiagnostic(WebMessageDropReason Reason, string Origin, Guid ChannelId);

public sealed class CustomSchemeRegistration
{
    public required string SchemeName { get; init; }
    public bool HasAuthorityComponent { get; init; }
    public bool TreatAsSecure { get; init; }
}

public sealed class PdfPrintOptions
{
    public bool Landscape { get; set; }
    public double PageWidth { get; set; } = 8.5;
    public double PageHeight { get; set; } = 11.0;
    public double MarginTop { get; set; } = 0.4;
    public double MarginBottom { get; set; } = 0.4;
    public double MarginLeft { get; set; } = 0.4;
    public double MarginRight { get; set; } = 0.4;
    public double Scale { get; set; } = 1.0;
    public bool PrintBackground { get; set; } = true;
}

public sealed class ContextMenuRequestedEventArgs : EventArgs
{
    public double X { get; init; }
    public double Y { get; init; }
    public Uri? LinkUri { get; init; }
    public string? SelectionText { get; init; }
    public ContextMenuMediaType MediaType { get; init; }
    public Uri? MediaSourceUri { get; init; }
    public bool IsEditable { get; init; }
    public bool Handled { get; set; }
}

public sealed class FindInPageOptions
{
    public bool CaseSensitive { get; init; }
    public bool Forward { get; init; } = true;
}

public sealed class FindInPageEventArgs : EventArgs
{
    public int ActiveMatchIndex { get; init; }
    public int TotalMatches { get; init; }
}

public sealed class AuthOptions
{
    public Uri? AuthorizeUri { get; set; }
    public Uri? CallbackUri { get; set; }
    public bool UseEphemeralSession { get; set; } = true;
    public TimeSpan? Timeout { get; set; }
}

public sealed class WebAuthResult
{
    public WebAuthStatus Status { get; init; }
    public Uri? CallbackUri { get; init; }
    public string? Error { get; init; }
}

public sealed class NavigationStartingEventArgs : EventArgs
{
    public NavigationStartingEventArgs(Uri requestUri)
    {
        NavigationId = Guid.Empty;
        RequestUri = requestUri;
    }

    public NavigationStartingEventArgs(Guid navigationId, Uri requestUri)
    {
        NavigationId = navigationId;
        RequestUri = requestUri;
    }

    public Guid NavigationId { get; }
    public Uri RequestUri { get; }
    public bool Cancel { get; set; }
}

public sealed class NavigationCompletedEventArgs : EventArgs
{
    public NavigationCompletedEventArgs()
    {
        NavigationId = Guid.Empty;
        RequestUri = new Uri("about:blank");
        Status = NavigationCompletedStatus.Success;
        Error = null;
    }

    public NavigationCompletedEventArgs(
        Guid navigationId,
        Uri requestUri,
        NavigationCompletedStatus status,
        Exception? error)
    {
        if (status == NavigationCompletedStatus.Failure && error is null)
            throw new ArgumentNullException(nameof(error), "Error is required when Status=Failure.");

        if (status != NavigationCompletedStatus.Failure && error is not null)
            throw new ArgumentException("Error must be null when Status is not Failure.", nameof(error));

        NavigationId = navigationId;
        RequestUri = requestUri;
        Status = status;
        Error = error;
    }

    public Guid NavigationId { get; }
    public Uri RequestUri { get; }
    public NavigationCompletedStatus Status { get; }
    public Exception? Error { get; }
}

public sealed class NewWindowRequestedEventArgs : EventArgs
{
    public NewWindowRequestedEventArgs(Uri? uri = null)
    {
        Uri = uri;
    }

    public Uri? Uri { get; }
    public bool Handled { get; set; }
}

public sealed class WebMessageReceivedEventArgs : EventArgs
{
    public WebMessageReceivedEventArgs()
    {
        Body = string.Empty;
        Origin = string.Empty;
        ChannelId = Guid.Empty;
        ProtocolVersion = 1;
    }

    public WebMessageReceivedEventArgs(string body, string origin, Guid channelId)
    {
        Body = body;
        Origin = origin;
        ChannelId = channelId;
        ProtocolVersion = 1;
    }

    public WebMessageReceivedEventArgs(string body, string origin, Guid channelId, int protocolVersion)
    {
        Body = body;
        Origin = origin;
        ChannelId = channelId;
        ProtocolVersion = protocolVersion;
    }

    public string Body { get; }
    public string Origin { get; }
    public Guid ChannelId { get; }
    public int ProtocolVersion { get; }
}

public sealed class WebResourceRequestedEventArgs : EventArgs
{
    public WebResourceRequestedEventArgs() { }

    public WebResourceRequestedEventArgs(Uri requestUri, string method, IReadOnlyDictionary<string, string>? requestHeaders = null)
    {
        RequestUri = requestUri;
        Method = method;
        RequestHeaders = requestHeaders;
    }

    public Uri? RequestUri { get; init; }
    public string Method { get; init; } = "GET";
    public IReadOnlyDictionary<string, string>? RequestHeaders { get; init; }
    public Stream? ResponseBody { get; set; }
    public string ResponseContentType { get; set; } = "text/html";
    public int ResponseStatusCode { get; set; } = 200;
    public IDictionary<string, string>? ResponseHeaders { get; set; }
    public bool Handled { get; set; }
}

[Experimental("AGWV005")]
public sealed class EnvironmentRequestedEventArgs : EventArgs
{
}

public sealed class DownloadRequestedEventArgs : EventArgs
{
    public DownloadRequestedEventArgs(Uri downloadUri, string? suggestedFileName = null, string? contentType = null, long? contentLength = null)
    {
        DownloadUri = downloadUri;
        SuggestedFileName = suggestedFileName;
        ContentType = contentType;
        ContentLength = contentLength;
    }

    public Uri DownloadUri { get; }
    public string? SuggestedFileName { get; }
    public string? ContentType { get; }
    public long? ContentLength { get; }
    public string? DownloadPath { get; set; }
    public bool Cancel { get; set; }
    public bool Handled { get; set; }
}

public sealed class PermissionRequestedEventArgs : EventArgs
{
    public PermissionRequestedEventArgs(WebViewPermissionKind permissionKind, Uri? origin = null)
    {
        PermissionKind = permissionKind;
        Origin = origin;
    }

    public WebViewPermissionKind PermissionKind { get; }
    public Uri? Origin { get; }
    public PermissionState State { get; set; } = PermissionState.Default;
}

public sealed class AdapterCreatedEventArgs : EventArgs
{
    public AdapterCreatedEventArgs(INativeHandle? platformHandle)
    {
        PlatformHandle = platformHandle;
    }

    public INativeHandle? PlatformHandle { get; }
}

public class WebViewNavigationException : FuloraException
{
    public WebViewNavigationException(string message, Guid navigationId, Uri requestUri, Exception? innerException = null)
        : base(FuloraErrorCodes.NavigationFailed, message, innerException)
    {
        NavigationId = navigationId;
        RequestUri = requestUri;
    }

    protected WebViewNavigationException(string errorCode, string message, Guid navigationId, Uri requestUri, Exception? innerException = null)
        : base(errorCode, message, innerException)
    {
        NavigationId = navigationId;
        RequestUri = requestUri;
    }

    public Guid NavigationId { get; }
    public Uri RequestUri { get; }
}

public class WebViewNetworkException : WebViewNavigationException
{
    public WebViewNetworkException(string message, Guid navigationId, Uri requestUri, Exception? innerException = null)
        : base(FuloraErrorCodes.NavigationNetwork, message, navigationId, requestUri, innerException)
    {
    }
}

public class WebViewSslException : WebViewNavigationException
{
    public WebViewSslException(string message, Guid navigationId, Uri requestUri, Exception? innerException = null)
        : base(FuloraErrorCodes.NavigationSsl, message, navigationId, requestUri, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance from a structured
    /// <see cref="Agibuild.Fulora.Security.ServerCertificateErrorContext"/>.
    /// </summary>
    /// <remarks>
    /// Used by every platform WebView adapter when routing a server
    /// certificate validation failure through
    /// <see cref="Agibuild.Fulora.Security.INavigationSecurityHooks"/>.
    /// </remarks>
    internal WebViewSslException(
        Agibuild.Fulora.Security.ServerCertificateErrorContext context,
        Guid navigationId,
        Exception? innerException = null)
        : base(
            FuloraErrorCodes.NavigationSsl,
            BuildMessage(context),
            navigationId,
            (context ?? throw new ArgumentNullException(nameof(context))).RequestUri,
            innerException)
    {
        Host = context.Host;
        ErrorSummary = context.ErrorSummary;
        PlatformRawCode = context.PlatformRawCode;
        CertificateSubject = context.CertificateSubject;
        CertificateIssuer = context.CertificateIssuer;
        ValidFrom = context.ValidFrom;
        ValidTo = context.ValidTo;
    }

    /// <summary>Host of the request that failed certificate validation, when populated by the adapter.</summary>
    public string? Host { get; }

    /// <summary>Short, non-localized summary of the underlying failure (e.g. <c>CertificateExpired</c>).</summary>
    public string? ErrorSummary { get; }

    /// <summary>Native platform's raw error code; not comparable across platforms.</summary>
    public int PlatformRawCode { get; }

    /// <summary>Leaf certificate subject DN summary, when exposed by the native layer.</summary>
    public string? CertificateSubject { get; }

    /// <summary>Leaf certificate issuer DN summary, when exposed by the native layer.</summary>
    public string? CertificateIssuer { get; }

    /// <summary>Leaf certificate <c>NotBefore</c>, when exposed by the native layer.</summary>
    public DateTimeOffset? ValidFrom { get; }

    /// <summary>Leaf certificate <c>NotAfter</c>, when exposed by the native layer.</summary>
    public DateTimeOffset? ValidTo { get; }

    private static string BuildMessage(Agibuild.Fulora.Security.ServerCertificateErrorContext? context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return $"SSL certificate error for {context.Host}: {context.ErrorSummary} (raw={context.PlatformRawCode})";
    }
}

public class WebViewTimeoutException : WebViewNavigationException
{
    public WebViewTimeoutException(string message, Guid navigationId, Uri requestUri, Exception? innerException = null)
        : base(FuloraErrorCodes.NavigationTimeout, message, navigationId, requestUri, innerException)
    {
    }
}

public sealed record WebViewCookie(
    string Name,
    string Value,
    string Domain,
    string Path,
    DateTimeOffset? Expires,
    bool IsSecure,
    bool IsHttpOnly);

public class WebViewScriptException : FuloraException
{
    public WebViewScriptException(string message, Exception? innerException = null)
        : base(FuloraErrorCodes.ScriptError, message, innerException)
    {
    }
}

public class WebViewRpcException : FuloraException
{
    public WebViewRpcException(int code, string message)
        : base(FuloraErrorCodes.RpcError, message)
    {
        Code = code;
    }

    /// <summary>JSON-RPC style integer error code.</summary>
    public int Code { get; }
}

#pragma warning restore CS1591
