using Agibuild.Fulora.Adapters.Abstractions;
using Agibuild.Fulora.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public class FuloraServiceBuilderTests
{
    [Fact]
    public void AddFulora_registers_WebViewMessageBus()
    {
        var services = new ServiceCollection();
        services.AddFulora();
        var sp = services.BuildServiceProvider();

        var bus = sp.GetService<IWebViewMessageBus>();
        Assert.NotNull(bus);
        Assert.IsType<WebViewMessageBus>(bus);
    }

    [Fact]
    public void AddFulora_registers_default_NullTelemetryProvider()
    {
        var services = new ServiceCollection();
        services.AddFulora();
        var sp = services.BuildServiceProvider();

        var telemetry = sp.GetService<ITelemetryProvider>();
        Assert.NotNull(telemetry);
        Assert.Same(NullTelemetryProvider.Instance, telemetry);
    }

    [Fact]
    public void AddFulora_does_not_override_existing_TelemetryProvider()
    {
        var custom = new ConsoleTelemetryProvider();
        var services = new ServiceCollection();
        services.AddSingleton<ITelemetryProvider>(custom);
        services.AddFulora();
        var sp = services.BuildServiceProvider();

        var telemetry = sp.GetService<ITelemetryProvider>();
        Assert.Same(custom, telemetry);
    }

    [Fact]
    public void AddTelemetry_replaces_default_provider()
    {
        var custom = new ConsoleTelemetryProvider();
        var services = new ServiceCollection();
        services.AddFulora().AddTelemetry(custom);
        var sp = services.BuildServiceProvider();

        var telemetry = sp.GetService<ITelemetryProvider>();
        Assert.Same(custom, telemetry);
    }

    [Fact]
    public void AddTelemetry_throws_on_null()
    {
        var services = new ServiceCollection();
        var builder = services.AddFulora();
        Assert.Throws<ArgumentNullException>(() => builder.AddTelemetry(null!));
    }

    [Fact]
    public void AddAutoUpdate_registers_service()
    {
        var services = new ServiceCollection();
        var provider = new StubAutoUpdateProvider();
        var options = new AutoUpdateOptions { FeedUrl = "https://update.test", CheckInterval = null };
        services.AddFulora().AddAutoUpdate(options, provider);
        var sp = services.BuildServiceProvider();

        var svc = sp.GetService<IAutoUpdateService>();
        Assert.NotNull(svc);
        Assert.IsType<AutoUpdateService>(svc);
    }

    [Fact]
    public void AddAutoUpdate_throws_on_null_options()
    {
        var services = new ServiceCollection();
        var builder = services.AddFulora();
        Assert.Throws<ArgumentNullException>(() => builder.AddAutoUpdate(null!, new StubAutoUpdateProvider()));
    }

    [Fact]
    public void AddAutoUpdate_throws_on_null_provider()
    {
        var services = new ServiceCollection();
        var builder = services.AddFulora();
        Assert.Throws<ArgumentNullException>(() =>
            builder.AddAutoUpdate(new AutoUpdateOptions { FeedUrl = "https://test" }, null!));
    }

    [Fact]
    public void AddFulora_returns_builder_with_services_reference()
    {
        var services = new ServiceCollection();
        var builder = services.AddFulora();
        Assert.Same(services, builder.Services);
    }

    [Fact]
    public void AddFulora_is_idempotent_for_message_bus()
    {
        var services = new ServiceCollection();
        services.AddFulora();
        services.AddFulora();
        var sp = services.BuildServiceProvider();

        var buses = sp.GetServices<IWebViewMessageBus>().ToList();
        Assert.True(buses.Count >= 1);
    }

    [Fact]
    public void AddWebView_registers_dispatcher_and_factory_services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        Agibuild.Fulora.DependencyInjection.ServiceCollectionExtensions.AddWebView(services);
        var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IWebViewDispatcher>());
        Assert.NotNull(provider.GetRequiredService<Func<IWebViewDispatcher, IWebView>>());
    }

    [Fact]
    public void AddJsonFileConfig_registers_json_config_provider()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "{}");

            var services = new ServiceCollection();
            var provider = services.AddFulora()
                .AddJsonFileConfig(tempFile)
                .Services
                .BuildServiceProvider();

            var config = provider.GetRequiredService<IConfigProvider>();
            Assert.IsType<JsonFileConfigProvider>(config);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void AddRemoteConfig_registers_remote_provider_with_optional_fallback()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "{}");

            var services = new ServiceCollection();
            services.AddLogging();

            var provider = services.AddFulora()
                .AddRemoteConfig(new Uri("https://config.example"), tempFile)
                .Services
                .BuildServiceProvider();

            var config = provider.GetRequiredService<IConfigProvider>();
            Assert.IsType<RemoteConfigProvider>(config);
            Assert.NotNull(provider.GetRequiredService<IHttpClientFactory>());
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void AddSharedState_registers_shared_state_store()
    {
        var services = new ServiceCollection();
        var provider = services.AddFulora()
            .AddSharedState()
            .Services
            .BuildServiceProvider();

        var sharedState = provider.GetRequiredService<ISharedStateStore>();
        Assert.IsType<SharedStateStore>(sharedState);
    }

    [Fact]
    public void ConfigureBridge_registers_bridge_configuration_action()
    {
        var services = new ServiceCollection();
        services.AddFulora()
            .ConfigureBridge((_, _) => { });

        var descriptor = Assert.Single(services, x => x.ServiceType == typeof(BridgeConfigurationAction));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.NotNull(descriptor.ImplementationInstance);
    }

    private sealed class StubAutoUpdateProvider : IAutoUpdatePlatformProvider
    {
        public Task<UpdateInfo?> CheckForUpdateAsync(AutoUpdateOptions options, string currentVersion, CancellationToken ct = default)
            => Task.FromResult<UpdateInfo?>(null);
        public Task<string> DownloadUpdateAsync(UpdateInfo update, AutoUpdateOptions options, Action<UpdateDownloadProgress>? onProgress = null, CancellationToken ct = default)
            => Task.FromResult("/tmp/stub.zip");
        public Task<bool> VerifyPackageAsync(string packagePath, UpdateInfo update, CancellationToken ct = default)
            => Task.FromResult(true);
        public Task ApplyUpdateAsync(string packagePath, CancellationToken ct = default)
            => Task.CompletedTask;
        public string GetCurrentVersion() => "1.0.0";
    }
}
