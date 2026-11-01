namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.InterruptHandlers.Common.Contracts;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;

/// <summary>
/// One flat IR statement: the AST node, the instruction it came from and its effects, stored once.
/// </summary>
internal sealed class IrStatement {
    /// <summary>Creates a statement with its node, origin and effects.</summary>
    public IrStatement(IVisitableAstNode node, ICfgNode origin, IrEffects effects) {
        Node = node;
        Origin = origin;
        Effects = effects;
    }

    /// <summary>The statement node.</summary>
    public IVisitableAstNode Node { get; }

    /// <summary>The instruction the statement was lifted from.</summary>
    public ICfgNode Origin { get; }

    /// <summary>The statement effects.</summary>
    public IrEffects Effects { get; }

    /// <summary>Whether a statement nobody reads may be dropped: it writes no machine value
    /// (<see cref="CpuValues"/>), writes or reads no token (<see cref="IrTokens"/> - a memory read can
    /// have a device side effect, a VGA read loads the latches), cannot fault
    /// (<see cref="IrEffects.MayFault"/>) and delivers no interrupts
    /// (<see cref="IrEffects.DeliversInterrupts"/>).</summary>
    public bool IsRemovable => Effects.MayWrite == CpuValues.None
        && Effects.TokenWrites == IrTokens.None && !Effects.MayFault
        && !Effects.DeliversInterrupts && Effects.TokenReads == IrTokens.None;

    /// <summary>Creates a statement, computing its effects from <paramref name="node"/> and <paramref name="origin"/>.</summary>
    public static IrStatement Create(IVisitableAstNode node, ICfgNode origin) =>
        new(node, origin, IrEffectsVisitor.Of(node, MayFault(node, origin)));

    /// <summary>
    /// Whether <paramref name="node"/> can fault: its instruction has an observed CPU fault edge and the
    /// node belongs to the instruction body, not to its boundary (event checks, speculative guards and
    /// instruction-start comments are boundary nodes).
    /// </summary>
    public static bool MayFault(IVisitableAstNode node, ICfgNode origin) =>
        CpuFaultSuccessors.HasCpuFaultSuccessor(origin)
        && node is not CheckExternalEventsNode
        && node is not SpeculativeGuardNode
        && node is not InstructionStartNode
        && node is not CommentNode;
}
