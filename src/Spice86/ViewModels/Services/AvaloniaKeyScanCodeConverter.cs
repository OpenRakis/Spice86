namespace Spice86.ViewModels.Services;

using Spice86.Shared.Emulator.Keyboard;

using System.Collections.Frozen;
using System.Collections.Generic;

/// <summary>
/// A utility class that provides mapping from AvaloniaUI <see cref="Key"/> values to keyboard scan codes.
/// </summary>
internal class AvaloniaKeyScanCodeConverter {
    /// <summary>
    /// A dictionary that maps <see cref="Key"/> values to their corresponding keyboard scan codes.
    /// </summary>
    private static readonly FrozenDictionary<Key, byte> _keyPressedScanCode;

    /// <summary>
    /// Initializes static members of the <see cref="AvaloniaKeyScanCodeConverter"/> class.
    /// </summary>
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
        }.ToFrozenDictionary();
    }

    /// <summary>
    /// Retrieves the scancode of a pressed key, if it exists in the _keyPressedScanCode dictionary.
    /// </summary>
    /// <param name="key">The key for which to retrieve the scancode.</param>
    /// <returns>The scancode of the pressed key, or null if the key is not present in the dictionary.</returns>
    public byte? GetKeyPressedScancode(Key key) {
        if (_keyPressedScanCode.TryGetValue(key, out byte value)) {
            return value;
        }
        return null;
    }

    /// <summary>
    /// Retrieves the scancode of a released key, if it exists in the _keyPressedScanCode dictionary.
    /// </summary>
    /// <param name="key">The key for which to retrieve the scancode.</param>
    /// <returns>The scancode of the released key, or null if the key is not present in the dictionary.</returns>
    public byte? GetKeyReleasedScancode(Key key) {
        byte? pressed = GetKeyPressedScancode(key);
        if (pressed != null) {
            return (byte)(pressed + 0x80);
        }
        return null;
    }
}