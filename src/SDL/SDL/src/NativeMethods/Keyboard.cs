using System;
using System.Runtime.InteropServices;

namespace SDLSharp
{
    static unsafe partial class NativeMethods
    {
        [DllImport("SDL2")]
        public static extern Keycode SDL_GetKeyFromName(/*const char*/ byte* name);

        [DllImport("SDL2")]
        public static extern Keycode SDL_GetKeyFromScancode(Scancode scancode);

        [DllImport("SDL2")]
        public static extern /*const char*/ byte* SDL_GetKeyName(Keycode key);

        [DllImport("SDL2")]
        public static extern IntPtr SDL_GetKeyboardFocus();

        [DllImport("SDL2")]
        public static extern byte* SDL_GetKeyboardState(out int numkeys);

        [DllImport("SDL2")]
        public static extern Keymod SDL_GetModState();

        [DllImport("SDL2")]
        public static extern Scancode SDL_GetScancodeFromKey(Keycode key);

        [DllImport("SDL2")]
        public static extern Scancode SDL_GetScancodeFromName(/*const char*/ byte* name);

        [DllImport("SDL2")]
        public static extern /*cosnt char**/ byte* SDL_GetScancodeName(Scancode scancode);

        [DllImport("SDL2")]
        public static extern SDL_Bool SDL_HasScreenKeyboardSupport();

        [DllImport("SDL2")]
        public static extern SDL_Bool SDL_IsScreenKeyboardShown(Window window);

        [DllImport("SDL2")]
        public static extern SDL_Bool SDL_IsTextInputActive();

        [DllImport("SDL2")]
        public static extern void SDL_SetModState(Keymod modstate);

        [DllImport("SDL2")]
        public static extern void SDL_SetInputRect(Rect* rect);

        [DllImport("SDL2")]
        public static extern void SDL_StartTextInput();

        [DllImport("SDL2")]
        public static extern void SDL_StopTextInput();

        public const int SCANCODE_MASK = (1 << 30);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Keysym
    {
        public Scancode scancode;
        public Keycode keycode;
        public Keymod mod;
        uint unused;

        public override string ToString()
        {
            string md = mod != Keymod.None ? $"{mod}+" : "";
            return $"{{{mod}+{keycode}|{scancode}}}";
        }
    }

    [Flags]
    public enum Keymod : ushort
    {
        None = 0,
        LeftShift = 0x1,
        RightShift = 0x2,
        Shift = LeftShift | RightShift,
        LeftControl = 0x40,
        RightControl = 0x80,
        Control = LeftControl | RightControl,
        LeftAlt = 0x100,
        RightAlt = 0x200,
        Alt = LeftAlt | RightAlt,
        LeftGUI = 0x400,
        RightGUI = 0x800,
        GUI = LeftGUI | RightGUI,
        Num = 0x1000,
        Caps = 0x2000,
        Mode = 0x4000,
        Reserved = 0x8000,
    }


    public enum Keycode : int
    {
        Unknown = 0,

        Return = '\r',
        Escape = 27,
        Backspace = '\b',
        Tab = '\t',
        Space = ' ',
        Exclaim = '!',
        QuoteDbl = '"',
        Hash = '#',
        Percent = '%',
        Dollar = '$',
        Amppersand = '&',
        Quoet = '\'',
        LeftParen = '(',
        RightParen = ')',
        Asterisk = '*',
        Plus = '+',
        Comma = ',',
        Minus = '-',
        Period = '.',
        Slash = '/',
        Num0 = '0',
        Num1 = '1',
        Num2 = '2',
        Num3 = '3',
        Num4 = '4',
        Num5 = '5',
        Num6 = '6',
        Num7 = '7',
        Num8 = '8',
        Num9 = '9',
        Colon = ':',
        Semicolon = ';',
        Less = '<',
        Equals = '=',
        Greater = '>',
        Question = '?',
        At = '@',
        /*
           Skip uppercase letters
         */
        Leftbracket = '[',
        Backslash = '\\',
        Rightbracket = ']',
        Caret = '^',
        Underscore = '_',
        Backquote = '`',
        A = 'a',
        B = 'b',
        C = 'c',
        D = 'd',
        E = 'e',
        F = 'f',
        G = 'g',
        H = 'h',
        I = 'i',
        J = 'j',
        K = 'k',
        L = 'l',
        M = 'm',
        N = 'n',
        O = 'o',
        P = 'p',
        Q = 'q',
        R = 'r',
        S = 's',
        T = 't',
        U = 'u',
        V = 'v',
        W = 'w',
        X = 'x',
        Y = 'y',
        Z = 'z',

        CapsLock = Scancode.CapsLock | NativeMethods.SCANCODE_MASK,

        F1 = Scancode.F1 | NativeMethods.SCANCODE_MASK,
        F2 = Scancode.F2 | NativeMethods.SCANCODE_MASK,
        F3 = Scancode.F3 | NativeMethods.SCANCODE_MASK,
        F4 = Scancode.F4 | NativeMethods.SCANCODE_MASK,
        F5 = Scancode.F5 | NativeMethods.SCANCODE_MASK,
        F6 = Scancode.F6 | NativeMethods.SCANCODE_MASK,
        F7 = Scancode.F7 | NativeMethods.SCANCODE_MASK,
        F8 = Scancode.F8 | NativeMethods.SCANCODE_MASK,
        F9 = Scancode.F9 | NativeMethods.SCANCODE_MASK,
        F10 = Scancode.F10 | NativeMethods.SCANCODE_MASK,
        F11 = Scancode.F11 | NativeMethods.SCANCODE_MASK,
        F12 = Scancode.F12 | NativeMethods.SCANCODE_MASK,

        PrintScreen = Scancode.PrintScreen | NativeMethods.SCANCODE_MASK,
        ScrollLock = Scancode.ScrollLock | NativeMethods.SCANCODE_MASK,
        Pause = Scancode.Pause | NativeMethods.SCANCODE_MASK,
        Insert = Scancode.Insert | NativeMethods.SCANCODE_MASK,
        Home = Scancode.Home | NativeMethods.SCANCODE_MASK,
        PageUp = Scancode.PageUp | NativeMethods.SCANCODE_MASK,
        Delete = 127,
        End = Scancode.End | NativeMethods.SCANCODE_MASK,
        PageDown = Scancode.PageDown | NativeMethods.SCANCODE_MASK,
        Right = Scancode.Right | NativeMethods.SCANCODE_MASK,
        Left = Scancode.Left | NativeMethods.SCANCODE_MASK,
        Down = Scancode.Down | NativeMethods.SCANCODE_MASK,
        Up = Scancode.Up | NativeMethods.SCANCODE_MASK,

        NumLockClear = Scancode.NumLockClear | NativeMethods.SCANCODE_MASK,
        KeypadDivide = Scancode.KeypadDivide | NativeMethods.SCANCODE_MASK,
        KeypadMultiply = Scancode.KeypadMultiply | NativeMethods.SCANCODE_MASK,
        KeypadMinus = Scancode.KeypadMinus | NativeMethods.SCANCODE_MASK,
        KeypadPlus = Scancode.KeypadPlus | NativeMethods.SCANCODE_MASK,
        KeypadEnter = Scancode.KeypadEnter | NativeMethods.SCANCODE_MASK,
        Keypad1 = Scancode.Keypad1 | NativeMethods.SCANCODE_MASK,
        Keypad2 = Scancode.Keypad2 | NativeMethods.SCANCODE_MASK,
        Keypad3 = Scancode.Keypad3 | NativeMethods.SCANCODE_MASK,
        Keypad4 = Scancode.Keypad4 | NativeMethods.SCANCODE_MASK,
        Keypad5 = Scancode.Keypad5 | NativeMethods.SCANCODE_MASK,
        Keypad6 = Scancode.Keypad6 | NativeMethods.SCANCODE_MASK,
        Keypad7 = Scancode.Keypad7 | NativeMethods.SCANCODE_MASK,
        Keypad8 = Scancode.Keypad8 | NativeMethods.SCANCODE_MASK,
        Keypad9 = Scancode.Keypad9 | NativeMethods.SCANCODE_MASK,
        Keypad0 = Scancode.Keypad0 | NativeMethods.SCANCODE_MASK,
        KeypadPeriod = Scancode.KeypadPeriod | NativeMethods.SCANCODE_MASK,

        Application = Scancode.Application | NativeMethods.SCANCODE_MASK,
        Power = Scancode.Power | NativeMethods.SCANCODE_MASK,
        KeypadEquals = Scancode.KeypadEquals | NativeMethods.SCANCODE_MASK,
        F13 = Scancode.F13 | NativeMethods.SCANCODE_MASK,
        F14 = Scancode.F14 | NativeMethods.SCANCODE_MASK,
        F15 = Scancode.F15 | NativeMethods.SCANCODE_MASK,
        F16 = Scancode.F16 | NativeMethods.SCANCODE_MASK,
        F17 = Scancode.F17 | NativeMethods.SCANCODE_MASK,
        F18 = Scancode.F18 | NativeMethods.SCANCODE_MASK,
        F19 = Scancode.F19 | NativeMethods.SCANCODE_MASK,
        F20 = Scancode.F20 | NativeMethods.SCANCODE_MASK,
        F21 = Scancode.F21 | NativeMethods.SCANCODE_MASK,
        F22 = Scancode.F22 | NativeMethods.SCANCODE_MASK,
        F23 = Scancode.F23 | NativeMethods.SCANCODE_MASK,
        F24 = Scancode.F24 | NativeMethods.SCANCODE_MASK,
        Execute = Scancode.Execute | NativeMethods.SCANCODE_MASK,
        Help = Scancode.Help | NativeMethods.SCANCODE_MASK,
        Menu = Scancode.Menu | NativeMethods.SCANCODE_MASK,
        Select = Scancode.Select | NativeMethods.SCANCODE_MASK,
        Stop = Scancode.Stop | NativeMethods.SCANCODE_MASK,
        Again = Scancode.Again | NativeMethods.SCANCODE_MASK,
        Undo = Scancode.Undo | NativeMethods.SCANCODE_MASK,
        Cut = Scancode.Cut | NativeMethods.SCANCODE_MASK,
        Copy = Scancode.Copy | NativeMethods.SCANCODE_MASK,
        Paste = Scancode.Paste | NativeMethods.SCANCODE_MASK,
        Find = Scancode.Find | NativeMethods.SCANCODE_MASK,
        Mute = Scancode.Mute | NativeMethods.SCANCODE_MASK,
        VolumeUp = Scancode.VolumeUp | NativeMethods.SCANCODE_MASK,
        VolumeDown = Scancode.VolumeDown | NativeMethods.SCANCODE_MASK,
        KeypadComma = Scancode.KeypadComma | NativeMethods.SCANCODE_MASK,
        KeypadEqualsAS400 = Scancode.KeypadEqualsAS400 | NativeMethods.SCANCODE_MASK,

        AltErase = Scancode.AltErase | NativeMethods.SCANCODE_MASK,
        SysRq = Scancode.SysRq | NativeMethods.SCANCODE_MASK,
        Cancel = Scancode.Cancel | NativeMethods.SCANCODE_MASK,
        Clear = Scancode.Clear | NativeMethods.SCANCODE_MASK,
        Prior = Scancode.Prior | NativeMethods.SCANCODE_MASK,
        Return2 = Scancode.Return2 | NativeMethods.SCANCODE_MASK,
        Separator = Scancode.Separator | NativeMethods.SCANCODE_MASK,
        Out = Scancode.Out | NativeMethods.SCANCODE_MASK,
        Oper = Scancode.Oper | NativeMethods.SCANCODE_MASK,
        ClearAgain = Scancode.ClearAgain | NativeMethods.SCANCODE_MASK,
        CrSel = Scancode.CrSel | NativeMethods.SCANCODE_MASK,
        ExSel = Scancode.ExSel | NativeMethods.SCANCODE_MASK,

        Keypad00 = Scancode.Keypad00 | NativeMethods.SCANCODE_MASK,
        Keypad000 = Scancode.Keypad000 | NativeMethods.SCANCODE_MASK,
        ThousandsSeparator = Scancode.ThousandsSeparator | NativeMethods.SCANCODE_MASK,
        DecimalSeparator = Scancode.DecimalSeparator | NativeMethods.SCANCODE_MASK,
        CurrencyUnit = Scancode.CurrencyUnit | NativeMethods.SCANCODE_MASK,
        CurrencySubUnit = Scancode.CurrencySubUnit | NativeMethods.SCANCODE_MASK,
        KeypadLeftParen = Scancode.KeypadLeftParen | NativeMethods.SCANCODE_MASK,
        KeypadRightParen = Scancode.KeypadRightParen | NativeMethods.SCANCODE_MASK,
        KeypadLeftBrace = Scancode.KeypadLeftBrace | NativeMethods.SCANCODE_MASK,
        KeypadRightBrace = Scancode.KeypadRightBrace | NativeMethods.SCANCODE_MASK,
        KeypadTab = Scancode.KeypadTab | NativeMethods.SCANCODE_MASK,
        KeypadBackspace = Scancode.KeypadBackspace | NativeMethods.SCANCODE_MASK,
        KeypadA = Scancode.KeypadA | NativeMethods.SCANCODE_MASK,
        KeypadB = Scancode.KeypadB | NativeMethods.SCANCODE_MASK,
        KeypadC = Scancode.KeypadC | NativeMethods.SCANCODE_MASK,
        KeypadD = Scancode.KeypadD | NativeMethods.SCANCODE_MASK,
        KeypadE = Scancode.KeypadE | NativeMethods.SCANCODE_MASK,
        KeypadF = Scancode.KeypadF | NativeMethods.SCANCODE_MASK,
        KeypadXOR = Scancode.KeypadXOR | NativeMethods.SCANCODE_MASK,
        KeypadPower = Scancode.KeypadPower | NativeMethods.SCANCODE_MASK,
        KeypadPercent = Scancode.KeypadPercent | NativeMethods.SCANCODE_MASK,
        KeypadLess = Scancode.KeypadLess | NativeMethods.SCANCODE_MASK,
        KeypadGreater = Scancode.KeypadGreater | NativeMethods.SCANCODE_MASK,
        KeypadAmpersand = Scancode.KeypadAmpersand | NativeMethods.SCANCODE_MASK,
        KeypadDoubleAmpersand = Scancode.KeypadDoubleAmpersand | NativeMethods.SCANCODE_MASK,
        KeypadVerticalBar = Scancode.KeypadVerticalBar | NativeMethods.SCANCODE_MASK,
        KeypadDoubleVerticalBar = Scancode.KeypadDoubleVerticalBar | NativeMethods.SCANCODE_MASK,
        KeypadColon = Scancode.KeypadColon | NativeMethods.SCANCODE_MASK,
        KeypadHash = Scancode.KeypadHash | NativeMethods.SCANCODE_MASK,
        KeypadSpace = Scancode.KeypadSpace | NativeMethods.SCANCODE_MASK,
        KeypadAt = Scancode.KeypadAt | NativeMethods.SCANCODE_MASK,
        KeypadExclaim = Scancode.KeypadExclaim | NativeMethods.SCANCODE_MASK,
        KeypadMemStore = Scancode.KeypadMemStore | NativeMethods.SCANCODE_MASK,
        KeypadMemRecall = Scancode.KeypadMemRecall | NativeMethods.SCANCODE_MASK,
        KeypadMemClear = Scancode.KeypadMemClear | NativeMethods.SCANCODE_MASK,
        KeypadMemAdd = Scancode.KeypadMemAdd | NativeMethods.SCANCODE_MASK,
        KeypadMemSubtract = Scancode.KeypadMemSubtract | NativeMethods.SCANCODE_MASK,
        KeypadMemMultiply = Scancode.KeypadMemMultiply | NativeMethods.SCANCODE_MASK,
        KeypadMemDivide = Scancode.KeypadMemDivide | NativeMethods.SCANCODE_MASK,
        KeypadPlusMinus = Scancode.KeypadPlusMinus | NativeMethods.SCANCODE_MASK,
        KeypadClear = Scancode.KeypadClear | NativeMethods.SCANCODE_MASK,
        KeypadClearEntry = Scancode.KeypadClearEntry | NativeMethods.SCANCODE_MASK,
        KeypadBinary = Scancode.KeypadBinary | NativeMethods.SCANCODE_MASK,
        KeypadOctal = Scancode.KeypadOctal | NativeMethods.SCANCODE_MASK,
        KeypadDecimal = Scancode.KeypadDecimal | NativeMethods.SCANCODE_MASK,
        KeypadHexadecimal = Scancode.KeypadHexadecimal | NativeMethods.SCANCODE_MASK,

        LeftControl = Scancode.LeftControl | NativeMethods.SCANCODE_MASK,
        LeftShift = Scancode.LeftShift | NativeMethods.SCANCODE_MASK,
        LeftAlt = Scancode.LeftAlt | NativeMethods.SCANCODE_MASK,
        LeftGUI = Scancode.LeftGUI | NativeMethods.SCANCODE_MASK,
        RightControl = Scancode.RightControl | NativeMethods.SCANCODE_MASK,
        RightShift = Scancode.RightShift | NativeMethods.SCANCODE_MASK,
        RightAlt = Scancode.RightAlt | NativeMethods.SCANCODE_MASK,
        RightGUI = Scancode.RightGUI | NativeMethods.SCANCODE_MASK,

        Mode = Scancode.Mode | NativeMethods.SCANCODE_MASK,

        AudioNext = Scancode.AudioNext | NativeMethods.SCANCODE_MASK,
        AudioPrev = Scancode.AudioPrev | NativeMethods.SCANCODE_MASK,
        AudioStop = Scancode.AudioStop | NativeMethods.SCANCODE_MASK,
        AudioPlay = Scancode.AudioPlay | NativeMethods.SCANCODE_MASK,
        AudioMute = Scancode.AudioMute | NativeMethods.SCANCODE_MASK,
        MediaSelect = Scancode.MediaSelect | NativeMethods.SCANCODE_MASK,
        WWW = Scancode.WWW | NativeMethods.SCANCODE_MASK,
        Mail = Scancode.Mail | NativeMethods.SCANCODE_MASK,
        Calculator = Scancode.Calculator | NativeMethods.SCANCODE_MASK,
        Computer = Scancode.Computer | NativeMethods.SCANCODE_MASK,
        ACSearch = Scancode.ACSearch | NativeMethods.SCANCODE_MASK,
        ACHome = Scancode.ACHome | NativeMethods.SCANCODE_MASK,
        ACBack = Scancode.ACBack | NativeMethods.SCANCODE_MASK,
        ACForward = Scancode.ACForward | NativeMethods.SCANCODE_MASK,
        ACStop = Scancode.ACStop | NativeMethods.SCANCODE_MASK,
        ACRefresh = Scancode.ACRefresh | NativeMethods.SCANCODE_MASK,
        ACBookmarks = Scancode.ACBookmarks | NativeMethods.SCANCODE_MASK,

        BrightnessDown = Scancode.BrightnessDown | NativeMethods.SCANCODE_MASK,
        BrightnessUp = Scancode.BrightnessUp | NativeMethods.SCANCODE_MASK,
        DisplaySwitch = Scancode.DisplaySwitch | NativeMethods.SCANCODE_MASK,
        KeyboardIlluminationToggle = Scancode.KeyboardIlluminationToggle | NativeMethods.SCANCODE_MASK,

        KeyboardIlluminationDown = Scancode.KeyboardIlluminationDown | NativeMethods.SCANCODE_MASK,
        KeyboardIlluminationUp = Scancode.KeyboardIlluminationUp | NativeMethods.SCANCODE_MASK,
        Eject = Scancode.Eject | NativeMethods.SCANCODE_MASK,
        Sleep = Scancode.Sleep | NativeMethods.SCANCODE_MASK,
    }

    public enum Scancode
    {
        Unknown = 0,

        A = 4,
        B = 5,
        C = 6,
        D = 7,
        E = 8,
        F = 9,
        G = 10,
        H = 11,
        I = 12,
        J = 13,
        K = 14,
        L = 15,
        M = 16,
        N = 17,
        O = 18,
        P = 19,
        Q = 20,
        R = 21,
        S = 22,
        T = 23,
        U = 24,
        V = 25,
        W = 26,
        X = 27,
        Y = 28,
        Z = 29,

        Num1 = 30,
        Num2 = 31,
        Num3 = 32,
        Num4 = 33,
        Num5 = 34,
        Num6 = 35,
        Num7 = 36,
        Num8 = 37,
        Num9 = 38,
        Num0 = 39,

        Return = 40,
        Escape = 41,
        Backspace = 42,
        Tab = 43,
        Space = 44,

        Minus = 45,
        Equals = 46,
        LeftBracket = 47,
        RightBracket = 48,
        Backslash = 49,
        NonUSHash = 50,
        Semicolon = 51,
        Apostrophe = 52,
        Grave = 53,
        Comma = 54,
        Period = 55,
        Slash = 56,

        CapsLock = 57,

        F1 = 58,
        F2 = 59,
        F3 = 60,
        F4 = 61,
        F5 = 62,
        F6 = 63,
        F7 = 64,
        F8 = 65,
        F9 = 66,
        F10 = 67,
        F11 = 68,
        F12 = 69,

        PrintScreen = 70,
        ScrollLock = 71,
        Pause = 72,
        Insert = 73, /**< insert on PC, help on some Mac keyboards (but
                                   does send code 73, not 117) */
        Home = 74,
        PageUp = 75,
        Delete = 76,
        End = 77,
        PageDown = 78,
        Right = 79,
        Left = 80,
        Down = 81,
        Up = 82,

        NumLockClear = 83, /**< num lock on PC, clear on Mac keyboards
                                     */
        KeypadDivide = 84,
        KeypadMultiply = 85,
        KeypadMinus = 86,
        KeypadPlus = 87,
        KeypadEnter = 88,
        Keypad1 = 89,
        Keypad2 = 90,
        Keypad3 = 91,
        Keypad4 = 92,
        Keypad5 = 93,
        Keypad6 = 94,
        Keypad7 = 95,
        Keypad8 = 96,
        Keypad9 = 97,
        Keypad0 = 98,
        KeypadPeriod = 99,

        NonUSBackslash = 100,
        Application = 101,
        Power = 102,
        KeypadEquals = 103,
        F13 = 104,
        F14 = 105,
        F15 = 106,
        F16 = 107,
        F17 = 108,
        F18 = 109,
        F19 = 110,
        F20 = 111,
        F21 = 112,
        F22 = 113,
        F23 = 114,
        F24 = 115,
        Execute = 116,
        Help = 117,
        Menu = 118,
        Select = 119,
        Stop = 120,
        Again = 121,   /**< redo */
        Undo = 122,
        Cut = 123,
        Copy = 124,
        Paste = 125,
        Find = 126,
        Mute = 127,
        VolumeUp = 128,
        VolumeDown = 129,
        KeypadComma = 133,
        KeypadEqualsAS400 = 134,

        International1 = 135,
        International2 = 136,
        International3 = 137, /**< Yen */
        International4 = 138,
        International5 = 139,
        International6 = 140,
        International7 = 141,
        International8 = 142,
        International9 = 143,
        Lang1 = 144, /**< Hangul/English toggle */
        Lang2 = 145, /**< Hanja conversion */
        Lang3 = 146, /**< Katakana */
        Lang4 = 147, /**< Hiragana */
        Lang5 = 148, /**< Zenkaku/Hankaku */
        Lang6 = 149, /**< reserved */
        Lang7 = 150, /**< reserved */
        Lang8 = 151, /**< reserved */
        Lang9 = 152, /**< reserved */

        AltErase = 153, /**< Erase-Eaze */
        SysRq = 154,
        Cancel = 155,
        Clear = 156,
        Prior = 157,
        Return2 = 158,
        Separator = 159,
        Out = 160,
        Oper = 161,
        ClearAgain = 162,
        CrSel = 163,
        ExSel = 164,

        Keypad00 = 176,
        Keypad000 = 177,
        ThousandsSeparator = 178,
        DecimalSeparator = 179,
        CurrencyUnit = 180,
        CurrencySubUnit = 181,
        KeypadLeftParen = 182,
        KeypadRightParen = 183,
        KeypadLeftBrace = 184,
        KeypadRightBrace = 185,
        KeypadTab = 186,
        KeypadBackspace = 187,
        KeypadA = 188,
        KeypadB = 189,
        KeypadC = 190,
        KeypadD = 191,
        KeypadE = 192,
        KeypadF = 193,
        KeypadXOR = 194,
        KeypadPower = 195,
        KeypadPercent = 196,
        KeypadLess = 197,
        KeypadGreater = 198,
        KeypadAmpersand = 199,
        KeypadDoubleAmpersand = 200,
        KeypadVerticalBar = 201,
        KeypadDoubleVerticalBar = 202,
        KeypadColon = 203,
        KeypadHash = 204,
        KeypadSpace = 205,
        KeypadAt = 206,
        KeypadExclaim = 207,
        KeypadMemStore = 208,
        KeypadMemRecall = 209,
        KeypadMemClear = 210,
        KeypadMemAdd = 211,
        KeypadMemSubtract = 212,
        KeypadMemMultiply = 213,
        KeypadMemDivide = 214,
        KeypadPlusMinus = 215,
        KeypadClear = 216,
        KeypadClearEntry = 217,
        KeypadBinary = 218,
        KeypadOctal = 219,
        KeypadDecimal = 220,
        KeypadHexadecimal = 221,

        LeftControl = 224,
        LeftShift = 225,
        LeftAlt = 226, /**< alt, option */
        LeftGUI = 227, /**< windows, command (apple), meta */
        RightControl = 228,
        RightShift = 229,
        RightAlt = 230, /**< alt gr, option */
        RightGUI = 231, /**< windows, command (apple), meta */

        Mode = 257,

        AudioNext = 258,
        AudioPrev = 259,
        AudioStop = 260,
        AudioPlay = 261,
        AudioMute = 262,
        MediaSelect = 263,
        WWW = 264,
        Mail = 265,
        Calculator = 266,
        Computer = 267,
        ACSearch = 268,
        ACHome = 269,
        ACBack = 270,
        ACForward = 271,
        ACStop = 272,
        ACRefresh = 273,
        ACBookmarks = 274,

        BrightnessDown = 275,
        BrightnessUp = 276,
        DisplaySwitch = 277,
        KeyboardIlluminationToggle = 278,
        KeyboardIlluminationDown = 279,
        KeyboardIlluminationUp = 280,
        Eject = 281,
        Sleep = 282,

        App1 = 283,
        App2 = 284,

        //SDL_NUM_SCANCODES = 512
    }
}
