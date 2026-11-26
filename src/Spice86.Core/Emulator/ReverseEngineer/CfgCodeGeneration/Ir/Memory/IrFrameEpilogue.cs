namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;

/// <summary>How one return exit of a framed method tears the frame down.</summary>
/// <param name="Exit">The return terminator.</param>
/// <param name="Kind">The recognized teardown shape.</param>
/// <param name="Restore">The frame restore statement of a pop epilogue (<c>pop bp</c>, or the
/// bare pop the dead-write removal left), null for the other kinds.</param>
internal sealed record IrFrameEpilogue(IrExit Exit, IrFrameEpilogueKind Kind,
    IrStatement? Restore);
