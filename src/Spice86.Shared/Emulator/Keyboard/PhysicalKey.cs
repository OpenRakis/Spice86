namespace Spice86.Shared.Emulator.Keyboard;

/// <summary>
/// Represents a keyboard physical key.
/// </summary>
/// <remarks>
/// The names follow the W3C codes: https://www.w3.org/TR/uievents-code/
/// </remarks>
public enum PhysicalKey {
    /// <summary>
    /// Represents no key.
    /// </summary>
    None,
    /// <summary>
    /// `~ on a US keyboard. This is the 半角/全角/漢字 (hankaku/zenkaku/kanji) key on Japanese keyboards.
    /// </summary>
    Backquote,
    /// <summary>
    /// Used for both the US \| (on the 101-key layout) and also for the key located between the " and Enter keys on row C of the 102-, 104- and 106-key layouts. #~ on a UK (102) keyboard.
    /// </summary>
    Backslash,
    /// <summary>
    /// [{ on a US keyboard.
    /// </summary>
    BracketLeft,
    /// <summary>
    /// ]} on a US keyboard.
    /// </summary>
    BracketRight,
    /// <summary>
    /// ,&lt; on a US keyboard.
    /// </summary>
    Comma,
    /// <summary>
    /// 0) on a US keyboard.
    /// </summary>
    Digit0,
    /// <summary>
    /// 1! on a US keyboard.
    /// </summary>
    Digit1,
    /// <summary>
    /// 2@ on a US keyboard.
    /// </summary>
    Digit2,
    /// <summary>
    /// 3# on a US keyboard.
    /// </summary>
    Digit3,
    /// <summary>
    /// 4$ on a US keyboard.
    /// </summary>
    Digit4,
    /// <summary>
    /// 5% on a US keyboard.
    /// </summary>
    Digit5,
    /// <summary>
    /// 6^ on a US keyboard.
    /// </summary>
    Digit6,
    /// <summary>
    /// 7&amp; on a US keyboard.
    /// </summary>
    Digit7,
    /// <summary>
    /// 8* on a US keyboard.
    /// </summary>
    Digit8,
    /// <summary>
    /// 9( on a US keyboard.
    /// </summary>
    Digit9,
    /// <summary>
    /// =+ on a US keyboard.
    /// </summary>
    Equal,
    /// <summary>
    /// Located between the left Shift and Z keys. \| on a UK keyboard.
    /// </summary>
    IntlBackslash,
    /// <summary>
    /// Located between the / and right Shift keys. \ろ (ro) on a Japanese keyboard.
    /// </summary>
    IntlRo,
    /// <summary>
    /// Located between the = and Backspace keys. ¥ (yen) on a Japanese keyboard. \/ on a Russian keyboard.
    /// </summary>
    IntlYen,
    /// <summary>
    /// a on a US keyboard. q on an AZERTY (e.g., French) keyboard.
    /// </summary>
    A,
    /// <summary>
    /// b on a US keyboard.
    /// </summary>
    B,
    /// <summary>
    /// c on a US keyboard.
    /// </summary>
    C,
    /// <summary>
    /// d on a US keyboard.
    /// </summary>
    D,
    /// <summary>
    /// e on a US keyboard.
    /// </summary>
    E,
    /// <summary>
    /// f on a US keyboard.
    /// </summary>
    F,
    /// <summary>
    /// g on a US keyboard.
    /// </summary>
    G,
    /// <summary>
    /// h on a US keyboard.
    /// </summary>
    H,
    /// <summary>
    /// i on a US keyboard.
    /// </summary>
    I,
    /// <summary>
    /// j on a US keyboard.
    /// </summary>
    J,
    /// <summary>
    /// k on a US keyboard.
    /// </summary>
    K,
    /// <summary>
    /// l on a US keyboard.
    /// </summary>
    L,
    /// <summary>
    /// m on a US keyboard.
    /// </summary>
    M,
    /// <summary>
    /// n on a US keyboard.
    /// </summary>
    N,
    /// <summary>
    /// o on a US keyboard.
    /// </summary>
    O,
    /// <summary>
    /// p on a US keyboard.
    /// </summary>
    P,
    /// <summary>
    /// q on a US keyboard. a on an AZERTY (e.g., French) keyboard.
    /// </summary>
    Q,
    /// <summary>
    /// r on a US keyboard.
    /// </summary>
    R,
    /// <summary>
    /// s on a US keyboard.
    /// </summary>
    S,
    /// <summary>
    /// t on a US keyboard.
    /// </summary>
    T,
    /// <summary>
    /// u on a US keyboard.
    /// </summary>
    U,
    /// <summary>
    /// v on a US keyboard.
    /// </summary>
    V,
    /// <summary>
    /// w on a US keyboard. z on an AZERTY (e.g., French) keyboard.
    /// </summary>
    W,
    /// <summary>
    /// x on a US keyboard.
    /// </summary>
    X,
    /// <summary>
    /// y on a US keyboard. z on a QWERTZ (e.g., German) keyboard.
    /// </summary>
    Y,
    /// <summary>
    /// z on a US keyboard. w on an AZERTY (e.g., French) keyboard. y on a QWERTZ (e.g., German) keyboard.
    /// </summary>
    Z,
    /// <summary>
    /// -_ on a US keyboard.
    /// </summary>
    Minus,
    /// <summary>
    /// .&gt; on a US keyboard.
    /// </summary>
    Period,
    /// <summary>
    /// '" on a US keyboard.
    /// </summary>
    Quote,
    /// <summary>
    /// ;: on a US keyboard.
    /// </summary>
    Semicolon,
    /// <summary>
    /// /? on a US keyboard.
    /// </summary>
    Slash,
    /// <summary>
    /// Alt, Option or ⌥.
    /// </summary>
    AltLeft,
    /// <summary>
    /// Alt, Option or ⌥. This is labelled AltGr key on many keyboard layouts.
    /// </summary>
    AltRight,
    /// <summary>
    /// Backspace or ⌫. Labelled Delete on Apple keyboards.
    /// </summary>
    Backspace,
    /// <summary>
    /// CapsLock or ⇪.
    /// </summary>
    CapsLock,
    /// <summary>
    /// The application context menu key, which is typically found between the right Meta key and the right Control key.
    /// </summary>
    ContextMenu,
    /// <summary>
    /// Control or ⌃.
    /// </summary>
    ControlLeft,
    /// <summary>
    /// Control or ⌃.
    /// </summary>
    ControlRight,
    /// <summary>
    /// Enter or ↵. Labelled Return on Apple keyboards.
    /// </summary>
    Enter,
    /// <summary>
    /// The ⊞ (Windows), ⌘, Command or other OS symbol key.
    /// </summary>
    MetaLeft,
    /// <summary>
    /// The ⊞ (Windows), ⌘, Command or other OS symbol key.
    /// </summary>
    MetaRight,
    /// <summary>
    /// Shift or ⇧.
    /// </summary>
    ShiftLeft,
    /// <summary>
    /// Shift or ⇧.
    /// </summary>
    ShiftRight,
    /// <summary>
    /// (space).
    /// </summary>
    Space,
    /// <summary>
    /// Tab or ⇥.
    /// </summary>
    Tab,
    /// <summary>
    /// Japanese: 変換 (henkan).
    /// </summary>
    Convert,
    /// <summary>
    /// Japanese: カタカナ/ひらがな/ローマ字 (katakana/hiragana/romaji).
    /// </summary>
    KanaMode,
    /// <summary>
    /// Korean: HangulMode 한/영 (han/yeong). Japanese (Mac keyboard): かな (kana).
    /// </summary>
    Lang1,
    /// <summary>
    /// Korean: Hanja 한자 (hanja). Japanese (Mac keyboard): 英数 (eisu).
    /// </summary>
    Lang2,
    /// <summary>
    /// Japanese (word-processing keyboard): Katakana.
    /// </summary>
    Lang3,
    /// <summary>
    /// Japanese (word-processing keyboard): Hiragana.
    /// </summary>
    Lang4,
    /// <summary>
    /// Japanese (word-processing keyboard): Zenkaku/Hankaku.
    /// </summary>
    Lang5,
    /// <summary>
    /// Japanese: 無変換 (muhenkan).
    /// </summary>
    NonConvert,
    /// <summary>
    /// ⌦. The forward delete key. Note that on Apple keyboards, the key labelled Delete on the main part of the keyboard is Avalonia.Input.PhysicalKey.Backspace.
    /// </summary>
    Delete,
    /// <summary>
    /// End or ↘.
    /// </summary>
    End,
    /// <summary>
    /// Help. Not present on standard PC keyboards.
    /// </summary>
    Help,
    /// <summary>
    /// Home or ↖.
    /// </summary>
    Home,
    /// <summary>
    /// Insert or Ins. Not present on Apple keyboards.
    /// </summary>
    Insert,
    /// <summary>
    /// Page Down, PgDn or ⇟.
    /// </summary>
    PageDown,
    /// <summary>
    /// Page Up, PgUp or ⇞.
    /// </summary>
    PageUp,
    /// <summary>
    /// ↓.
    /// </summary>
    ArrowDown,
    /// <summary>
    /// ←.
    /// </summary>
    ArrowLeft,
    /// <summary>
    /// →.
    /// </summary>
    ArrowRight,
    /// <summary>
    /// ↑.
    /// </summary>
    ArrowUp,
    /// <summary>
    /// Numeric keypad Num Lock. On the Mac, this is used for the numpad Clear key.
    /// </summary>
    NumLock,
    /// <summary>
    /// Numeric keypad 0 Ins on a keyboard. 0 on a phone or remote control.
    /// </summary>
    NumPad0,
    /// <summary>
    /// Numeric keypad 1 End on a keyboard. 1 or 1 QZ on a phone or remote control.
    /// </summary>
    NumPad1,
    /// <summary>
    /// Numeric keypad 2 ↓ on a keyboard. 2 ABC on a phone or remote control.
    /// </summary>
    NumPad2,
    /// <summary>
    /// Numeric keypad 3 PgDn on a keyboard. 3 DEF on a phone or remote control.
    /// </summary>
    NumPad3,
    /// <summary>
    /// Numeric keypad 4 ← on a keyboard. 4 GHI on a phone or remote control.
    /// </summary>
    NumPad4,
    /// <summary>
    /// Numeric keypad 5 on a keyboard. 5 JKL on a phone or remote control.
    /// </summary>
    NumPad5,
    /// <summary>
    /// Numeric keypad 6 → on a keyboard. 6 MNO on a phone or remote control.
    /// </summary>
    NumPad6,
    /// <summary>
    /// Numeric keypad 7 Home on a keyboard. 7 PQRS or 7 PRS on a phone or remote control.
    /// </summary>
    NumPad7,
    /// <summary>
    /// Numeric keypad 8 ↑ on a keyboard. 8 TUV on a phone or remote control.
    /// </summary>
    NumPad8,
    /// <summary>
    /// Numeric keypad 9 PgUp on a keyboard. 9 WXYZ or 9 WXY on a phone or remote control.
    /// </summary>
    NumPad9,
    /// <summary>
    /// Numeric keypad +.
    /// </summary>
    NumPadAdd,
    /// <summary>
    /// Numeric keypad C or AC (All Clear). Also for use with numpads that have a Clear key that is separate from the NumLock key. On the Mac, the numpad Clear key is Avalonia.Input.PhysicalKey.NumLock.
    /// </summary>
    NumPadClear,
    /// <summary>
    /// Numeric keypad , (thousands separator). For locales where the thousands separator is a "." (e.g., Brazil), this key may generate a ..
    /// </summary>
    NumPadComma,
    /// <summary>
    /// Numeric keypad . Del. For locales where the decimal separator is "," (e.g., Brazil), this key may generate a ,.
    /// </summary>
    NumPadDecimal,
    /// <summary>
    /// Numeric keypad /.
    /// </summary>
    NumPadDivide,
    /// <summary>
    /// Numeric keypad Enter.
    /// </summary>
    NumPadEnter,
    /// <summary>
    /// Numeric keypad =.
    /// </summary>
    NumPadEqual,
    /// <summary>
    /// Numeric keypad * on a keyboard. For use with numpads that provide mathematical operations (+, -, * and /).
    /// </summary>
    NumPadMultiply,
    /// <summary>
    /// Numeric keypad (. Found on the Microsoft Natural Keyboard.
    /// </summary>
    NumPadParenLeft,
    /// <summary>
    /// Numeric keypad ). Found on the Microsoft Natural Keyboard.
    /// </summary>
    NumPadParenRight,
    /// <summary>
    /// Numeric keypad -.
    /// </summary>
    NumPadSubtract,
    /// <summary>
    /// Esc or ⎋.
    /// </summary>
    Escape,
    /// <summary>
    /// F1.
    /// </summary>
    F1,
    /// <summary>
    /// F2.
    /// </summary>
    F2,
    /// <summary>
    /// F3.
    /// </summary>
    F3,
    /// <summary>
    /// F4.
    /// </summary>
    F4,
    /// <summary>
    /// F5.
    /// </summary>
    F5,
    /// <summary>
    /// F6.
    /// </summary>
    F6,
    /// <summary>
    /// F7.
    /// </summary>
    F7,
    /// <summary>
    /// F8.
    /// </summary>
    F8,
    /// <summary>
    /// F9.
    /// </summary>
    F9,
    /// <summary>
    /// F10.
    /// </summary>
    F10,
    /// <summary>
    /// F11.
    /// </summary>
    F11,
    /// <summary>
    /// F12.
    /// </summary>
    F12,
    /// <summary>
    /// F13.
    /// </summary>
    F13,
    /// <summary>
    /// F14.
    /// </summary>
    F14,
    /// <summary>
    /// F15.
    /// </summary>
    F15,
    /// <summary>
    /// F16.
    /// </summary>
    F16,
    /// <summary>
    /// F17.
    /// </summary>
    F17,
    /// <summary>
    /// F18.
    /// </summary>
    F18,
    /// <summary>
    /// F19.
    /// </summary>
    F19,
    /// <summary>
    /// F20.
    /// </summary>
    F20,
    /// <summary>
    /// F21.
    /// </summary>
    F21,
    /// <summary>
    /// F22.
    /// </summary>
    F22,
    /// <summary>
    /// F23.
    /// </summary>
    F23,
    /// <summary>
    /// F24.
    /// </summary>
    F24,
    /// <summary>
    /// PrtScr SysRq or Print Screen.
    /// </summary>
    PrintScreen,
    /// <summary>
    /// Scroll Lock.
    /// </summary>
    ScrollLock,
    /// <summary>
    /// Pause Break.
    /// </summary>
    Pause,
    /// <summary>
    /// Browser Back. Some laptops place this key to the left of the ↑ key.
    /// </summary>
    BrowserBack,
    /// <summary>
    /// Browser Favorites.
    /// </summary>
    BrowserFavorites,
    /// <summary>
    /// Browser Forward. Some laptops place this key to the right of the ↑ key.
    /// </summary>
    BrowserForward,
    /// <summary>
    /// Browser Home.
    /// </summary>
    BrowserHome,
    /// <summary>
    /// Browser Refresh.
    /// </summary>
    BrowserRefresh,
    /// <summary>
    /// Browser Search.
    /// </summary>
    BrowserSearch,
    /// <summary>
    /// Browser Stop.
    /// </summary>
    BrowserStop,
    /// <summary>
    /// Eject or ⏏. This key is placed in the function section on some Apple keyboards.
    /// </summary>
    Eject,
    /// <summary>
    /// App 1. Sometimes labelled My Computer on the keyboard.
    /// </summary>
    LaunchApp1,
    /// <summary>
    /// App 2. Sometimes labelled Calculator on the keyboard.
    /// </summary>
    LaunchApp2,
    /// <summary>
    /// Mail.
    /// </summary>
    LaunchMail,
    /// <summary>
    /// Media Play/Pause or ⏵⏸.
    /// </summary>
    MediaPlayPause,
    /// <summary>
    /// Media Select.
    /// </summary>
    MediaSelect,
    /// <summary>
    /// Media Stop or ⏹.
    /// </summary>
    MediaStop,
    /// <summary>
    /// Media Next or ⏭.
    /// </summary>
    MediaTrackNext,
    /// <summary>
    /// Media Previous or ⏮.
    /// </summary>
    MediaTrackPrevious,
    /// <summary>
    /// Power.
    /// </summary>
    Power,
    /// <summary>
    /// Sleep.
    /// </summary>
    Sleep,
    /// <summary>
    /// Volume Down.
    /// </summary>
    AudioVolumeDown,
    /// <summary>
    /// Mute.
    /// </summary>
    AudioVolumeMute,
    /// <summary>
    /// Volume Up.
    /// </summary>
    AudioVolumeUp,
    /// <summary>
    /// Wake Up.
    /// </summary>
    WakeUp,
    /// <summary>
    /// Again. Legacy. Found on Sun’s USB keyboard.
    /// </summary>
    Again,
    /// <summary>
    /// Copy. Legacy. Found on Sun’s USB keyboard.
    /// </summary>
    Copy,
    /// <summary>
    /// Cut. Legacy. Found on Sun’s USB keyboard.
    /// </summary>
    Cut,
    /// <summary>
    /// Find. Legacy. Found on Sun’s USB keyboard.
    /// </summary>
    Find,
    /// <summary>
    /// Open. Legacy. Found on Sun’s USB keyboard.
    /// </summary>
    Open,
    /// <summary>
    /// Paste. Legacy. Found on Sun’s USB keyboard.
    /// </summary>
    Paste,
    /// <summary>
    /// Props. Legacy. Found on Sun’s USB keyboard.
    /// </summary>
    Props,
    /// <summary>
    /// Select. Legacy. Found on Sun’s USB keyboard.
    /// </summary>
    Select,
    /// <summary>
    /// Undo. Legacy. Found on Sun’s USB keyboard.
    /// </summary>
    Undo
}