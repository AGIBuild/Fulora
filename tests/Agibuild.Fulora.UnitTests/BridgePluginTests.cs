using Agibuild.Fulora;
using Agibuild.Fulora.Plugin.AuthToken;
using Agibuild.Fulora.Plugin.Biometric;
using Agibuild.Fulora.Plugin.Database;
using Agibuild.Fulora.Plugin.FileSystem;
using Agibuild.Fulora.Plugin.HttpClient;
using Agibuild.Fulora.Plugin.LocalStorage;
using Agibuild.Fulora.Plugin.Notifications;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

#region Test Doubles

[JsExport]
public interface ITestPluginServiceA
{
    Task<string> GetValue();
}

[JsExport]
public interface ITestPluginServiceB
{
    Task<int> Add(int a, int b);
}

internal sealed class TestServiceA : ITestPluginServiceA
{
    public string Value { get; init; } = "default";
    public Task<string> GetValue() => Task.FromResult(Value);
}

internal sealed class TestServiceB : ITestPluginServiceB
{
    public Task<int> Add(int a, int b) => Task.FromResult(a + b);
}

internal sealed class TwoServicePlugin : IBridgePlugin
{
    public static BridgePluginMetadata GetMetadata()
        => new(
            "test.two-service",
            ["test.required"],
            ["test.optional"],
            ["Test-only manifest for bridge registration coverage."],
            ["desktop"]);

    public static IEnumerable<BridgePluginServiceDescriptor> GetServices()
    {
        yield return BridgePluginServiceDescriptor.Create<ITestPluginServiceA>(
            _ => new TestServiceA());
        yield return BridgePluginServiceDescriptor.Create<ITestPluginServiceB>(
            _ => new TestServiceB());
    }
}

internal sealed class DiAwarePlugin : IBridgePlugin
{
    public static BridgePluginMetadata GetMetadata()
        => new(
            "test.di-aware",
            ["test.di"],
            [],
            ["Uses optional service provider injection during registration."],
            ["desktop"]);

    public static IEnumerable<BridgePluginServiceDescriptor> GetServices()
    {
        yield return BridgePluginServiceDescriptor.Create<ITestPluginServiceA>(
            sp =>
            {
                var config = sp?.GetService(typeof(string)) as string;
                return new TestServiceA { Value = config ?? "no-di" };
            });
    }
}

internal sealed class OptionsPlugin : IBridgePlugin
{
    public static BridgeOptions? LastAppliedOptions { get; private set; }

    public static BridgePluginMetadata GetMetadata()
        => new(
            "test.options",
            ["test.options.required"],
            [],
            ["Carries per-service bridge options in the manifest."],
            ["desktop"]);

    public static IEnumerable<BridgePluginServiceDescriptor> GetServices()
    {
        var opts = new BridgeOptions
        {
            RateLimit = new RateLimit(10, TimeSpan.FromSeconds(1))
        };
        yield return BridgePluginServiceDescriptor.Create<ITestPluginServiceA>(
            _ => new TestServiceA(), opts);
    }
}

internal sealed class EmptyPlugin : IBridgePlugin
{
    public static BridgePluginMetadata GetMetadata()
        => new(
            "test.empty",
            [],
            [],
            ["Empty plugin used to verify no-op registration behavior."],
            ["test-only"]);

    public static IEnumerable<BridgePluginServiceDescriptor> GetServices()
        => [];
}

internal sealed class TrackingBridgeService : IBridgeService
{
    public List<(Type InterfaceType, object Implementation, BridgeOptions? Options)> Exposed { get; } = new();

    public void Expose<T>(T implementation, BridgeOptions? options = null) where T : class
    {
        Exposed.Add((typeof(T), implementation, options));
    }

    public T GetProxy<T>() where T : class => throw new NotImplementedException();
    public void Remove<T>() where T : class => throw new NotImplementedException();
}

internal sealed class FakeServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, object> _services = new();
    public void Register<T>(T service) where T : class => _services[typeof(T)] = service;
    public object? GetService(Type serviceType) => _services.GetValueOrDefault(serviceType);
}

#endregion

public class BridgePluginTests
{
    [Fact]
    public void UsePlugin_MetadataCanBeReadViaPluginContract()
    {
        var metadata = BridgePluginExtensions.GetMetadata<TwoServicePlugin>();

        Assert.Equal("test.two-service", metadata.PluginId);
        Assert.Equal(["test.required"], metadata.RequiredCapabilities);
        Assert.Equal(["test.optional"], metadata.OptionalCapabilities);
        Assert.Equal(["Test-only manifest for bridge registration coverage."], metadata.SecurityNotes);
        Assert.Equal(["desktop"], metadata.PlatformConstraints);
    }

    [Fact]
    public void OfficialPlugins_ExposeCapabilityMetadata()
    {
        var manifests = new[]
        {
            (BridgePluginExtensions.GetMetadata<FileSystemPlugin>(),
                Required: new[] { "plugin.filesystem.read", "plugin.filesystem.write" },
                Optional: new[] { "plugin.filesystem.pick" },
                Constraints: new[] { "desktop-hosts", "sandbox-or-policy-governed-paths" }),
            (BridgePluginExtensions.GetMetadata<HttpClientPlugin>(),
                Required: new[] { "plugin.http.outbound" },
                Optional: Array.Empty<string>(),
                Constraints: new[] { "desktop-hosts", "policy-governed-network-egress" }),
            (BridgePluginExtensions.GetMetadata<NotificationPlugin>(),
                Required: new[] { "plugin.notification.post" },
                Optional: Array.Empty<string>(),
                Constraints: new[] { "desktop-hosts", "platform-delivery-variance" }),
            (BridgePluginExtensions.GetMetadata<AuthTokenPlugin>(),
                Required: new[] { "plugin.auth.token.read", "plugin.auth.token.write" },
                Optional: Array.Empty<string>(),
                Constraints: new[] { "desktop-hosts", "secure-storage-provider-required-for-production" }),
            (BridgePluginExtensions.GetMetadata<BiometricPlugin>(),
                Required: new[] { "plugin.biometric.prompt" },
                Optional: Array.Empty<string>(),
                Constraints: new[] { "windows", "macos", "ios", "android" }),
            (BridgePluginExtensions.GetMetadata<LocalStoragePlugin>(),
                Required: new[] { "plugin.localstorage.read", "plugin.localstorage.write" },
                Optional: Array.Empty<string>(),
                Constraints: new[] { "desktop-hosts", "app-profile-scope" }),
            (BridgePluginExtensions.GetMetadata<DatabasePlugin>(),
                Required: new[] { "plugin.database.read", "plugin.database.write" },
                Optional: Array.Empty<string>(),
                Constraints: new[] { "desktop-hosts", "connection-options-required-for-production" })
        };

        foreach (var (metadata, required, optional, constraints) in manifests)
        {
            Assert.False(string.IsNullOrWhiteSpace(metadata.PluginId));
            Assert.Equal(required, metadata.RequiredCapabilities);
            Assert.Equal(optional, metadata.OptionalCapabilities);
            Assert.NotEmpty(metadata.SecurityNotes);
            Assert.Equal(constraints, metadata.PlatformConstraints);
        }
    }

    [Fact]
    public void UsePlugin_RegistersAllServices()
    {
        var bridge = new TrackingBridgeService();

        bridge.UsePlugin<TwoServicePlugin>();

        Assert.Equal(2, bridge.Exposed.Count);
        Assert.Contains(bridge.Exposed, e => e.InterfaceType == typeof(ITestPluginServiceA));
        Assert.Contains(bridge.Exposed, e => e.InterfaceType == typeof(ITestPluginServiceB));
    }

    [Fact]
    public void UsePlugin_FactoryReceivesServiceProvider()
    {
        var bridge = new TrackingBridgeService();
        var sp = new FakeServiceProvider();
        sp.Register("injected-value");

        bridge.UsePlugin<DiAwarePlugin>(sp);

        Assert.Single(bridge.Exposed);
        var impl = bridge.Exposed[0].Implementation as TestServiceA;
        Assert.NotNull(impl);
        Assert.Equal("injected-value", impl.Value);
    }

    [Fact]
    public void UsePlugin_WithoutServiceProvider_FactoryGetsNull()
    {
        var bridge = new TrackingBridgeService();

        bridge.UsePlugin<DiAwarePlugin>();

        var impl = bridge.Exposed[0].Implementation as TestServiceA;
        Assert.NotNull(impl);
        Assert.Equal("no-di", impl.Value);
    }

    [Fact]
    public void UsePlugin_BridgeOptionsApplied()
    {
        var bridge = new TrackingBridgeService();

        bridge.UsePlugin<OptionsPlugin>();

        Assert.Single(bridge.Exposed);
        var (_, _, opts) = bridge.Exposed[0];
        Assert.NotNull(opts);
        Assert.NotNull(opts.RateLimit);
        Assert.Equal(10, opts.RateLimit.MaxCalls);
    }

    [Fact]
    public void UsePlugin_EmptyPlugin_NoRegistrations()
    {
        var bridge = new TrackingBridgeService();

        bridge.UsePlugin<EmptyPlugin>();

        Assert.Empty(bridge.Exposed);
    }

    [Fact]
    public void Descriptor_InterfaceType_MatchesGenericParameter()
    {
        var desc = BridgePluginServiceDescriptor.Create<ITestPluginServiceA>(_ => new TestServiceA());
        Assert.Equal(typeof(ITestPluginServiceA), desc.InterfaceType);
    }

    [Fact]
    public void UsePlugin_ThrowsOnNullBridge()
    {
        Assert.Throws<ArgumentNullException>(() =>
            BridgePluginExtensions.UsePlugin<TwoServicePlugin>(null!));
    }
}
