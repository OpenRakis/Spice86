namespace Spice86.Core.Emulator.Devices.Input.Keyboard;

/// <summary>
/// Keyboard and keyboard controller commands. <br/>
/// Reference: <br/>
/// - https://www.win.tue.nl/~aeb/linux/kbd/scancodes-11.html#kcc60 <br/>
/// A20 Gate commands: <br/>
/// - https://mrhopehub.github.io/2014/12/26/enabling-the-A20-Gate.html <br/>
/// </summary>
/// <remarks>
/// Most of them are unimplemented right now.
/// </remarks>
public enum KeyboardCommand : byte {
    /// <summary>
    /// No command set.
    /// </summary>
    None = 0x00,

    /// <summary>
    /// Set keyboard LEDs
    /// </summary>
    SetLeds = 0xED,

    /// <summary>
    /// Echo command - diagnostic
    /// </summary>
    Echo = 0xEE,

    /// <summary>
    /// Get/set scancode set
    /// </summary>
    CodeSet = 0xF0,

    /// <summary>
    /// Identify keyboard
    /// </summary>
    Identify = 0xF2,

    /// <summary>
    /// Set typematic rate and delay
    /// </summary>
    SetTypeRate = 0xF3,

    /// <summary>
    /// Clear buffer and enable scanning
    /// </summary>
    ClearEnable = 0xF4,

    /// <summary>
    /// Set defaults and disable scanning
    /// </summary>
    DefaultDisable = 0xF5,

    /// <summary>
    /// Set defaults and enable scanning
    /// </summary>
    ResetEnable = 0xF6,

    /// <summary>
    /// Set all keys to typematic (scancode set 3)
    /// </summary>
    Set3AllTypematic = 0xF7,

    /// <summary>
    /// Set all keys to make/break (scancode set 3)
    /// </summary>
    Set3AllMakeBreak = 0xF8,

    /// <summary>
    /// Set all keys to make only (scancode set 3)
    /// </summary>
    Set3AllMakeOnly = 0xF9,

    /// <summary>
    /// Set all keys to typematic/make/break (scancode set 3)
    /// </summary>
    Set3AllTypeMakeBreak = 0xFA,

    /// <summary>
    /// Set specific key to typematic (scancode set 3)
    /// </summary>
    Set3KeyTypematic = 0xFB,

    /// <summary>
    /// Set specific key to make/break (scancode set 3)
    /// </summary>
    Set3KeyMakeBreak = 0xFC,

    /// <summary>
    /// Set specific key to make only (scancode set 3)
    /// </summary>
    Set3KeyMakeOnly = 0xFD,

    /// <summary>
    /// Resend last byte
    /// </summary>
    Resend = 0xFE,

    /// <summary>
    /// Reset and self-test
    /// </summary>
    Reset = 0xFF,

    // 8042 controller commands (sent to port 0x64)

    /// <summary>
    /// Read controller configuration byte
    /// </summary>
    ReadByteConfig = 0x20,

    /// <summary>
    /// Write controller configuration byte
    /// </summary>
    WriteByteConfig = 0x60,

    /// <summary>
    /// Disable auxiliary port (mouse)
    /// </summary>
    DisablePortAux = 0xA7,

    /// <summary>
    /// Enable auxiliary port (mouse)
    /// </summary>
    EnablePortAux = 0xA8,

    /// <summary>
    /// Test auxiliary port
    /// </summary>
    TestPortAux = 0xA9,

    /// <summary>
    /// Test controller
    /// </summary>
    TestController = 0xAA,

    /// <summary>
    /// Test keyboard port
    /// </summary>
    TestPortKbd = 0xAB,

    /// <summary>
    /// Diagnostic dump
    /// </summary>
    DiagnosticDump = 0xAC,

    /// <summary>
    /// Disable keyboard port
    /// </summary>
    DisablePortKbd = 0xAD,

    /// <summary>
    /// Enable keyboard port
    /// </summary>
    EnablePortKbd = 0xAE,

    /// <summary>
    /// Obsolete, only some controllers implement it.
    /// </summary>
    ReadKeyboardVersion = 0xAF,

    /// <summary>
    /// Read input port
    /// </summary>
    ReadInputPort = 0xC0,

    /// <summary>
    /// Read controller mode
    /// </summary>
    ReadControllerMode = 0xCA,

    /// <summary>
    /// Write controller mode
    /// </summary>
    WriteControllerMode = 0xCB,

    /// <summary>
    /// Read output port
    /// </summary>
    ReadOutputPort = 0xD0,

    /// <summary>
    /// Write to the output port
    /// </summary>
    WriteOutputPort = 0xD1,

    /// <summary>
    /// Simulate keyboard input
    /// </summary>
    SimulateInputKbd = 0xD2,

    /// <summary>
    /// Simulate auxiliary input (mouse)
    /// </summary>
    SimulateInputAux = 0xD3,

    /// <summary>
    /// Send byte to auxiliary device
    /// </summary>
    WriteAux = 0xD4,

    /// <summary>
    /// Disable A20 Address Line
    /// </summary>
    DisableA20 = 0xDD,

    /// <summary>
    /// Enable A20 Address Line
    /// </summary>
    EnableA20 = 0xDF,

    /// <summary>
    /// Read test inputs
    /// </summary>
    ReadTestInputs = 0xE0,
}