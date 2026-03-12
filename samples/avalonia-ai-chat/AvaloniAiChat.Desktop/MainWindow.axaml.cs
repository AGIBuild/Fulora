using Agibuild.Fulora;
using Agibuild.Fulora.AI;
using Agibuild.Fulora.AI.Ollama;
using Agibuild.Fulora.Shell;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniAiChat.Bridge.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AvaloniAiChat.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        AlignInitialBackgroundToSystemTheme();

        WebView.EnvironmentOptions = new WebViewEnvironmentOptions { EnableDevTools = true, TransparentBackground = true };

        Loaded += async (_, _) =>
        {
            var runtime = ResolveRuntime();

            var services = new ServiceCollection();
            services.AddFuloraAi(ai =>
            {
                runtime.ConfigureProvider(ai);
                ai.AddResilience();
                ai.AddMetering();
            });
            var sp = services.BuildServiceProvider();

            var registry = sp.GetRequiredService<IAiProviderRegistry>();
            var chatService = new AiChatService(
                registry.GetChatClient(),
                runtime.BackendName,
                runtime.Endpoint,
                runtime.RequiredModel,
                runtime.UseEchoMode);

            var chromeProvider = new AvaloniaWindowChromeProvider();
            chromeProvider.TrackWindow(this, new WindowChromeTrackingOptions
            {
                CustomChrome = true,
                DragRegionHeight = 28
            });
            var themeProvider = new AvaloniaThemeProvider();
            var shellService = new WindowShellService(chromeProvider, themeProvider);

            WebView.DropCompleted += (_, e) =>
            {
                var file = e.Payload.Files?.FirstOrDefault();
                if (file is not null)
                    chatService.SetDroppedFile(file.Path);
            };

            await WebView.BootstrapSpaProfileAsync(new SpaBootstrapProfileOptions
            {
                BootstrapOptions = new SpaBootstrapOptions
                {
    #if DEBUG
                    DevServerUrl = "http://localhost:5175",
    #else
                    EmbeddedResourcePrefix = "wwwroot",
                    ResourceAssembly = typeof(MainWindow).Assembly,
    #endif
                },
                Extensions =
                [
                    new SpaBootstrapProfileExtension
                    {
                        Id = "ai-chat-services",
                        Configure = (bridge, _, _) =>
                        {
                            bridge.Expose<IAiChatService>(chatService);
                            bridge.Expose<IWindowShellBridgeService>(new WindowShellBridgeServiceAdapter(shellService));
                        }
                    }
                ],
                Teardowns =
                [
                    new SpaBootstrapProfileTeardown
                    {
                        Id = "window-chrome-provider-dispose",
                        Execute = (_, _) => chromeProvider.Dispose()
                    },
                    new SpaBootstrapProfileTeardown
                    {
                        Id = "theme-provider-dispose",
                        Execute = (_, _) => themeProvider.Dispose()
                    }
                ]
            });
        };
    }

    private sealed class WindowShellBridgeServiceAdapter(IWindowShellService inner) : IWindowShellBridgeService
    {
        public Task<WindowShellState> GetWindowShellState() => inner.GetWindowShellState();

        public Task<WindowShellState> UpdateWindowShellSettings(WindowShellSettings settings)
            => inner.UpdateWindowShellSettings(settings);

        public IAsyncEnumerable<WindowShellState> StreamWindowShellState(CancellationToken cancellationToken = default)
            => inner.StreamWindowShellState(cancellationToken);
    }

    private static AiRuntime ResolveRuntime()
    {
        var provider = Environment.GetEnvironmentVariable("AI__PROVIDER");
        if (string.Equals(provider, "echo", StringComparison.OrdinalIgnoreCase))
        {
            return new AiRuntime(
                true,
                new Uri("http://localhost:11434"),
                "echo-demo",
                "Echo (demo mode)",
                ai => ai.AddChatClient("default", new EchoChatClient()));
        }

        var endpoint = new Uri(Environment.GetEnvironmentVariable("AI__ENDPOINT") ?? "http://localhost:11434");
        var model = Environment.GetEnvironmentVariable("AI__MODEL") ?? "qwen2.5:3b";
        return new AiRuntime(
            false,
            endpoint,
            model,
            $"Ollama ({model})",
            ai => ai.AddOllama("default", endpoint, model));
    }

    private sealed record AiRuntime(
        bool UseEchoMode,
        Uri Endpoint,
        string RequiredModel,
        string BackendName,
        Action<FuloraAiBuilder> ConfigureProvider);

    private void AlignInitialBackgroundToSystemTheme()
    {
        var variant = Application.Current?.ActualThemeVariant?.ToString() ?? string.Empty;
        var isDark = variant.Contains("dark", StringComparison.OrdinalIgnoreCase);

        const int defaultOpacity = 78;
        var alpha = (byte)Math.Clamp(30 + (int)(defaultOpacity / 100d * 210), 30, 240);
        Background = isDark
            ? new SolidColorBrush(Color.FromArgb(alpha, 9, 18, 35))
            : new SolidColorBrush(Color.FromArgb(alpha, 248, 250, 252));
    }
}
