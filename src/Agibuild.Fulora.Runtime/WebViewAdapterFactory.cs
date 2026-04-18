using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Agibuild.Fulora.Adapters.Abstractions;

namespace Agibuild.Fulora;

[ExcludeFromCodeCoverage]
internal static class WebViewAdapterFactory
{
    private static bool DiagnosticsEnabled
        => string.Equals(Environment.GetEnvironmentVariable("AGIBUILD_WEBVIEW_DIAG"), "1", StringComparison.Ordinal);

    public static IWebViewAdapter CreateDefaultAdapter()
    {
        EnsurePlatformAdaptersLoaded();

        if (WebViewAdapterRegistry.TryCreateForCurrentPlatform(out var adapter, out var reason))
        {
            return adapter;
        }

        throw new PlatformNotSupportedException(reason);
    }

    private static readonly string[] CandidateAssemblyNames =
    [
        // Unified desktop platforms assembly (Windows/WebView2, macOS/WKWebView, Linux/WebKitGTK).
        "Agibuild.Fulora.Platforms",
        // Mobile adapters remain as separate TFM-specific assemblies.
        "Agibuild.Fulora.Adapters.iOS",
        "Agibuild.Fulora.Adapters.Android"
    ];

    private static void EnsurePlatformAdaptersLoaded()
    {
        if (WebViewAdapterRegistry.HasAnyForCurrentPlatform())
        {
            return;
        }

        foreach (var assemblyName in CandidateAssemblyNames)
        {
            TryLoadByName(assemblyName);
        }
    }

    private static void TryLoadByName(string assemblyName)
    {
        try
        {
            var asm = AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(assemblyName));
            RuntimeHelpers.RunModuleConstructor(asm.ManifestModule.ModuleHandle);
            if (DiagnosticsEnabled)
            {
                Console.WriteLine($"[Agibuild.WebView] Loaded by name '{assemblyName}': {asm.FullName}");
                Console.WriteLine($"[Agibuild.WebView] Registry assembly: {typeof(WebViewAdapterRegistry).Assembly.FullName}");
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            if (DiagnosticsEnabled)
            {
                Console.WriteLine($"[Agibuild.WebView] LoadFromAssemblyName failed for '{assemblyName}': {ex.GetType().Name}: {ex.Message}");
            }

            // Fallback: probe next to the app for a copied plugin assembly.
            TryLoadFromAppBaseDirectory(assemblyName);
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Dynamic assembly loading is the intended fallback for plugin discovery.")]
    private static void TryLoadFromAppBaseDirectory(string assemblyName)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll");
            if (!File.Exists(path))
            {
                if (DiagnosticsEnabled)
                {
                    Console.WriteLine($"[Agibuild.WebView] Probe next to app: missing '{path}'.");
                }
                return;
            }

            var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            RuntimeHelpers.RunModuleConstructor(asm.ManifestModule.ModuleHandle);
            if (DiagnosticsEnabled)
            {
                Console.WriteLine($"[Agibuild.WebView] Loaded from app base '{assemblyName}' at '{path}'.");
                Console.WriteLine($"[Agibuild.WebView] Registry assembly: {typeof(WebViewAdapterRegistry).Assembly.FullName}");
                Console.WriteLine($"[Agibuild.WebView] Has adapter for current platform: {WebViewAdapterRegistry.HasAnyForCurrentPlatform()}");
            }
        }
        catch (Exception ex)
        {
            if (DiagnosticsEnabled)
            {
                Console.WriteLine($"[Agibuild.WebView] LoadFromAssemblyPath failed for '{assemblyName}': {ex}");
            }

            // Ignore - plugin not available or failed to load.
        }
    }
}
