using Avalonia.Input;

namespace Agibuild.Fulora.Shell;

/// <summary>
/// Maps between Avalonia <see cref="Key"/>/<see cref="KeyModifiers"/>
/// and Fulora <see cref="ShortcutKey"/>/<see cref="ShortcutModifiers"/>.
/// </summary>
internal static class ShortcutKeyMapper
{
    internal static ShortcutKey ToShortcutKey(Key key) => key switch
    {
        Key.A => ShortcutKey.A,
        Key.B => ShortcutKey.B,
        Key.C => ShortcutKey.C,
        Key.D => ShortcutKey.D,
        Key.E => ShortcutKey.E,
        Key.F => ShortcutKey.F,
        Key.G => ShortcutKey.G,
        Key.H => ShortcutKey.H,
        Key.I => ShortcutKey.I,
        Key.J => ShortcutKey.J,
        Key.K => ShortcutKey.K,
        Key.L => ShortcutKey.L,
        Key.M => ShortcutKey.M,
        Key.N => ShortcutKey.N,
        Key.O => ShortcutKey.O,
        Key.P => ShortcutKey.P,
        Key.Q => ShortcutKey.Q,
        Key.R => ShortcutKey.R,
        Key.S => ShortcutKey.S,
        Key.T => ShortcutKey.T,
        Key.U => ShortcutKey.U,
        Key.V => ShortcutKey.V,
        Key.W => ShortcutKey.W,
        Key.X => ShortcutKey.X,
        Key.Y => ShortcutKey.Y,
        Key.Z => ShortcutKey.Z,

        Key.D0 => ShortcutKey.D0,
        Key.D1 => ShortcutKey.D1,
        Key.D2 => ShortcutKey.D2,
        Key.D3 => ShortcutKey.D3,
        Key.D4 => ShortcutKey.D4,
        Key.D5 => ShortcutKey.D5,
        Key.D6 => ShortcutKey.D6,
        Key.D7 => ShortcutKey.D7,
        Key.D8 => ShortcutKey.D8,
        Key.D9 => ShortcutKey.D9,

        Key.F1 => ShortcutKey.F1,
        Key.F2 => ShortcutKey.F2,
        Key.F3 => ShortcutKey.F3,
        Key.F4 => ShortcutKey.F4,
        Key.F5 => ShortcutKey.F5,
        Key.F6 => ShortcutKey.F6,
        Key.F7 => ShortcutKey.F7,
        Key.F8 => ShortcutKey.F8,
        Key.F9 => ShortcutKey.F9,
        Key.F10 => ShortcutKey.F10,
        Key.F11 => ShortcutKey.F11,
        Key.F12 => ShortcutKey.F12,

        Key.Escape => ShortcutKey.Escape,
        Key.Space => ShortcutKey.Space,
        Key.Enter => ShortcutKey.Enter,
        Key.Tab => ShortcutKey.Tab,
        Key.Back => ShortcutKey.Backspace,
        Key.Delete => ShortcutKey.Delete,
        Key.Insert => ShortcutKey.Insert,
        Key.Home => ShortcutKey.Home,
        Key.End => ShortcutKey.End,
        Key.PageUp => ShortcutKey.PageUp,
        Key.PageDown => ShortcutKey.PageDown,
        Key.Left => ShortcutKey.Left,
        Key.Up => ShortcutKey.Up,
        Key.Right => ShortcutKey.Right,
        Key.Down => ShortcutKey.Down,

        _ => ShortcutKey.None
    };

    internal static ShortcutModifiers ToShortcutModifiers(KeyModifiers mods)
    {
        var result = ShortcutModifiers.None;
        if ((mods & KeyModifiers.Control) != 0) result |= ShortcutModifiers.Ctrl;
        if ((mods & KeyModifiers.Alt) != 0) result |= ShortcutModifiers.Alt;
        if ((mods & KeyModifiers.Shift) != 0) result |= ShortcutModifiers.Shift;
        if ((mods & KeyModifiers.Meta) != 0) result |= ShortcutModifiers.Meta;
        return result;
    }
}
