using Agibuild.Fulora;
using Agibuild.Fulora.Adapters.Abstractions;
using Agibuild.Fulora.Testing;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

/// <summary>
/// Cross-platform behavior contract for server-certificate failures on
/// <see cref="IWebViewAdapter"/>: each platform supplies an adapter instance
/// plus a callback that reproduces its native SSL rejection path.
/// </summary>
/// <remarks>
/// <para>
/// The factory uses an <c>out</c> callback whose parameters match
/// <see cref="MockWebViewAdapter.TriggerServerCertificateError"/> so the mock
/// can forward directly and real adapters can wrap their harness without an
/// extra indirection type.
/// </para>
/// <para>
/// Metadata-poor platforms (e.g. Android in 1.6.x) pass null subject/issuer and
/// skip <see cref="ServerCertificateError_propagates_certificate_metadata_when_supplied"/>
/// in their integration assembly via <c>[Fact(Skip = ...)]</c>.
/// </para>
/// </remarks>
internal static class AdapterSslRejectionContract
{
    /// <summary>
    /// Supplies an <see cref="IWebViewAdapter"/> and a trigger aligned with
    /// <see cref="MockWebViewAdapter.TriggerServerCertificateError"/> for
    /// certificate-error simulation.
    /// </summary>
    internal delegate IWebViewAdapter SslRejectionTriggerFactory(
        out Action<Uri, string?, string?, DateTimeOffset?, DateTimeOffset?, string?, int> triggerSslError);

    public static async Task ServerCertificateError_raises_NavigationCompleted_with_Failure_status(
        SslRejectionTriggerFactory factory)
    {
        var adapter = factory(out var trigger);
        var tcs = new TaskCompletionSource<NavigationCompletedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? _, NavigationCompletedEventArgs e) => tcs.TrySetResult(e);
        adapter.NavigationCompleted += Handler;

        var uri = new Uri("https://ssl-contract.example/path");
        trigger(uri, null, null, null, null, null, 0);

        var args = await tcs.Task;
        Assert.Equal(NavigationCompletedStatus.Failure, args.Status);
    }

    public static async Task ServerCertificateError_exception_is_WebViewSslException_with_host_and_summary(
        SslRejectionTriggerFactory factory)
    {
        var adapter = factory(out var trigger);
        var tcs = new TaskCompletionSource<NavigationCompletedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        adapter.NavigationCompleted += (_, e) => tcs.TrySetResult(e);

        var uri = new Uri("https://ssl-host.example/");
        trigger(uri, null, null, null, null, null, 0);

        var args = await tcs.Task;
        var ex = Assert.IsType<WebViewSslException>(args.Error);
        Assert.Equal(uri.Host, ex.Host);
        Assert.Equal("MockSslError", ex.ErrorSummary);
        Assert.False(string.IsNullOrEmpty(ex.Host));
        Assert.False(string.IsNullOrEmpty(ex.ErrorSummary));
    }

    public static async Task ServerCertificateError_always_cancels_navigation(SslRejectionTriggerFactory factory)
    {
        var adapter = factory(out var trigger);
        Guid? sslFailureNavId = null;
        var violation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? _, NavigationCompletedEventArgs e)
        {
            if (e.Status == NavigationCompletedStatus.Failure && e.Error is WebViewSslException)
            {
                sslFailureNavId = e.NavigationId;
                return;
            }

            if (sslFailureNavId is { } id
                && e.NavigationId == id
                && e.Status == NavigationCompletedStatus.Success)
            {
                violation.TrySetResult();
            }
        }

        adapter.NavigationCompleted += Handler;

        var uri = new Uri("https://ssl-cancel.example/");
        trigger(uri, null, null, null, null, null, 0);

        Assert.True(sslFailureNavId.HasValue);

        var winner = await Task.WhenAny(violation.Task, Task.Delay(TimeSpan.FromMilliseconds(50)));
        Assert.NotSame(violation.Task, winner);
    }

    public static async Task ServerCertificateError_propagates_certificate_metadata_when_supplied(
        SslRejectionTriggerFactory factory)
    {
        var adapter = factory(out var trigger);
        var tcs = new TaskCompletionSource<NavigationCompletedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        adapter.NavigationCompleted += (_, e) => tcs.TrySetResult(e);

        var uri = new Uri("https://ssl-meta.example/");
        var subject = "CN=leaf.test";
        var issuer = "CN=issuer.test";
        var validFrom = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var validTo = new DateTimeOffset(2030, 6, 15, 12, 30, 0, TimeSpan.Zero);

        trigger(uri, subject, issuer, validFrom, validTo, "meta-summary", 99);

        var args = await tcs.Task;
        var ex = Assert.IsType<WebViewSslException>(args.Error);
        Assert.Equal(subject, ex.CertificateSubject);
        Assert.Equal(issuer, ex.CertificateIssuer);
        Assert.Equal(validFrom, ex.ValidFrom);
        Assert.Equal(validTo, ex.ValidTo);
        Assert.Equal("meta-summary", ex.ErrorSummary);
        Assert.Equal(99, ex.PlatformRawCode);
    }

    public static async Task PlatformProvidesCertificateMetadata_when_supported(
        SslRejectionTriggerFactory factory,
        bool platformSupportsMetadata)
    {
        var adapter = factory(out var trigger);
        var tcs = new TaskCompletionSource<NavigationCompletedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        adapter.NavigationCompleted += (_, e) => tcs.TrySetResult(e);

        var uri = new Uri("https://ssl-platform-metadata.example/");
        var subject = platformSupportsMetadata ? "CN=apple-leaf.test" : null;
        var issuer = platformSupportsMetadata ? "CN=apple-issuer.test" : null;
        DateTimeOffset? validFrom = platformSupportsMetadata
            ? new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)
            : null;
        DateTimeOffset? validTo = platformSupportsMetadata
            ? new DateTimeOffset(2031, 1, 1, 0, 0, 0, TimeSpan.Zero)
            : null;

        trigger(uri, subject, issuer, validFrom, validTo, "platform-summary", 101);

        var args = await tcs.Task;
        var ex = Assert.IsType<WebViewSslException>(args.Error);
        if (platformSupportsMetadata)
        {
            Assert.NotNull(ex.CertificateSubject);
            Assert.NotNull(ex.CertificateIssuer);
            Assert.NotNull(ex.ValidFrom);
            Assert.NotNull(ex.ValidTo);
        }
        else
        {
            Assert.Null(ex.CertificateSubject);
            Assert.Null(ex.CertificateIssuer);
            Assert.Null(ex.ValidFrom);
            Assert.Null(ex.ValidTo);
        }
    }
}
