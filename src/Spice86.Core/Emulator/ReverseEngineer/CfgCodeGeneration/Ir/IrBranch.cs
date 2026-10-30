namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

/// <summary>A conditional jump to one of two blocks.</summary>
internal sealed record IrBranch : IrTerminator {
    /// <summary>Creates a branch, computing the condition's effects.</summary>
    public IrBranch(ICfgNode origin, ValueNode condition, IrEdge whenTrue, IrEdge whenFalse) : base(origin) {
        Condition = condition;
        WhenTrue = whenTrue;
        WhenFalse = whenFalse;
        Effects = IrEffectsVisitor.Of(condition, false);
    }

    /// <summary>The condition choosing the edge.</summary>
    public ValueNode Condition { get; }

    /// <summary>The edge taken when the condition holds.</summary>
    public IrEdge WhenTrue { get; }

    /// <summary>The edge taken when the condition does not hold.</summary>
    public IrEdge WhenFalse { get; }

    /// <inheritdoc />
    public override IrEffects Effects { get; }

    /// <inheritdoc />
    public override IReadOnlyList<IrEdge> Edges => [WhenTrue, WhenFalse];
}
