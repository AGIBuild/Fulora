using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Agibuild.Fulora.Shell;

/// <summary>
/// Framework-level shell-window service. Manages global appearance settings (theme,
/// transparency, chrome metrics) across all tracked windows. Designed as an app-level
/// singleton shared across multiple WebViews via <c>Bridge.Expose&lt;IWindowShellService&gt;()</c>.
/// </summary>
public sealed class WindowShellService : IWindowShellService, IDisposable
{
    private static readonly string[] ValidThemePreferences = ["system", "liquid", "classic"];

    private readonly IWindowChromeProvider _chromeProvider;
    private readonly IPlatformThemeProvider _themeProvider;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _stateSignal = new(0, 256);
    private WindowShellSettings _settings = new();
    private bool _disposed;

    /// <summary>Creates a window shell service with the given chrome and theme providers.</summary>
    public WindowShellService(IWindowChromeProvider chromeProvider, IPlatformThemeProvider themeProvider)
    {
        _chromeProvider = chromeProvider ?? throw new ArgumentNullException(nameof(chromeProvider));
        _themeProvider = themeProvider ?? throw new ArgumentNullException(nameof(themeProvider));

        _themeProvider.ThemeChanged += OnThemeChanged;
        _chromeProvider.AppearanceChanged += OnAppearanceChanged;
    }

    /// <inheritdoc />
    public Task<WindowShellState> GetWindowShellState()
        => Task.FromResult(BuildState());

    /// <inheritdoc />
    public async Task<WindowShellState> UpdateWindowShellSettings(WindowShellSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_gate)
            _settings = ClampSettings(settings);

        var effectiveTheme = ResolveEffectiveTheme();
        await _chromeProvider.ApplyWindowAppearanceAsync(new WindowAppearanceRequest
        {
            EnableTransparency = _settings.EnableTransparency,
            OpacityPercent = _settings.GlassOpacityPercent,
            EffectiveThemeMode = effectiveTheme
        });

        NotifyStateChanged();
        return BuildState();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<WindowShellState> StreamWindowShellState(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var current = BuildState();
        var lastSignature = BuildSignature(current);
        yield return current;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _ = await _stateSignal.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }

            current = BuildState();
            var signature = BuildSignature(current);
            if (!string.Equals(signature, lastSignature, StringComparison.Ordinal))
            {
                lastSignature = signature;
                yield return current;
            }
        }
    }

    private WindowShellState BuildState()
    {
        WindowShellSettings snapshot;
        lock (_gate)
            snapshot = _settings;

        var effectiveTheme = ResolveEffectiveTheme();
        var transparencyState = _chromeProvider.GetTransparencyState();
        var chromeMetrics = _chromeProvider.GetChromeMetrics();

        var capabilities = BuildCapabilities(snapshot, transparencyState);

        return new WindowShellState
        {
            Settings = snapshot,
            EffectiveThemeMode = effectiveTheme,
            Capabilities = capabilities,
            ChromeMetrics = chromeMetrics
        };
    }

    private WindowShellCapabilities BuildCapabilities(
        WindowShellSettings settings,
        TransparencyEffectiveState transparencyState)
    {
        var isEnabled = settings.EnableTransparency;
        var isEffective = isEnabled && transparencyState.IsEffective;
        var level = isEffective ? transparencyState.Level : TransparencyLevel.None;
        var validationMessage = BuildValidationMessage(isEnabled, isEffective, level);

        return new WindowShellCapabilities
        {
            Platform = _chromeProvider.Platform,
            SupportsTransparency = _chromeProvider.SupportsTransparency,
            IsTransparencyEnabled = isEnabled,
            IsTransparencyEffective = isEffective,
            EffectiveTransparencyLevel = level,
            ValidationMessage = validationMessage,
            AppliedOpacityPercent = settings.GlassOpacityPercent
        };
    }

    private string ResolveEffectiveTheme()
    {
        WindowShellSettings snapshot;
        lock (_gate)
            snapshot = _settings;

        var normalized = snapshot.ThemePreference?.Trim().ToLowerInvariant();
        if (normalized == "liquid")
            return "liquid";
        if (normalized == "classic")
            return "classic";

        var osTheme = _themeProvider.GetThemeMode();
        return osTheme.Contains("dark", StringComparison.OrdinalIgnoreCase) ? "liquid" : "classic";
    }

    internal static WindowShellSettings ClampSettings(WindowShellSettings settings)
    {
        var preference = settings.ThemePreference?.Trim().ToLowerInvariant() switch
        {
            "liquid" => "liquid",
            "classic" => "classic",
            "system" => "system",
            _ => "system"
        };

        var opacity = Math.Clamp(settings.GlassOpacityPercent, 20, 95);

        return new WindowShellSettings
        {
            ThemePreference = preference,
            EnableTransparency = settings.EnableTransparency,
            GlassOpacityPercent = opacity
        };
    }

    internal static string BuildValidationMessage(bool isEnabled, bool isEffective, TransparencyLevel level)
    {
        if (!isEnabled)
            return "Transparency is disabled in appearance settings.";
        if (isEffective && level != TransparencyLevel.None)
            return $"Transparency is active. Effective level: {level}.";
        return "Transparency is enabled but not effective on this platform.";
    }

    internal static string BuildSignature(WindowShellState state)
        => $"{state.Settings.ThemePreference}|{state.Settings.EnableTransparency}|{state.Settings.GlassOpacityPercent}|{state.EffectiveThemeMode}|{state.Capabilities.EffectiveTransparencyLevel}|{state.Capabilities.IsTransparencyEffective}|{state.ChromeMetrics.TitleBarHeight}|{state.ChromeMetrics.DragRegionHeight}|{state.ChromeMetrics.SafeInsets.Top}|{state.ChromeMetrics.SafeInsets.Right}|{state.ChromeMetrics.SafeInsets.Bottom}|{state.ChromeMetrics.SafeInsets.Left}";

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;

        bool followSystem;
        lock (_gate)
            followSystem = string.Equals(_settings.ThemePreference, "system", StringComparison.OrdinalIgnoreCase);

        if (!followSystem) return;

        var effectiveTheme = ResolveEffectiveTheme();
        _ = _chromeProvider.ApplyWindowAppearanceAsync(new WindowAppearanceRequest
        {
            EnableTransparency = _settings.EnableTransparency,
            OpacityPercent = _settings.GlassOpacityPercent,
            EffectiveThemeMode = effectiveTheme
        });

        NotifyStateChanged();
    }

    private void OnAppearanceChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        try { _stateSignal.Release(); }
        catch (SemaphoreFullException) { }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _themeProvider.ThemeChanged -= OnThemeChanged;
        _chromeProvider.AppearanceChanged -= OnAppearanceChanged;
        _stateSignal.Dispose();
    }
}
