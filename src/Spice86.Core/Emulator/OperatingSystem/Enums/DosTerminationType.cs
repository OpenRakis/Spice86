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
/// <para>
/// <strong>MCB Note:</strong> In FreeDOS kernel, process termination behavior differs slightly
/// from MS-DOS in error handling scenarios. See FreeDOS kernel/task.c and kernel/int2f.c
/// for reference implementation details.
/// </para>
/// </remarks>
public enum DosTerminationType : byte {
    /// <summary>
    /// Normal termination (via INT 21h AH=4Ch, INT 21h AH=00h, or INT 20h).
    /// The exit code in AL is meaningful and was set by the program.
    /// </summary>
    Normal = 0x00,

    /// <summary>
    /// Terminated by Ctrl-C (via INT 23h).
    /// The exit code in AL is undefined.
    /// </summary>
    CtrlC = 0x01,

    /// <summary>
    /// Terminated due to critical error (via INT 24h abort response).
    /// The exit code in AL is undefined.
    /// </summary>
    /// <remarks>
    /// <strong>MCB Note:</strong> FreeDOS and MS-DOS handle INT 24h abort differently
    /// for self-parented processes (like COMMAND.COM). In FreeDOS, aborting a 
    /// self-parented process terminates it normally, while MS-DOS may behave differently.
    /// See https://github.com/FDOS/kernel/issues/213 for details.
    /// </remarks>
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
