namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// Represents the result of a DOS EXEC (INT 21h, AH=4Bh) operation.
/// </summary>
/// <remarks>
/// Based on MS-DOS 4.0 EXEC.ASM error handling.
/// </remarks>
/// <param name="Success">Whether the EXEC operation succeeded.</param>
/// <param name="ErrorCode">The DOS error code if the operation failed.</param>
/// <param name="ChildPspSegment">The PSP segment of the loaded program (for LoadOnly mode).</param>
/// <param name="InitialCS">The initial CS value (for LoadOnly mode).</param>
/// <param name="InitialIP">The initial IP value (for LoadOnly mode).</param>
/// <param name="InitialSS">The initial SS value (for LoadOnly mode).</param>
/// <param name="InitialSP">The initial SP value (for LoadOnly mode).</param>
public record DosExecResult(
    bool Success,
    DosErrorCode ErrorCode,
    ushort ChildPspSegment,
    ushort InitialCS,
    ushort InitialIP,
    ushort InitialSS,
    ushort InitialSP) {

    /// <summary>
    /// Creates a successful EXEC result.
    /// </summary>
    public static DosExecResult Succeeded() => new(
        Success: true,
        ErrorCode: DosErrorCode.NoError,
        ChildPspSegment: 0,
        InitialCS: 0, InitialIP: 0, InitialSS: 0, InitialSP: 0);

    /// <summary>
    /// Creates a successful EXEC result with child process information (for LoadOnly).
    /// </summary>
    public static DosExecResult Succeeded(ushort childPspSegment, ushort cs, ushort ip, ushort ss, ushort sp) =>
        new(Success: true, ErrorCode: DosErrorCode.NoError, childPspSegment, cs, ip, ss, sp);

    /// <summary>
    /// Creates a failed EXEC result.
    /// </summary>
    public static DosExecResult Failed(DosErrorCode errorCode) => new(
        Success: false,
        ErrorCode: errorCode,
        ChildPspSegment: 0,
        InitialCS: 0, InitialIP: 0, InitialSS: 0, InitialSP: 0);
}
