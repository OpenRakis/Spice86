namespace Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// DOS process termination types returned by INT 21h AH=4Dh (Get Return Code of Child Process).
/// </summary>
/// <remarks>
/// <para>
/// The termination type is returned in AH by INT 21h AH=4Dh and indicates how the child
/// process terminated. The return code (exit code/ERRORLEVEL) is returned in AL.
/// </para>
/// <para>
/// Based on RBIL documentation for INT 21h/AH=4Dh.
/// </para>
/// </remarks>
public enum DosTerminationType : byte {
    /// <summary>
    /// Normal termination (via INT 21h AH=4Ch, INT 21h AH=00h, or INT 20h).
    /// </summary>
    Normal = 0x00,

    /// <summary>
    /// Terminated by Ctrl-C (via INT 23h).
    /// </summary>
    CtrlC = 0x01,

    /// <summary>
    /// Terminated due to critical error (via INT 24h abort response).
    /// The exit code in AL is undefined.
    /// </summary>
    CriticalError = 0x02,

    /// <summary>
    /// Terminated and stayed resident (via INT 21h AH=31h or INT 27h).
    /// The exit code in AL is the return code passed to the TSR function.
    /// </summary>
    /// <remarks>
    /// TSR (Terminate and Stay Resident) programs remain in memory after termination.
    /// The memory allocated for them is not freed.
    /// </remarks>
    TSR = 0x03
}
