namespace Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// Represents the different ways a DOS process can terminate.
/// Used by INT 21h function 4Dh (Get Return Code of Child Process).
/// </summary>
public enum DosTerminationMode : byte {
    /// <summary>
    /// Child terminated normally (that is, exited via INT 20h or INT 21h Function 00h or Function 4Ch).
    /// </summary>
    Normal = 0x00,

    /// <summary>
    /// Child was terminated by user's entry of a Ctrl-C.
    /// </summary>
    ControlC = 0x01,

    /// <summary>
    /// Child was terminated by critical error handler (either the user responded with A to the
    /// Abort, Retry, Ignore prompt from the system's default INT 24h handler, or a custom
    /// INT 24h handler returned to MS-DOS with action code = 02h in register AL).
    /// </summary>
    CriticalError = 0x02,

    /// <summary>
    /// Child terminated normally and stayed resident (that is, exited via INT 21h Function 31h or INT 27h).
    /// </summary>
    TerminateAndStayResident = 0x03
}