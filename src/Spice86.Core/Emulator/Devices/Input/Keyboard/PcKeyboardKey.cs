namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

/// <summary>
/// Specifies the set of keys that can be represented on a standard keyboard, including alphanumeric, function,
/// modifier, and special keys.
/// </summary>
/// <remarks>
/// This is used to translate the first stage: Avalonia (UI) physical key to internal emulator representation.</remarks>
public enum PcKeyboardKey {
    /// <summary>
    /// No key pressed.
    /// </summary>
    None,

    /// <summary>
    /// The 1 key.
    /// </summary>
    D1,

    /// <summary>
    /// The 2 key.
    /// </summary>
    D2,

    /// <summary>
    /// The 3 key.
    /// </summary>
    D3,

    /// <summary>
    /// The 4 key.
    /// </summary>
    D4,

    /// <summary>
    /// The 5 key.
    /// </summary>
    D5,

    /// <summary>
    /// The 6 key.
    /// </summary>
    D6,

    /// <summary>
    /// The 7 key.
    /// </summary>
    D7,

    /// <summary>
    /// The 8 key.
    /// </summary>
    D8,

    /// <summary>
    /// The 9 key.
    /// </summary>
    D9,

    /// <summary>
    /// The 0 key.
    /// </summary>
    D0,

    /// <summary>
    /// The Q key.
    /// </summary>
    Q,

    /// <summary>
    /// The W key.
    /// </summary>
    W,

    /// <summary>
    /// The E key.
    /// </summary>
    E,

    /// <summary>
    /// The R key.
    /// </summary>
    R,

    /// <summary>
    /// The T key.
    /// </summary>
    T,

    /// <summary>
    /// The Y key.
    /// </summary>
    Y,

    /// <summary>
    /// The U key.
    /// </summary>
    U,

    /// <summary>
    /// The I key.
    /// </summary>
    I,

    /// <summary>
    /// The O key.
    /// </summary>
    O,

    /// <summary>
    /// The P key.
    /// </summary>
    P,

    /// <summary>
    /// The A key.
    /// </summary>
    A,

    /// <summary>
    /// The S key.
    /// </summary>
    S,

    /// <summary>
    /// The D key.
    /// </summary>
    D,

    /// <summary>
    /// The F key.
    /// </summary>
    F,

    /// <summary>
    /// The G key.
    /// </summary>
    G,

    /// <summary>
    /// The H key.
    /// </summary>
    H,

    /// <summary>
    /// The J key.
    /// </summary>
    J,

    /// <summary>
    /// The K key.
    /// </summary>
    K,

    /// <summary>
    /// The L key.
    /// </summary>
    L,

    /// <summary>
    /// The Z key.
    /// </summary>
    Z,

    /// <summary>
    /// The X key.
    /// </summary>
    X,

    /// <summary>
    /// The C key.
    /// </summary>
    C,

    /// <summary>
    /// The V key.
    /// </summary>
    V,

    /// <summary>
    /// The B key.
    /// </summary>
    B,

    /// <summary>
    /// The N key.
    /// </summary>
    N,

    /// <summary>
    /// The M key.
    /// </summary>
    M,

    /// <summary>
    /// The F1 key.
    /// </summary>
    F1,

    /// <summary>
    /// The F2 key.
    /// </summary>
    F2,

    /// <summary>
    /// The F3 key.
    /// </summary>
    F3,

    /// <summary>
    /// The F4 key.
    /// </summary>
    F4,

    /// <summary>
    /// The F5 key.
    /// </summary>
    F5,

    /// <summary>
    /// The F6 key.
    /// </summary>
    F6,

    /// <summary>
    /// The F7 key.
    /// </summary>
    F7,

    /// <summary>
    /// The F8 key.
    /// </summary>
    F8,

    /// <summary>
    /// The F9 key.
    /// </summary>
    F9,

    /// <summary>
    /// The F10 key.
    /// </summary>
    F10,

    /// <summary>
    /// The F11 key.
    /// </summary>
    F11,

    /// <summary>
    /// The F12 key.
    /// </summary>
    F12,

    /// <summary>
    /// The Escape key.
    /// </summary>
    Escape,

    /// <summary>
    /// The Tab key.
    /// </summary>
    Tab,

    /// <summary>
    /// The Backspace key.
    /// </summary>
    Backspace,

    /// <summary>
    /// The Enter key.
    /// </summary>
    Enter,

    /// <summary>
    /// The Space bar.
    /// </summary>
    Space,

    /// <summary>
    /// The left Alt key.
    /// </summary>
    LeftAlt,

    /// <summary>
    /// The right Alt key.
    /// </summary>
    RightAlt,

    /// <summary>
    /// The left Ctrl key.
    /// </summary>
    LeftCtrl,

    /// <summary>
    /// The right Ctrl key.
    /// </summary>
    RightCtrl,

    /// <summary>
    /// The left Windows (GUI) key.
    /// </summary>
    LeftGui,

    /// <summary>
    /// The right Windows (GUI) key.
    /// </summary>
    RightGui,

    /// <summary>
    /// The left Shift key.
    /// </summary>
    LeftShift,

    /// <summary>
    /// The right Shift key.
    /// </summary>
    RightShift,

    /// <summary>
    /// The Caps Lock key.
    /// </summary>
    CapsLock,

    /// <summary>
    /// The Scroll Lock key.
    /// </summary>
    ScrollLock,

    /// <summary>
    /// The Num Lock key.
    /// </summary>
    NumLock,

    /// <summary>
    /// The Grave (backtick/tilde) key.
    /// </summary>
    Grave,

    /// <summary>
    /// The Minus (dash/underscore) key.
    /// </summary>
    Minus,

    /// <summary>
    /// The Equals (plus/equals) key.
    /// </summary>
    Equals,

    /// <summary>
    /// The Backslash key.
    /// </summary>
    Backslash,

    /// <summary>
    /// The Left Bracket ([/{}) key.
    /// </summary>
    LeftBracket,

    /// <summary>
    /// The Right Bracket (]/{}) key.
    /// </summary>
    RightBracket,

    /// <summary>
    /// The Semicolon (:) key.
    /// </summary>
    Semicolon,

    /// <summary>
    /// The Quote (") key.
    /// </summary>
    Quote,

    /// <summary>
    /// The OEM 102 key (usually between Shift and Z).
    /// </summary>
    Oem102,

    /// <summary>
    /// The Period (.) key.
    /// </summary>
    Period,

    /// <summary>
    /// The Comma (,) key.
    /// </summary>
    Comma,

    /// <summary>
    /// The Slash (/) key.
    /// </summary>
    Slash,

    /// <summary>
    /// The ABNT C1 (Brazilian) key.
    /// </summary>
    Abnt1,

    /// <summary>
    /// The Print Screen key.
    /// </summary>
    PrintScreen,

    /// <summary>
    /// The Pause key.
    /// </summary>
    Pause,

    /// <summary>
    /// The Insert key.
    /// </summary>
    Insert,

    /// <summary>
    /// The Home key.
    /// </summary>
    Home,

    /// <summary>
    /// The Page Up key.
    /// </summary>
    PageUp,

    /// <summary>
    /// The Delete key.
    /// </summary>
    Delete,

    /// <summary>
    /// The End key.
    /// </summary>
    End,

    /// <summary>
    /// The Page Down key.
    /// </summary>
    PageDown,

    /// <summary>
    /// The Left arrow key.
    /// </summary>
    Left,

    /// <summary>
    /// The Up arrow key.
    /// </summary>
    Up,

    /// <summary>
    /// The Down arrow key.
    /// </summary>
    Down,

    /// <summary>
    /// The Right arrow key.
    /// </summary>
    Right,

    /// <summary>
    /// The 1 key on the numeric keypad.
    /// </summary>
    Kp1,

    /// <summary>
    /// The 2 key on the numeric keypad.
    /// </summary>
    Kp2,

    /// <summary>
    /// The 3 key on the numeric keypad.
    /// </summary>
    Kp3,

    /// <summary>
    /// The 4 key on the numeric keypad.
    /// </summary>
    Kp4,

    /// <summary>
    /// The 5 key on the numeric keypad.
    /// </summary>
    Kp5,

    /// <summary>
    /// The 6 key on the numeric keypad.
    /// </summary>
    Kp6,

    /// <summary>
    /// The 7 key on the numeric keypad.
    /// </summary>
    Kp7,

    /// <summary>
    /// The 8 key on the numeric keypad.
    /// </summary>
    Kp8,

    /// <summary>
    /// The 9 key on the numeric keypad.
    /// </summary>
    Kp9,

    /// <summary>
    /// The 0 key on the numeric keypad.
    /// </summary>
    Kp0,

    /// <summary>
    /// The Divide key on the numeric keypad.
    /// </summary>
    KpDivide,

    /// <summary>
    /// The Multiply key on the numeric keypad.
    /// </summary>
    KpMultiply,

    /// <summary>
    /// The Minus key on the numeric keypad.
    /// </summary>
    KpMinus,

    /// <summary>
    /// The Plus key on the numeric keypad.
    /// </summary>
    KpPlus,

    /// <summary>
    /// The Enter key on the numeric keypad.
    /// </summary>
    KpEnter,

    /// <summary>
    /// The Period key on the numeric keypad.
    /// </summary>
    KpPeriod,
}