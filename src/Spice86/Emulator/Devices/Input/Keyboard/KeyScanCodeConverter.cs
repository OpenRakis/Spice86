using Avalonia.Input;

using System.Collections.Generic;

namespace Spice86.Emulator.Devices.Input.Keyboard;

public class KeyScancodeConverter
{
    private static readonly Dictionary<Key, int> _keyPressedScanCode = new();
    private static readonly Dictionary<int, int> _scanCodeToAscii = new();

    static KeyScancodeConverter()
    {
        // Some keys are not supported by AvaloniaUI so not putting them.
        _keyPressedScanCode.Add(Key.LeftCtrl, 0x1D);
        _keyPressedScanCode.Add(Key.RightCtrl, 0x1D);
        _keyPressedScanCode.Add(Key.LeftShift, 0x2A);
        _keyPressedScanCode.Add(Key.RightShift, 0x2A);
        _keyPressedScanCode.Add(Key.F1, 0x3B);
        _keyPressedScanCode.Add(Key.F2, 0x3C);
        _keyPressedScanCode.Add(Key.F3, 0x3D);
        _keyPressedScanCode.Add(Key.F4, 0x3E);
        _keyPressedScanCode.Add(Key.F5, 0x3F);
        _keyPressedScanCode.Add(Key.F6, 0x40);
        _keyPressedScanCode.Add(Key.F7, 0x41);
        _keyPressedScanCode.Add(Key.F8, 0x42);
        _keyPressedScanCode.Add(Key.F9, 0x43);
        _keyPressedScanCode.Add(Key.F10, 0x44);
        _keyPressedScanCode.Add(Key.F11, 0x57);
        _keyPressedScanCode.Add(Key.F12, 0x58);
        _keyPressedScanCode.Add(Key.NumLock, 0x45);
        _keyPressedScanCode.Add(Key.LeftAlt, 0x38);
        _keyPressedScanCode.Add(Key.RightAlt, 0x38);
        _keyPressedScanCode.Add(Key.A, 0x1E);
        _keyPressedScanCode.Add(Key.B, 0x30);
        _keyPressedScanCode.Add(Key.C, 0x2E);
        _keyPressedScanCode.Add(Key.D, 0x20);
        _keyPressedScanCode.Add(Key.E, 0x12);
        _keyPressedScanCode.Add(Key.F, 0x21);
        _keyPressedScanCode.Add(Key.G, 0x22);
        _keyPressedScanCode.Add(Key.H, 0x23);
        _keyPressedScanCode.Add(Key.I, 0x17);
        _keyPressedScanCode.Add(Key.J, 0x24);
        _keyPressedScanCode.Add(Key.K, 0x25);
        _keyPressedScanCode.Add(Key.L, 0x26);
        _keyPressedScanCode.Add(Key.M, 0x32);
        _keyPressedScanCode.Add(Key.N, 0x31);
        _keyPressedScanCode.Add(Key.O, 0x18);
        _keyPressedScanCode.Add(Key.P, 0x19);
        _keyPressedScanCode.Add(Key.Q, 0x10);
        _keyPressedScanCode.Add(Key.R, 0x13);
        _keyPressedScanCode.Add(Key.S, 0x1F);
        _keyPressedScanCode.Add(Key.T, 0x14);
        _keyPressedScanCode.Add(Key.U, 0x16);
        _keyPressedScanCode.Add(Key.V, 0x2F);
        _keyPressedScanCode.Add(Key.W, 0x11);
        _keyPressedScanCode.Add(Key.X, 0x2D);
        _keyPressedScanCode.Add(Key.Y, 0x15);
        _keyPressedScanCode.Add(Key.Z, 0x2C);
        _keyPressedScanCode.Add(Key.D0, 0xB);
        _keyPressedScanCode.Add(Key.D1, 0x2);
        _keyPressedScanCode.Add(Key.D2, 0x3);
        _keyPressedScanCode.Add(Key.D3, 0x4);
        _keyPressedScanCode.Add(Key.D4, 0x5);
        _keyPressedScanCode.Add(Key.D5, 0x6);
        _keyPressedScanCode.Add(Key.D6, 0x7);
        _keyPressedScanCode.Add(Key.D7, 0x8);
        _keyPressedScanCode.Add(Key.D8, 0x9);
        _keyPressedScanCode.Add(Key.D9, 0xA);
        _keyPressedScanCode.Add(Key.Escape, 0x1);
        _keyPressedScanCode.Add(Key.Space, 0x39);
        _keyPressedScanCode.Add(Key.OemQuotes, 0x28);
        _keyPressedScanCode.Add(Key.OemComma, 0x33);
        _keyPressedScanCode.Add(Key.OemPeriod, 0x34);
        //_keyPressedScanCode.Add(Key.Slash, 0x35); ?
        _keyPressedScanCode.Add(Key.OemSemicolon, 0x27);
        //_keyPressedScanCode.Add(Key.Equals, 0xD); ?
        _keyPressedScanCode.Add(Key.OemOpenBrackets, 0x1A);
        _keyPressedScanCode.Add(Key.OemBackslash, 0x2B);
        _keyPressedScanCode.Add(Key.OemCloseBrackets, 0x1B);
        _keyPressedScanCode.Add(Key.OemMinus, 0xC);
        _keyPressedScanCode.Add(Key.OemQuotes, 0x29);
        _keyPressedScanCode.Add(Key.Back, 0xE);
        _keyPressedScanCode.Add(Key.Enter, 0x1C);
        _keyPressedScanCode.Add(Key.Tab, 0xF);
        _keyPressedScanCode.Add(Key.Add, 0x4E);
        _keyPressedScanCode.Add(Key.Subtract, 0x4A);
        _keyPressedScanCode.Add(Key.End, 0x4F);
        _keyPressedScanCode.Add(Key.Down, 0x50);
        _keyPressedScanCode.Add(Key.PageDown, 0x51);
        _keyPressedScanCode.Add(Key.Left, 0x4B);
        _keyPressedScanCode.Add(Key.Right, 0x4D);
        _keyPressedScanCode.Add(Key.Home, 0x47);
        _keyPressedScanCode.Add(Key.Up, 0x48);
        _keyPressedScanCode.Add(Key.PageUp, 0x49);
        _keyPressedScanCode.Add(Key.Insert, 0x52);
        _keyPressedScanCode.Add(Key.Delete, 0x53);
        _keyPressedScanCode.Add(Key.D5, 0x4C);
        _keyPressedScanCode.Add(Key.Multiply, 0x37);
        _scanCodeToAscii.Add(0x01, 0x1B);
        _scanCodeToAscii.Add(0x02, 0x31);
        _scanCodeToAscii.Add(0x03, 0x32);
        _scanCodeToAscii.Add(0x04, 0x33);
        _scanCodeToAscii.Add(0x05, 0x34);
        _scanCodeToAscii.Add(0x06, 0x35);
        _scanCodeToAscii.Add(0x07, 0x36);
        _scanCodeToAscii.Add(0x08, 0x37);
        _scanCodeToAscii.Add(0x09, 0x38);
        _scanCodeToAscii.Add(0x0A, 0x39);
        _scanCodeToAscii.Add(0x0B, 0x30);
        _scanCodeToAscii.Add(0x0C, 0x2D);
        _scanCodeToAscii.Add(0x0D, 0x3D);
        _scanCodeToAscii.Add(0x0E, 0x08);
        _scanCodeToAscii.Add(0x0F, 0x09);
        _scanCodeToAscii.Add(0x10, 0x71);
        _scanCodeToAscii.Add(0x11, 0x77);
        _scanCodeToAscii.Add(0x12, 0x65);
        _scanCodeToAscii.Add(0x13, 0x72);
        _scanCodeToAscii.Add(0x14, 0x74);
        _scanCodeToAscii.Add(0x15, 0x79);
        _scanCodeToAscii.Add(0x16, 0x75);
        _scanCodeToAscii.Add(0x17, 0x69);
        _scanCodeToAscii.Add(0x18, 0x6F);
        _scanCodeToAscii.Add(0x19, 0x70);
        _scanCodeToAscii.Add(0x1A, 0x5B);
        _scanCodeToAscii.Add(0x1B, 0x5D);
        _scanCodeToAscii.Add(0x1C, 0x0D);
        _scanCodeToAscii.Add(0x1E, 0x61);
        _scanCodeToAscii.Add(0x1F, 0x73);
        _scanCodeToAscii.Add(0x20, 0x64);
        _scanCodeToAscii.Add(0x21, 0x66);
        _scanCodeToAscii.Add(0x22, 0x67);
        _scanCodeToAscii.Add(0x23, 0x68);
        _scanCodeToAscii.Add(0x24, 0x6A);
        _scanCodeToAscii.Add(0x25, 0x6B);
        _scanCodeToAscii.Add(0x26, 0x6C);
        _scanCodeToAscii.Add(0x27, 0x3B);
        _scanCodeToAscii.Add(0x28, 0x27);
        _scanCodeToAscii.Add(0x29, 0x60);
        _scanCodeToAscii.Add(0x2B, 0x5C);
        _scanCodeToAscii.Add(0x2C, 0x7A);
        _scanCodeToAscii.Add(0x2D, 0x78);
        _scanCodeToAscii.Add(0x2E, 0x63);
        _scanCodeToAscii.Add(0x2F, 0x76);
        _scanCodeToAscii.Add(0x30, 0x62);
        _scanCodeToAscii.Add(0x31, 0x6E);
        _scanCodeToAscii.Add(0x32, 0x6D);
        _scanCodeToAscii.Add(0x33, 0x2C);
        _scanCodeToAscii.Add(0x34, 0x2E);
        _scanCodeToAscii.Add(0x35, 0x2F);
        _scanCodeToAscii.Add(0x37, 0x2A);
        _scanCodeToAscii.Add(0x39, 0x20);
        _scanCodeToAscii.Add(0x4A, 0x2D);
        _scanCodeToAscii.Add(0x4C, 0x35);
        _scanCodeToAscii.Add(0x4E, 0x2B);
    }

    public int GetAsciiCode(int scancode)
    {
        int keypressedScancode = scancode;
        if (keypressedScancode > 0x7F)
        {
            keypressedScancode -= 0x80;
        }

        return _scanCodeToAscii[keypressedScancode];
    }

    public int GetKeyPressedScancode(Key keyCode)
    {
        return _keyPressedScanCode[keyCode];
    }

    public int? GetKeyReleasedScancode(Key keyCode)
    {
        int? pressed = GetKeyPressedScancode(keyCode);
        if (pressed != null)
        {
            return pressed + 0x80;
        }

        return null;
    }
}