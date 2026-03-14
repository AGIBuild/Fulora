using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Agibuild.Fulora.Integration.Tests.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Agibuild.Fulora.Integration.Tests.Views;

public partial class MainView : UserControl
{
    private const int SmokeTabIndex = 2;

    public MainView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private MainViewModel? _vm;

    /// <summary>
    /// Cache page views so that WebView-backed pages are not disposed and recreated
    /// every time the user switches tabs.  Combined with the Panel-based PageHost,
    /// pages stay in the visual tree (only IsVisible is toggled) which prevents
    /// NativeControlHost from detaching / disposing the native WebView handle.
    /// </summary>
    private readonly Dictionary<int, Control?> _pageCache = new();
    private int _currentTabIndex = -1;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;

        _vm = DataContext as MainViewModel;

        // Clear the cache when the ViewModel changes (fresh start)
        _pageCache.Clear();
        _currentTabIndex = -1;
        var host = this.FindControl<Grid>("PageHost");
        host?.Children.Clear();

        if (_vm is not null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
            LoadPage(_vm.SelectedTabIndex);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedTabIndex) && _vm is not null)
            LoadPage(_vm.SelectedTabIndex);
    }

    /// <summary>
    /// Panel-based page management: pages are added as children of a Grid and
    /// kept alive in the visual tree.  Switching tabs toggles IsVisible instead
    /// of replacing ContentControl.Content.  This ensures NativeControlHost-backed
    /// controls (WebView) are never detached, preventing ObjectDisposedException.
    /// </summary>
    private void LoadPage(int index)
    {
        if (_vm is null) return;

        if (_currentTabIndex == SmokeTabIndex &&
            index != SmokeTabIndex &&
            ShouldUseWebView2Smoke())
        {
            // Keep only one active WebView2 instance when leaving the smoke tab.
            // This prevents teardown races between cached smoke adapter and feature WebView.
            _vm.WebView2Smoke.Detach();
        }

        if (!_pageCache.TryGetValue(index, out var content))
        {
            content = index switch
            {
                0 => (Control)new ConsumerWebViewE2EView { DataContext = _vm.ConsumerE2E },
                1 => new AdvancedFeaturesE2EView { DataContext = _vm.AdvancedE2E },
                2 => CreatePlatformSmokeView(),
                3 => new FeatureE2EView { DataContext = _vm.FeatureE2E },
                4 => new ConsoleView { DataContext = _vm },
                _ => null
            };
            _pageCache[index] = content;
        }

        var host = this.FindControl<Grid>("PageHost");
        if (host is null || content is null) return;

        // Add to panel if not already a child
        if (!host.Children.Contains(content))
            host.Children.Add(content);

        // Toggle visibility: hide all, show selected
        foreach (var child in host.Children)
            child.IsVisible = false;
        content.IsVisible = true;
        _currentTabIndex = index;
    }

    /// <summary>
    /// Auto-detect the current platform and show the appropriate smoke test view.
    /// macOS → WKWebView smoke, Windows → WebView2 smoke.
    /// Android/iOS → placeholder (no platform-specific smoke tests available).
    /// </summary>
    private Control? CreatePlatformSmokeView()
    {
        if (_vm is null) return null;

        // Android/iOS don't have WebView2 or WKWebView — show a placeholder.
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
        {
            return new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "N/A",
                        FontSize = 28,
                        FontWeight = FontWeight.Bold,
                        Foreground = Brushes.Gray,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = "WebView2 / WKWebView smoke tests\nare not available on this platform.",
                        FontSize = 13,
                        Foreground = Brushes.Gray,
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    }
                }
            };
        }

        if (OperatingSystem.IsLinux())
            return new GtkWebViewSmokeView { DataContext = _vm.GtkWebViewSmoke };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return new WkWebViewSmokeView { DataContext = _vm.WkWebViewSmoke };

        // Windows and other platforms use WebView2 smoke
        return new WebView2SmokeView { DataContext = _vm.WebView2Smoke };
    }

    private static bool ShouldUseWebView2Smoke()
    {
        if (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() || OperatingSystem.IsLinux())
        {
            return false;
        }

        return !RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    }

    private void OnNavSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Close the hamburger drawer after a nav item is selected
        var toggle = this.FindControl<ToggleButton>("NavToggle");
        if (toggle is not null)
            toggle.IsChecked = false;
    }
}
