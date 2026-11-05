namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;

/// <summary>How a return exit of a framed method tears the frame down.</summary>
internal enum IrFrameEpilogueKind {
    /// <summary><c>mov sp,bp</c> (the read BP is the frame one) then <c>pop bp</c>.</summary>
    MoveSpPopBp,
    /// <summary>A <c>leave</c>.</summary>
    Leave,
    /// <summary>A bare <c>pop bp</c> while SP is still at frame delta -2 (frame without locals).</summary>
    PopBp,
    /// <summary>None of the recognized shapes.</summary>
    Other
}
