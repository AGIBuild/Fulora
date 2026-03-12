using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Agibuild.Fulora.Shell;

/// <summary>
/// Options for tracking a window under the chrome provider.
/// </summary>
public sealed class WindowChromeTrackingOptions
{
    /// <summary>
    /// Whether to enable custom chrome (extend client area to decorations).
    /// </summary>
    public bool CustomChrome { get; init; } = true;

    /// <summary>
    /// Height of the drag region from the top of the window, in device-independent pixels.
    /// </summary>
    public double DragRegionHeight { get; init; } = 32;

    /// <summary>
    /// Optional override for interactive element detection.
    /// Return <c>true</c> to treat the source as interactive (excluded from drag).
    /// </summary>
    public Func<object?, bool>? IsInteractiveOverride { get; init; }
}

/// <summary>
/// Avalonia-based <see cref="IWindowChromeProvider"/> that manages window transparency,
/// custom chrome drag regions, and appearance for one or more tracked Avalonia windows.
/// </summary>
public sealed class AvaloniaWindowChromeProvider : IWindowChromeProvider, IDisposable
{
    private readonly List<TrackedWindow> _windows = [];
    private readonly object _gate = new();
    private bool _disposed;

    public string Platform { get; } = DetectPlatform();

    public bool SupportsTransparency =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public event EventHandler? AppearanceChanged;

    /// <summary>
    /// Begins tracking a window for appearance management and optional drag region handling.
    /// </summary>
    public void TrackWindow(Window window, WindowChromeTrackingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        options ??= new WindowChromeTrackingOptions();

        lock (_gate)
        {
            if (_windows.Exists(tw => ReferenceEquals(tw.Window, window)))
                return;

            var tracked = new TrackedWindow(window, options);
            _windows.Add(tracked);

            if (options.CustomChrome)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    window.ExtendClientAreaToDecorationsHint = true;
                    window.WindowDecorations = WindowDecorations.Full;
                });

                window.AddHandler(InputElement.PointerPressedEvent,
                    (sender, e) => OnPointerPressed(tracked, e),
                    Avalonia.Interactivity.RoutingStrategies.Tunnel);
            }

            window.Closed += (_, _) => UntrackWindow(window);
        }

        if (Application.Current is { } app)
            app.ActualThemeVariantChanged += OnThemeVariantChanged;
    }

    /// <summary>
    /// Stops tracking a window.
    /// </summary>
    public void UntrackWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        lock (_gate)
            _windows.RemoveAll(tw => ReferenceEquals(tw.Window, window));
    }

    public async Task ApplyWindowAppearanceAsync(WindowAppearanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<TrackedWindow> snapshot;
        lock (_gate)
            snapshot = [.. _windows];

        foreach (var tw in snapshot)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ApplyAppearanceToWindow(tw.Window, request);
            });
        }
    }

    public TransparencyEffectiveState GetTransparencyState()
    {
        TrackedWindow? primary;
        lock (_gate)
            primary = _windows.Count > 0 ? _windows[0] : null;

        if (primary is null)
        {
            return new TransparencyEffectiveState
            {
                IsEnabled = false,
                IsEffective = false,
                Level = TransparencyLevel.None,
                AppliedOpacityPercent = 0,
                ValidationMessage = "No tracked windows."
            };
        }

        var level = ReadActualTransparencyLevel(primary.Window);
        var isEffective = level != TransparencyLevel.None;

        return new TransparencyEffectiveState
        {
            IsEnabled = isEffective,
            IsEffective = isEffective,
            Level = level,
            AppliedOpacityPercent = 0,
            ValidationMessage = isEffective
                ? $"Transparency is active. Effective level: {level}."
                : "Platform reported no transparency level."
        };
    }

    public WindowChromeMetrics GetChromeMetrics()
    {
        TrackedWindow? primary;
        lock (_gate)
            primary = _windows.Count > 0 ? _windows[0] : null;

        var dragHeight = primary?.Options.DragRegionHeight ?? 32;

        return new WindowChromeMetrics
        {
            TitleBarHeight = dragHeight,
            DragRegionHeight = dragHeight,
            SafeInsets = new WindowSafeInsets()
        };
    }

    private static void ApplyAppearanceToWindow(Window window, WindowAppearanceRequest request)
    {
        window.ExtendClientAreaToDecorationsHint = true;
        window.WindowDecorations = WindowDecorations.Full;

        var pct = request.OpacityPercent / 100d;
        var windowAlpha = (byte)Math.Clamp((int)(30 + pct * 210), 30, 240);

        var isDark = request.EffectiveThemeMode == "liquid";
        var tintedBackground = isDark
            ? new SolidColorBrush(Color.FromArgb(windowAlpha, 9, 18, 35))
            : new SolidColorBrush(Color.FromArgb(windowAlpha, 248, 250, 252));

        if (request.EnableTransparency)
        {
            window.TransparencyLevelHint = BuildTransparencyLevelHint();
            window.Background = tintedBackground;
        }
        else
        {
            window.TransparencyLevelHint = [WindowTransparencyLevel.None];
            window.Background = isDark
                ? new SolidColorBrush(Color.FromRgb(5, 9, 20))
                : new SolidColorBrush(Color.FromRgb(248, 250, 252));
        }
    }

    private void OnPointerPressed(TrackedWindow tracked, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(tracked.Window).Properties.IsLeftButtonPressed)
            return;

        var position = e.GetPosition(tracked.Window);
        if (position.Y > tracked.Options.DragRegionHeight)
            return;

        if (IsInteractiveElement(tracked, e.Source))
            return;

        tracked.Window.BeginMoveDrag(e);
        e.Handled = true;
    }

    internal static bool IsInteractiveElement(TrackedWindow tracked, object? source)
    {
        if (tracked.Options.IsInteractiveOverride is { } customCheck && customCheck(source))
            return true;

        return IsDefaultInteractiveElement(source);
    }

    internal static bool IsDefaultInteractiveElement(object? source)
    {
        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Button or ToggleButton or TextBox or ComboBox or Slider)
                return true;
        }

        return false;
    }

    private void OnThemeVariantChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
            AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    private static TransparencyLevel ReadActualTransparencyLevel(Window window)
    {
        var property = window.GetType().GetProperty("TransparencyLevel")
            ?? window.GetType().GetProperty("ActualTransparencyLevel");
        var value = property?.GetValue(window);
        return ParseTransparencyLevel(value?.ToString());
    }

    internal static TransparencyLevel ParseTransparencyLevel(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return TransparencyLevel.None;

        var lower = raw.Trim().ToLowerInvariant();
        if (lower.Contains("acrylic", StringComparison.Ordinal))
            return TransparencyLevel.AcrylicBlur;
        if (lower.Contains("mica", StringComparison.Ordinal))
            return TransparencyLevel.Mica;
        if (lower.Contains("blur", StringComparison.Ordinal))
            return TransparencyLevel.Blur;
        if (lower.Contains("transparent", StringComparison.Ordinal) || lower.Contains("translucent", StringComparison.Ordinal))
            return TransparencyLevel.Transparent;

        return TransparencyLevel.None;
    }

    private static WindowTransparencyLevel[] BuildTransparencyLevelHint()
    {
        string[] preferenceOrder =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? ["Mica", "AcrylicBlur", "Blur", "Transparent"]
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? ["Blur", "Transparent"]
                    : ["AcrylicBlur", "Blur", "Transparent"];

        var result = new List<WindowTransparencyLevel>(4);
        foreach (var name in preferenceOrder)
        {
            if (!TryResolveTransparencyLevelByName(name, out var level))
                continue;
            if (level == WindowTransparencyLevel.None || result.Contains(level))
                continue;
            result.Add(level);
        }

        if (result.Count == 0)
        {
            result.Add(WindowTransparencyLevel.Blur);
            result.Add(WindowTransparencyLevel.Transparent);
        }

        return [.. result];
    }

    private static bool TryResolveTransparencyLevelByName(string memberName, out WindowTransparencyLevel level)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase;
        var type = typeof(WindowTransparencyLevel);

        if (type.GetProperty(memberName, Flags)?.GetValue(null) is WindowTransparencyLevel propertyValue)
        {
            level = propertyValue;
            return true;
        }

        if (type.GetField(memberName, Flags)?.GetValue(null) is WindowTransparencyLevel fieldValue)
        {
            level = fieldValue;
            return true;
        }

        level = WindowTransparencyLevel.None;
        return false;
    }

    private static string DetectPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "macOS";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "Windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "Linux";
        return "Unknown";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (Application.Current is { } app)
            app.ActualThemeVariantChanged -= OnThemeVariantChanged;

        lock (_gate)
            _windows.Clear();
    }

    internal sealed class TrackedWindow(Window window, WindowChromeTrackingOptions options)
    {
        public Window Window { get; } = window;
        public WindowChromeTrackingOptions Options { get; } = options;
    }
}
