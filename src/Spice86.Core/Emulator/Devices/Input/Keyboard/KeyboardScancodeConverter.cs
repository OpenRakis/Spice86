namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

using Spice86.Shared.Emulator.Keyboard;

using System.Collections.Frozen;
using System.Collections.Generic;

/// <summary>
/// Converts between KbdKey enum and keyboard scancodes for different code sets.
/// Based on DOSBox keyboard_scancodes.cpp implementation.
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
            return new List<byte>();
        }

        switch(codeSet) {
            case 1: return GetScanCode1(keyType, isPressed);
            case 2: return GetScanCode2(keyType, isPressed);
            case 3: return GetScanCode3(keyType, isPressed);
            default: return GetScanCode1(keyType, isPressed); // Default to set 1
        }
    }

    private List<byte> GetScanCode1(KbdKey keyType, bool isPressed) {
        byte code = 0x00;
        bool extend = false;

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
                    return new List<byte> { 
                        0xE1, 0x1D, 0x45, 0xE1, 
                        (byte)(0x1D | 0x80), (byte)(0x45 | 0x80) 
                    };
                }
                return new List<byte>();
                
            case KbdKey.PrintScreen:
                return new List<byte> {
                    0xE0,
                    (byte)(0x2A | (isPressed ? 0 : 0x80)),
                    0xE0,
                    (byte)(0x37 | (isPressed ? 0 : 0x80))
                };
                
            default:
                return new List<byte>();
        }
        
        List<byte> result = new List<byte>();
        
        if (extend) {
            result.Add(0xE0);
        }
        
        result.Add((byte)(code | (isPressed ? 0 : 0x80)));
        return result;
    }

    private List<byte> GetScanCode2(KbdKey keyType, bool isPressed) {
        // Implement scancode set 2 conversion if needed
        // This would be a direct port of KEYBOARD_GetScanCode2
        return GetScanCode1(keyType, isPressed); // Fallback to set 1 for now
    }
    
    private List<byte> GetScanCode3(KbdKey keyType, bool isPressed) {
        // Implement scancode set 3 conversion if needed
        // This would be a direct port of KEYBOARD_GetScanCode3
        return GetScanCode1(keyType, isPressed); // Fallback to set 1 for now
    }
    
    /// <summary>
    /// ASCII lookup table for scancode set 1
    /// Based on DOSBox BIOS keyboard table
    /// </summary>
    private static readonly FrozenDictionary<byte, (byte normal, byte shift, byte ctrl, byte alt)> _scancodeToAscii = new
        Dictionary<byte, (byte normal, byte shift, byte ctrl, byte alt)>() {
        { 0x02, (0x31, 0x21, 0x00, 0x78) }, // 1!
        { 0x03, (0x32, 0x40, 0x00, 0x79) }, // 2@
        { 0x04, (0x33, 0x23, 0x00, 0x7A) }, // 3#
        { 0x05, (0x34, 0x24, 0x00, 0x7B) }, // 4$
        { 0x06, (0x35, 0x25, 0x00, 0x7C) }, // 5%
        { 0x07, (0x36, 0x5E, 0x1E, 0x7D) }, // 6^
        { 0x08, (0x37, 0x26, 0x00, 0x7E) }, // 7&
        { 0x09, (0x38, 0x2A, 0x00, 0x7F) }, // 8*
        { 0x0A, (0x39, 0x28, 0x00, 0x80) }, // 9(
        { 0x0B, (0x30, 0x29, 0x00, 0x81) }, // 0)
        { 0x0C, (0x2D, 0x5F, 0x1F, 0x82) }, // -_
        { 0x0D, (0x3D, 0x2B, 0x00, 0x83) }, // =+
        { 0x0E, (0x08, 0x08, 0x7F, 0x0E) }, // backspace
        { 0x0F, (0x09, 0x00, 0x94, 0x00) }, // tab
        { 0x10, (0x71, 0x51, 0x11, 0x10) }, // Q
        { 0x11, (0x77, 0x57, 0x17, 0x11) }, // W
        { 0x12, (0x65, 0x45, 0x05, 0x12) }, // E
        { 0x13, (0x72, 0x52, 0x12, 0x13) }, // R
        { 0x14, (0x74, 0x54, 0x14, 0x14) }, // T
        { 0x15, (0x79, 0x59, 0x19, 0x15) }, // Y
        { 0x16, (0x75, 0x55, 0x15, 0x16) }, // U
        { 0x17, (0x69, 0x49, 0x09, 0x17) }, // I
        { 0x18, (0x6F, 0x4F, 0x0F, 0x18) }, // O
        { 0x19, (0x70, 0x50, 0x10, 0x19) }, // P
        { 0x1A, (0x5B, 0x7B, 0x1B, 0x1A) }, // [{
        { 0x1B, (0x5D, 0x7D, 0x1D, 0x1B) }, // ]}
        { 0x1C, (0x0D, 0x0D, 0x0A, 0x00) }, // Enter
        { 0x1E, (0x61, 0x41, 0x01, 0x1E) }, // A
        { 0x1F, (0x73, 0x53, 0x13, 0x1F) }, // S
        { 0x20, (0x64, 0x44, 0x04, 0x20) }, // D
        { 0x21, (0x66, 0x46, 0x06, 0x21) }, // F
        { 0x22, (0x67, 0x47, 0x07, 0x22) }, // G
        { 0x23, (0x68, 0x48, 0x08, 0x23) }, // H
        { 0x24, (0x6A, 0x4A, 0x0A, 0x24) }, // J
        { 0x25, (0x6B, 0x4B, 0x0B, 0x25) }, // K
        { 0x26, (0x6C, 0x4C, 0x0C, 0x26) }, // L
        { 0x27, (0x3B, 0x3A, 0x00, 0x27) }, // ;:
        { 0x28, (0x27, 0x22, 0x00, 0x28) }, // '"
        { 0x29, (0x60, 0x7E, 0x00, 0x29) }, // `~
        { 0x2B, (0x5C, 0x7C, 0x1C, 0x2B) }, // \|
        { 0x2C, (0x7A, 0x5A, 0x1A, 0x2C) }, // Z
        { 0x2D, (0x78, 0x58, 0x18, 0x2D) }, // X
        { 0x2E, (0x63, 0x43, 0x03, 0x2E) }, // C
        { 0x2F, (0x76, 0x56, 0x16, 0x2F) }, // V
        { 0x30, (0x62, 0x42, 0x02, 0x30) }, // B
        { 0x31, (0x6E, 0x4E, 0x0E, 0x31) }, // N
        { 0x32, (0x6D, 0x4D, 0x0D, 0x32) }, // M
        { 0x33, (0x2C, 0x3C, 0x00, 0x33) }, // ,<
        { 0x34, (0x2E, 0x3E, 0x00, 0x34) }, // .>
        { 0x35, (0x2F, 0x3F, 0x00, 0x35) }, // /?
        { 0x37, (0x2A, 0x2A, 0x96, 0x37) }, // *
        { 0x39, (0x20, 0x20, 0x20, 0x20) }, // space
        // Function keys
        { 0x3B, (0x00, 0x00, 0x00, 0x68) }, // F1
        { 0x3C, (0x00, 0x00, 0x00, 0x69) }, // F2
        { 0x3D, (0x00, 0x00, 0x00, 0x6A) }, // F3
        { 0x3E, (0x00, 0x00, 0x00, 0x6B) }, // F4
        { 0x3F, (0x00, 0x00, 0x00, 0x6C) }, // F5
        { 0x40, (0x00, 0x00, 0x00, 0x6D) }, // F6
        { 0x41, (0x00, 0x00, 0x00, 0x6E) }, // F7
        { 0x42, (0x00, 0x00, 0x00, 0x6F) }, // F8
        { 0x43, (0x00, 0x00, 0x00, 0x70) }, // F9
        { 0x44, (0x00, 0x00, 0x00, 0x71) }, // F10
        // Keypad
        { 0x47, (0x00, 0x37, 0x77, 0x07) }, // Keypad 7/Home
        { 0x48, (0x00, 0x38, 0x8D, 0x08) }, // Keypad 8/Up
        { 0x49, (0x00, 0x39, 0x84, 0x09) }, // Keypad 9/PgUp
        { 0x4A, (0x2D, 0x2D, 0x8E, 0x4A) }, // Keypad -
        { 0x4B, (0x00, 0x34, 0x73, 0x04) }, // Keypad 4/Left
        { 0x4C, (0x00, 0x35, 0x8F, 0x05) }, // Keypad 5
        { 0x4D, (0x00, 0x36, 0x74, 0x06) }, // Keypad 6/Right
        { 0x4E, (0x2B, 0x2B, 0x90, 0x4E) }, // Keypad +
        { 0x4F, (0x00, 0x31, 0x75, 0x01) }, // Keypad 1/End
        { 0x50, (0x00, 0x32, 0x91, 0x02) }, // Keypad 2/Down
        { 0x51, (0x00, 0x33, 0x76, 0x03) }, // Keypad 3/PgDn
        { 0x52, (0x00, 0x30, 0x92, 0x00) }, // Keypad 0/Ins
        { 0x53, (0x00, 0x2E, 0x93, 0x00) }, // Keypad ./Del
    }.ToFrozenDictionary();

    /// <summary>
    /// Gets ASCII code for a scancode based on keyboard flag state
    /// </summary>
    public byte GetAsciiCode(byte scanCode, byte flags1 = 0) {
        // Don't process key releases (high bit set)
        if ((scanCode & 0x80) != 0) {
            return 0;
        }
        
        // Check if we have ASCII mapping for this scancode
        if (_scancodeToAscii.TryGetValue(scanCode, out (byte normal, byte shift, byte ctrl, byte alt) ascii)) {
            bool isShift = (flags1 & 0x03) != 0; // Shift flags
            bool isCtrl = (flags1 & 0x04) != 0;  // Ctrl flag
            bool isAlt = (flags1 & 0x08) != 0;   // Alt flag
            
            if (isAlt) return ascii.alt;
            if (isCtrl) return ascii.ctrl;
            if (isShift) return ascii.shift;
            
            return ascii.normal;
        }
        
        return 0;
    }
}