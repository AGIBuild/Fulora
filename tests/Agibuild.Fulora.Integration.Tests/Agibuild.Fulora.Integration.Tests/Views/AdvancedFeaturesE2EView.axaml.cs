using Agibuild.Fulora.Integration.Tests.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Agibuild.Fulora.Integration.Tests.Views;

public partial class AdvancedFeaturesE2EView : UserControl
{
    public AdvancedFeaturesE2EView()
    {
        InitializeComponent();
        Loaded += OnViewLoaded;
    }

    private void OnViewLoaded(object? sender, RoutedEventArgs e)
    {
        Loaded -= OnViewLoaded;

        if (DataContext is AdvancedFeaturesE2EViewModel vm)
        {
            var webView = this.FindControl<WebView>("WebViewControl");
            if (webView is not null)
            {
                vm.WebViewControl = webView;
                vm.OnWebViewLoaded();
            }

            vm.RequestWebViewRecreation += () =>
            {
                RecreateWebView(vm);
            };
        }
    }

    private void RecreateWebView(AdvancedFeaturesE2EViewModel vm)
    {
        var host = this.FindControl<Border>("WebViewHost");
        if (host is null) return;

        host.Child = null;

        var newWebView = new WebView
        {
            Source = new System.Uri("https://github.com")
        };

        host.Child = newWebView;
        vm.WebViewControl = newWebView;
        vm.OnWebViewLoaded();
    }
}
