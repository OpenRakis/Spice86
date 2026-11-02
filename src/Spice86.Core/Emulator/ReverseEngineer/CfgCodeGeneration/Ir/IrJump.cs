namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

/// <summary>An unconditional jump to another block.</summary>
internal sealed record IrJump(ICfgNode Origin, IrEdge Edge) : IrTerminator(Origin) {
    /// <inheritdoc />
    public override IrEffects Effects => IrEffects.None;

    /// <inheritdoc />
    public override IReadOnlyList<IrEdge> Edges => [Edge];
}
