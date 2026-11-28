namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

/// <summary>
/// A transfer out of the method that never completes: a <c>ReturnNode</c>, <c>FailAsUntestedNode</c>,
/// <c>ThrowNode</c> or <c>CyclicJumpNode</c>, or a sequence ending with one of them. A
/// <c>FailAsUntestedNode</c> aborts the program, so nothing after it observes machine state: it uses
/// only its message parts instead of everything.
/// </summary>
internal sealed record IrExit : IrTerminator {
    private readonly IrEffects? _effects;

    /// <summary>Creates an exit terminator for <paramref name="transfer"/> with an explicit effect,
    /// or <see langword="null"/> to compute it from the transfer.</summary>
    public IrExit(ICfgNode origin, IVisitableAstNode transfer, IrEffects? effects = null) : base(origin) {
        Transfer = transfer;
        _effects = effects;
    }

    /// <summary>The transfer out of the method.</summary>
    public IVisitableAstNode Transfer { get; }

    /// <inheritdoc />
    public override IrEffects Effects => _effects ?? (Transfer is FailAsUntestedNode or InterruptReturnNode
        ? IrEffectsVisitor.Of(Transfer, false)
        : IrEffects.ExitUse);

    /// <inheritdoc />
    public override IReadOnlyList<IrEdge> Edges => [];
}
