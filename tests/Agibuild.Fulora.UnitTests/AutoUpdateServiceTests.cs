using Xunit;

namespace Agibuild.Fulora.UnitTests;

public class AutoUpdateServiceTests
{
    private static AutoUpdateOptions DefaultOptions(string feedUrl = "https://update.example.com/feed.json") =>
        new() { FeedUrl = feedUrl, CheckInterval = null };

    [Fact]
    public async Task CheckForUpdate_returns_UpToDate_when_no_update()
    {
        var provider = new MockAutoUpdateProvider(currentVersion: "1.0.0", availableUpdate: null);
        using var svc = new AutoUpdateService(provider, DefaultOptions());

        var result = await svc.CheckForUpdate();

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
        Assert.Null(result.Update);
    }

    [Fact]
    public async Task CheckForUpdate_returns_UpdateAvailable_when_newer_version_exists()
    {
        var update = new UpdateInfo { Version = "1.1.0", DownloadUrl = "https://example.com/v1.1.0.zip" };
        var provider = new MockAutoUpdateProvider(currentVersion: "1.0.0", availableUpdate: update);
        using var svc = new AutoUpdateService(provider, DefaultOptions());

        var result = await svc.CheckForUpdate();

        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
        Assert.NotNull(result.Update);
        Assert.Equal("1.1.0", result.Update.Version);
    }

    [Fact]
    public async Task CheckForUpdate_emits_UpdateAvailable_event()
    {
        var update = new UpdateInfo { Version = "2.0.0", DownloadUrl = "https://example.com/v2.0.0.zip" };
        var provider = new MockAutoUpdateProvider(currentVersion: "1.0.0", availableUpdate: update);
        using var svc = new AutoUpdateService(provider, DefaultOptions());

        UpdateInfo? received = null;
        var eventProp = (BridgeEvent<UpdateInfo>)svc.UpdateAvailable;
        eventProp.Connect(info => received = info);

        await svc.CheckForUpdate();

        Assert.NotNull(received);
        Assert.Equal("2.0.0", received.Version);
    }

    [Fact]
    public async Task CheckForUpdate_returns_Error_on_provider_failure()
    {
        var provider = new MockAutoUpdateProvider(currentVersion: "1.0.0", checkThrows: new InvalidOperationException("Network error"));
        using var svc = new AutoUpdateService(provider, DefaultOptions());

        var result = await svc.CheckForUpdate();

        Assert.Equal(UpdateStatus.Error, result.Status);
        Assert.Contains("Network error", result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadUpdate_returns_Error_when_no_update_checked()
    {
        var provider = new MockAutoUpdateProvider(currentVersion: "1.0.0", availableUpdate: null);
        using var svc = new AutoUpdateService(provider, DefaultOptions());

        var result = await svc.DownloadUpdate();

        Assert.Equal(UpdateStatus.Error, result.Status);
        Assert.Contains("No update available", result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadUpdate_returns_ReadyToInstall_on_success()
    {
        var update = new UpdateInfo { Version = "1.1.0", DownloadUrl = "https://example.com/v1.1.0.zip" };
        var provider = new MockAutoUpdateProvider(currentVersion: "1.0.0", availableUpdate: update);
        using var svc = new AutoUpdateService(provider, DefaultOptions());

        await svc.CheckForUpdate();
        var result = await svc.DownloadUpdate();

        Assert.Equal(UpdateStatus.ReadyToInstall, result.Status);
        Assert.NotNull(result.Update);
    }

    [Fact]
    public async Task DownloadUpdate_emits_progress_events()
    {
        var update = new UpdateInfo { Version = "1.1.0", DownloadUrl = "https://example.com/v1.1.0.zip" };
        var provider = new MockAutoUpdateProvider(currentVersion: "1.0.0", availableUpdate: update, emitProgress: true);
        using var svc = new AutoUpdateService(provider, DefaultOptions());

        var progressEvents = new List<UpdateDownloadProgress>();
        var eventProp = (BridgeEvent<UpdateDownloadProgress>)svc.DownloadProgress;
        eventProp.Connect(p => progressEvents.Add(p));

        await svc.CheckForUpdate();
        await svc.DownloadUpdate();

        Assert.NotEmpty(progressEvents);
        Assert.True(progressEvents.Last().BytesDownloaded > 0);
    }

    [Fact]
    public async Task DownloadUpdate_returns_Error_on_verification_failure()
    {
        var update = new UpdateInfo { Version = "1.1.0", DownloadUrl = "https://example.com/v1.1.0.zip" };
        var provider = new MockAutoUpdateProvider(currentVersion: "1.0.0", availableUpdate: update, verifyFails: true);
        using var svc = new AutoUpdateService(provider, DefaultOptions());

        await svc.CheckForUpdate();
        var result = await svc.DownloadUpdate();

        Assert.Equal(UpdateStatus.Error, result.Status);
        Assert.Contains("integrity verification failed", result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadUpdate_returns_Error_on_download_failure()
    {
        var update = new UpdateInfo { Version = "1.1.0", DownloadUrl = "https://example.com/v1.1.0.zip" };
        var provider = new MockAutoUpdateProvider(currentVersion: "1.0.0", availableUpdate: update, downloadThrows: new IOException("Disk full"));
        using var svc = new AutoUpdateService(provider, DefaultOptions());

        await svc.CheckForUpdate();
        var result = await svc.DownloadUpdate();

        Assert.Equal(UpdateStatus.Error, result.Status);
        Assert.Contains("Disk full", result.ErrorMessage);
    }

    [Fact]
    public async Task ApplyUpdate_returns_Error_when_no_download()
    {
        var provider = new MockAutoUpdateProvider(currentVersion: "1.0.0", availableUpdate: null);
        using var svc = new AutoUpdateService(provider, DefaultOptions());

        var result = await svc.ApplyUpdate();

        Assert.Equal(UpdateStatus.Error, result.Status);
        Assert.Contains("No downloaded update", result.ErrorMessage);
    }

    [Fact]
    public async Task ApplyUpdate_succeeds_after_download()
    {
        var update = new UpdateInfo { Version = "1.1.0", DownloadUrl = "https://example.com/v1.1.0.zip" };
        var provider = new MockAutoUpdateProvider(currentVersion: "1.0.0", availableUpdate: update);
        using var svc = new AutoUpdateService(provider, DefaultOptions());

        await svc.CheckForUpdate();
        await svc.DownloadUpdate();
        var result = await svc.ApplyUpdate();

        Assert.Equal(UpdateStatus.ReadyToInstall, result.Status);
        Assert.True(provider.ApplyWasCalled);
    }

    [Fact]
    public async Task ApplyUpdate_returns_Error_on_apply_failure()
    {
        var update = new UpdateInfo { Version = "1.1.0", DownloadUrl = "https://example.com/v1.1.0.zip" };
        var provider = new MockAutoUpdateProvider(currentVersion: "1.0.0", availableUpdate: update, applyThrows: new UnauthorizedAccessException("Permission denied"));
        using var svc = new AutoUpdateService(provider, DefaultOptions());

        await svc.CheckForUpdate();
        await svc.DownloadUpdate();
        var result = await svc.ApplyUpdate();

        Assert.Equal(UpdateStatus.Error, result.Status);
        Assert.Contains("Permission denied", result.ErrorMessage);
    }

    [Fact]
    public async Task GetCurrentVersion_returns_version_from_provider()
    {
        var provider = new MockAutoUpdateProvider(currentVersion: "3.2.1", availableUpdate: null);
        using var svc = new AutoUpdateService(provider, DefaultOptions());

        var version = await svc.GetCurrentVersion();

        Assert.Equal("3.2.1", version);
    }

    [Fact]
    public void Constructor_throws_on_null_provider()
    {
        Assert.Throws<ArgumentNullException>(() => new AutoUpdateService(null!, DefaultOptions()));
    }

    [Fact]
    public void Constructor_throws_on_null_options()
    {
        var provider = new MockAutoUpdateProvider(currentVersion: "1.0.0", availableUpdate: null);
        Assert.Throws<ArgumentNullException>(() => new AutoUpdateService(provider, null!));
    }

    [Fact]
    public async Task Disposed_service_throws_ObjectDisposedException()
    {
        var provider = new MockAutoUpdateProvider(currentVersion: "1.0.0", availableUpdate: null);
        var svc = new AutoUpdateService(provider, DefaultOptions());
        svc.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(svc.CheckForUpdate);
        await Assert.ThrowsAsync<ObjectDisposedException>(svc.DownloadUpdate);
        await Assert.ThrowsAsync<ObjectDisposedException>(svc.ApplyUpdate);
        await Assert.ThrowsAsync<ObjectDisposedException>(svc.GetCurrentVersion);
    }

    [Fact]
    public void Dispose_twice_does_not_throw()
    {
        var provider = new MockAutoUpdateProvider(currentVersion: "1.0.0", availableUpdate: null);
        var svc = new AutoUpdateService(provider, DefaultOptions());
        svc.Dispose();
        svc.Dispose();
    }

    [Fact]
    public async Task AutoDownload_triggers_download_after_check()
    {
        var update = new UpdateInfo { Version = "1.1.0", DownloadUrl = "https://example.com/v1.1.0.zip" };
        var provider = new MockAutoUpdateProvider(currentVersion: "1.0.0", availableUpdate: update);
        var options = new AutoUpdateOptions { FeedUrl = "https://update.example.com/feed.json", AutoDownload = true, CheckInterval = null };
        using var svc = new AutoUpdateService(provider, options);

        await svc.CheckForUpdate();

        // The service fires auto-download off the main task via Task.Run; await the mock's
        // deterministic signal (TaskCompletionSource) instead of a wall-clock Task.Delay, which
        // was too tight for busy Windows CI agents.
        await provider.DownloadCalledAwaiter.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.True(provider.DownloadWasCalled);
    }

    [Fact]
    public void UpdateInfo_properties_are_accessible()
    {
        var info = new UpdateInfo
        {
            Version = "2.0.0",
            ReleaseNotes = "Bug fixes",
            DownloadUrl = "https://example.com/update.zip",
            SizeBytes = 1024 * 1024,
            IsMandatory = true,
            Sha256 = "abc123"
        };

        Assert.Equal("2.0.0", info.Version);
        Assert.Equal("Bug fixes", info.ReleaseNotes);
        Assert.Equal("https://example.com/update.zip", info.DownloadUrl);
        Assert.Equal(1024 * 1024, info.SizeBytes);
        Assert.True(info.IsMandatory);
        Assert.Equal("abc123", info.Sha256);
    }

    [Fact]
    public void AutoUpdateOptions_defaults()
    {
        var options = new AutoUpdateOptions { FeedUrl = "https://example.com" };
        Assert.Equal(TimeSpan.FromHours(1), options.CheckInterval);
        Assert.False(options.AutoDownload);
        Assert.Null(options.Headers);
    }

    [Fact]
    public void UpdateDownloadProgress_properties()
    {
        var progress = new UpdateDownloadProgress
        {
            BytesDownloaded = 500,
            TotalBytes = 1000,
            ProgressPercent = 50.0
        };

        Assert.Equal(500, progress.BytesDownloaded);
        Assert.Equal(1000, progress.TotalBytes);
        Assert.Equal(50.0, progress.ProgressPercent);
    }

    private sealed class MockAutoUpdateProvider : IAutoUpdatePlatformProvider
    {
        private readonly string _currentVersion;
        private readonly UpdateInfo? _availableUpdate;
        private readonly Exception? _checkThrows;
        private readonly Exception? _downloadThrows;
        private readonly Exception? _applyThrows;
        private readonly bool _verifyFails;
        private readonly bool _emitProgress;

        private readonly TaskCompletionSource _downloadCalled =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool DownloadWasCalled { get; private set; }
        public bool ApplyWasCalled { get; private set; }

        /// <summary>
        /// Completes when <see cref="DownloadUpdateAsync"/> has been entered. Lets tests await the
        /// background auto-download signal deterministically instead of polling/sleeping for a
        /// fixed wall-clock duration.
        /// </summary>
        public Task DownloadCalledAwaiter => _downloadCalled.Task;

        public MockAutoUpdateProvider(
            string currentVersion,
            UpdateInfo? availableUpdate = null,
            Exception? checkThrows = null,
            Exception? downloadThrows = null,
            Exception? applyThrows = null,
            bool verifyFails = false,
            bool emitProgress = false)
        {
            _currentVersion = currentVersion;
            _availableUpdate = availableUpdate;
            _checkThrows = checkThrows;
            _downloadThrows = downloadThrows;
            _applyThrows = applyThrows;
            _verifyFails = verifyFails;
            _emitProgress = emitProgress;
        }

        public Task<UpdateInfo?> CheckForUpdateAsync(AutoUpdateOptions options, string currentVersion, CancellationToken ct = default)
        {
            if (_checkThrows != null) throw _checkThrows;
            return Task.FromResult(_availableUpdate);
        }

        public Task<string> DownloadUpdateAsync(UpdateInfo update, AutoUpdateOptions options, Action<UpdateDownloadProgress>? onProgress = null, CancellationToken ct = default)
        {
            DownloadWasCalled = true;
            _downloadCalled.TrySetResult();
            if (_downloadThrows != null) throw _downloadThrows;

            if (_emitProgress && onProgress != null)
            {
                onProgress(new UpdateDownloadProgress { BytesDownloaded = 500, TotalBytes = 1000, ProgressPercent = 50 });
                onProgress(new UpdateDownloadProgress { BytesDownloaded = 1000, TotalBytes = 1000, ProgressPercent = 100 });
            }

            return Task.FromResult("/tmp/update-package.zip");
        }

        public Task<bool> VerifyPackageAsync(string packagePath, UpdateInfo update, CancellationToken ct = default)
            => Task.FromResult(!_verifyFails);

        public Task ApplyUpdateAsync(string packagePath, CancellationToken ct = default)
        {
            ApplyWasCalled = true;
            if (_applyThrows != null) throw _applyThrows;
            return Task.CompletedTask;
        }

        public string GetCurrentVersion() => _currentVersion;
    }
}
