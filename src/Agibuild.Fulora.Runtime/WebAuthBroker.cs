namespace Agibuild.Fulora;

/// <summary>
/// Concrete implementation of <see cref="IWebAuthBroker"/> that opens a <see cref="IWebDialog"/>
/// to perform the OAuth authentication flow.
/// <para>
/// Flow:
/// 1. Create an ephemeral WebDialog via <see cref="IWebDialogFactory"/>.
/// 2. Navigate to <see cref="AuthOptions.AuthorizeUri"/>.
/// 3. Listen for <see cref="IWebView.NavigationStarted"/> to detect the callback URI.
/// 4. On match: close dialog, return <see cref="WebAuthResult"/> with Success.
/// 5. On user close: return UserCancel.
/// 6. On timeout: return Timeout.
/// </para>
/// </summary>
public sealed class WebAuthBroker : IWebAuthBroker
{
    private readonly IWebDialogFactory _dialogFactory;

    /// <inheritdoc />
    public WebAuthBroker(IWebDialogFactory dialogFactory)
    {
        _dialogFactory = dialogFactory ?? throw new ArgumentNullException(nameof(dialogFactory));
    }

    /// <inheritdoc />
    public async Task<WebAuthResult> AuthenticateAsync(ITopLevelWindow owner, AuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(options);

        if (options.AuthorizeUri is null)
        {
            throw new ArgumentException("AuthorizeUri is required.", nameof(options));
        }

        if (options.CallbackUri is null)
        {
            throw new ArgumentException("CallbackUri is required.", nameof(options));
        }

        // Create dialog with ephemeral options if requested.
        IWebViewEnvironmentOptions? envOptions = options.UseEphemeralSession
            ? new WebViewEnvironmentOptions { UseEphemeralSession = true }
            : null;

        using var dialog = _dialogFactory.Create(envOptions);
        dialog.Title = "Sign In";

        var tcs = new TaskCompletionSource<WebAuthResult>();

        // Monitor navigation for callback URI match.
        dialog.NavigationStarted += OnNavigationStarted;

        // Monitor dialog close (user cancel).
        dialog.Closing += OnClosing;

        try
        {
            // Show the dialog.
            if (owner.PlatformHandle is not null)
            {
                dialog.Show(owner.PlatformHandle);
            }
            else
            {
                dialog.Show();
            }

            // Navigate to the authorize URL.
            // If the user closes the dialog while navigation is in progress,
            // WebViewCore.Dispose() faults the active navigation with
            // ObjectDisposedException — treat this as UserCancel.
            try
            {
                await dialog.NavigateAsync(options.AuthorizeUri);
            }
            catch (ObjectDisposedException)
            {
                return new WebAuthResult { Status = WebAuthStatus.UserCancel };
            }

            // Apply timeout if specified.
            using var cts = options.Timeout.HasValue
                ? new CancellationTokenSource(options.Timeout.Value)
                : new CancellationTokenSource();

            if (options.Timeout.HasValue)
            {
                cts.Token.Register(() =>
                {
                    tcs.TrySetResult(new WebAuthResult
                    {
                        Status = WebAuthStatus.Timeout,
                        Error = $"Authentication timed out after {options.Timeout.Value.TotalSeconds}s."
                    });
                });
            }

            return await tcs.Task;
        }
        finally
        {
            dialog.NavigationStarted -= OnNavigationStarted;
            dialog.Closing -= OnClosing;
            dialog.Close();
        }

        void OnNavigationStarted(object? sender, NavigationStartingEventArgs e)
        {
            if (WebAuthCallbackMatcher.IsStrictMatch(options.CallbackUri, e.RequestUri))
            {
                e.Cancel = true;
                tcs.TrySetResult(new WebAuthResult
                {
                    Status = WebAuthStatus.Success,
                    CallbackUri = e.RequestUri
                });
            }
        }

        void OnClosing(object? sender, EventArgs e)
        {
            tcs.TrySetResult(new WebAuthResult
            {
                Status = WebAuthStatus.UserCancel
            });
        }
    }
}
