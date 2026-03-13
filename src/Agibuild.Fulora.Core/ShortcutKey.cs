namespace Agibuild.Fulora;

/// <summary>
/// Virtual key codes for global shortcut registration.
/// Subset of commonly used keys across all platforms.
/// </summary>
public enum ShortcutKey
{
    /// <summary>No key.</summary>
    None = 0,

    /// <summary>The A key.</summary>
    A = 65,
    /// <summary>The B key.</summary>
    B,
    /// <summary>The C key.</summary>
    C,
    /// <summary>The D key.</summary>
    D,
    /// <summary>The E key.</summary>
    E,
    /// <summary>The F key.</summary>
    F,
    /// <summary>The G key.</summary>
    G,
    /// <summary>The H key.</summary>
    H,
    /// <summary>The I key.</summary>
    I,
    /// <summary>The J key.</summary>
    J,
    /// <summary>The K key.</summary>
    K,
    /// <summary>The L key.</summary>
    L,
    /// <summary>The M key.</summary>
    M,
    /// <summary>The N key.</summary>
    N,
    /// <summary>The O key.</summary>
    O,
    /// <summary>The P key.</summary>
    P,
    /// <summary>The Q key.</summary>
    Q,
    /// <summary>The R key.</summary>
    R,
    /// <summary>The S key.</summary>
    S,
    /// <summary>The T key.</summary>
    T,
    /// <summary>The U key.</summary>
    U,
    /// <summary>The V key.</summary>
    V,
    /// <summary>The W key.</summary>
    W,
    /// <summary>The X key.</summary>
    X,
    /// <summary>The Y key.</summary>
    Y,
    /// <summary>The Z key.</summary>
    Z,

    /// <summary>The 0 key.</summary>
    D0 = 48,
    /// <summary>The 1 key.</summary>
    D1,
    /// <summary>The 2 key.</summary>
    D2,
    /// <summary>The 3 key.</summary>
    D3,
    /// <summary>The 4 key.</summary>
    D4,
    /// <summary>The 5 key.</summary>
    D5,
    /// <summary>The 6 key.</summary>
    D6,
    /// <summary>The 7 key.</summary>
    D7,
    /// <summary>The 8 key.</summary>
    D8,
    /// <summary>The 9 key.</summary>
    D9,

    /// <summary>The F1 function key.</summary>
    F1 = 112,
    /// <summary>The F2 function key.</summary>
    F2,
    /// <summary>The F3 function key.</summary>
    F3,
    /// <summary>The F4 function key.</summary>
    F4,
    /// <summary>The F5 function key.</summary>
    F5,
    /// <summary>The F6 function key.</summary>
    F6,
    /// <summary>The F7 function key.</summary>
    F7,
    /// <summary>The F8 function key.</summary>
    F8,
    /// <summary>The F9 function key.</summary>
    F9,
    /// <summary>The F10 function key.</summary>
    F10,
    /// <summary>The F11 function key.</summary>
    F11,
    /// <summary>The F12 function key.</summary>
    F12,

    /// <summary>The Escape key.</summary>
    Escape = 27,
    /// <summary>The Space key.</summary>
    Space = 32,
    /// <summary>The Enter (Return) key.</summary>
    Enter = 13,
    /// <summary>The Tab key.</summary>
    Tab = 9,
    /// <summary>The Backspace key.</summary>
    Backspace = 8,
    /// <summary>The Delete key.</summary>
    Delete = 46,
    /// <summary>The Insert key.</summary>
    Insert = 45,
    /// <summary>The Home key.</summary>
    Home = 36,
    /// <summary>The End key.</summary>
    End = 35,
    /// <summary>The Page Up key.</summary>
    PageUp = 33,
    /// <summary>The Page Down key.</summary>
    PageDown = 34,
    /// <summary>The Left arrow key.</summary>
    Left = 37,
    /// <summary>The Up arrow key.</summary>
    Up = 38,
    /// <summary>The Right arrow key.</summary>
    Right = 39,
    /// <summary>The Down arrow key.</summary>
    Down = 40,

    /// <summary>The minus/hyphen (-) key.</summary>
    Minus = 189,
    /// <summary>The plus/equals (=) key.</summary>
    Plus = 187,
    /// <summary>The comma (,) key.</summary>
    Comma = 188,
    /// <summary>The period (.) key.</summary>
    Period = 190,
    /// <summary>The forward slash (/) key.</summary>
    Slash = 191,
    /// <summary>The backslash (\) key.</summary>
    Backslash = 220,
    /// <summary>The semicolon (;) key.</summary>
    Semicolon = 186,
    /// <summary>The single quote (') key.</summary>
    Quote = 222,
    /// <summary>The left bracket ([) key.</summary>
    BracketLeft = 219,
    /// <summary>The right bracket (]) key.</summary>
    BracketRight = 221,
    /// <summary>The backtick/grave accent (`) key.</summary>
    Backtick = 192,

    /// <summary>The numpad 0 key.</summary>
    NumPad0 = 96,
    /// <summary>The numpad 1 key.</summary>
    NumPad1,
    /// <summary>The numpad 2 key.</summary>
    NumPad2,
    /// <summary>The numpad 3 key.</summary>
    NumPad3,
    /// <summary>The numpad 4 key.</summary>
    NumPad4,
    /// <summary>The numpad 5 key.</summary>
    NumPad5,
    /// <summary>The numpad 6 key.</summary>
    NumPad6,
    /// <summary>The numpad 7 key.</summary>
    NumPad7,
    /// <summary>The numpad 8 key.</summary>
    NumPad8,
    /// <summary>The numpad 9 key.</summary>
    NumPad9,

    /// <summary>The Print Screen key.</summary>
    PrintScreen = 44,
    /// <summary>The Pause/Break key.</summary>
    Pause = 19,
    /// <summary>The Scroll Lock key.</summary>
    ScrollLock = 145
}

/// <summary>
/// Modifier key flags for global shortcut registration.
/// </summary>
[Flags]
public enum ShortcutModifiers
{
    /// <summary>No modifier keys.</summary>
    None = 0,
    /// <summary>Ctrl key (Control on macOS).</summary>
    Ctrl = 1,
    /// <summary>Alt key (Option on macOS).</summary>
    Alt = 2,
    /// <summary>Shift key.</summary>
    Shift = 4,
    /// <summary>Meta key (Cmd on macOS, Win on Windows, Super on Linux).</summary>
    Meta = 8
}
