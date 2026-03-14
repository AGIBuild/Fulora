using System.Runtime.Versioning;
using System.Threading.Tasks;
using Agibuild.Fulora.Integration.Tests;
using Avalonia;
using Avalonia.Browser;

internal sealed partial class Program
{
    private static Task Main(string[] args) => BuildAvaloniaApp()
            .WithInterFont()
            .StartBrowserAppAsync("out");

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
