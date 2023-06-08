namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

/// <summary>
/// Keyboard commands that can be sent to the keyboard controller. <br/>
/// https://www.win.tue.nl/~aeb/linux/kbd/scancodes-11.html#kcc60 <br/>
/// A20 Gate commands: <br/>
/// https://mrhopehub.github.io/2014/12/26/enabling-the-A20-Gate.html
/// </summary>
public enum KeyboardCommand : byte {
    /// <summary>
    /// No command set.
    /// </summary>
    None = 0x0,
    
    /// <summary>
    /// Enable A20 Address Line
    /// </summary>
    EnableA20Gate = 0xDD,
    
    /// <summary>
    /// Disable A20 Address Line
    /// </summary>
    DisableA20Gate = 0xDF,
    
    /// <summary>
    /// Write to the output port
    /// </summary>
    SetOutputPort = 0xD1,
}