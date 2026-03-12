using Agibuild.Fulora;
using Avalonia.Controls;
using AvaloniVue.Bridge.Services;

namespace AvaloniVue.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        WebView.EnvironmentOptions = new WebViewEnvironmentOptions { EnableDevTools = true };

        Loaded += async (_, _) =>
        {
            await WebView.BootstrapSpaProfileAsync(new SpaBootstrapProfileOptions
            {
                BootstrapOptions = new SpaBootstrapOptions
                {
    #if DEBUG
                    DevServerUrl = "http://localhost:5174",
    #else
                    EmbeddedResourcePrefix = "wwwroot",
                    ResourceAssembly = typeof(MainWindow).Assembly,
#endif
                },
                Extensions =
                [
                    new SpaBootstrapProfileExtension
                    {
                        Id = "vue-sample-services",
                        Configure = (bridge, _, _) =>
                        {
                            bridge.Expose<IAppShellService>(new AppShellService());
                            bridge.Expose<ISystemInfoService>(new SystemInfoService());
                            bridge.Expose<IChatService>(new ChatService());
                            bridge.Expose<IFileService>(new FileService());
                            bridge.Expose<ISettingsService>(new SettingsService());
                        }
                    }
                ]
            });
        };
    }
}
