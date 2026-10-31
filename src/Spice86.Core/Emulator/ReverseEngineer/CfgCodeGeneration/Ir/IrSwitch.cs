namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;

using System.Linq;

/// <summary>A multi-way branch on a value.</summary>
internal sealed record IrSwitch : IrTerminator {
    /// <summary>Creates a switch, computing the value's effects.</summary>
    public IrSwitch(ICfgNode origin, ValueNode value, IReadOnlyList<IrSwitchCase> cases, IrEdge defaultTarget)
        : base(origin) {
        Value = value;
        Cases = cases;
        Default = defaultTarget;
        Effects = IrEffectsVisitor.Of(value, false);
    }

    /// <summary>The value switched on.</summary>
    public ValueNode Value { get; }

    /// <summary>The labelled edges.</summary>
    public IReadOnlyList<IrSwitchCase> Cases { get; }

    /// <summary>The edge taken when no case matches.</summary>
    public IrEdge Default { get; }

    /// <inheritdoc />
    public override IrEffects Effects { get; }

    /// <inheritdoc />
    public override IReadOnlyList<IrEdge> Edges =>
        [.. Cases.Select(c => c.Target), Default];
}
