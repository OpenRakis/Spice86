namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using Spice86.Shared.Emulator.Keyboard;

using System.Collections.Frozen;
using System.Collections.Generic;

/// <summary>
/// Converts between KbdKey enum and keyboard scancodes for different code sets.
/// </summary>
public class KeyboardScancodeConverter {
    private static readonly FrozenDictionary<PhysicalKey, PcKeyboardKey> _keyToKbdKey = new Dictionary<PhysicalKey, PcKeyboardKey>() {
        // Number row
        { PhysicalKey.Digit1, PcKeyboardKey.D1 }, { PhysicalKey.Digit2, PcKeyboardKey.D2 }, { PhysicalKey.Digit3, PcKeyboardKey.D3 },
        { PhysicalKey.Digit4, PcKeyboardKey.D4 }, { PhysicalKey.Digit5, PcKeyboardKey.D5 }, { PhysicalKey.Digit6, PcKeyboardKey.D6 },
        { PhysicalKey.Digit7, PcKeyboardKey.D7 }, { PhysicalKey.Digit8, PcKeyboardKey.D8 }, { PhysicalKey.Digit9, PcKeyboardKey.D9 },
        { PhysicalKey.Digit0, PcKeyboardKey.D0 },
        
        // Letters
        { PhysicalKey.Q, PcKeyboardKey.Q }, { PhysicalKey.W, PcKeyboardKey.W }, { PhysicalKey.E, PcKeyboardKey.E }, { PhysicalKey.R, PcKeyboardKey.R },
        { PhysicalKey.T, PcKeyboardKey.T }, { PhysicalKey.Y, PcKeyboardKey.Y }, { PhysicalKey.U, PcKeyboardKey.U }, { PhysicalKey.I, PcKeyboardKey.I },
        { PhysicalKey.O, PcKeyboardKey.O }, { PhysicalKey.P, PcKeyboardKey.P }, { PhysicalKey.A, PcKeyboardKey.A }, { PhysicalKey.S, PcKeyboardKey.S },
        { PhysicalKey.D, PcKeyboardKey.D }, { PhysicalKey.F, PcKeyboardKey.F }, { PhysicalKey.G, PcKeyboardKey.G }, { PhysicalKey.H, PcKeyboardKey.H },
        { PhysicalKey.J, PcKeyboardKey.J }, { PhysicalKey.K, PcKeyboardKey.K }, { PhysicalKey.L, PcKeyboardKey.L }, { PhysicalKey.Z, PcKeyboardKey.Z },
        { PhysicalKey.X, PcKeyboardKey.X }, { PhysicalKey.C, PcKeyboardKey.C }, { PhysicalKey.V, PcKeyboardKey.V }, { PhysicalKey.B, PcKeyboardKey.B },
        { PhysicalKey.N, PcKeyboardKey.N }, { PhysicalKey.M, PcKeyboardKey.M },
        
        // Function keys
        { PhysicalKey.F1, PcKeyboardKey.F1 }, { PhysicalKey.F2, PcKeyboardKey.F2 }, { PhysicalKey.F3, PcKeyboardKey.F3 },
        { PhysicalKey.F4, PcKeyboardKey.F4 }, { PhysicalKey.F5, PcKeyboardKey.F5 }, { PhysicalKey.F6, PcKeyboardKey.F6 },
        { PhysicalKey.F7, PcKeyboardKey.F7 }, { PhysicalKey.F8, PcKeyboardKey.F8 }, { PhysicalKey.F9, PcKeyboardKey.F9 },
        { PhysicalKey.F10, PcKeyboardKey.F10 }, { PhysicalKey.F11, PcKeyboardKey.F11 }, { PhysicalKey.F12, PcKeyboardKey.F12 },
        
        // Special keys
        { PhysicalKey.Escape, PcKeyboardKey.Escape }, { PhysicalKey.Tab, PcKeyboardKey.Tab },
        { PhysicalKey.Backspace, PcKeyboardKey.Backspace }, { PhysicalKey.Enter, PcKeyboardKey.Enter },
        { PhysicalKey.Space, PcKeyboardKey.Space },
        
        // Modifier keys
        { PhysicalKey.AltLeft, PcKeyboardKey.LeftAlt }, { PhysicalKey.AltRight, PcKeyboardKey.RightAlt },
        { PhysicalKey.ControlLeft, PcKeyboardKey.LeftCtrl }, { PhysicalKey.ControlRight, PcKeyboardKey.RightCtrl },
        { PhysicalKey.ShiftLeft, PcKeyboardKey.LeftShift }, { PhysicalKey.ShiftRight, PcKeyboardKey.RightShift },
        { PhysicalKey.MetaLeft, PcKeyboardKey.LeftGui }, { PhysicalKey.MetaRight, PcKeyboardKey.RightGui },
        
        // Lock keys
        { PhysicalKey.CapsLock, PcKeyboardKey.CapsLock }, { PhysicalKey.ScrollLock, PcKeyboardKey.ScrollLock },
        { PhysicalKey.NumLock, PcKeyboardKey.NumLock },
        
        // Punctuation
        { PhysicalKey.Backquote, PcKeyboardKey.Grave }, { PhysicalKey.Minus, PcKeyboardKey.Minus },
        { PhysicalKey.Equal, PcKeyboardKey.Equals }, { PhysicalKey.Backslash, PcKeyboardKey.Backslash },
        { PhysicalKey.BracketLeft, PcKeyboardKey.LeftBracket },
        { PhysicalKey.BracketRight, PcKeyboardKey.RightBracket },
        { PhysicalKey.Semicolon, PcKeyboardKey.Semicolon }, { PhysicalKey.Quote, PcKeyboardKey.Quote },
        { PhysicalKey.IntlBackslash, PcKeyboardKey.Oem102 }, { PhysicalKey.Period, PcKeyboardKey.Period },
        { PhysicalKey.Comma, PcKeyboardKey.Comma }, { PhysicalKey.Slash, PcKeyboardKey.Slash },
        
        // Navigation keys
        { PhysicalKey.PrintScreen, PcKeyboardKey.PrintScreen }, { PhysicalKey.Pause, PcKeyboardKey.Pause },
        { PhysicalKey.Insert, PcKeyboardKey.Insert }, { PhysicalKey.Home, PcKeyboardKey.Home },
        { PhysicalKey.PageUp, PcKeyboardKey.PageUp }, { PhysicalKey.Delete, PcKeyboardKey.Delete },
        { PhysicalKey.End, PcKeyboardKey.End }, { PhysicalKey.PageDown, PcKeyboardKey.PageDown },
        { PhysicalKey.ArrowLeft, PcKeyboardKey.Left }, { PhysicalKey.ArrowUp, PcKeyboardKey.Up },
        { PhysicalKey.ArrowDown, PcKeyboardKey.Down }, { PhysicalKey.ArrowRight, PcKeyboardKey.Right },
        
        // Keypad
        { PhysicalKey.NumPad0, PcKeyboardKey.Kp0 }, { PhysicalKey.NumPad1, PcKeyboardKey.Kp1 },
        { PhysicalKey.NumPad2, PcKeyboardKey.Kp2 }, { PhysicalKey.NumPad3, PcKeyboardKey.Kp3 },
        { PhysicalKey.NumPad4, PcKeyboardKey.Kp4 }, { PhysicalKey.NumPad5, PcKeyboardKey.Kp5 },
        { PhysicalKey.NumPad6, PcKeyboardKey.Kp6 }, { PhysicalKey.NumPad7, PcKeyboardKey.Kp7 },
        { PhysicalKey.NumPad8, PcKeyboardKey.Kp8 }, { PhysicalKey.NumPad9, PcKeyboardKey.Kp9 },
        { PhysicalKey.NumPadDivide, PcKeyboardKey.KpDivide }, { PhysicalKey.NumPadMultiply, PcKeyboardKey.KpMultiply },
        { PhysicalKey.NumPadSubtract, PcKeyboardKey.KpMinus }, { PhysicalKey.NumPadAdd, PcKeyboardKey.KpPlus },
        { PhysicalKey.NumPadEnter, PcKeyboardKey.KpEnter }, { PhysicalKey.NumPadDecimal, PcKeyboardKey.KpPeriod }
    }.ToFrozenDictionary();

    /// <summary>
    /// Gets the KbdKey equivalent for an Avalonia Key
    /// </summary>
    public PcKeyboardKey ConvertToKbdKey(PhysicalKey key) {
        return _keyToKbdKey.TryGetValue(key, out PcKeyboardKey kbdKey) ? kbdKey : PcKeyboardKey.None;
    }

    /// <summary>
    /// Gets all scancodes for a key based on the specified code set
    /// Equivalent to DOSBox KEYBOARD_GetScanCode1/2/3 functions
    /// </summary>
    public List<byte> GetScancodes(PcKeyboardKey keyType, bool isPressed, byte codeSet) {
        if (keyType == PcKeyboardKey.None) {
            return [];
        }

        return codeSet switch {
            1 => GetScanCode1(keyType, isPressed),
            2 => GetScanCode2(keyType, isPressed),
            3 => GetScanCode3(keyType, isPressed),
            _ => GetScanCode1(keyType, isPressed),// Default to set 1
        };
    }

    public List<byte> GetScanCode1(PcKeyboardKey keyType, bool isPressed) {
        bool extend = false;

        byte code;
        // This table directly matches the DOSBox scancode table in keyboard_scancodes.cpp
        switch (keyType) {
            case PcKeyboardKey.Escape: code = 0x01; break;
            case PcKeyboardKey.D1: code = 0x02; break;
            case PcKeyboardKey.D2: code = 0x03; break;
            case PcKeyboardKey.D3: code = 0x04; break;
            case PcKeyboardKey.D4: code = 0x05; break;
            case PcKeyboardKey.D5: code = 0x06; break;
            case PcKeyboardKey.D6: code = 0x07; break;
            case PcKeyboardKey.D7: code = 0x08; break;
            case PcKeyboardKey.D8: code = 0x09; break;
            case PcKeyboardKey.D9: code = 0x0A; break;
            case PcKeyboardKey.D0: code = 0x0B; break;

            case PcKeyboardKey.Minus: code = 0x0C; break;
            case PcKeyboardKey.Equals: code = 0x0D; break;
            case PcKeyboardKey.Backspace: code = 0x0E; break;
            case PcKeyboardKey.Tab: code = 0x0F; break;

            case PcKeyboardKey.Q: code = 0x10; break;
            case PcKeyboardKey.W: code = 0x11; break;
            case PcKeyboardKey.E: code = 0x12; break;
            case PcKeyboardKey.R: code = 0x13; break;
            case PcKeyboardKey.T: code = 0x14; break;
            case PcKeyboardKey.Y: code = 0x15; break;
            case PcKeyboardKey.U: code = 0x16; break;
            case PcKeyboardKey.I: code = 0x17; break;
            case PcKeyboardKey.O: code = 0x18; break;
            case PcKeyboardKey.P: code = 0x19; break;

            case PcKeyboardKey.LeftBracket: code = 0x1A; break;
            case PcKeyboardKey.RightBracket: code = 0x1B; break;
            case PcKeyboardKey.Enter: code = 0x1C; break;
            case PcKeyboardKey.LeftCtrl: code = 0x1D; break;

            case PcKeyboardKey.A: code = 0x1E; break;
            case PcKeyboardKey.S: code = 0x1F; break;
            case PcKeyboardKey.D: code = 0x20; break;
            case PcKeyboardKey.F: code = 0x21; break;
            case PcKeyboardKey.G: code = 0x22; break;
            case PcKeyboardKey.H: code = 0x23; break;
            case PcKeyboardKey.J: code = 0x24; break;
            case PcKeyboardKey.K: code = 0x25; break;
            case PcKeyboardKey.L: code = 0x26; break;

            case PcKeyboardKey.Semicolon: code = 0x27; break;
            case PcKeyboardKey.Quote: code = 0x28; break;
            case PcKeyboardKey.Grave: code = 0x29; break;
            case PcKeyboardKey.LeftShift: code = 0x2A; break;
            case PcKeyboardKey.Backslash: code = 0x2B; break;

            case PcKeyboardKey.Z: code = 0x2C; break;
            case PcKeyboardKey.X: code = 0x2D; break;
            case PcKeyboardKey.C: code = 0x2E; break;
            case PcKeyboardKey.V: code = 0x2F; break;
            case PcKeyboardKey.B: code = 0x30; break;
            case PcKeyboardKey.N: code = 0x31; break;
            case PcKeyboardKey.M: code = 0x32; break;

            case PcKeyboardKey.Comma: code = 0x33; break;
            case PcKeyboardKey.Period: code = 0x34; break;
            case PcKeyboardKey.Slash: code = 0x35; break;
            case PcKeyboardKey.RightShift: code = 0x36; break;
            case PcKeyboardKey.KpMultiply: code = 0x37; break;
            case PcKeyboardKey.LeftAlt: code = 0x38; break;
            case PcKeyboardKey.Space: code = 0x39; break;
            case PcKeyboardKey.CapsLock: code = 0x3A; break;

            case PcKeyboardKey.F1: code = 0x3B; break;
            case PcKeyboardKey.F2: code = 0x3C; break;
            case PcKeyboardKey.F3: code = 0x3D; break;
            case PcKeyboardKey.F4: code = 0x3E; break;
            case PcKeyboardKey.F5: code = 0x3F; break;
            case PcKeyboardKey.F6: code = 0x40; break;
            case PcKeyboardKey.F7: code = 0x41; break;
            case PcKeyboardKey.F8: code = 0x42; break;
            case PcKeyboardKey.F9: code = 0x43; break;
            case PcKeyboardKey.F10: code = 0x44; break;

            case PcKeyboardKey.NumLock: code = 0x45; break;
            case PcKeyboardKey.ScrollLock: code = 0x46; break;

            case PcKeyboardKey.Kp7: code = 0x47; break;
            case PcKeyboardKey.Kp8: code = 0x48; break;
            case PcKeyboardKey.Kp9: code = 0x49; break;
            case PcKeyboardKey.KpMinus: code = 0x4A; break;
            case PcKeyboardKey.Kp4: code = 0x4B; break;
            case PcKeyboardKey.Kp5: code = 0x4C; break;
            case PcKeyboardKey.Kp6: code = 0x4D; break;
            case PcKeyboardKey.KpPlus: code = 0x4E; break;
            case PcKeyboardKey.Kp1: code = 0x4F; break;
            case PcKeyboardKey.Kp2: code = 0x50; break;
            case PcKeyboardKey.Kp3: code = 0x51; break;
            case PcKeyboardKey.Kp0: code = 0x52; break;
            case PcKeyboardKey.KpPeriod: code = 0x53; break;

            case PcKeyboardKey.Oem102: code = 0x56; break;
            case PcKeyboardKey.F11: code = 0x57; break;
            case PcKeyboardKey.F12: code = 0x58; break;

            case PcKeyboardKey.Abnt1: code = 0x73; break;

            // Extended keys
            case PcKeyboardKey.KpEnter: extend = true; code = 0x1C; break;
            case PcKeyboardKey.RightCtrl: extend = true; code = 0x1D; break;
            case PcKeyboardKey.KpDivide: extend = true; code = 0x35; break;
            case PcKeyboardKey.RightAlt: extend = true; code = 0x38; break;
            case PcKeyboardKey.Home: extend = true; code = 0x47; break;
            case PcKeyboardKey.Up: extend = true; code = 0x48; break;
            case PcKeyboardKey.PageUp: extend = true; code = 0x49; break;
            case PcKeyboardKey.Left: extend = true; code = 0x4B; break;
            case PcKeyboardKey.Right: extend = true; code = 0x4D; break;
            case PcKeyboardKey.End: extend = true; code = 0x4F; break;
            case PcKeyboardKey.Down: extend = true; code = 0x50; break;
            case PcKeyboardKey.PageDown: extend = true; code = 0x51; break;
            case PcKeyboardKey.Insert: extend = true; code = 0x52; break;
            case PcKeyboardKey.Delete: extend = true; code = 0x53; break;
            case PcKeyboardKey.LeftGui: extend = true; code = 0x5B; break;
            case PcKeyboardKey.RightGui: extend = true; code = 0x5C; break;

            // Special cases
            case PcKeyboardKey.Pause:
                if (isPressed) {
                    // Pause key gets released as soon as it is pressed
                    return [
                        0xE1, 0x1D, 0x45, 0xE1,
                        (byte)(0x1D | 0x80), (byte)(0x45 | 0x80)
                    ];
                }
                return [];

            case PcKeyboardKey.PrintScreen:
                return [
                    0xE0,
                    (byte)(0x2A | (isPressed ? 0 : 0x80)),
                    0xE0,
                    (byte)(0x37 | (isPressed ? 0 : 0x80))
                ];

            default:
                return [];
        }

        List<byte> result = [];

        if (extend) {
            result.Add(0xE0);
        }

        result.Add((byte)(code | (isPressed ? 0 : 0x80)));
        return result;
    }

    /// <summary>
    /// Gets scan codes for keyboard scan code set 2.
    /// </summary>
    /// <param name="keyType">The keyboard key type</param>
    /// <param name="isPressed">Whether the key is pressed (true) or released (false)</param>
    /// <returns>List of scan code bytes</returns>
    /// <remarks>
    /// Scan code set 2 is not implemented as it was primarily used on IBM PS/2 Model 50+ systems
    /// which never gained widespread adoption. Most DOS programs use scan code set 1 (XT/AT compatible).
    /// This method falls back to scan code set 1 for compatibility.
    /// </remarks>
    public List<byte> GetScanCode2(PcKeyboardKey keyType, bool isPressed) {
        return GetScanCode1(keyType, isPressed); // Fallback to set 1
    }

    /// <summary>
    /// Gets scan codes for keyboard scan code set 3.
    /// </summary>
    /// <param name="keyType">The keyboard key type</param>
    /// <param name="isPressed">Whether the key is pressed (true) or released (false)</param>
    /// <returns>List of scan code bytes</returns>
    /// <remarks>
    /// Scan code set 3 is not implemented as it was used on rare IBM 3270 terminal keyboards
    /// and never became common in personal computers. Most DOS programs use scan code set 1 (XT/AT compatible).
    /// This method falls back to scan code set 1 for compatibility.
    /// </remarks>
    public List<byte> GetScanCode3(PcKeyboardKey keyType, bool isPressed) {
        return GetScanCode1(keyType, isPressed); // Fallback to set 1
    }
}