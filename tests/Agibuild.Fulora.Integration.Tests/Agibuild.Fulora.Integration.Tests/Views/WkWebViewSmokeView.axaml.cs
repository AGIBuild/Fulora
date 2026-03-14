using Agibuild.Fulora.Integration.Tests.Controls;
using Agibuild.Fulora.Integration.Tests.ViewModels;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Agibuild.Fulora.Integration.Tests.Views;

public partial class WkWebViewSmokeView : UserControl
{
    public WkWebViewSmokeView()
    {
        InitializeComponent();

        var host = this.FindControl<AdapterNativeControlHost>("NativeHost");
        host!.HandleCreated += OnHandleCreated;
        host!.HandleDestroyed += OnHandleDestroyed;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnHandleCreated(global::Avalonia.Platform.IPlatformHandle handle)
    {
        if (DataContext is WkWebViewSmokeViewModel vm)
        {
            vm.SetHostHandle(handle);
        }
    }

    private void OnHandleDestroyed()
    {
        if (DataContext is WkWebViewSmokeViewModel vm)
        {
            vm.Detach();
        }
    }
}
