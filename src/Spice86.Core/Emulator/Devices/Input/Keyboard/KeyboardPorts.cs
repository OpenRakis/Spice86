namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

/// <summary>
/// IDs of keyboard ports for both read and write. <br/>
/// https://www.win.tue.nl/~aeb/linux/kbd/scancodes-11.html
/// </summary>
public static class KeyboardPorts {
    /// <summary>
    /// Port used by the CPU to read the input buffer, or write to the output buffer, of the keyboard controller.
    /// </summary>
    public const byte Data = 0x60;

    /// <summary>
    /// Port that can be used by the CPU to read the status register of the keyboard controller.
    /// </summary>
    public const byte StatusRegister = 0x64;

    /// <summary>
    /// If the CPU writes to port 0x64, the byte is interpreted as a command byte.
    /// </summary>
    public const byte Command = 0x64;
}