namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

/// <summary>
/// Keyboard key identifiers, equivalent to DOSBox's KBD_KEYS enum.
/// </summary>
public enum KbdKey {
    None,

    D1, D2, D3, D4, D5, D6, D7, D8, D9, D0,
    Q, W, E, R, T, Y, U, I, O, P,
    A, S, D, F, G, H, J, K, L,
    Z, X, C, V, B, N, M,

    F1, F2, F3, F4, F5, F6,
    F7, F8, F9, F10, F11, F12,

    Escape, Tab, Backspace, Enter, Space,

    LeftAlt, RightAlt,
    LeftCtrl, RightCtrl,
    LeftGui, RightGui, // 'windows' keys
    LeftShift, RightShift,

    CapsLock, ScrollLock, NumLock,

    Grave, Minus, Equals, Backslash,
    LeftBracket, RightBracket,
    Semicolon, Quote,
    Oem102, // usually between SHIFT and Z
    Period, Comma, Slash, Abnt1,

    PrintScreen, Pause,

    Insert, Home, PageUp,
    Delete, End, PageDown,

    Left, Up, Down, Right,

    Kp1, Kp2, Kp3, Kp4, Kp5, Kp6, Kp7, Kp8, Kp9, Kp0,
    KpDivide, KpMultiply, KpMinus, KpPlus,
    KpEnter, KpPeriod,

    Last
}