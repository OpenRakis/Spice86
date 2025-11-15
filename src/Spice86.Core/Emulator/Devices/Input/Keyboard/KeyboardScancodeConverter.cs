namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using Spice86.Shared.Emulator.Keyboard;

using System.Collections.Frozen;
using System.Collections.Generic;

/// <summary>
/// Converts between KbdKey enum and keyboard scancodes for different code sets.
/// </summary>
public class KeyboardScancodeConverter {
    private static readonly FrozenDictionary<PhysicalKey, KbdKey> _keyToKbdKey = new Dictionary<PhysicalKey, KbdKey>() {
        // Number row
        { PhysicalKey.Digit1, KbdKey.D1 }, { PhysicalKey.Digit2, KbdKey.D2 }, { PhysicalKey.Digit3, KbdKey.D3 }, 
        { PhysicalKey.Digit4, KbdKey.D4 }, { PhysicalKey.Digit5, KbdKey.D5 }, { PhysicalKey.Digit6, KbdKey.D6 }, 
        { PhysicalKey.Digit7, KbdKey.D7 }, { PhysicalKey.Digit8, KbdKey.D8 }, { PhysicalKey.Digit9, KbdKey.D9 }, 
        { PhysicalKey.Digit0, KbdKey.D0 },
        
        // Letters
        { PhysicalKey.Q, KbdKey.Q }, { PhysicalKey.W, KbdKey.W }, { PhysicalKey.E, KbdKey.E }, { PhysicalKey.R, KbdKey.R },
        { PhysicalKey.T, KbdKey.T }, { PhysicalKey.Y, KbdKey.Y }, { PhysicalKey.U, KbdKey.U }, { PhysicalKey.I, KbdKey.I },
        { PhysicalKey.O, KbdKey.O }, { PhysicalKey.P, KbdKey.P }, { PhysicalKey.A, KbdKey.A }, { PhysicalKey.S, KbdKey.S },
        { PhysicalKey.D, KbdKey.D }, { PhysicalKey.F, KbdKey.F }, { PhysicalKey.G, KbdKey.G }, { PhysicalKey.H, KbdKey.H },
        { PhysicalKey.J, KbdKey.J }, { PhysicalKey.K, KbdKey.K }, { PhysicalKey.L, KbdKey.L }, { PhysicalKey.Z, KbdKey.Z },
        { PhysicalKey.X, KbdKey.X }, { PhysicalKey.C, KbdKey.C }, { PhysicalKey.V, KbdKey.V }, { PhysicalKey.B, KbdKey.B },
        { PhysicalKey.N, KbdKey.N }, { PhysicalKey.M, KbdKey.M },
        
        // Function keys
        { PhysicalKey.F1, KbdKey.F1 }, { PhysicalKey.F2, KbdKey.F2 }, { PhysicalKey.F3, KbdKey.F3 }, 
        { PhysicalKey.F4, KbdKey.F4 }, { PhysicalKey.F5, KbdKey.F5 }, { PhysicalKey.F6, KbdKey.F6 },
        { PhysicalKey.F7, KbdKey.F7 }, { PhysicalKey.F8, KbdKey.F8 }, { PhysicalKey.F9, KbdKey.F9 }, 
        { PhysicalKey.F10, KbdKey.F10 }, { PhysicalKey.F11, KbdKey.F11 }, { PhysicalKey.F12, KbdKey.F12 },
        
        // Special keys
        { PhysicalKey.Escape, KbdKey.Escape }, { PhysicalKey.Tab, KbdKey.Tab }, 
        { PhysicalKey.Backspace, KbdKey.Backspace }, { PhysicalKey.Enter, KbdKey.Enter }, 
        { PhysicalKey.Space, KbdKey.Space },
        
        // Modifier keys
        { PhysicalKey.AltLeft, KbdKey.LeftAlt }, { PhysicalKey.AltRight, KbdKey.RightAlt },
        { PhysicalKey.ControlLeft, KbdKey.LeftCtrl }, { PhysicalKey.ControlRight, KbdKey.RightCtrl },
        { PhysicalKey.ShiftLeft, KbdKey.LeftShift }, { PhysicalKey.ShiftRight, KbdKey.RightShift },
        { PhysicalKey.MetaLeft, KbdKey.LeftGui }, { PhysicalKey.MetaRight, KbdKey.RightGui },
        
        // Lock keys
        { PhysicalKey.CapsLock, KbdKey.CapsLock }, { PhysicalKey.ScrollLock, KbdKey.ScrollLock }, 
        { PhysicalKey.NumLock, KbdKey.NumLock },
        
        // Punctuation
        { PhysicalKey.Backquote, KbdKey.Grave }, { PhysicalKey.Minus, KbdKey.Minus }, 
        { PhysicalKey.Equal, KbdKey.Equals }, { PhysicalKey.Backslash, KbdKey.Backslash },
        { PhysicalKey.BracketLeft, KbdKey.LeftBracket }, 
        { PhysicalKey.BracketRight, KbdKey.RightBracket },
        { PhysicalKey.Semicolon, KbdKey.Semicolon }, { PhysicalKey.Quote, KbdKey.Quote },
        { PhysicalKey.IntlBackslash, KbdKey.Oem102 }, { PhysicalKey.Period, KbdKey.Period }, 
        { PhysicalKey.Comma, KbdKey.Comma }, { PhysicalKey.Slash, KbdKey.Slash },
        
        // Navigation keys
        { PhysicalKey.PrintScreen, KbdKey.PrintScreen }, { PhysicalKey.Pause, KbdKey.Pause },
        { PhysicalKey.Insert, KbdKey.Insert }, { PhysicalKey.Home, KbdKey.Home }, 
        { PhysicalKey.PageUp, KbdKey.PageUp }, { PhysicalKey.Delete, KbdKey.Delete }, 
        { PhysicalKey.End, KbdKey.End }, { PhysicalKey.PageDown, KbdKey.PageDown },
        { PhysicalKey.ArrowLeft, KbdKey.Left }, { PhysicalKey.ArrowUp, KbdKey.Up }, 
        { PhysicalKey.ArrowDown, KbdKey.Down }, { PhysicalKey.ArrowRight, KbdKey.Right },
        
        // Keypad
        { PhysicalKey.NumPad0, KbdKey.Kp0 }, { PhysicalKey.NumPad1, KbdKey.Kp1 }, 
        { PhysicalKey.NumPad2, KbdKey.Kp2 }, { PhysicalKey.NumPad3, KbdKey.Kp3 }, 
        { PhysicalKey.NumPad4, KbdKey.Kp4 }, { PhysicalKey.NumPad5, KbdKey.Kp5 }, 
        { PhysicalKey.NumPad6, KbdKey.Kp6 }, { PhysicalKey.NumPad7, KbdKey.Kp7 }, 
        { PhysicalKey.NumPad8, KbdKey.Kp8 }, { PhysicalKey.NumPad9, KbdKey.Kp9 },
        { PhysicalKey.NumPadDivide, KbdKey.KpDivide }, { PhysicalKey.NumPadMultiply, KbdKey.KpMultiply }, 
        { PhysicalKey.NumPadSubtract, KbdKey.KpMinus }, { PhysicalKey.NumPadAdd, KbdKey.KpPlus },
        { PhysicalKey.NumPadEnter, KbdKey.KpEnter }, { PhysicalKey.NumPadDecimal, KbdKey.KpPeriod }
    }.ToFrozenDictionary();

    /// <summary>
    /// Gets the KbdKey equivalent for an Avalonia Key
    /// </summary>
    public KbdKey ConvertToKbdKey(PhysicalKey key) {
        return _keyToKbdKey.TryGetValue(key, out KbdKey kbdKey) ? kbdKey : KbdKey.None;
    }

    /// <summary>
    /// Gets all scancodes for a key based on the specified code set
    /// Equivalent to DOSBox KEYBOARD_GetScanCode1/2/3 functions
    /// </summary>
    public List<byte> GetScancodes(KbdKey keyType, bool isPressed, byte codeSet) {
        if (keyType == KbdKey.None) {
            return [];
        }

        return codeSet switch {
            1 => GetScanCode1(keyType, isPressed),
            2 => GetScanCode2(keyType, isPressed),
            3 => GetScanCode3(keyType, isPressed),
            _ => GetScanCode1(keyType, isPressed),// Default to set 1
        };
    }

    public List<byte> GetScanCode1(KbdKey keyType, bool isPressed) {
        bool extend = false;

        byte code;
        // This table directly matches the DOSBox scancode table in keyboard_scancodes.cpp
        switch (keyType) {
            case KbdKey.Escape: code = 0x01; break;
            case KbdKey.D1: code = 0x02; break;
            case KbdKey.D2: code = 0x03; break;
            case KbdKey.D3: code = 0x04; break;
            case KbdKey.D4: code = 0x05; break;
            case KbdKey.D5: code = 0x06; break;
            case KbdKey.D6: code = 0x07; break;
            case KbdKey.D7: code = 0x08; break;
            case KbdKey.D8: code = 0x09; break;
            case KbdKey.D9: code = 0x0A; break;
            case KbdKey.D0: code = 0x0B; break;

            case KbdKey.Minus: code = 0x0C; break;
            case KbdKey.Equals: code = 0x0D; break;
            case KbdKey.Backspace: code = 0x0E; break;
            case KbdKey.Tab: code = 0x0F; break;

            case KbdKey.Q: code = 0x10; break;
            case KbdKey.W: code = 0x11; break;
            case KbdKey.E: code = 0x12; break;
            case KbdKey.R: code = 0x13; break;
            case KbdKey.T: code = 0x14; break;
            case KbdKey.Y: code = 0x15; break;
            case KbdKey.U: code = 0x16; break;
            case KbdKey.I: code = 0x17; break;
            case KbdKey.O: code = 0x18; break;
            case KbdKey.P: code = 0x19; break;

            case KbdKey.LeftBracket: code = 0x1A; break;
            case KbdKey.RightBracket: code = 0x1B; break;
            case KbdKey.Enter: code = 0x1C; break;
            case KbdKey.LeftCtrl: code = 0x1D; break;

            case KbdKey.A: code = 0x1E; break;
            case KbdKey.S: code = 0x1F; break;
            case KbdKey.D: code = 0x20; break;
            case KbdKey.F: code = 0x21; break;
            case KbdKey.G: code = 0x22; break;
            case KbdKey.H: code = 0x23; break;
            case KbdKey.J: code = 0x24; break;
            case KbdKey.K: code = 0x25; break;
            case KbdKey.L: code = 0x26; break;

            case KbdKey.Semicolon: code = 0x27; break;
            case KbdKey.Quote: code = 0x28; break;
            case KbdKey.Grave: code = 0x29; break;
            case KbdKey.LeftShift: code = 0x2A; break;
            case KbdKey.Backslash: code = 0x2B; break;

            case KbdKey.Z: code = 0x2C; break;
            case KbdKey.X: code = 0x2D; break;
            case KbdKey.C: code = 0x2E; break;
            case KbdKey.V: code = 0x2F; break;
            case KbdKey.B: code = 0x30; break;
            case KbdKey.N: code = 0x31; break;
            case KbdKey.M: code = 0x32; break;

            case KbdKey.Comma: code = 0x33; break;
            case KbdKey.Period: code = 0x34; break;
            case KbdKey.Slash: code = 0x35; break;
            case KbdKey.RightShift: code = 0x36; break;
            case KbdKey.KpMultiply: code = 0x37; break;
            case KbdKey.LeftAlt: code = 0x38; break;
            case KbdKey.Space: code = 0x39; break;
            case KbdKey.CapsLock: code = 0x3A; break;

            case KbdKey.F1: code = 0x3B; break;
            case KbdKey.F2: code = 0x3C; break;
            case KbdKey.F3: code = 0x3D; break;
            case KbdKey.F4: code = 0x3E; break;
            case KbdKey.F5: code = 0x3F; break;
            case KbdKey.F6: code = 0x40; break;
            case KbdKey.F7: code = 0x41; break;
            case KbdKey.F8: code = 0x42; break;
            case KbdKey.F9: code = 0x43; break;
            case KbdKey.F10: code = 0x44; break;

            case KbdKey.NumLock: code = 0x45; break;
            case KbdKey.ScrollLock: code = 0x46; break;

            case KbdKey.Kp7: code = 0x47; break;
            case KbdKey.Kp8: code = 0x48; break;
            case KbdKey.Kp9: code = 0x49; break;
            case KbdKey.KpMinus: code = 0x4A; break;
            case KbdKey.Kp4: code = 0x4B; break;
            case KbdKey.Kp5: code = 0x4C; break;
            case KbdKey.Kp6: code = 0x4D; break;
            case KbdKey.KpPlus: code = 0x4E; break;
            case KbdKey.Kp1: code = 0x4F; break;
            case KbdKey.Kp2: code = 0x50; break;
            case KbdKey.Kp3: code = 0x51; break;
            case KbdKey.Kp0: code = 0x52; break;
            case KbdKey.KpPeriod: code = 0x53; break;

            case KbdKey.Oem102: code = 0x56; break;
            case KbdKey.F11: code = 0x57; break;
            case KbdKey.F12: code = 0x58; break;

            case KbdKey.Abnt1: code = 0x73; break;

            // Extended keys
            case KbdKey.KpEnter: extend = true; code = 0x1C; break;
            case KbdKey.RightCtrl: extend = true; code = 0x1D; break;
            case KbdKey.KpDivide: extend = true; code = 0x35; break;
            case KbdKey.RightAlt: extend = true; code = 0x38; break;
            case KbdKey.Home: extend = true; code = 0x47; break;
            case KbdKey.Up: extend = true; code = 0x48; break;
            case KbdKey.PageUp: extend = true; code = 0x49; break;
            case KbdKey.Left: extend = true; code = 0x4B; break;
            case KbdKey.Right: extend = true; code = 0x4D; break;
            case KbdKey.End: extend = true; code = 0x4F; break;
            case KbdKey.Down: extend = true; code = 0x50; break;
            case KbdKey.PageDown: extend = true; code = 0x51; break;
            case KbdKey.Insert: extend = true; code = 0x52; break;
            case KbdKey.Delete: extend = true; code = 0x53; break;
            case KbdKey.LeftGui: extend = true; code = 0x5B; break;
            case KbdKey.RightGui: extend = true; code = 0x5C; break;

            // Special cases
            case KbdKey.Pause:
                if (isPressed) {
                    // Pause key gets released as soon as it is pressed
                    return [
                        0xE1, 0x1D, 0x45, 0xE1,
                        (byte)(0x1D | 0x80), (byte)(0x45 | 0x80)
                    ];
                }
                return [];

            case KbdKey.PrintScreen:
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
    public List<byte> GetScanCode2(KbdKey keyType, bool isPressed) {
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
    public List<byte> GetScanCode3(KbdKey keyType, bool isPressed) {
        return GetScanCode1(keyType, isPressed); // Fallback to set 1
    }
}