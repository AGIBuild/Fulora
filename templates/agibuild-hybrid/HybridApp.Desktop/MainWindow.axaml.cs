using Agibuild.Fulora;
using Avalonia.Controls;
using Avalonia.Input;
using HybridApp.Bridge;

namespace HybridApp.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            InitializeShellPreset();

#if DEBUG
            DevToolsOverlay.Attach(WebView);
            DevToolsOverlay.RegisterToggleShortcut(this,
                new KeyGesture(Key.D, KeyModifiers.Control | KeyModifiers.Shift));
#endif

            await WebView.BootstrapSpaProfileAsync(new SpaBootstrapProfileOptions
            {
                BootstrapOptions = new SpaBootstrapOptions
                {
                    EmbeddedResourcePrefix = "wwwroot",
                    ResourceAssembly = typeof(MainWindow).Assembly
                },
                Extensions =
                [
                    new SpaBootstrapProfileExtension
                    {
                        Id = "template-greeter-service",
                        Configure = (bridge, _, _) =>
                        {
                            bridge.Expose<IGreeterService>(new GreeterServiceImpl());
                            RegisterShellPresetBridgeServices();
                        }
                    }
                ]
            });
        };

        Unloaded += (_, _) =>
        {
            DevToolsOverlay.Dispose();
            DisposeShellPreset();
        };
    }

    partial void InitializeShellPreset();
    partial void DisposeShellPreset();
    partial void RegisterShellPresetBridgeServices();
}
