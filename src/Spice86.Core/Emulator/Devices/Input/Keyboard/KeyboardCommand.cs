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
    /// Set keyboard controller command
    /// </summary>
    SetCommand = 0x4,
    
    /// <summary>
    /// Write to the output port
    /// </summary>
    SetOutputPort = 0x03
}