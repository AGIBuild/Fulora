using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Agibuild.Fulora.Adapters.Gtk.Interop;

/// <summary>
/// Holds a GObject signal connection and the managed state object associated with it.
/// </summary>
/// <remarks>
/// <para>
/// Pattern (mirrors Avalonia.Controls.WebView's <c>GtkSignal</c>): to route a native
/// GObject signal into a managed <see cref="UnmanagedCallersOnlyAttribute"/> callback,
/// we must give the native layer an opaque pointer that keeps the managed state
/// reachable for as long as the signal connection is alive. <see cref="GCHandle"/> is
/// the standard mechanism; GLib will call our <see cref="s_onDestroy"/> thunk when the
/// signal is disconnected, which frees the handle.
/// </para>
/// <para>
/// Callers do not free the handle manually — they either <see cref="Dispose"/> this
/// wrapper (which calls <c>g_signal_handler_disconnect</c> and lets GLib invoke the
/// destroy thunk) or let the native instance die, in which case GLib still invokes
/// the thunk. Never both.
/// </para>
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class GtkSignal : IDisposable
{
    /// <summary>
    /// Unmanaged function pointer to <see cref="OnDestroy"/>. Captured once at
    /// type-init time so every signal connection reuses the same thunk.
    /// </summary>
    private static readonly unsafe IntPtr s_onDestroy =
        (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&OnDestroy;

    private readonly IntPtr _instance;
    private readonly ulong _handlerId;
    private GCHandle _stateHandle;
    private int _disposed;

    /// <summary>
    /// Connects <paramref name="callback"/> to <paramref name="signalName"/> on
    /// <paramref name="instance"/>, using <paramref name="state"/> as the managed
    /// payload delivered to the callback (unpacked via
    /// <see cref="TryGetState{TState}(IntPtr, out TState)"/>).
    /// </summary>
    /// <param name="instance">GObject* receiving the signal.</param>
    /// <param name="signalName">GLib signal name (e.g. <c>"load-changed"</c>).</param>
    /// <param name="callback">
    /// Pointer to an <c>[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]</c>
    /// thunk with the signature GLib expects for this signal.
    /// </param>
    /// <param name="state">Managed payload. Kept reachable for the lifetime of the connection.</param>
    public GtkSignal(IntPtr instance, string signalName, IntPtr callback, object state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (instance == IntPtr.Zero) throw new ArgumentException("GObject instance is null.", nameof(instance));
        if (callback == IntPtr.Zero) throw new ArgumentException("Callback is null.", nameof(callback));

        _stateHandle = GCHandle.Alloc(state);
        _instance = instance;
        _handlerId = GtkInterop.g_signal_connect_data(
            instance,
            signalName,
            callback,
            GCHandle.ToIntPtr(_stateHandle),
            s_onDestroy,
            connectFlags: 0);

        if (_handlerId == 0)
        {
            _stateHandle.Free();
            throw new InvalidOperationException(
                $"g_signal_connect_data returned 0 for signal '{signalName}'.");
        }
    }

    /// <summary>
    /// Unpack the managed state pointer delivered by a signal thunk back into its
    /// typed form. Returns <see langword="false"/> when the handle has been freed
    /// (stray callback racing with disposal) or the stored object is of a different type.
    /// </summary>
    public static bool TryGetState<TState>(IntPtr statePtr, [NotNullWhen(true)] out TState? state)
        where TState : class
    {
        if (statePtr == IntPtr.Zero)
        {
            state = null;
            return false;
        }

        try
        {
            var handle = GCHandle.FromIntPtr(statePtr);
            if (handle.IsAllocated && handle.Target is TState typed)
            {
                state = typed;
                return true;
            }
        }
        catch (InvalidOperationException)
        {
            // Handle was freed between the signal firing and our dereference.
        }

        state = null;
        return false;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnDestroy(IntPtr data, IntPtr closure)
    {
        if (data == IntPtr.Zero) return;

        try
        {
            var handle = GCHandle.FromIntPtr(data);
            if (handle.IsAllocated)
            {
                handle.Free();
            }
        }
        catch (InvalidOperationException)
        {
            // Already freed; GLib can re-enter destroy in pathological cases.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // Disconnect triggers our destroy thunk, which frees _stateHandle.
        GtkInterop.g_signal_handler_disconnect(_instance, _handlerId);
    }
}
