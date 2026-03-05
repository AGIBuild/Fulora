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

    [Fact]
    public async Task LinuxBiometricProvider_returns_not_supported()
    {
        var provider = new LinuxBiometricProvider();
        var availability = await provider.CheckAvailabilityAsync(TestContext.Current.CancellationToken);
        Assert.False(availability.IsAvailable);
        Assert.Equal("platform_not_supported", availability.ErrorReason);

        var result = await provider.AuthenticateAsync("test", TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.Equal("not_available", result.ErrorCode);
    }

    [Fact]
    public async Task MacOsBiometricProvider_reports_availability_based_on_platform()
    {
        var provider = new MacOsBiometricProvider();
        var availability = await provider.CheckAvailabilityAsync(TestContext.Current.CancellationToken);

        if (OperatingSystem.IsMacOS())
        {
            // On macOS: either available (with native lib) or graceful error (without native lib)
            Assert.True(availability.IsAvailable || availability.ErrorReason is "native_lib_not_found" or not null);
        }
        else
        {
            Assert.False(availability.IsAvailable);
            Assert.Equal("wrong_platform", availability.ErrorReason);
        }
    }

    [Fact]
    public async Task WindowsBiometricProvider_reports_availability_based_on_platform()
    {
        var provider = new WindowsBiometricProvider();
        var availability = await provider.CheckAvailabilityAsync(TestContext.Current.CancellationToken);
        Assert.Equal(OperatingSystem.IsWindows(), availability.IsAvailable);
        Assert.Equal("windows_hello", availability.BiometricType);
        Assert.Equal(OperatingSystem.IsWindows() ? null : "wrong_platform", availability.ErrorReason);
    }

    [Fact]
    public async Task IosBiometricProvider_auth_returns_not_implemented()
    {
        var provider = new IosBiometricProvider();
        var result = await provider.AuthenticateAsync("test", TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.Equal("not_implemented", result.ErrorCode);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task AndroidBiometricProvider_auth_returns_not_implemented()
    {
        var provider = new AndroidBiometricProvider();
        var result = await provider.AuthenticateAsync("test", TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.Equal("not_implemented", result.ErrorCode);
        Assert.NotNull(result.ErrorMessage);
    }

    private sealed class ThrowingBiometricProvider : IBiometricPlatformProvider
    {
        public Task<BiometricAvailability> CheckAvailabilityAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("Provider threw");

        public Task<BiometricResult> AuthenticateAsync(string reason, CancellationToken ct = default) =>
            throw new InvalidOperationException("Provider threw");
    }
}
