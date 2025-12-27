namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.OperatingSystem.Enums;
using Spice86.Shared.Emulator.Errors;

/// <summary>
/// Result of an INT 21h EXEC operation.
/// </summary>
public sealed class DosExecResult {
    private DosExecResult(bool success, DosErrorCode errorCode,
        ushort cs, ushort ip, ushort ss, ushort sp) {
        Success = success;
        ErrorCode = errorCode;
        InitialCS = cs;
        InitialIP = ip;
        InitialSS = ss;
        InitialSP = sp;
    }

    public bool Success { get; }
    public DosErrorCode ErrorCode { get; }
    public ushort InitialCS { get; }
    public ushort InitialIP { get; }
    public ushort InitialSS { get; }
    public ushort InitialSP { get; }

    public static DosExecResult Fail(DosErrorCode code) => new(false, code, 0, 0, 0, 0);

    public static DosExecResult SuccessExecute(ushort cs, ushort ip, ushort ss, ushort sp)
        => new(true, DosErrorCode.NoError, cs, ip, ss, sp);

    public static DosExecResult SuccessLoadOnly(ushort cs, ushort ip, ushort ss, ushort sp)
        => new(true, DosErrorCode.NoError, cs, ip, ss, sp);
}
