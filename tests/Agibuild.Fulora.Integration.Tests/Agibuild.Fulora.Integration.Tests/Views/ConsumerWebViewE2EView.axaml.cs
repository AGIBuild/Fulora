using Agibuild.Fulora.Integration.Tests.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Agibuild.Fulora.Integration.Tests.Views;

public partial class ConsumerWebViewE2EView : UserControl
{
    public ConsumerWebViewE2EView()
    {
        InitializeComponent();
        // Use Loaded instead of DataContextChanged — by the time Loaded fires
        // the WebView's NativeControlHost has been attached to the visual tree
        // and the native adapter is ready for navigation.
        Loaded += OnViewLoaded;
    }

    private void OnViewLoaded(object? sender, RoutedEventArgs e)
    {
        Loaded -= OnViewLoaded;

        if (DataContext is ConsumerWebViewE2EViewModel vm)
        {
            var webView = this.FindControl<WebView>("WebViewControl");
            if (webView is not null)
            {
                vm.WebViewControl = webView;
                vm.OnWebViewLoaded();
            }
        }
    }

    private void OnAddressKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ConsumerWebViewE2EViewModel vm)
            vm.GoToAddressCommand.Execute(null);
    }

    private void OnScriptKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter
            && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
            && DataContext is ConsumerWebViewE2EViewModel vm)
            vm.RunScriptCommand.Execute(null);
    }
}
