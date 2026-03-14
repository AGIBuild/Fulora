using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using SharpHook;
using SharpHook.Data;

namespace Agibuild.Fulora.Shell;

/// <summary>
/// Cross-platform global shortcut provider using SharpHook (libuiohook).
/// Supports Windows, macOS (requires Accessibility permission), and Linux X11.
/// The hook is started lazily on first registration and stopped on last unregistration/dispose.
/// </summary>
public sealed class SharpHookGlobalShortcutProvider : IGlobalShortcutPlatformProvider
{
    private readonly ConcurrentDictionary<string, ShortcutChord> _registrations = new();
    private readonly ConcurrentDictionary<ShortcutChord, string> _chordToId = new();
    private readonly object _hookLock = new();
    private TaskPoolGlobalHook? _hook;
    private bool _disposed;

    /// <inheritdoc />
    public bool IsSupported =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
        (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
         Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is null);

    /// <inheritdoc />
    public event Action<string>? ShortcutActivated;

    /// <inheritdoc />
    public bool Register(string id, ShortcutKey key, ShortcutModifiers modifiers)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var chord = new ShortcutChord(key, modifiers);

        if (!_chordToId.TryAdd(chord, id))
            return false;

        _registrations[id] = chord;
        EnsureHookRunning();
        return true;
    }

    /// <inheritdoc />
    public bool Unregister(string id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_registrations.TryRemove(id, out var chord))
            return false;

        _chordToId.TryRemove(chord, out _);

        if (_registrations.IsEmpty)
            StopHook();

        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopHook();
        _registrations.Clear();
        _chordToId.Clear();
    }

    private void EnsureHookRunning()
    {
        lock (_hookLock)
        {
            if (_hook is not null || _disposed) return;

            var hook = new TaskPoolGlobalHook();
            hook.KeyPressed += OnKeyPressed;
            _hook = hook;
            _ = hook.RunAsync();
        }
    }

    private void StopHook()
    {
        lock (_hookLock)
        {
            if (_hook is null) return;

            _hook.KeyPressed -= OnKeyPressed;

            try { _hook.Dispose(); } catch { /* best-effort cleanup */ }
            _hook = null;
        }
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        var key = MapKeyCode(e.Data.KeyCode);
        var modifiers = MapModifiers(e.RawEvent.Mask);

        var chord = new ShortcutChord(key, modifiers);
        if (_chordToId.TryGetValue(chord, out var id))
        {
            ShortcutActivated?.Invoke(id);
        }
    }

    internal static ShortcutKey MapKeyCode(KeyCode keyCode) => keyCode switch
    {
        KeyCode.VcA => ShortcutKey.A,
        KeyCode.VcB => ShortcutKey.B,
        KeyCode.VcC => ShortcutKey.C,
        KeyCode.VcD => ShortcutKey.D,
        KeyCode.VcE => ShortcutKey.E,
        KeyCode.VcF => ShortcutKey.F,
        KeyCode.VcG => ShortcutKey.G,
        KeyCode.VcH => ShortcutKey.H,
        KeyCode.VcI => ShortcutKey.I,
        KeyCode.VcJ => ShortcutKey.J,
        KeyCode.VcK => ShortcutKey.K,
        KeyCode.VcL => ShortcutKey.L,
        KeyCode.VcM => ShortcutKey.M,
        KeyCode.VcN => ShortcutKey.N,
        KeyCode.VcO => ShortcutKey.O,
        KeyCode.VcP => ShortcutKey.P,
        KeyCode.VcQ => ShortcutKey.Q,
        KeyCode.VcR => ShortcutKey.R,
        KeyCode.VcS => ShortcutKey.S,
        KeyCode.VcT => ShortcutKey.T,
        KeyCode.VcU => ShortcutKey.U,
        KeyCode.VcV => ShortcutKey.V,
        KeyCode.VcW => ShortcutKey.W,
        KeyCode.VcX => ShortcutKey.X,
        KeyCode.VcY => ShortcutKey.Y,
        KeyCode.VcZ => ShortcutKey.Z,

        KeyCode.Vc0 => ShortcutKey.D0,
        KeyCode.Vc1 => ShortcutKey.D1,
        KeyCode.Vc2 => ShortcutKey.D2,
        KeyCode.Vc3 => ShortcutKey.D3,
        KeyCode.Vc4 => ShortcutKey.D4,
        KeyCode.Vc5 => ShortcutKey.D5,
        KeyCode.Vc6 => ShortcutKey.D6,
        KeyCode.Vc7 => ShortcutKey.D7,
        KeyCode.Vc8 => ShortcutKey.D8,
        KeyCode.Vc9 => ShortcutKey.D9,

        KeyCode.VcF1 => ShortcutKey.F1,
        KeyCode.VcF2 => ShortcutKey.F2,
        KeyCode.VcF3 => ShortcutKey.F3,
        KeyCode.VcF4 => ShortcutKey.F4,
        KeyCode.VcF5 => ShortcutKey.F5,
        KeyCode.VcF6 => ShortcutKey.F6,
        KeyCode.VcF7 => ShortcutKey.F7,
        KeyCode.VcF8 => ShortcutKey.F8,
        KeyCode.VcF9 => ShortcutKey.F9,
        KeyCode.VcF10 => ShortcutKey.F10,
        KeyCode.VcF11 => ShortcutKey.F11,
        KeyCode.VcF12 => ShortcutKey.F12,

        KeyCode.VcEscape => ShortcutKey.Escape,
        KeyCode.VcSpace => ShortcutKey.Space,
        KeyCode.VcEnter => ShortcutKey.Enter,
        KeyCode.VcTab => ShortcutKey.Tab,
        KeyCode.VcBackspace => ShortcutKey.Backspace,
        KeyCode.VcDelete => ShortcutKey.Delete,
        KeyCode.VcInsert => ShortcutKey.Insert,
        KeyCode.VcHome => ShortcutKey.Home,
        KeyCode.VcEnd => ShortcutKey.End,
        KeyCode.VcPageUp => ShortcutKey.PageUp,
        KeyCode.VcPageDown => ShortcutKey.PageDown,
        KeyCode.VcLeft => ShortcutKey.Left,
        KeyCode.VcUp => ShortcutKey.Up,
        KeyCode.VcRight => ShortcutKey.Right,
        KeyCode.VcDown => ShortcutKey.Down,

        KeyCode.VcMinus => ShortcutKey.Minus,
        KeyCode.VcEquals => ShortcutKey.Plus,
        KeyCode.VcComma => ShortcutKey.Comma,
        KeyCode.VcPeriod => ShortcutKey.Period,
        KeyCode.VcSlash => ShortcutKey.Slash,
        KeyCode.VcBackslash => ShortcutKey.Backslash,
        KeyCode.VcSemicolon => ShortcutKey.Semicolon,
        KeyCode.VcQuote => ShortcutKey.Quote,
        KeyCode.VcOpenBracket => ShortcutKey.BracketLeft,
        KeyCode.VcCloseBracket => ShortcutKey.BracketRight,
        KeyCode.VcBackQuote => ShortcutKey.Backtick,

        KeyCode.VcPrintScreen => ShortcutKey.PrintScreen,
        KeyCode.VcPause => ShortcutKey.Pause,
        KeyCode.VcScrollLock => ShortcutKey.ScrollLock,

        _ => ShortcutKey.None,
    };

    internal static ShortcutModifiers MapModifiers(EventMask mask)
    {
        var mods = ShortcutModifiers.None;

        if ((mask & EventMask.Ctrl) != 0) mods |= ShortcutModifiers.Ctrl;
        if ((mask & EventMask.Alt) != 0) mods |= ShortcutModifiers.Alt;
        if ((mask & EventMask.Shift) != 0) mods |= ShortcutModifiers.Shift;
        if ((mask & EventMask.Meta) != 0) mods |= ShortcutModifiers.Meta;

        return mods;
    }

    private readonly record struct ShortcutChord(ShortcutKey Key, ShortcutModifiers Modifiers);
}
