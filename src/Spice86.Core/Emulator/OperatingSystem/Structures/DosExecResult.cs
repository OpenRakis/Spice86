namespace Spice86.Core.Emulator.OperatingSystem.Structures;

using Spice86.Core.Emulator.OperatingSystem.Enums;

/// <summary>
/// Result of an INT 21h EXEC operation.
/// </summary>
public sealed class DosExecResult : IEquatable<DosExecResult?> {
    // Cache the most common error codes into a fast lookup array.
    private const int ErrorCacheLength = 20;
    private static readonly DosExecResult?[] s_errorCache = new DosExecResult[ErrorCacheLength];

    private DosExecResult(bool success, DosErrorCode errorCode,
        ushort cs, ushort ip, ushort ss, ushort sp, ushort? ax = null, ushort? dx = null) {
        Success = success;
        ErrorCode = errorCode;
        InitialCS = cs;
        InitialIP = ip;
        InitialSS = ss;
        InitialSP = sp;
        AX = ax;
        DX = dx;
    }

    public bool Success { get; }
    public DosErrorCode ErrorCode { get; }
    public ushort InitialCS { get; }
    public ushort InitialIP { get; }
    public ushort InitialSS { get; }
    public ushort InitialSP { get; }
    /// <summary>
    /// Optional AX value to set after operation (used by LoadOverlay to return AX=0)
    /// </summary>
    public ushort? AX { get; }
    /// <summary>
    /// Optional DX value to set after operation (used by LoadOverlay to return DX=0)
    /// </summary>
    public ushort? DX { get; }

    public static DosExecResult Fail(DosErrorCode code) {
        // Use cache for common error codes.
        if ((int)code is >= 0 and < ErrorCacheLength) {
            ref DosExecResult? errorCacheEntry = ref s_errorCache[(uint)code];
            errorCacheEntry ??= NewErrorResult(code);
            return errorCacheEntry;
        }

        return NewErrorResult(code);

        static DosExecResult NewErrorResult(DosErrorCode errorCode)
            => new(success: false, errorCode: errorCode, cs: 0, ip: 0, ss: 0, sp: 0);
    }

    public static DosExecResult SuccessExecute(ushort cs, ushort ip, ushort ss, ushort sp)
        => new(success: true, DosErrorCode.NoError, cs, ip, ss, sp);

    public static DosExecResult SuccessLoadOnly(ushort cs, ushort ip, ushort ss, ushort sp)
        => new(success: true, DosErrorCode.NoError, cs, ip, ss, sp);
    
    public static DosExecResult SuccessLoadOverlay()
        => new(success: true, DosErrorCode.NoError, cs: 0, ip: 0, ss: 0, sp: 0, ax: 0, dx: 0);

    public override bool Equals(object? obj) {
        return Equals(obj as DosExecResult);
    }

    public bool Equals(DosExecResult? other) {
        return other is not null &&
               Success == other.Success &&
               ErrorCode == other.ErrorCode &&
               InitialCS == other.InitialCS &&
               InitialIP == other.InitialIP &&
               InitialSS == other.InitialSS &&
               InitialSP == other.InitialSP &&
               AX == other.AX &&
               DX == other.DX;
    }

    public override int GetHashCode() {
        return HashCode.Combine(Success, ErrorCode, InitialCS, InitialIP, InitialSS, InitialSP, AX, DX);
    }
}
