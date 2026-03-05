using System.Diagnostics;
using Agibuild.Fulora;
using Avalonia.Controls;
using AvaloniAiChat.Bridge.Services;
using Microsoft.Extensions.AI;

namespace AvaloniAiChat.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            try
            {
#if DEBUG
                await WebView.NavigateAsync(new Uri("http://localhost:5173"));
#else
                WebView.EnableSpaHosting(new SpaHostingOptions
                {
                    EmbeddedResourcePrefix = "wwwroot",
                    ResourceAssembly = typeof(MainWindow).Assembly,
                });
                await WebView.NavigateAsync(new Uri("app://localhost/index.html"));
#endif
            }
            catch (WebViewNavigationException ex)
            {
                Debug.WriteLine($"Navigation failed: {ex.Message}");
                await WebView.NavigateToStringAsync(
                    "<html><body style='font-family:system-ui;padding:2em;color:#333'>" +
                    "<h2>Navigation failed</h2>" +
                    $"<p>{ex.Message}</p>" +
#if DEBUG
                    "<p>Make sure the Vite dev server is running:<br>" +
                    "<code>cd AvaloniAiChat.Web && npm run dev</code></p>" +
#endif
                    "</body></html>");
                return;
            }

            var (chatClient, backendName) = CreateChatClient();
            WebView.Bridge.Expose<IAiChatService>(new AiChatService(chatClient, backendName));
        };
    }

    private static (IChatClient Client, string Name) CreateChatClient()
    {
        var provider = Environment.GetEnvironmentVariable("AI__PROVIDER")?.ToLowerInvariant();
        var model = Environment.GetEnvironmentVariable("AI__MODEL") ?? "llama3.2";

        return provider switch
        {
            "ollama" => (
                new OllamaChatClient(
                    new Uri(Environment.GetEnvironmentVariable("AI__ENDPOINT") ?? "http://localhost:11434"),
                    model),
                $"Ollama ({model})"),
            _ => (new EchoChatClient(), "Echo (demo mode)"),
        };
    }
}
