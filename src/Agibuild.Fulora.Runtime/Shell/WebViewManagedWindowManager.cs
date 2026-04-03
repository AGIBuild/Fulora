using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Agibuild.Fulora;

namespace Agibuild.Fulora.Shell;

internal sealed class WebViewManagedWindowManager
{
    private readonly WebViewShellExperienceOptions _options;
    private readonly Guid _rootWindowId;
    private readonly WebViewShellSessionDecision? _sessionDecision;
    private readonly WebViewSessionPermissionProfile? _rootProfile;
    private readonly ShellWindowingRuntime _windowingRuntime;
    private readonly Action<WebViewShellPolicyDomain, Exception> _reportPolicyFailure;
    private readonly Action<Guid, Guid?, string, WebViewSessionPermissionProfile, WebViewShellSessionDecision, WebViewPermissionKind?, WebViewPermissionProfileDecision> _raiseSessionPermissionProfileDiagnostic;
    private readonly Action<WebViewManagedWindowLifecycleEventArgs> _onManagedWindowLifecycleChanged;
    private readonly object _managedWindowsLock = new();
    private readonly Dictionary<Guid, ManagedWindowEntry> _managedWindows = new();

    public WebViewManagedWindowManager(
        WebViewShellExperienceOptions options,
        Guid rootWindowId,
        WebViewShellSessionDecision? sessionDecision,
        WebViewSessionPermissionProfile? rootProfile,
        ShellWindowingRuntime windowingRuntime,
        Action<WebViewShellPolicyDomain, Exception> reportPolicyFailure,
        Action<Guid, Guid?, string, WebViewSessionPermissionProfile, WebViewShellSessionDecision, WebViewPermissionKind?, WebViewPermissionProfileDecision> raiseSessionPermissionProfileDiagnostic,
        Action<WebViewManagedWindowLifecycleEventArgs> onManagedWindowLifecycleChanged)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _rootWindowId = rootWindowId;
        _sessionDecision = sessionDecision;
        _rootProfile = rootProfile;
        _windowingRuntime = windowingRuntime ?? throw new ArgumentNullException(nameof(windowingRuntime));
        _reportPolicyFailure = reportPolicyFailure ?? throw new ArgumentNullException(nameof(reportPolicyFailure));
        _raiseSessionPermissionProfileDiagnostic = raiseSessionPermissionProfileDiagnostic
                                                  ?? throw new ArgumentNullException(nameof(raiseSessionPermissionProfileDiagnostic));
        _onManagedWindowLifecycleChanged = onManagedWindowLifecycleChanged
                                           ?? throw new ArgumentNullException(nameof(onManagedWindowLifecycleChanged));
    }

    public int ManagedWindowCount
    {
        get
        {
            lock (_managedWindowsLock)
                return _managedWindows.Count;
        }
    }

    public IReadOnlyList<Guid> GetManagedWindowIds()
    {
        lock (_managedWindowsLock)
            return [.. _managedWindows.Keys];
    }

    public bool TryGetManagedWindow(Guid windowId, out IWebView? managedWindow)
    {
        lock (_managedWindowsLock)
        {
            if (_managedWindows.TryGetValue(windowId, out var entry))
            {
                managedWindow = entry.Window;
                return true;
            }
        }

        managedWindow = null;
        return false;
    }

    public bool TryCreateManagedWindow(Guid windowId, Uri? targetUri, string? scopeIdentityOverride)
    {
        if (_options.ManagedWindowFactory is null)
            return false;

        var scopeIdentity = string.IsNullOrWhiteSpace(scopeIdentityOverride)
            ? _options.SessionContext.ScopeIdentity
            : scopeIdentityOverride.Trim();

        var sessionContext = _options.SessionContext with
        {
            ScopeIdentity = scopeIdentity,
            WindowId = windowId,
            ParentWindowId = _rootWindowId,
            RequestUri = targetUri
        };

        var sessionDecision = _windowingRuntime.ResolveSessionDecision(sessionContext, _sessionDecision);

        WebViewSessionPermissionProfile? resolvedProfile = null;
        var profileIdentity = _rootProfile?.ProfileIdentity;
        if (_options.SessionPermissionProfileResolver is not null)
        {
            var profileContext = new WebViewSessionPermissionProfileContext(
                _rootWindowId,
                ParentWindowId: _rootWindowId,
                WindowId: windowId,
                ScopeIdentity: scopeIdentity,
                RequestUri: targetUri,
                PermissionKind: null);

            resolvedProfile = _windowingRuntime.ResolveSessionPermissionProfile(profileContext, _rootProfile);

            if (resolvedProfile is not null)
            {
                sessionDecision = resolvedProfile.ResolveSessionDecision(
                    parentDecision: _sessionDecision,
                    fallbackDecision: sessionDecision,
                    scopeIdentity: scopeIdentity);
                profileIdentity = resolvedProfile.ProfileIdentity;

                if (sessionDecision is not null)
                {
                    _raiseSessionPermissionProfileDiagnostic(
                        windowId,
                        _rootWindowId,
                        scopeIdentity,
                        resolvedProfile,
                        sessionDecision,
                        null,
                        WebViewPermissionProfileDecision.DefaultFallback());
                }
            }
        }

        var createContext = new WebViewManagedWindowCreateContext(
            windowId,
            _rootWindowId,
            targetUri,
            scopeIdentity,
            sessionDecision,
            profileIdentity);

        var managedWindow = _windowingRuntime.CreateManagedWindow(createContext);

        if (managedWindow is null)
            return false;

        var entry = new ManagedWindowEntry(windowId, _rootWindowId, managedWindow, sessionDecision, profileIdentity);
        lock (_managedWindowsLock)
            _managedWindows[windowId] = entry;

        if (!TryTransitionManagedWindowState(entry, WebViewManagedWindowLifecycleState.Created))
            return false;
        if (!TryTransitionManagedWindowState(entry, WebViewManagedWindowLifecycleState.Attached))
            return false;
        if (!TryTransitionManagedWindowState(entry, WebViewManagedWindowLifecycleState.Ready))
            return false;

        return true;
    }

    public async Task<bool> CloseManagedWindowAsync(
        Guid windowId,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ManagedWindowEntry? entry;
        lock (_managedWindowsLock)
        {
            if (!_managedWindows.TryGetValue(windowId, out entry))
                return false;
        }

        if (entry is null)
            return false;

        if (!TryTransitionManagedWindowState(entry, WebViewManagedWindowLifecycleState.Closing))
            return false;

        var closeHandler = _options.ManagedWindowCloseAsync ?? DefaultManagedWindowCloseAsync;
        var closeTimeout = timeout ?? _options.ManagedWindowCloseTimeout;
        var closeSucceeded = true;

        using var timeoutCts = new CancellationTokenSource(closeTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
        try
        {
            await closeHandler(entry.Window, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            closeSucceeded = false;
            _reportPolicyFailure(WebViewShellPolicyDomain.ManagedWindowLifecycle, ex);
        }
        catch (Exception ex)
        {
            closeSucceeded = false;
            _reportPolicyFailure(WebViewShellPolicyDomain.ManagedWindowLifecycle, ex);
        }
        finally
        {
            lock (_managedWindowsLock)
                _managedWindows.Remove(windowId);

            TryTransitionManagedWindowState(entry, WebViewManagedWindowLifecycleState.Closed);
        }

        return closeSucceeded;
    }

    public void DisposeManagedWindows()
    {
        List<ManagedWindowEntry> entries;
        lock (_managedWindowsLock)
        {
            entries = [.. _managedWindows.Values];
            _managedWindows.Clear();
        }

        foreach (var entry in entries)
        {
            if (entry.State is not WebViewManagedWindowLifecycleState.Closing and not WebViewManagedWindowLifecycleState.Closed)
                TryTransitionManagedWindowState(entry, WebViewManagedWindowLifecycleState.Closing);
            try
            {
                entry.Window.Dispose();
            }
            catch (Exception ex)
            {
                _reportPolicyFailure(WebViewShellPolicyDomain.ManagedWindowLifecycle, ex);
            }

            TryTransitionManagedWindowState(entry, WebViewManagedWindowLifecycleState.Closed);
        }
    }

    private static Task DefaultManagedWindowCloseAsync(IWebView webView, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        webView.Dispose();
        return Task.CompletedTask;
    }

    private bool TryTransitionManagedWindowState(ManagedWindowEntry entry, WebViewManagedWindowLifecycleState nextState)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!IsTransitionAllowed(entry.State, nextState))
        {
            _reportPolicyFailure(
                WebViewShellPolicyDomain.ManagedWindowLifecycle,
                new InvalidOperationException($"Invalid managed window lifecycle transition '{entry.State?.ToString() ?? "None"}' -> '{nextState}'."));
            return false;
        }

        entry.State = nextState;
        _onManagedWindowLifecycleChanged(
            new WebViewManagedWindowLifecycleEventArgs(
                entry.WindowId,
                entry.ParentWindowId,
                nextState,
                entry.SessionDecision,
                entry.ProfileIdentity));
        return true;
    }

    private static bool IsTransitionAllowed(
        WebViewManagedWindowLifecycleState? currentState,
        WebViewManagedWindowLifecycleState nextState)
    {
        return currentState switch
        {
            null => nextState == WebViewManagedWindowLifecycleState.Created,
            WebViewManagedWindowLifecycleState.Created => nextState is WebViewManagedWindowLifecycleState.Attached or WebViewManagedWindowLifecycleState.Closing,
            WebViewManagedWindowLifecycleState.Attached => nextState is WebViewManagedWindowLifecycleState.Ready or WebViewManagedWindowLifecycleState.Closing,
            WebViewManagedWindowLifecycleState.Ready => nextState == WebViewManagedWindowLifecycleState.Closing,
            WebViewManagedWindowLifecycleState.Closing => nextState == WebViewManagedWindowLifecycleState.Closed,
            WebViewManagedWindowLifecycleState.Closed => false,
            _ => false
        };
    }

    private sealed class ManagedWindowEntry
    {
        public ManagedWindowEntry(
            Guid windowId,
            Guid parentWindowId,
            IWebView window,
            WebViewShellSessionDecision? sessionDecision,
            string? profileIdentity)
        {
            WindowId = windowId;
            ParentWindowId = parentWindowId;
            Window = window;
            SessionDecision = sessionDecision;
            ProfileIdentity = profileIdentity;
        }

        public Guid WindowId { get; }
        public Guid ParentWindowId { get; }
        public IWebView Window { get; }
        public WebViewShellSessionDecision? SessionDecision { get; }
        public string? ProfileIdentity { get; }
        public WebViewManagedWindowLifecycleState? State { get; set; }
    }
}
