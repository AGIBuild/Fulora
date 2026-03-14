using System.Text.Json;
using Agibuild.Fulora;
using Agibuild.Fulora.Plugin.LocalStorage;
using Agibuild.Fulora.Testing;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Agibuild.Fulora.Integration.Tests.Automation;

public sealed class BridgePluginIntegrationTests
{
    private readonly TestDispatcher _dispatcher = new();

    private (WebViewCore Core, MockWebViewAdapter Adapter) CreateCoreWithBridge()
    {
        var adapter = MockWebViewAdapter.Create();
        var core = new WebViewCore(adapter, _dispatcher);
        core.EnableWebMessageBridge(new WebMessageBridgeOptions
        {
            AllowedOrigins = new HashSet<string> { "*" }
        });
        return (core, adapter);
    }

    [AvaloniaFact]
    public void UsePlugin_RegistersLocalStorageService()
    {
        var (core, _) = CreateCoreWithBridge();

        core.Bridge.UsePlugin<LocalStoragePlugin>();

        core.Dispose();
    }

    [AvaloniaFact]
    public async Task LocalStoragePlugin_SetAndGet_ViaRpc()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fulora-integ-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var storagePath = Path.Combine(tempDir, "storage.json");

        try
        {
            var (core, adapter) = CreateCoreWithBridge();
            var svc = new LocalStorageService(storagePath);
            core.Bridge.Expose<ILocalStorageService>(svc);

            await svc.SetValue("lang", "zh");
            var val = await svc.GetValue("lang");
            Assert.Equal("zh", val);

            var keys = await svc.GetKeys();
            Assert.Single(keys);
            Assert.Equal("lang", keys[0]);

            await svc.Remove("lang");
            Assert.Null(await svc.GetValue("lang"));

            core.Dispose();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task LocalStoragePlugin_Persistence_AcrossServiceRestart()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fulora-integ-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var storagePath = Path.Combine(tempDir, "storage.json");

        try
        {
            var svc1 = new LocalStorageService(storagePath);
            await svc1.SetValue("greeting", "hello 🌍");

            var svc2 = new LocalStorageService(storagePath);
            Assert.Equal("hello 🌍", await svc2.GetValue("greeting"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [AvaloniaFact]
    public void UsePlugin_MultipleDescriptors_AllRegistered()
    {
        var (core, _) = CreateCoreWithBridge();

        core.Bridge.UsePlugin<TwoServiceTestPlugin>();

        core.Dispose();
    }
}

[JsExport]
internal interface IPluginTestServiceA
{
    Task<string> Ping();
}

[JsExport]
internal interface IPluginTestServiceB
{
    Task<int> Add(int a, int b);
}

internal sealed class PluginTestServiceA : IPluginTestServiceA
{
    public Task<string> Ping() => Task.FromResult("pong");
}

internal sealed class PluginTestServiceB : IPluginTestServiceB
{
    public Task<int> Add(int a, int b) => Task.FromResult(a + b);
}

internal sealed class TwoServiceTestPlugin : IBridgePlugin
{
    public static IEnumerable<BridgePluginServiceDescriptor> GetServices()
    {
        yield return BridgePluginServiceDescriptor.Create<IPluginTestServiceA>(
            _ => new PluginTestServiceA());
        yield return BridgePluginServiceDescriptor.Create<IPluginTestServiceB>(
            _ => new PluginTestServiceB());
    }
}
