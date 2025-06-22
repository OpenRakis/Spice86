namespace Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// Some ASCII characters used in the emulator.
/// </summary>
public enum AsciiControlCodes : byte {
    // Control characters
    Null = 0x00,
    CtrlC = 0x03,
    Backspace = 0x08,
    LineFeed = 0x0a,
    FormFeed = 0x0c,
    CarriageReturn = 0x0d,
    Escape = 0x1b,
    Delete = 0x7f,
    Extended = 0xe0,
}