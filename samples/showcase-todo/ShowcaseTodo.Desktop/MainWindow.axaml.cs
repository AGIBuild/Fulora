using System.Diagnostics;
using Agibuild.Fulora;
using Avalonia.Controls;
using ShowcaseTodo.Bridge;

namespace ShowcaseTodo.Desktop;

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
                    DevServerUrl = "http://localhost:5176",
#else
                    EmbeddedResourcePrefix = "wwwroot",
                    ResourceAssembly = typeof(MainWindow).Assembly,
#endif
                    ErrorPageFactory = ex =>
                    {
                        Debug.WriteLine($"Navigation failed: {ex.Message}");
                        return "<html><body style='font-family:system-ui;padding:2em;color:#333'>" +
                               "<h2>Navigation failed</h2>" +
                               $"<p>{ex.Message}</p>" +
#if DEBUG
                               "<p>Run the Vite dev server: <code>cd ShowcaseTodo.Web && npm run dev</code></p>" +
#endif
                               "</body></html>";
                    }
                },
                Extensions =
                [
                    new SpaBootstrapProfileExtension
                    {
                        Id = "todo-sample-services",
                        Configure = (bridge, _, _) => bridge.Expose<ITodoService>(new TodoService())
                    }
                ]
            });
        };
    }
}
