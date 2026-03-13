using System;
using System.Threading.Tasks;

namespace Agibuild.Fulora.Shell;

/// <summary>
/// Bridge service implementation that exposes OS theme information to JavaScript.
/// Uses <see cref="IPlatformThemeProvider"/> for OS-specific detection and
/// <see cref="BridgeEvent{T}"/> for push-based theme change notifications.
/// </summary>
public sealed class ThemeService : IThemeService, IDisposable
{
    private readonly IPlatformThemeProvider _provider;
    private readonly BridgeEvent<ThemeChangedEvent> _themeChanged = new();
    private string _lastMode;
    private bool _disposed;

    /// <summary>Creates a theme service using the given platform provider.</summary>
    public ThemeService(IPlatformThemeProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _lastMode = _provider.GetThemeMode();
        _provider.ThemeChanged += OnProviderThemeChanged;
    }

    /// <inheritdoc />
    public IBridgeEvent<ThemeChangedEvent> ThemeChanged => _themeChanged;

    /// <inheritdoc />
    public Task<ThemeInfo> GetCurrentTheme()
    {
        return Task.FromResult(BuildThemeInfo());
    }

    /// <inheritdoc />
    public Task<string?> GetAccentColor()
    {
        return Task.FromResult(_provider.GetAccentColor());
    }

    /// <inheritdoc />
    public Task<bool> GetHighContrastMode()
    {
        return Task.FromResult(_provider.GetIsHighContrast());
    }

    private void OnProviderThemeChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;

        var newMode = _provider.GetThemeMode();
        var previousMode = _lastMode;

        // Deduplicate: only fire when the effective mode actually changed
        if (string.Equals(newMode, previousMode, StringComparison.Ordinal))
            return;

        _lastMode = newMode;

        _themeChanged.Emit(new ThemeChangedEvent
        {
            CurrentTheme = BuildThemeInfo(),
            PreviousMode = previousMode
        });
    }

    private ThemeInfo BuildThemeInfo() => new()
    {
        Mode = _provider.GetThemeMode(),
        AccentColor = _provider.GetAccentColor(),
        IsHighContrast = _provider.GetIsHighContrast()
    };

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _provider.ThemeChanged -= OnProviderThemeChanged;
    }
}
