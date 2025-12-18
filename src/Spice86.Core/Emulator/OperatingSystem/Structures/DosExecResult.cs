namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// Represents the result of a DOS EXEC (INT 21h, AH=4Bh) operation.
/// </summary>
public record DosExecResult(
    bool Success,
    DosErrorCode ErrorCode,
    ushort ChildPspSegment,
    ushort InitialCS,
    ushort InitialIP,
    ushort InitialSS,
    ushort InitialSP) {

    public static DosExecResult Succeeded() => new(true, DosErrorCode.NoError, 0, 0, 0, 0, 0);

    public static DosExecResult Succeeded(ushort childPspSegment, ushort cs, ushort ip, ushort ss, ushort sp) =>
        new(true, DosErrorCode.NoError, childPspSegment, cs, ip, ss, sp);

    public static DosExecResult Failed(DosErrorCode errorCode) =>
        new(false, errorCode, 0, 0, 0, 0, 0);
}
