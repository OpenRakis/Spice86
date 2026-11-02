namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.ReverseEngineer;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// The <c>InstructionExecutionHelper</c> / <c>CSharpOverrideHelper</c> property path and method name
/// each typed helper call node maps to. The only place the helper names exist as a mapping; every
/// reader of the call nodes switches on the enum fields instead.
/// </summary>
internal static class HelperCallNames {
    /// <summary>The <c>Alu8</c> / <c>Alu16</c> / <c>Alu32</c> property for <paramref name="width"/>.</summary>
    public static string AluPropertyPath(BitWidth width) => width switch {
        BitWidth.BYTE_8 => nameof(Spice86.Core.Emulator.CPU.Alu8),
        BitWidth.WORD_16 => nameof(Spice86.Core.Emulator.CPU.Alu16),
        BitWidth.DWORD_32 => nameof(Spice86.Core.Emulator.CPU.Alu32),
        _ => throw new ArgumentOutOfRangeException(nameof(width), width, "No ALU helper for this width.")
    };

    /// <summary>The ALU method name for <paramref name="operation"/>.</summary>
    public static string AluMethodName(AluOperation operation) => operation.ToString();

    /// <summary>The <c>Stack</c> method name for <paramref name="operation"/> at <paramref name="width"/>.</summary>
    public static string StackMethodName(StackOperation operation, BitWidth width) =>
        $"{operation}{(int)width}";

    /// <summary>The port method name for <paramref name="direction"/> at <paramref name="width"/>.</summary>
    public static string PortMethodName(PortDirection direction, BitWidth width) =>
        $"{direction}{(int)width}";

    /// <summary>The property path of a stack call.</summary>
    public static string StackPropertyPath => nameof(Spice86.Core.Emulator.CPU.Stack);

    /// <summary>The <c>CheckBound</c> method name.</summary>
    public static string BoundCheckMethodName => nameof(InstructionExecutionHelper.CheckBound);

    /// <summary>The <c>BitScanForwardN</c> / <c>BitScanReverseN</c> method name for
    /// <paramref name="direction"/> at <paramref name="width"/>.</summary>
    public static string BitScanMethodName(BitScanDirection direction, BitWidth width) =>
        $"BitScan{direction}{(int)width}";

    /// <summary>The <c>NearCall</c> / <c>FarCall</c> method name for <paramref name="distance"/> at
    /// <paramref name="width"/>: the 16-bit helpers carry no width suffix.</summary>
    public static string ProcedureCallMethodName(CallDistance distance, BitWidth width) =>
        (distance, width) switch {
            (CallDistance.Near, BitWidth.WORD_16) => nameof(CSharpOverrideHelper.NearCall),
            (CallDistance.Near, BitWidth.DWORD_32) => nameof(CSharpOverrideHelper.NearCall32),
            (CallDistance.Far, BitWidth.WORD_16) => nameof(CSharpOverrideHelper.FarCall),
            (CallDistance.Far, BitWidth.DWORD_32) => nameof(CSharpOverrideHelper.FarCall32),
            _ => throw new ArgumentOutOfRangeException(nameof(width), width, "No call helper for this width.")
        };

    /// <summary>The <c>NearCallReturnedElsewhere</c> / <c>FarCallReturnedElsewhere</c> method name for
    /// <paramref name="distance"/> at <paramref name="width"/>: the 16-bit helpers carry no width suffix.</summary>
    public static string ProcedureCallReturnedElsewhereMethodName(CallDistance distance, BitWidth width) =>
        (distance, width) switch {
            (CallDistance.Near, BitWidth.WORD_16) => nameof(CSharpOverrideHelper.NearCallReturnedElsewhere),
            (CallDistance.Near, BitWidth.DWORD_32) => nameof(CSharpOverrideHelper.NearCall32ReturnedElsewhere),
            (CallDistance.Far, BitWidth.WORD_16) => nameof(CSharpOverrideHelper.FarCallReturnedElsewhere),
            (CallDistance.Far, BitWidth.DWORD_32) => nameof(CSharpOverrideHelper.FarCall32ReturnedElsewhere),
            _ => throw new ArgumentOutOfRangeException(nameof(width), width, "No call helper for this width.")
        };

    /// <summary>The <c>NearRet</c> / <c>FarRet</c> method name for <paramref name="distance"/> at
    /// <paramref name="width"/>: the 16-bit helpers carry no width suffix.</summary>
    public static string ProcedureReturnMethodName(CallDistance distance, BitWidth width) =>
        (distance, width) switch {
            (CallDistance.Near, BitWidth.WORD_16) => nameof(CSharpOverrideHelper.NearRet),
            (CallDistance.Near, BitWidth.DWORD_32) => nameof(CSharpOverrideHelper.NearRet32),
            (CallDistance.Far, BitWidth.WORD_16) => nameof(CSharpOverrideHelper.FarRet),
            (CallDistance.Far, BitWidth.DWORD_32) => nameof(CSharpOverrideHelper.FarRet32),
            _ => throw new ArgumentOutOfRangeException(nameof(width), width, "No return helper for this width.")
        };

    /// <summary>The <c>Hlt</c> method name.</summary>
    public static string HltMethodName => nameof(CSharpOverrideHelper.Hlt);

    /// <summary>The <c>InterruptCall</c> method name.</summary>
    public static string InterruptCallMethodName => nameof(CSharpOverrideHelper.InterruptCall);

    /// <summary>The <c>InterruptCallReturnedElsewhere</c> method name.</summary>
    public static string InterruptCallReturnedElsewhereMethodName => nameof(CSharpOverrideHelper.InterruptCallReturnedElsewhere);

    /// <summary>The <c>Callback</c> method name.</summary>
    public static string CallbackMethodName => nameof(CSharpOverrideHelper.Callback);

    /// <summary>The <c>CallbackMovedCsIp</c> method name.</summary>
    public static string CallbackMovedCsIpMethodName => nameof(CSharpOverrideHelper.CallbackMovedCsIp);

    /// <summary>The <c>SelectorSignatureMatches</c> method name.</summary>
    public static string SignatureMatchesMethodName => nameof(CSharpOverrideHelper.SelectorSignatureMatches);
}
