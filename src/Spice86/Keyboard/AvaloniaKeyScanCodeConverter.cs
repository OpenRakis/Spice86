namespace Spice86.Keyboard;

using Avalonia.Input;

using Spice86.Shared;
using Spice86.Shared.Emulator.Keyboard;
using Spice86.Shared.Interfaces;

using System.Collections.Generic;

public class AvaloniaKeyScanCodeConverter : IKeyScanCodeConverter {
    private static readonly Dictionary<Key, byte> _keyPressedScanCode;
    private static readonly Dictionary<byte, byte> _scanCodeToAscii;

    static AvaloniaKeyScanCodeConverter() {
        // Some keys are not supported by AvaloniaUI so not putting them.
        _keyPressedScanCode = new Dictionary<Key, byte>()
        {
            {Key.LeftCtrl, 0x1D},
            {Key.RightCtrl, 0x1D},
            {Key.LeftShift, 0x2A},
            {Key.RightShift, 0x2A},
            {Key.F1, 0x3B},
            {Key.F2, 0x3C},
            {Key.F3, 0x3D},
            {Key.F4, 0x3E},
            {Key.F5, 0x3F},
            {Key.F6, 0x40},
            {Key.F7, 0x41},
            {Key.F8, 0x42},
            {Key.F9, 0x43},
            {Key.F10, 0x44},
            {Key.F11, 0x57},
            {Key.F12, 0x58},
            {Key.NumLock, 0x45},
            {Key.LeftAlt, 0x38},
            {Key.RightAlt, 0x38},
            {Key.A, 0x1E},
            {Key.B, 0x30},
            {Key.C, 0x2E},
            {Key.D, 0x20},
            {Key.E, 0x12},
            {Key.F, 0x21},
            {Key.G, 0x22},
            {Key.H, 0x23},
            {Key.I, 0x17},
            {Key.J, 0x24},
            {Key.K, 0x25},
            {Key.L, 0x26},
            {Key.M, 0x32},
            {Key.N, 0x31},
            {Key.O, 0x18},
            {Key.P, 0x19},
            {Key.Q, 0x10},
            {Key.R, 0x13},
            {Key.S, 0x1F},
            {Key.T, 0x14},
            {Key.U, 0x16},
            {Key.V, 0x2F},
            {Key.W, 0x11},
            {Key.X, 0x2D},
            {Key.Y, 0x15},
            {Key.Z, 0x2C},
            {Key.D0, 0xB},
            {Key.D1, 0x2},
            {Key.D2, 0x3},
            {Key.D3, 0x4},
            {Key.D4, 0x5},
            {Key.D5, 0x6},
            {Key.D6, 0x7},
            {Key.D7, 0x8},
            {Key.D8, 0x9},
            {Key.D9, 0xA},
            {Key.Escape, 0x1},
            {Key.Space, 0x39},
            {Key.OemQuotes, 0x28},
            {Key.OemComma, 0x33},
            {Key.OemPeriod, 0x34},
            //{Key.Slash, 0x35}, ?
            {Key.OemSemicolon, 0x27},
            //{Key.Equals, 0xD}, ?
            {Key.OemOpenBrackets, 0x1A},
            {Key.OemBackslash, 0x2B},
            {Key.OemCloseBrackets, 0x1B},
            {Key.OemMinus, 0xC},
            //{Key.OemQuotes, 0x29}, ?
            {Key.Back, 0xE},
            {Key.Enter, 0x1C},
            {Key.Tab, 0xF},
            {Key.Add, 0x4E},
            {Key.Subtract, 0x4A},
            {Key.End, 0x4F},
            {Key.Down, 0x50},
            {Key.PageDown, 0x51},
            {Key.Left, 0x4B},
            {Key.Right, 0x4D},
            {Key.Home, 0x47},
            {Key.Up, 0x48},
            {Key.PageUp, 0x49},
            {Key.Insert, 0x52},
            {Key.Delete, 0x53},
            //{Key.D5, 0x4C}, ?
            {Key.Multiply, 0x37},
        };
        _scanCodeToAscii = new Dictionary<byte, byte>()
        {
            {0x01, 0x1B},
            {0x02, 0x31},
            {0x03, 0x32},
            {0x04, 0x33},
            {0x05, 0x34},
            {0x06, 0x35},
            {0x07, 0x36},
            {0x08, 0x37},
            {0x09, 0x38},
            {0x0A, 0x39},
            {0x0B, 0x30},
            {0x0C, 0x2D},
            {0x0D, 0x3D},
            {0x0E, 0x08},
            {0x0F, 0x09},
            {0x10, 0x71},
            {0x11, 0x77},
            {0x12, 0x65},
            {0x13, 0x72},
            {0x14, 0x74},
            {0x15, 0x79},
            {0x16, 0x75},
            {0x17, 0x69},
            {0x18, 0x6F},
            {0x19, 0x70},
            {0x1A, 0x5B},
            {0x1B, 0x5D},
            {0x1C, 0x0D},
            {0x1E, 0x61},
            {0x1F, 0x73},
            {0x20, 0x64},
            {0x21, 0x66},
            {0x22, 0x67},
            {0x23, 0x68},
            {0x24, 0x6A},
            {0x25, 0x6B},
            {0x26, 0x6C},
            {0x27, 0x3B},
            {0x28, 0x27},
            {0x29, 0x60},
            {0x2B, 0x5C},
            {0x2C, 0x7A},
            {0x2D, 0x78},
            {0x2E, 0x63},
            {0x2F, 0x76},
            {0x30, 0x62},
            {0x31, 0x6E},
            {0x32, 0x6D},
            {0x33, 0x2C},
            {0x34, 0x2E},
            {0x35, 0x2F},
            {0x37, 0x2A},
            {0x39, 0x20},
            {0x4A, 0x2D},
            {0x4C, 0x35},
            {0x4E, 0x2B},
        };
    }

    public byte? GetAsciiCode(byte scancode) {
        byte keypressedScancode = scancode;
        if (keypressedScancode > 0x7F) {
            keypressedScancode -= 0x80;
        }
        if (!_scanCodeToAscii.ContainsKey(keypressedScancode)) {
            return null;
        }
        return _scanCodeToAscii[keypressedScancode];
    }

    public byte? GetKeyPressedScancode(KeyboardInput keyInput) {
        if (!_keyPressedScanCode.ContainsKey(((KeyEventArgs)keyInput.EventArgs).Key)) {
            return null;
        }
        return _keyPressedScanCode[((KeyEventArgs)keyInput.EventArgs).Key];
    }

    public byte? GetKeyReleasedScancode(KeyboardInput keyInput) {
        byte? pressed = GetKeyPressedScancode(keyInput);
        if (pressed != null) {
            return (byte)(pressed + 0x80);
        }
        return null;
    }
}