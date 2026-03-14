using System;
using System.Globalization;
using System.IO;
using Serilog;
using Serilog.Core;

namespace Agibuild.Fulora.Integration.Tests;

/// <summary>
/// Centralized Serilog logger for the integration test app.
/// Call <see cref="Initialize"/> once at startup; then use <see cref="Write"/> to log lines
/// that are already formatted by ViewModels (with timestamp prefix).
/// The file sink writes to <c>{LocalAppData}/e2e-logs/e2e-{Date}.log</c>,
/// which on Android maps to the internal files dir (retrievable via <c>adb pull</c>).
/// </summary>
internal static class AppLog
{
    private static Logger? _logger;

    /// <summary>
    /// The resolved directory where log files are written.
    /// Useful for displaying the path in the UI for users to locate logs.
    /// </summary>
    public static string LogDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// Initialize Serilog file sink. Safe to call multiple times (idempotent).
    /// </summary>
    public static void Initialize()
    {
        if (_logger is not null) return;

        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(baseDir))
        {
            // Fallback for platforms where LocalApplicationData is empty
            baseDir = AppContext.BaseDirectory;
        }

        LogDirectory = Path.Combine(baseDir, "e2e-logs");
        Directory.CreateDirectory(LogDirectory);

        var logPath = Path.Combine(LogDirectory, "e2e-.log");

        _logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Message:lj}{NewLine}",
                formatProvider: CultureInfo.InvariantCulture)
            .CreateLogger();
    }

    /// <summary>
    /// Write a pre-formatted log line (from ViewModel) to the file sink.
    /// </summary>
    public static void Write(string line)
    {
        _logger?.Information(line);
    }

    /// <summary>
    /// Flush and close. Call on app shutdown if needed.
    /// </summary>
    public static void CloseAndFlush()
    {
        _logger?.Dispose();
        _logger = null;
    }
}
