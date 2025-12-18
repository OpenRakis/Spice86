namespace Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// Termination type returned by INT 21h AH=4Dh.
/// </summary>
public enum DosTerminationType : byte {
    Normal = 0x00,
    CtrlC = 0x01,
    CriticalError = 0x02,
    TerminateAndStayResident = 0x03
}
