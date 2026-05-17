namespace Spice86.Core.Emulator.StateSerialization.ControlFlow;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

/// <summary>
/// Represents one exported CFG block without serialization or rendering concerns.
/// </summary>
public sealed record CfgBlockGraphNode {
    public required CfgBlock Block { get; init; }
    public required bool IsExecutingBlock { get; init; }
}
