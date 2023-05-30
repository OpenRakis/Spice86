namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

/// <summary>
/// Keyboard commands that can be sent to the keyboard controller. <br/>
/// https://www.win.tue.nl/~aeb/linux/kbd/scancodes-11.html#kcc60
/// </summary>
public enum KeyboardCommand : byte {
    /// <summary>
    /// No command set.
    /// </summary>
    None = 0x0,
    
    /// <summary>
    /// Read from input port
    /// </summary>
    ReadInputPort = 0xD0,
    
    /// <summary>
    /// Write to the output port
    /// </summary>
    SetOutputPort = 0xD1,
}