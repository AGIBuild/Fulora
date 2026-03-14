using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Agibuild.Fulora.Integration.Tests.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnGlobalKeyDown;
    }

    /// <summary>
    /// Global keyboard shortcut: F12 or Cmd+Shift+I (macOS) / Ctrl+Shift+I (Windows/Linux)
    /// opens DevTools on the currently active WebView.
    /// </summary>
    private async void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        var isDevToolsShortcut = e.Key == Key.F12
            || (e.Key == Key.I
                && e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                && (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? e.KeyModifiers.HasFlag(KeyModifiers.Meta)
                    : e.KeyModifiers.HasFlag(KeyModifiers.Control)));

        if (!isDevToolsShortcut) return;

        // Find any WebView in the current visual tree and open DevTools on it.
        var webView = this.GetVisualDescendants().OfType<WebView>().FirstOrDefault();
        if (webView is not null)
        {
            await webView.OpenDevToolsAsync();
            e.Handled = true;
        }
    }
}
