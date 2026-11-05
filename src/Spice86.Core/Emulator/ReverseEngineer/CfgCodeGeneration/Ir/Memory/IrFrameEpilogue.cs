namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir.Memory;

/// <summary>How one return exit of a framed method tears the frame down.</summary>
/// <param name="Exit">The return terminator.</param>
/// <param name="Kind">The recognized teardown shape.</param>
internal sealed record IrFrameEpilogue(IrExit Exit, IrFrameEpilogueKind Kind);
