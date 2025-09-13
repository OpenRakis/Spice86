namespace Spice86.Shared.Emulator.Keyboard;

//
// Summary:
//     Represents a keyboard physical key.
//
// Remarks:
//     The names follow the W3C codes: https://www.w3.org/TR/uievents-code/
public enum PhysicalKey {
    //
    // Summary:
    //     Represents no key.
    None,
    //
    // Summary:
    //     `~ on a US keyboard. This is the 半角/全角/漢字 (hankaku/zenkaku/kanji) key on Japanese
    //     keyboards.
    Backquote,
    //
    // Summary:
    //     Used for both the US \| (on the 101-key layout) and also for the key located
    //     between the " and Enter keys on row C of the 102-, 104- and 106-key layouts.
    //     #~ on a UK (102) keyboard.
    Backslash,
    //
    // Summary:
    //     [{ on a US keyboard.
    BracketLeft,
    //
    // Summary:
    //     ]} on a US keyboard.
    BracketRight,
    //
    // Summary:
    //     ,< on a US keyboard.
    Comma,
    //
    // Summary:
    //     0) on a US keyboard.
    Digit0,
    //
    // Summary:
    //     1! on a US keyboard.
    Digit1,
    //
    // Summary:
    //     2@ on a US keyboard.
    Digit2,
    //
    // Summary:
    //     3# on a US keyboard.
    Digit3,
    //
    // Summary:
    //     4$ on a US keyboard.
    Digit4,
    //
    // Summary:
    //     5% on a US keyboard.
    Digit5,
    //
    // Summary:
    //     6^ on a US keyboard.
    Digit6,
    //
    // Summary:
    //     7& on a US keyboard.
    Digit7,
    //
    // Summary:
    //     8* on a US keyboard.
    Digit8,
    //
    // Summary:
    //     9( on a US keyboard.
    Digit9,
    //
    // Summary:
    //     =+ on a US keyboard.
    Equal,
    //
    // Summary:
    //     Located between the left Shift and Z keys. \| on a UK keyboard.
    IntlBackslash,
    //
    // Summary:
    //     Located between the / and right Shift keys. \ろ (ro) on a Japanese keyboard.
    IntlRo,
    //
    // Summary:
    //     Located between the = and Backspace keys. ¥ (yen) on a Japanese keyboard. \/
    //     on a Russian keyboard.
    IntlYen,
    //
    // Summary:
    //     a on a US keyboard. q on an AZERTY (e.g., French) keyboard.
    A,
    //
    // Summary:
    //     b on a US keyboard.
    B,
    //
    // Summary:
    //     c on a US keyboard.
    C,
    //
    // Summary:
    //     d on a US keyboard.
    D,
    //
    // Summary:
    //     e on a US keyboard.
    E,
    //
    // Summary:
    //     f on a US keyboard.
    F,
    //
    // Summary:
    //     g on a US keyboard.
    G,
    //
    // Summary:
    //     h on a US keyboard.
    H,
    //
    // Summary:
    //     i on a US keyboard.
    I,
    //
    // Summary:
    //     j on a US keyboard.
    J,
    //
    // Summary:
    //     k on a US keyboard.
    K,
    //
    // Summary:
    //     l on a US keyboard.
    L,
    //
    // Summary:
    //     m on a US keyboard.
    M,
    //
    // Summary:
    //     n on a US keyboard.
    N,
    //
    // Summary:
    //     o on a US keyboard.
    O,
    //
    // Summary:
    //     p on a US keyboard.
    P,
    //
    // Summary:
    //     q on a US keyboard. a on an AZERTY (e.g., French) keyboard.
    Q,
    //
    // Summary:
    //     r on a US keyboard.
    R,
    //
    // Summary:
    //     s on a US keyboard.
    S,
    //
    // Summary:
    //     t on a US keyboard.
    T,
    //
    // Summary:
    //     u on a US keyboard.
    U,
    //
    // Summary:
    //     v on a US keyboard.
    V,
    //
    // Summary:
    //     w on a US keyboard. z on an AZERTY (e.g., French) keyboard.
    W,
    //
    // Summary:
    //     x on a US keyboard.
    X,
    //
    // Summary:
    //     y on a US keyboard. z on a QWERTZ (e.g., German) keyboard.
    Y,
    //
    // Summary:
    //     z on a US keyboard. w on an AZERTY (e.g., French) keyboard. y on a QWERTZ (e.g.,
    //     German) keyboard.
    Z,
    //
    // Summary:
    //     -_ on a US keyboard.
    Minus,
    //
    // Summary:
    //     .> on a US keyboard.
    Period,
    //
    // Summary:
    //     '" on a US keyboard.
    Quote,
    //
    // Summary:
    //     ;: on a US keyboard.
    Semicolon,
    //
    // Summary:
    //     /? on a US keyboard.
    Slash,
    //
    // Summary:
    //     Alt, Option or ⌥.
    AltLeft,
    //
    // Summary:
    //     Alt, Option or ⌥. This is labelled AltGr key on many keyboard layouts.
    AltRight,
    //
    // Summary:
    //     Backspace or ⌫. Labelled Delete on Apple keyboards.
    Backspace,
    //
    // Summary:
    //     CapsLock or ⇪.
    CapsLock,
    //
    // Summary:
    //     The application context menu key, which is typically found between the right
    //     Meta key and the right Control key.
    ContextMenu,
    //
    // Summary:
    //     Control or ⌃.
    ControlLeft,
    //
    // Summary:
    //     Control or ⌃.
    ControlRight,
    //
    // Summary:
    //     Enter or ↵. Labelled Return on Apple keyboards.
    Enter,
    //
    // Summary:
    //     The ⊞ (Windows), ⌘, Command or other OS symbol key.
    MetaLeft,
    //
    // Summary:
    //     The ⊞ (Windows), ⌘, Command or other OS symbol key.
    MetaRight,
    //
    // Summary:
    //     Shift or ⇧.
    ShiftLeft,
    //
    // Summary:
    //     Shift or ⇧.
    ShiftRight,
    //
    // Summary:
    //     (space).
    Space,
    //
    // Summary:
    //     Tab or ⇥.
    Tab,
    //
    // Summary:
    //     Japanese: 変換 (henkan).
    Convert,
    //
    // Summary:
    //     Japanese: カタカナ/ひらがな/ローマ字 (katakana/hiragana/romaji).
    KanaMode,
    //
    // Summary:
    //     Korean: HangulMode 한/영 (han/yeong). Japanese (Mac keyboard): かな (kana).
    Lang1,
    //
    // Summary:
    //     Korean: Hanja 한자 (hanja). Japanese (Mac keyboard): 英数 (eisu).
    Lang2,
    //
    // Summary:
    //     Japanese (word-processing keyboard): Katakana.
    Lang3,
    //
    // Summary:
    //     Japanese (word-processing keyboard): Hiragana.
    Lang4,
    //
    // Summary:
    //     Japanese (word-processing keyboard): Zenkaku/Hankaku.
    Lang5,
    //
    // Summary:
    //     Japanese: 無変換 (muhenkan).
    NonConvert,
    //
    // Summary:
    //     ⌦. The forward delete key. Note that on Apple keyboards, the key labelled Delete
    //     on the main part of the keyboard is Avalonia.Input.PhysicalKey.Backspace.
    Delete,
    //
    // Summary:
    //     End or ↘.
    End,
    //
    // Summary:
    //     Help. Not present on standard PC keyboards.
    Help,
    //
    // Summary:
    //     Home or ↖.
    Home,
    //
    // Summary:
    //     Insert or Ins. Not present on Apple keyboards.
    Insert,
    //
    // Summary:
    //     Page Down, PgDn or ⇟.
    PageDown,
    //
    // Summary:
    //     Page Up, PgUp or ⇞.
    PageUp,
    //
    // Summary:
    //     ↓.
    ArrowDown,
    //
    // Summary:
    //     ←.
    ArrowLeft,
    //
    // Summary:
    //     →.
    ArrowRight,
    //
    // Summary:
    //     ↑.
    ArrowUp,
    //
    // Summary:
    //     Numeric keypad Num Lock. On the Mac, this is used for the numpad Clear key.
    NumLock,
    //
    // Summary:
    //     Numeric keypad 0 Ins on a keyboard. 0 on a phone or remote control.
    NumPad0,
    //
    // Summary:
    //     Numeric keypad 1 End on a keyboard. 1 or 1 QZ on a phone or remote control.
    NumPad1,
    //
    // Summary:
    //     Numeric keypad 2 ↓ on a keyboard. 2 ABC on a phone or remote control.
    NumPad2,
    //
    // Summary:
    //     Numeric keypad 3 PgDn on a keyboard. 3 DEF on a phone or remote control.
    NumPad3,
    //
    // Summary:
    //     Numeric keypad 4 ← on a keyboard. 4 GHI on a phone or remote control.
    NumPad4,
    //
    // Summary:
    //     Numeric keypad 5 on a keyboard. 5 JKL on a phone or remote control.
    NumPad5,
    //
    // Summary:
    //     Numeric keypad 6 → on a keyboard. 6 MNO on a phone or remote control.
    NumPad6,
    //
    // Summary:
    //     Numeric keypad 7 Home on a keyboard. 7 PQRS or 7 PRS on a phone or remote control.
    NumPad7,
    //
    // Summary:
    //     Numeric keypad 8 ↑ on a keyboard. 8 TUV on a phone or remote control.
    NumPad8,
    //
    // Summary:
    //     Numeric keypad 9 PgUp on a keyboard. 9 WXYZ or 9 WXY on a phone or remote control.
    NumPad9,
    //
    // Summary:
    //     Numeric keypad +.
    NumPadAdd,
    //
    // Summary:
    //     Numeric keypad C or AC (All Clear). Also for use with numpads that have a Clear
    //     key that is separate from the NumLock key. On the Mac, the numpad Clear key is
    //     Avalonia.Input.PhysicalKey.NumLock.
    NumPadClear,
    //
    // Summary:
    //     Numeric keypad , (thousands separator). For locales where the thousands separator
    //     is a "." (e.g., Brazil), this key may generate a ..
    NumPadComma,
    //
    // Summary:
    //     Numeric keypad . Del. For locales where the decimal separator is "," (e.g., Brazil),
    //     this key may generate a ,.
    NumPadDecimal,
    //
    // Summary:
    //     Numeric keypad /.
    NumPadDivide,
    //
    // Summary:
    //     Numeric keypad Enter.
    NumPadEnter,
    //
    // Summary:
    //     Numeric keypad =.
    NumPadEqual,
    //
    // Summary:
    //     Numeric keypad * on a keyboard. For use with numpads that provide mathematical
    //     operations (+, -, * and /).
    NumPadMultiply,
    //
    // Summary:
    //     Numeric keypad (. Found on the Microsoft Natural Keyboard.
    NumPadParenLeft,
    //
    // Summary:
    //     Numeric keypad ). Found on the Microsoft Natural Keyboard.
    NumPadParenRight,
    //
    // Summary:
    //     Numeric keypad -.
    NumPadSubtract,
    //
    // Summary:
    //     Esc or ⎋.
    Escape,
    //
    // Summary:
    //     F1.
    F1,
    //
    // Summary:
    //     F2.
    F2,
    //
    // Summary:
    //     F3.
    F3,
    //
    // Summary:
    //     F4.
    F4,
    //
    // Summary:
    //     F5.
    F5,
    //
    // Summary:
    //     F6.
    F6,
    //
    // Summary:
    //     F7.
    F7,
    //
    // Summary:
    //     F8.
    F8,
    //
    // Summary:
    //     F9.
    F9,
    //
    // Summary:
    //     F10.
    F10,
    //
    // Summary:
    //     F11.
    F11,
    //
    // Summary:
    //     F12.
    F12,
    //
    // Summary:
    //     F13.
    F13,
    //
    // Summary:
    //     F14.
    F14,
    //
    // Summary:
    //     F15.
    F15,
    //
    // Summary:
    //     F16.
    F16,
    //
    // Summary:
    //     F17.
    F17,
    //
    // Summary:
    //     F18.
    F18,
    //
    // Summary:
    //     F19.
    F19,
    //
    // Summary:
    //     F20.
    F20,
    //
    // Summary:
    //     F21.
    F21,
    //
    // Summary:
    //     F22.
    F22,
    //
    // Summary:
    //     F23.
    F23,
    //
    // Summary:
    //     F24.
    F24,
    //
    // Summary:
    //     PrtScr SysRq or Print Screen.
    PrintScreen,
    //
    // Summary:
    //     Scroll Lock.
    ScrollLock,
    //
    // Summary:
    //     Pause Break.
    Pause,
    //
    // Summary:
    //     Browser Back. Some laptops place this key to the left of the ↑ key.
    BrowserBack,
    //
    // Summary:
    //     Browser Favorites.
    BrowserFavorites,
    //
    // Summary:
    //     Browser Forward. Some laptops place this key to the right of the ↑ key.
    BrowserForward,
    //
    // Summary:
    //     Browser Home.
    BrowserHome,
    //
    // Summary:
    //     Browser Refresh.
    BrowserRefresh,
    //
    // Summary:
    //     Browser Search.
    BrowserSearch,
    //
    // Summary:
    //     Browser Stop.
    BrowserStop,
    //
    // Summary:
    //     Eject or ⏏. This key is placed in the function section on some Apple keyboards.
    Eject,
    //
    // Summary:
    //     App 1. Sometimes labelled My Computer on the keyboard.
    LaunchApp1,
    //
    // Summary:
    //     App 2. Sometimes labelled Calculator on the keyboard.
    LaunchApp2,
    //
    // Summary:
    //     Mail.
    LaunchMail,
    //
    // Summary:
    //     Media Play/Pause or ⏵⏸.
    MediaPlayPause,
    //
    // Summary:
    //     Media Select.
    MediaSelect,
    //
    // Summary:
    //     Media Stop or ⏹.
    MediaStop,
    //
    // Summary:
    //     Media Next or ⏭.
    MediaTrackNext,
    //
    // Summary:
    //     Media Previous or ⏮.
    MediaTrackPrevious,
    //
    // Summary:
    //     Power.
    Power,
    //
    // Summary:
    //     Sleep.
    Sleep,
    //
    // Summary:
    //     Volume Down.
    AudioVolumeDown,
    //
    // Summary:
    //     Mute.
    AudioVolumeMute,
    //
    // Summary:
    //     Volume Up.
    AudioVolumeUp,
    //
    // Summary:
    //     Wake Up.
    WakeUp,
    //
    // Summary:
    //     Again. Legacy. Found on Sun’s USB keyboard.
    Again,
    //
    // Summary:
    //     Copy. Legacy. Found on Sun’s USB keyboard.
    Copy,
    //
    // Summary:
    //     Cut. Legacy. Found on Sun’s USB keyboard.
    Cut,
    //
    // Summary:
    //     Find. Legacy. Found on Sun’s USB keyboard.
    Find,
    //
    // Summary:
    //     Open. Legacy. Found on Sun’s USB keyboard.
    Open,
    //
    // Summary:
    //     Paste. Legacy. Found on Sun’s USB keyboard.
    Paste,
    //
    // Summary:
    //     Props. Legacy. Found on Sun’s USB keyboard.
    Props,
    //
    // Summary:
    //     Select. Legacy. Found on Sun’s USB keyboard.
    Select,
    //
    // Summary:
    //     Undo. Legacy. Found on Sun’s USB keyboard.
    Undo
}