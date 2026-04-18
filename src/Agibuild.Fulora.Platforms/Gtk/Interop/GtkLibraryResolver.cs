using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Agibuild.Fulora.Adapters.Gtk.Interop;

/// <summary>
/// Resolves the logical library names used by <see cref="GtkInterop"/> P/Invoke
/// declarations to the concrete <c>.so</c> files shipped by common Linux distributions.
/// </summary>
/// <remarks>
/// <para>
/// P/Invoke declarations in <see cref="GtkInterop"/> use short logical names
/// (<c>libwebkit2gtk</c>, <c>libgtk</c>, …) that intentionally do not exist on disk.
/// The single call to <see cref="Register"/> installs a <see cref="NativeLibrary"/>
/// resolver that maps each logical name to an ordered list of real candidates and
/// loads the first one that succeeds.
/// </para>
/// <para>
/// Intent: this is the only place in the Gtk adapter that knows about distro-specific
/// <c>.so</c> naming. Adding a new candidate never requires touching the P/Invoke
/// declarations themselves.
/// </para>
/// </remarks>
[SupportedOSPlatform("linux")]
internal static class GtkLibraryResolver
{
    /// <summary>Module-wide latch: the resolver is registered at most once per AppDomain.</summary>
    private static int s_registered;

    /// <summary>
    /// Logical name → ordered candidate list. First match wins.
    /// WebKit is listed with both 4.1 and the legacy 4.0 ABI to tolerate older
    /// distributions during the transition window.
    /// </summary>
    private static readonly Dictionary<string, string[]> s_candidates = new(StringComparer.Ordinal)
    {
        [GtkInterop.LibGtk] = ["libgtk-3.so.0", "libgtk-3.so"],
        [GtkInterop.LibGdk] = ["libgdk-3.so.0", "libgdk-3.so"],
        [GtkInterop.LibGLib] = ["libglib-2.0.so.0", "libglib-2.0.so"],
        [GtkInterop.LibGObject] = ["libgobject-2.0.so.0", "libgobject-2.0.so"],
        [GtkInterop.LibGio] = ["libgio-2.0.so.0", "libgio-2.0.so"],
        [GtkInterop.LibWebKit] =
        [
            "libwebkit2gtk-4.1.so.0",
            "libwebkit2gtk-4.1.so",
            "libwebkit2gtk-4.0.so.37",
            "libwebkit2gtk-4.0.so",
        ],
        [GtkInterop.LibSoup] =
        [
            "libsoup-3.0.so.0",
            "libsoup-3.0.so",
            "libsoup-2.4.so.1",
            "libsoup-2.4.so",
        ],
        [GtkInterop.LibCairo] = ["libcairo.so.2", "libcairo.so"],
    };

    /// <summary>
    /// Installs the resolver for the assembly that owns <see cref="GtkInterop"/>.
    /// Idempotent: safe to call from every entry point into the Gtk adapter.
    /// </summary>
    public static void Register()
    {
        if (Interlocked.Exchange(ref s_registered, 1) != 0)
        {
            return;
        }

        NativeLibrary.SetDllImportResolver(typeof(GtkInterop).Assembly, Resolve);
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (s_candidates.TryGetValue(libraryName, out var candidates))
        {
            foreach (var candidate in candidates)
            {
                if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out var handle))
                {
                    return handle;
                }
            }

            // Fall through to the default resolver with the logical name so the
            // eventual DllNotFoundException carries the most useful diagnostics.
        }

        return NativeLibrary.TryLoad(libraryName, assembly, searchPath, out var fallback)
            ? fallback
            : IntPtr.Zero;
    }

    /// <summary>Expose the candidate table for diagnostic tooling (read-only).</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays",
        Justification = "Diagnostic-only accessor; defensive copy avoids caller mutation.")]
    public static IReadOnlyDictionary<string, string[]> Candidates => s_candidates;
}
