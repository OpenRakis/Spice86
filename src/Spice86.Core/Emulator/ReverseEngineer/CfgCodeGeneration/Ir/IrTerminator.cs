namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

/// <summary>The last thing of an <see cref="IrBlock"/>: a jump, a branch, a switch or a transfer out of the method.</summary>
/// <param name="Origin">The instruction the terminator was lifted from.</param>
internal abstract record IrTerminator(ICfgNode Origin) {
    /// <summary>The terminator effects.</summary>
    public abstract IrEffects Effects { get; }

    /// <summary>The outgoing edges, in order.</summary>
    public abstract IReadOnlyList<IrEdge> Edges { get; }
}
