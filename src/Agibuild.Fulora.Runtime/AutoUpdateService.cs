namespace Agibuild.Fulora;

/// <summary>
/// Runtime implementation of <see cref="IAutoUpdateService"/>.
/// Coordinates the update lifecycle (check → download → verify → apply) via an
/// <see cref="IAutoUpdatePlatformProvider"/> and exposes bridge events for JS consumers.
/// </summary>
public sealed class AutoUpdateService : IAutoUpdateService, IDisposable
{
    private readonly IAutoUpdatePlatformProvider _provider;
    private readonly AutoUpdateOptions _options;
    private readonly BridgeEvent<UpdateInfo> _updateAvailable = new();
    private readonly BridgeEvent<UpdateDownloadProgress> _downloadProgress = new();
    private readonly object _lock = new();

    private UpdateInfo? _latestUpdate;
    private string? _downloadedPackagePath;
    private Timer? _checkTimer;
    private bool _disposed;

    /// <summary>Initializes the auto-update service with the given platform provider and options.</summary>
    public AutoUpdateService(IAutoUpdatePlatformProvider provider, AutoUpdateOptions options)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (_options.CheckInterval is { } interval && interval > TimeSpan.Zero)
        {
            _checkTimer = new Timer(OnAutoCheckTimer, null, interval, interval);
        }
    }

    /// <inheritdoc />
    public IBridgeEvent<UpdateInfo> UpdateAvailable => _updateAvailable;

    /// <inheritdoc />
    public IBridgeEvent<UpdateDownloadProgress> DownloadProgress => _downloadProgress;

    /// <inheritdoc />
    public async Task<UpdateResult> CheckForUpdate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            var currentVersion = _provider.GetCurrentVersion();
            var update = await _provider.CheckForUpdateAsync(_options, currentVersion);

            if (update is null)
            {
                return new UpdateResult { Status = UpdateStatus.UpToDate };
            }

            lock (_lock) { _latestUpdate = update; }
            _updateAvailable.Emit(update);

            if (_options.AutoDownload)
            {
                _ = Task.Run(() => DownloadUpdate());
            }

            return new UpdateResult { Status = UpdateStatus.UpdateAvailable, Update = update };
        }
        catch (Exception ex)
        {
            return new UpdateResult { Status = UpdateStatus.Error, ErrorMessage = ex.Message };
        }
    }

    /// <inheritdoc />
    public async Task<UpdateResult> DownloadUpdate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        UpdateInfo? update;
        lock (_lock) { update = _latestUpdate; }

        if (update is null)
        {
            return new UpdateResult { Status = UpdateStatus.Error, ErrorMessage = "No update available. Call CheckForUpdate first." };
        }

        try
        {
            var packagePath = await _provider.DownloadUpdateAsync(
                update,
                _options,
                progress => _downloadProgress.Emit(progress));

            var isValid = await _provider.VerifyPackageAsync(packagePath, update);
            if (!isValid)
            {
                return new UpdateResult { Status = UpdateStatus.Error, ErrorMessage = "Package integrity verification failed." };
            }

            lock (_lock) { _downloadedPackagePath = packagePath; }

            return new UpdateResult { Status = UpdateStatus.ReadyToInstall, Update = update };
        }
        catch (Exception ex)
        {
            return new UpdateResult { Status = UpdateStatus.Error, ErrorMessage = ex.Message };
        }
    }

    /// <inheritdoc />
    public async Task<UpdateResult> ApplyUpdate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        string? packagePath;
        UpdateInfo? update;
        lock (_lock)
        {
            packagePath = _downloadedPackagePath;
            update = _latestUpdate;
        }

        if (packagePath is null || update is null)
        {
            return new UpdateResult { Status = UpdateStatus.Error, ErrorMessage = "No downloaded update to apply. Call DownloadUpdate first." };
        }

        try
        {
            await _provider.ApplyUpdateAsync(packagePath);
            return new UpdateResult { Status = UpdateStatus.ReadyToInstall, Update = update };
        }
        catch (Exception ex)
        {
            return new UpdateResult { Status = UpdateStatus.Error, ErrorMessage = ex.Message };
        }
    }

    /// <inheritdoc />
    public Task<string> GetCurrentVersion()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Task.FromResult(_provider.GetCurrentVersion());
    }

    private async void OnAutoCheckTimer(object? state)
    {
        if (_disposed) return;

        try
        {
            await CheckForUpdate();
        }
        catch
        {
            // Swallow auto-check exceptions to not crash the app.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _checkTimer?.Dispose();
        _checkTimer = null;
    }
}
