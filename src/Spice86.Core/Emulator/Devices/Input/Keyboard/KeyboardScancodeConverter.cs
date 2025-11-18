namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using Spice86.Shared.Emulator.Keyboard;

using System.Collections.Frozen;
using System.Collections.Generic;

/// <summary>
/// Converts UI PhysicalKey to internal PcKeyboardKey and ultimately returns the corresponding
/// scancode sequences for IBM Code Set 1.
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
    /// Converts a specified physical key to its corresponding PC keyboard key value.
    /// </summary>
    /// <param name="key">The physical key to convert to a PC keyboard key.</param>
    /// <returns>The corresponding <see cref="PcKeyboardKey"/> value if the mapping exists; otherwise, <see
    /// cref="PcKeyboardKey.None"/>.</returns>
    public PcKeyboardKey ConvertToKbdKey(PhysicalKey key) {
        return _keyToKbdKey.TryGetValue(key, out PcKeyboardKey kbdKey) ? kbdKey : PcKeyboardKey.None;
    }

    /// <summary>
    /// Gets the sequence of scancodes corresponding to the specified PC keyboard key, key state, and scancode set.
    /// </summary>
    /// <param name="keyType">The keyboard key for which to retrieve scancodes. Must not be PcKeyboardKey.None.</param>
    /// <param name="isPressed">A value indicating whether the key is being pressed (<see langword="true"/>) or released (<see
    /// langword="false"/>).</param>
    /// <param name="codeSet">The scancode set to use. Valid values are 1, 2, or 3. If an unsupported value is specified, set 1 is used by
    /// default.</param>
    /// <returns>A list of bytes representing the scancode sequence for the specified key and state. Returns an empty list if
    /// <paramref name="keyType"/> is PcKeyboardKey.None.</returns>
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
        switch (keyType) {
            case PcKeyboardKey.Escape: code = (byte)ScanCode1.Escape; break;
            case PcKeyboardKey.D1: code = (byte)ScanCode1.D1; break;
            case PcKeyboardKey.D2: code = (byte)ScanCode1.D2; break;
            case PcKeyboardKey.D3: code = (byte)ScanCode1.D3; break;
            case PcKeyboardKey.D4: code = (byte)ScanCode1.D4; break;
            case PcKeyboardKey.D5: code = (byte)ScanCode1.D5; break;
            case PcKeyboardKey.D6: code = (byte)ScanCode1.D6; break;
            case PcKeyboardKey.D7: code = (byte)ScanCode1.D7; break;
            case PcKeyboardKey.D8: code = (byte)ScanCode1.D8; break;
            case PcKeyboardKey.D9: code = (byte)ScanCode1.D9; break;
            case PcKeyboardKey.D0: code = (byte)ScanCode1.D0; break;

            case PcKeyboardKey.Minus: code = (byte)ScanCode1.Minus; break;
            case PcKeyboardKey.Equals: code = (byte)ScanCode1.Equals; break;
            case PcKeyboardKey.Backspace: code = (byte)ScanCode1.Backspace; break;
            case PcKeyboardKey.Tab: code = (byte)ScanCode1.Tab; break;

            case PcKeyboardKey.Q: code = (byte)ScanCode1.Q; break;
            case PcKeyboardKey.W: code = (byte)ScanCode1.W; break;
            case PcKeyboardKey.E: code = (byte)ScanCode1.E; break;
            case PcKeyboardKey.R: code = (byte)ScanCode1.R; break;
            case PcKeyboardKey.T: code = (byte)ScanCode1.T; break;
            case PcKeyboardKey.Y: code = (byte)ScanCode1.Y; break;
            case PcKeyboardKey.U: code = (byte)ScanCode1.U; break;
            case PcKeyboardKey.I: code = (byte)ScanCode1.I; break;
            case PcKeyboardKey.O: code = (byte)ScanCode1.O; break;
            case PcKeyboardKey.P: code = (byte)ScanCode1.P; break;

            case PcKeyboardKey.LeftBracket: code = (byte)ScanCode1.LeftBracket; break;
            case PcKeyboardKey.RightBracket: code = (byte)ScanCode1.RightBracket; break;
            case PcKeyboardKey.Enter: code = (byte)ScanCode1.Enter; break;
            case PcKeyboardKey.LeftCtrl: code = (byte)ScanCode1.LeftCtrl; break;

            case PcKeyboardKey.A: code = (byte)ScanCode1.A; break;
            case PcKeyboardKey.S: code = (byte)ScanCode1.S; break;
            case PcKeyboardKey.D: code = (byte)ScanCode1.D; break;
            case PcKeyboardKey.F: code = (byte)ScanCode1.F; break;
            case PcKeyboardKey.G: code = (byte)ScanCode1.G; break;
            case PcKeyboardKey.H: code = (byte)ScanCode1.H; break;
            case PcKeyboardKey.J: code = (byte)ScanCode1.J; break;
            case PcKeyboardKey.K: code = (byte)ScanCode1.K; break;
            case PcKeyboardKey.L: code = (byte)ScanCode1.L; break;

            case PcKeyboardKey.Semicolon: code = (byte)ScanCode1.Semicolon; break;
            case PcKeyboardKey.Quote: code = (byte)ScanCode1.Quote; break;
            case PcKeyboardKey.Grave: code = (byte)ScanCode1.Grave; break;
            case PcKeyboardKey.LeftShift: code = (byte)ScanCode1.LeftShift; break;
            case PcKeyboardKey.Backslash: code = (byte)ScanCode1.Backslash; break;

            case PcKeyboardKey.Z: code = (byte)ScanCode1.Z; break;
            case PcKeyboardKey.X: code = (byte)ScanCode1.X; break;
            case PcKeyboardKey.C: code = (byte)ScanCode1.C; break;
            case PcKeyboardKey.V: code = (byte)ScanCode1.V; break;
            case PcKeyboardKey.B: code = (byte)ScanCode1.B; break;
            case PcKeyboardKey.N: code = (byte)ScanCode1.N; break;
            case PcKeyboardKey.M: code = (byte)ScanCode1.M; break;

            case PcKeyboardKey.Comma: code = (byte)ScanCode1.Comma; break;
            case PcKeyboardKey.Period: code = (byte)ScanCode1.Period; break;
            case PcKeyboardKey.Slash: code = (byte)ScanCode1.Slash; break;
            case PcKeyboardKey.RightShift: code = (byte)ScanCode1.RightShift; break;
            case PcKeyboardKey.KpMultiply: code = (byte)ScanCode1.KpMultiply; break;
            case PcKeyboardKey.LeftAlt: code = (byte)ScanCode1.LeftAlt; break;
            case PcKeyboardKey.Space: code = (byte)ScanCode1.Space; break;
            case PcKeyboardKey.CapsLock: code = (byte)ScanCode1.CapsLock; break;

            case PcKeyboardKey.F1: code = (byte)ScanCode1.F1; break;
            case PcKeyboardKey.F2: code = (byte)ScanCode1.F2; break;
            case PcKeyboardKey.F3: code = (byte)ScanCode1.F3; break;
            case PcKeyboardKey.F4: code = (byte)ScanCode1.F4; break;
            case PcKeyboardKey.F5: code = (byte)ScanCode1.F5; break;
            case PcKeyboardKey.F6: code = (byte)ScanCode1.F6; break;
            case PcKeyboardKey.F7: code = (byte)ScanCode1.F7; break;
            case PcKeyboardKey.F8: code = (byte)ScanCode1.F8; break;
            case PcKeyboardKey.F9: code = (byte)ScanCode1.F9; break;
            case PcKeyboardKey.F10: code = (byte)ScanCode1.F10; break;

            case PcKeyboardKey.NumLock: code = (byte)ScanCode1.NumLock; break;
            case PcKeyboardKey.ScrollLock: code = (byte)ScanCode1.ScrollLock; break;

            case PcKeyboardKey.Kp7: code = (byte)ScanCode1.Kp7; break;
            case PcKeyboardKey.Kp8: code = (byte)ScanCode1.Kp8; break;
            case PcKeyboardKey.Kp9: code = (byte)ScanCode1.Kp9; break;
            case PcKeyboardKey.KpMinus: code = (byte)ScanCode1.KpMinus; break;
            case PcKeyboardKey.Kp4: code = (byte)ScanCode1.Kp4; break;
            case PcKeyboardKey.Kp5: code = (byte)ScanCode1.Kp5; break;
            case PcKeyboardKey.Kp6: code = (byte)ScanCode1.Kp6; break;
            case PcKeyboardKey.KpPlus: code = (byte)ScanCode1.KpPlus; break;
            case PcKeyboardKey.Kp1: code = (byte)ScanCode1.Kp1; break;
            case PcKeyboardKey.Kp2: code = (byte)ScanCode1.Kp2; break;
            case PcKeyboardKey.Kp3: code = (byte)ScanCode1.Kp3; break;
            case PcKeyboardKey.Kp0: code = (byte)ScanCode1.Kp0; break;
            case PcKeyboardKey.KpPeriod: code = (byte)ScanCode1.KpPeriod; break;

            case PcKeyboardKey.Oem102: code = (byte)ScanCode1.Oem102; break;
            case PcKeyboardKey.F11: code = (byte)ScanCode1.F11; break;
            case PcKeyboardKey.F12: code = (byte)ScanCode1.F12; break;

            case PcKeyboardKey.Abnt1: code = (byte)ScanCode1.Abnt1; break;

            // Extended keys
            case PcKeyboardKey.KpEnter: extend = true; code = (byte)ScanCode1.Enter; break;
            case PcKeyboardKey.RightCtrl: extend = true; code = (byte)ScanCode1.LeftCtrl; break;
            case PcKeyboardKey.KpDivide: extend = true; code = (byte)ScanCode1.Slash; break;
            case PcKeyboardKey.RightAlt: extend = true; code = (byte)ScanCode1.LeftAlt; break;
            case PcKeyboardKey.Home: extend = true; code = (byte)ScanCode1.Kp7; break;
            case PcKeyboardKey.Up: extend = true; code = (byte)ScanCode1.Kp8; break;
            case PcKeyboardKey.PageUp: extend = true; code = (byte)ScanCode1.Kp9; break;
            case PcKeyboardKey.Left: extend = true; code = (byte)ScanCode1.Kp4; break;
            case PcKeyboardKey.Right: extend = true; code = (byte)ScanCode1.Kp6; break;
            case PcKeyboardKey.End: extend = true; code = (byte)ScanCode1.Kp1; break;
            case PcKeyboardKey.Down: extend = true; code = (byte)ScanCode1.Kp2; break;
            case PcKeyboardKey.PageDown: extend = true; code = (byte)ScanCode1.Kp3; break;
            case PcKeyboardKey.Insert: extend = true; code = (byte)ScanCode1.Kp0; break;
            case PcKeyboardKey.Delete: extend = true; code = (byte)ScanCode1.KpPeriod; break;
            case PcKeyboardKey.LeftGui: extend = true; code = (byte)ScanCode1.LeftGui; break;
            case PcKeyboardKey.RightGui: extend = true; code = (byte)ScanCode1.RightGui; break;

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