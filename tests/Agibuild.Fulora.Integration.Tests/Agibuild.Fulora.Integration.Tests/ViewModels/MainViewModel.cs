using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Agibuild.Fulora.Integration.Tests.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public MainViewModel()
    {
        ConsumerE2E = new ConsumerWebViewE2EViewModel(AppendLog);
        AdvancedE2E = new AdvancedFeaturesE2EViewModel(AppendLog);
        WkWebViewSmoke = new WkWebViewSmokeViewModel(AppendLog);
        WebView2Smoke = new WebView2SmokeViewModel(AppendLog);
        GtkWebViewSmoke = new GtkWebViewSmokeViewModel(AppendLog);
        FeatureE2E = new FeatureE2EViewModel(AppendLog);
    }

    /// <summary>
    /// True on Android / iOS — drives adaptive layout (bottom tab bar vs nav rail).
    /// </summary>
    public static bool IsMobile => OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();

    [ObservableProperty]
    private int _selectedTabIndex;

    // --- Shared log / console ---

    [ObservableProperty]
    private string _sharedLog = string.Empty;

    public void AppendLog(string line)
    {
        SharedLog = $"{SharedLog}{line}{Environment.NewLine}";
        AppLog.Write(line);
    }

    [RelayCommand]
    private void ClearLog() => SharedLog = string.Empty;

    // --- Tab selection commands (for bottom tab bar buttons) ---

    [RelayCommand]
    private void SelectTab0() => SelectedTabIndex = 0;

    [RelayCommand]
    private void SelectTab1() => SelectedTabIndex = 1;

    [RelayCommand]
    private void SelectTab2() => SelectedTabIndex = 2;

    [RelayCommand]
    private void SelectTab3() => SelectedTabIndex = 3;

    [RelayCommand]
    private void SelectTab4() => SelectedTabIndex = 4;

    // --- Page ViewModels (nav order) ---

    /// <summary>Tab 0: Browser — full navigation, JS, HTML, cookies.</summary>
    public ConsumerWebViewE2EViewModel ConsumerE2E { get; }

    /// <summary>Tab 1: Advanced — Dialog, Auth, DevTools, Environment.</summary>
    public AdvancedFeaturesE2EViewModel AdvancedE2E { get; }

    /// <summary>Tab 2 (macOS): WKWebView smoke tests.</summary>
    public WkWebViewSmokeViewModel WkWebViewSmoke { get; }

    /// <summary>Tab 2 (Windows): WebView2 smoke tests.</summary>
    public WebView2SmokeViewModel WebView2Smoke { get; }

    /// <summary>Tab 2 (Linux): WebKitGTK smoke tests.</summary>
    public GtkWebViewSmokeViewModel GtkWebViewSmoke { get; }

    /// <summary>Tab 3: Feature E2E — automated 8-feature test dashboard.</summary>
    public FeatureE2EViewModel FeatureE2E { get; }
}
