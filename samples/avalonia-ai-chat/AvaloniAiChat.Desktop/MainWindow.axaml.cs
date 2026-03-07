using System.Diagnostics;
using Agibuild.Fulora;
using Agibuild.Fulora.AI;
using Agibuild.Fulora.AI.Ollama;
using Avalonia.Controls;
using AvaloniAiChat.Bridge.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AvaloniAiChat.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        WebView.EnvironmentOptions = new WebViewEnvironmentOptions { EnableDevTools = true };

        Loaded += async (_, _) =>
        {
            try
            {
#if DEBUG
                await WebView.NavigateAsync(new Uri("http://localhost:5175"));
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

            var (backendName, configureProvider) = DetectProvider();

            var services = new ServiceCollection();
            services.AddFuloraAi(ai =>
            {
                configureProvider(ai);
                ai.AddResilience();
                ai.AddMetering();
            });
            var sp = services.BuildServiceProvider();

            var registry = sp.GetRequiredService<IAiProviderRegistry>();
            var chatService = new AiChatService(registry.GetChatClient(), backendName);
            WebView.Bridge.Expose<IAiChatService>(chatService);

            WebView.DropCompleted += (_, e) =>
            {
                var file = e.Payload.Files?.FirstOrDefault();
                if (file is not null)
                    chatService.SetDroppedFile(file.Path);
            };
        };
    }

    private static (string BackendName, Action<FuloraAiBuilder> Configure) DetectProvider()
    {
        var endpoint = new Uri(Environment.GetEnvironmentVariable("AI__ENDPOINT") ?? "http://localhost:11434");
        var model = Environment.GetEnvironmentVariable("AI__MODEL");

        if (model is null)
        {
            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var json = http.GetStringAsync($"{endpoint}api/tags").GetAwaiter().GetResult();
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("models", out var models) && models.GetArrayLength() > 0)
                    model = models[0].GetProperty("name").GetString();
            }
            catch { /* Ollama not available */ }
        }

        if (model is not null)
        {
            var capturedModel = model;
            var capturedEndpoint = endpoint;
            return ($"Ollama ({model})", ai => ai.AddOllama("default", capturedEndpoint, capturedModel));
        }

        return ("Echo (demo mode)", ai => ai.AddChatClient("default", new EchoChatClient()));
    }
}
