using Agibuild.Fulora.Integration.Tests.Controls;
using Agibuild.Fulora.Integration.Tests.ViewModels;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Agibuild.Fulora.Integration.Tests.Views;

public partial class WebView2SmokeView : UserControl
{
    private Window? _hostWindow;
    private EventHandler<WindowClosingEventArgs>? _hostWindowClosingHandler;

    public WebView2SmokeView()
    {
        InitializeComponent();

        var host = this.FindControl<AdapterNativeControlHost>("NativeHost");
        host!.HandleCreated += OnHandleCreated;
        host!.HandleDestroyed += OnHandleDestroyed;

        AttachedToVisualTree += (_, _) => HookHostWindowClosing();
        DetachedFromVisualTree += (_, _) => UnhookHostWindowClosing();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnHandleCreated(global::Avalonia.Platform.IPlatformHandle handle)
    {
        if (DataContext is WebView2SmokeViewModel vm)
        {
            vm.SetHostHandle(handle);
        }
    }

    private void OnHandleDestroyed()
    {
        if (DataContext is WebView2SmokeViewModel vm)
        {
            vm.Detach();
        }
    }

    private void HookHostWindowClosing()
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        if (ReferenceEquals(window, _hostWindow))
        {
            return;
        }

        UnhookHostWindowClosing();

        if (window is null)
        {
            return;
        }

        _hostWindow = window;
        _hostWindowClosingHandler = (_, _) =>
        {
            if (DataContext is WebView2SmokeViewModel vm)
            {
                vm.Detach();
            }
        };
        _hostWindow.Closing += _hostWindowClosingHandler;
    }

    private void UnhookHostWindowClosing()
    {
        if (_hostWindow is not null && _hostWindowClosingHandler is not null)
        {
            _hostWindow.Closing -= _hostWindowClosingHandler;
        }

        _hostWindow = null;
        _hostWindowClosingHandler = null;
    }
}
