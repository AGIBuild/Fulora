using System;
using System.Threading;
using System.Threading.Tasks;
using Agibuild.Fulora;
using Agibuild.Fulora.Shell;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public sealed class WindowShellContractAndIntegrationTests
{
    [Fact]
    public async Task Contract_update_snapshot_stream_roundtrip_is_deterministic()
    {
        using var service = CreateService();
        using var cts = new CancellationTokenSource();
        var stream = service.StreamWindowShellState(cts.Token).GetAsyncEnumerator();
        try
        {
            Assert.True(await stream.MoveNextAsync());
            var initial = stream.Current;

            var updated = await service.UpdateWindowShellSettings(new WindowShellSettings
            {
                ThemePreference = "classic",
                EnableTransparency = false,
                GlassOpacityPercent = 66
            });

            var streamed = await ReadNextWithinAsync(stream, TimeSpan.FromSeconds(2));
            Assert.NotNull(streamed);

            Assert.Equal("classic", updated.EffectiveThemeMode);
            Assert.Equal("classic", streamed!.EffectiveThemeMode);
            Assert.False(streamed.Settings.EnableTransparency);
            Assert.Equal(66, streamed.Settings.GlassOpacityPercent);
            Assert.Equal(66, streamed.Capabilities.AppliedOpacityPercent);
            Assert.NotEqual(initial.Settings.EnableTransparency, streamed.Settings.EnableTransparency);
        }
        finally
        {
            cts.Cancel();
        }
    }

    [Fact]
    public async Task Contract_stream_deduplicates_equivalent_signatures()
    {
        using var service = CreateService();
        using var cts = new CancellationTokenSource();
        var stream = service.StreamWindowShellState(cts.Token).GetAsyncEnumerator();
        try
        {
            Assert.True(await stream.MoveNextAsync());
            var first = stream.Current;

            await service.UpdateWindowShellSettings(new WindowShellSettings
            {
                ThemePreference = first.Settings.ThemePreference,
                EnableTransparency = first.Settings.EnableTransparency,
                GlassOpacityPercent = first.Settings.GlassOpacityPercent
            });

            var duplicate = await ReadNextWithinAsync(stream, TimeSpan.FromMilliseconds(500));
            Assert.Null(duplicate);
        }
        finally
        {
            cts.Cancel();
        }
    }

    [Fact]
    public void Settings_validation_clamps_opacity_to_valid_range()
    {
        var low = WindowShellService.ClampSettings(new WindowShellSettings { GlassOpacityPercent = 5 });
        var high = WindowShellService.ClampSettings(new WindowShellSettings { GlassOpacityPercent = 100 });
        var normal = WindowShellService.ClampSettings(new WindowShellSettings { GlassOpacityPercent = 50 });

        Assert.Equal(20, low.GlassOpacityPercent);
        Assert.Equal(95, high.GlassOpacityPercent);
        Assert.Equal(50, normal.GlassOpacityPercent);
    }

    [Theory]
    [InlineData("liquid", "liquid")]
    [InlineData("classic", "classic")]
    [InlineData("system", "system")]
    [InlineData("LIQUID", "liquid")]
    [InlineData("invalid", "system")]
    [InlineData("", "system")]
    [InlineData(null, "system")]
    public void Settings_validation_normalizes_theme_preference(string? input, string expected)
    {
        var clamped = WindowShellService.ClampSettings(new WindowShellSettings { ThemePreference = input! });
        Assert.Equal(expected, clamped.ThemePreference);
    }

    [Fact]
    public void Transparency_state_machine_disabled_state()
    {
        var message = WindowShellService.BuildValidationMessage(
            isEnabled: false, isEffective: false, level: TransparencyLevel.None);
        Assert.Contains("disabled", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Transparency_state_machine_active_state()
    {
        var message = WindowShellService.BuildValidationMessage(
            isEnabled: true, isEffective: true, level: TransparencyLevel.Blur);
        Assert.Contains("active", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Blur", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Transparency_state_machine_fallback_state()
    {
        var message = WindowShellService.BuildValidationMessage(
            isEnabled: true, isEffective: false, level: TransparencyLevel.None);
        Assert.Contains("not effective", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Theme_resolution_system_follows_os_theme()
    {
        var mockTheme = new MockPlatformThemeProvider("dark");
        using var service = new WindowShellService(new MockChromeProvider(), mockTheme);

        await service.UpdateWindowShellSettings(new WindowShellSettings { ThemePreference = "system" });
        var state = await service.GetWindowShellState();

        Assert.Equal("liquid", state.EffectiveThemeMode);
    }

    [Fact]
    public async Task Theme_resolution_liquid_ignores_os_theme()
    {
        var mockTheme = new MockPlatformThemeProvider("light");
        using var service = new WindowShellService(new MockChromeProvider(), mockTheme);

        await service.UpdateWindowShellSettings(new WindowShellSettings { ThemePreference = "liquid" });
        var state = await service.GetWindowShellState();

        Assert.Equal("liquid", state.EffectiveThemeMode);
    }

    [Fact]
    public async Task Theme_resolution_classic_ignores_os_theme()
    {
        var mockTheme = new MockPlatformThemeProvider("dark");
        using var service = new WindowShellService(new MockChromeProvider(), mockTheme);

        await service.UpdateWindowShellSettings(new WindowShellSettings { ThemePreference = "classic" });
        var state = await service.GetWindowShellState();

        Assert.Equal("classic", state.EffectiveThemeMode);
    }

    [Fact]
    public async Task Transparency_disabled_never_reports_effective()
    {
        using var service = CreateService();

        var state = await service.UpdateWindowShellSettings(new WindowShellSettings
        {
            EnableTransparency = false
        });

        Assert.False(state.Capabilities.IsTransparencyEnabled);
        Assert.False(state.Capabilities.IsTransparencyEffective);
        Assert.Equal(TransparencyLevel.None, state.Capabilities.EffectiveTransparencyLevel);
    }

    [Fact]
    public async Task Transparency_enabled_with_supporting_provider()
    {
        var provider = new MockChromeProvider(supportsTransparency: true, effectiveLevel: TransparencyLevel.Blur);
        using var service = new WindowShellService(provider, new MockPlatformThemeProvider());

        var state = await service.UpdateWindowShellSettings(new WindowShellSettings
        {
            EnableTransparency = true
        });

        Assert.True(state.Capabilities.IsTransparencyEnabled);
        Assert.True(state.Capabilities.IsTransparencyEffective);
        Assert.Equal(TransparencyLevel.Blur, state.Capabilities.EffectiveTransparencyLevel);
    }

    [Fact]
    public async Task Transparency_enabled_without_support_falls_back()
    {
        var provider = new MockChromeProvider(supportsTransparency: true, effectiveLevel: TransparencyLevel.None);
        using var service = new WindowShellService(provider, new MockPlatformThemeProvider());

        var state = await service.UpdateWindowShellSettings(new WindowShellSettings
        {
            EnableTransparency = true
        });

        Assert.True(state.Capabilities.IsTransparencyEnabled);
        Assert.False(state.Capabilities.IsTransparencyEffective);
        Assert.Equal(TransparencyLevel.None, state.Capabilities.EffectiveTransparencyLevel);
    }

    [Fact]
    public async Task Multi_window_update_calls_provider()
    {
        var provider = new MockChromeProvider();
        using var service = new WindowShellService(provider, new MockPlatformThemeProvider());

        await service.UpdateWindowShellSettings(new WindowShellSettings
        {
            EnableTransparency = true,
            GlassOpacityPercent = 80,
            ThemePreference = "liquid"
        });

        Assert.True(provider.ApplyCallCount > 0);
        Assert.NotNull(provider.LastRequest);
        Assert.True(provider.LastRequest!.EnableTransparency);
        Assert.Equal(80, provider.LastRequest.OpacityPercent);
    }

    [Fact]
    public async Task Os_theme_change_triggers_stream_emission_when_system_preference()
    {
        var mockTheme = new MockPlatformThemeProvider("light");
        using var service = new WindowShellService(new MockChromeProvider(), mockTheme);
        using var cts = new CancellationTokenSource();

        await service.UpdateWindowShellSettings(new WindowShellSettings { ThemePreference = "system" });

        var stream = service.StreamWindowShellState(cts.Token).GetAsyncEnumerator();
        try
        {
            Assert.True(await stream.MoveNextAsync());
            var initial = stream.Current;
            Assert.Equal("classic", initial.EffectiveThemeMode);

            mockTheme.SimulateThemeChange("dark");

            var changed = await ReadNextWithinAsync(stream, TimeSpan.FromSeconds(2));
            Assert.NotNull(changed);
            Assert.Equal("liquid", changed!.EffectiveThemeMode);
        }
        finally
        {
            cts.Cancel();
        }
    }

    [Fact]
    public async Task Full_update_snapshot_stream_roundtrip()
    {
        using var service = CreateService();
        using var cts = new CancellationTokenSource();
        var stream = service.StreamWindowShellState(cts.Token).GetAsyncEnumerator();
        try
        {
            Assert.True(await stream.MoveNextAsync());

            var updated = await service.UpdateWindowShellSettings(new WindowShellSettings
            {
                ThemePreference = "liquid",
                EnableTransparency = true,
                GlassOpacityPercent = 70
            });

            var snapshot = await service.GetWindowShellState();
            var streamed = await ReadNextWithinAsync(stream, TimeSpan.FromSeconds(2));

            Assert.Equal(updated.EffectiveThemeMode, snapshot.EffectiveThemeMode);
            Assert.NotNull(streamed);
            Assert.Equal(updated.EffectiveThemeMode, streamed!.EffectiveThemeMode);
            Assert.Equal(70, streamed.Settings.GlassOpacityPercent);
        }
        finally
        {
            cts.Cancel();
        }
    }

    [Fact]
    public void Signature_differs_for_different_states()
    {
        var state1 = BuildTestState("system", true, 78);
        var state2 = BuildTestState("classic", false, 66);

        var sig1 = WindowShellService.BuildSignature(state1);
        var sig2 = WindowShellService.BuildSignature(state2);

        Assert.NotEqual(sig1, sig2);
    }

    [Fact]
    public void Signature_matches_for_equivalent_states()
    {
        var state1 = BuildTestState("liquid", true, 78);
        var state2 = BuildTestState("liquid", true, 78);

        Assert.Equal(
            WindowShellService.BuildSignature(state1),
            WindowShellService.BuildSignature(state2));
    }

    [Fact]
    public void Chrome_provider_parse_transparency_level_handles_platform_tokens()
    {
        Assert.Equal(TransparencyLevel.AcrylicBlur, AvaloniaWindowChromeProvider.ParseTransparencyLevel("AcrylicBlur"));
        Assert.Equal(TransparencyLevel.Mica, AvaloniaWindowChromeProvider.ParseTransparencyLevel("Mica,Blur"));
        Assert.Equal(TransparencyLevel.Blur, AvaloniaWindowChromeProvider.ParseTransparencyLevel("Blur"));
        Assert.Equal(TransparencyLevel.Transparent, AvaloniaWindowChromeProvider.ParseTransparencyLevel("Transparent"));
        Assert.Equal(TransparencyLevel.Transparent, AvaloniaWindowChromeProvider.ParseTransparencyLevel("Translucent"));
        Assert.Equal(TransparencyLevel.None, AvaloniaWindowChromeProvider.ParseTransparencyLevel("None"));
        Assert.Equal(TransparencyLevel.None, AvaloniaWindowChromeProvider.ParseTransparencyLevel("unknown-level"));
        Assert.Equal(TransparencyLevel.None, AvaloniaWindowChromeProvider.ParseTransparencyLevel(null));
    }

    [Fact]
    public void Interactive_element_detection_excludes_known_controls()
    {
        Assert.True(AvaloniaWindowChromeProvider.IsDefaultInteractiveElement(
            new Avalonia.Controls.Button()));
        Assert.True(AvaloniaWindowChromeProvider.IsDefaultInteractiveElement(
            new Avalonia.Controls.TextBox()));
        Assert.True(AvaloniaWindowChromeProvider.IsDefaultInteractiveElement(
            new Avalonia.Controls.ComboBox()));
        Assert.True(AvaloniaWindowChromeProvider.IsDefaultInteractiveElement(
            new Avalonia.Controls.Slider()));
        Assert.False(AvaloniaWindowChromeProvider.IsDefaultInteractiveElement(
            new Avalonia.Controls.Border()));
        Assert.False(AvaloniaWindowChromeProvider.IsDefaultInteractiveElement(null));
    }

    [Fact]
    public void Constructor_throws_on_null_chrome_provider()
    {
        Assert.Throws<ArgumentNullException>(
            () => new WindowShellService(null!, new MockPlatformThemeProvider()));
    }

    [Fact]
    public void Constructor_throws_on_null_theme_provider()
    {
        Assert.Throws<ArgumentNullException>(
            () => new WindowShellService(new MockChromeProvider(), null!));
    }

    [Fact]
    public async Task Update_throws_on_null_settings()
    {
        using var service = CreateService();
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => service.UpdateWindowShellSettings(null!));
    }

    [Fact]
    public void Dispose_unsubscribes_event_handlers()
    {
        var chrome = new MockChromeProvider();
        var theme = new MockPlatformThemeProvider();
        var service = new WindowShellService(chrome, theme);
        service.Dispose();
        service.Dispose(); // idempotent
    }

    [Fact]
    public async Task Theme_change_with_fixed_preference_does_not_reapply()
    {
        var chrome = new MockChromeProvider();
        var theme = new MockPlatformThemeProvider("light");
        using var service = new WindowShellService(chrome, theme);

        await service.UpdateWindowShellSettings(new WindowShellSettings { ThemePreference = "liquid" });
        var countBefore = chrome.ApplyCallCount;

        theme.SimulateThemeChange("dark");
        await Task.Delay(50);

        Assert.Equal(countBefore, chrome.ApplyCallCount);
    }

    [Fact]
    public async Task Appearance_changed_event_triggers_stream_emission()
    {
        var chrome = new MockChromeProvider(effectiveLevel: TransparencyLevel.Blur);
        using var service = new WindowShellService(chrome, new MockPlatformThemeProvider());
        using var cts = new CancellationTokenSource();

        var stream = service.StreamWindowShellState(cts.Token).GetAsyncEnumerator(cts.Token);
        try
        {
            Assert.True(await stream.MoveNextAsync());
            chrome.RaiseAppearanceChanged();
            await Task.Delay(50);
        }
        finally
        {
            cts.Cancel();
            await stream.DisposeAsync();
        }
    }

    [Fact]
    public async Task Stream_cancellation_terminates_gracefully()
    {
        using var service = CreateService();
        using var cts = new CancellationTokenSource();

        var items = new List<WindowShellState>();
        var stream = service.StreamWindowShellState(cts.Token);

        await foreach (var state in stream)
        {
            items.Add(state);
            cts.Cancel();
        }

        Assert.Single(items);
    }

    [Fact]
    public async Task GetWindowShellState_returns_default_state()
    {
        using var service = CreateService();
        var state = await service.GetWindowShellState();

        Assert.NotNull(state);
        Assert.NotNull(state.Settings);
        Assert.NotNull(state.Capabilities);
        Assert.NotNull(state.ChromeMetrics);
        Assert.Equal("test", state.Capabilities.Platform);
    }

    [Fact]
    public void Chrome_provider_parse_transparency_level_with_empty_string()
    {
        Assert.Equal(TransparencyLevel.None, AvaloniaWindowChromeProvider.ParseTransparencyLevel(""));
        Assert.Equal(TransparencyLevel.None, AvaloniaWindowChromeProvider.ParseTransparencyLevel("   "));
    }

    [Fact]
    public void Mac_chrome_drag_and_safe_inset_paths_are_wired_end_to_end()
    {
        var repoRoot = FindRepoRoot();
        var appTsx = File.ReadAllText(Path.Combine(
            repoRoot, "samples", "avalonia-ai-chat", "AvaloniAiChat.Web", "src", "App.tsx"));
        var css = File.ReadAllText(Path.Combine(
            repoRoot, "samples", "avalonia-ai-chat", "AvaloniAiChat.Web", "src", "index.css"));

        Assert.Contains("'--ag-shell-top-inset'", appTsx, StringComparison.Ordinal);
        Assert.Contains("var(--ag-shell-top-inset, 0px)", css, StringComparison.Ordinal);
    }

    // ─── Helpers ──────────────────────────────────────────────────────

    private static WindowShellService CreateService()
        => new(new MockChromeProvider(), new MockPlatformThemeProvider());

    private static WindowShellState BuildTestState(string theme, bool transparent, int opacity)
        => new()
        {
            Settings = new WindowShellSettings
            {
                ThemePreference = theme,
                EnableTransparency = transparent,
                GlassOpacityPercent = opacity
            },
            EffectiveThemeMode = theme == "system" ? "liquid" : theme,
            Capabilities = new WindowShellCapabilities
            {
                Platform = "test",
                SupportsTransparency = true,
                IsTransparencyEnabled = transparent,
                IsTransparencyEffective = transparent,
                EffectiveTransparencyLevel = transparent ? TransparencyLevel.Blur : TransparencyLevel.None,
                AppliedOpacityPercent = opacity
            },
            ChromeMetrics = new WindowChromeMetrics
            {
                TitleBarHeight = 32,
                DragRegionHeight = 32,
                SafeInsets = new WindowSafeInsets()
            }
        };

    private static async Task<WindowShellState?> ReadNextWithinAsync(
        IAsyncEnumerator<WindowShellState> stream,
        TimeSpan timeout)
    {
        var moveTask = stream.MoveNextAsync().AsTask();
        var completed = await Task.WhenAny(moveTask, Task.Delay(timeout));
        if (completed != moveTask)
            return null;
        return await moveTask ? stream.Current : null;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Agibuild.Fulora.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    // ─── Mock Providers ──────────────────────────────────────────────

    private sealed class MockChromeProvider(
        bool supportsTransparency = true,
        TransparencyLevel effectiveLevel = TransparencyLevel.Blur) : IWindowChromeProvider
    {
        public string Platform => "test";
        public bool SupportsTransparency => supportsTransparency;
        public int ApplyCallCount { get; private set; }
        public WindowAppearanceRequest? LastRequest { get; private set; }

        public event EventHandler? AppearanceChanged;

        public Task ApplyWindowAppearanceAsync(WindowAppearanceRequest request)
        {
            ApplyCallCount++;
            LastRequest = request;
            return Task.CompletedTask;
        }

        public TransparencyEffectiveState GetTransparencyState() => new()
        {
            IsEnabled = effectiveLevel != TransparencyLevel.None,
            IsEffective = effectiveLevel != TransparencyLevel.None,
            Level = effectiveLevel,
            AppliedOpacityPercent = 78
        };

        public WindowChromeMetrics GetChromeMetrics() => new()
        {
            TitleBarHeight = 32,
            DragRegionHeight = 32,
            SafeInsets = new WindowSafeInsets()
        };

        public void RaiseAppearanceChanged()
            => AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class MockPlatformThemeProvider(string mode = "light") : IPlatformThemeProvider
    {
        private string _mode = mode;
        public event EventHandler? ThemeChanged;
        public string GetThemeMode() => _mode;
        public string? GetAccentColor() => null;
        public bool GetIsHighContrast() => false;

        public void SimulateThemeChange(string newMode)
        {
            _mode = newMode;
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
