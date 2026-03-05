using Agibuild.Fulora;
using Agibuild.Fulora.Plugin.Biometric;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public class BiometricPluginTests
{
    [Fact]
    public async Task BiometricService_WithInMemoryProvider_AvailableAndSuccess()
    {
        var provider = new InMemoryBiometricProvider(true, true, "touchid");
        var service = new BiometricService(provider);

        var availability = await service.CheckAvailabilityAsync(TestContext.Current.CancellationToken);
        Assert.True(availability.IsAvailable);
        Assert.Equal("touchid", availability.BiometricType);
        Assert.Null(availability.ErrorReason);

        var result = await service.AuthenticateAsync("Confirm identity", TestContext.Current.CancellationToken);
        Assert.True(result.Success);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task BiometricService_WithInMemoryProvider_AvailableButUserCancelled()
    {
        var provider = new InMemoryBiometricProvider(true, false);
        var service = new BiometricService(provider);

        var result = await service.AuthenticateAsync("Confirm identity", TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.Equal("user_cancelled", result.ErrorCode);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task BiometricService_WithInMemoryProvider_NotAvailable()
    {
        var provider = new InMemoryBiometricProvider(false, false);
        var service = new BiometricService(provider);

        var availability = await service.CheckAvailabilityAsync(TestContext.Current.CancellationToken);
        Assert.False(availability.IsAvailable);
        Assert.Null(availability.BiometricType);
        Assert.Equal("not_available", availability.ErrorReason);

        var result = await service.AuthenticateAsync("Confirm identity", TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.Equal("not_available", result.ErrorCode);
    }

    [Fact]
    public void BiometricPlugin_RegistersServiceCorrectly()
    {
        var bridge = new TrackingBridgeService();

        bridge.UsePlugin<BiometricPlugin>();

        Assert.Single(bridge.Exposed);
        Assert.Equal(typeof(IBiometricService), bridge.Exposed[0].InterfaceType);
        var impl = bridge.Exposed[0].Implementation as IBiometricService;
        Assert.NotNull(impl);
    }

    [Fact]
    public void BiometricAvailability_CanBeConstructedCorrectly()
    {
        var available = new BiometricAvailability(true, "faceid", null);
        Assert.True(available.IsAvailable);
        Assert.Equal("faceid", available.BiometricType);
        Assert.Null(available.ErrorReason);

        var unavailable = new BiometricAvailability(false, null, "platform_not_supported");
        Assert.False(unavailable.IsAvailable);
        Assert.Null(unavailable.BiometricType);
        Assert.Equal("platform_not_supported", unavailable.ErrorReason);
    }

    [Fact]
    public void BiometricResult_CanBeConstructedCorrectly()
    {
        var success = new BiometricResult(true, null, null);
        Assert.True(success.Success);
        Assert.Null(success.ErrorCode);
        Assert.Null(success.ErrorMessage);

        var failure = new BiometricResult(false, "lockout", "Too many attempts");
        Assert.False(failure.Success);
        Assert.Equal("lockout", failure.ErrorCode);
        Assert.Equal("Too many attempts", failure.ErrorMessage);
    }

    [Fact]
    public async Task BiometricService_WrapsProviderException_InInternalError()
    {
        var provider = new ThrowingBiometricProvider();
        var service = new BiometricService(provider);

        var result = await service.AuthenticateAsync("reason", TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.Equal("internal_error", result.ErrorCode);
        Assert.Equal("Provider threw", result.ErrorMessage);
    }

    private sealed class ThrowingBiometricProvider : IBiometricPlatformProvider
    {
        public Task<BiometricAvailability> CheckAvailabilityAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("Provider threw");

        public Task<BiometricResult> AuthenticateAsync(string reason, CancellationToken ct = default) =>
            throw new InvalidOperationException("Provider threw");
    }
}
