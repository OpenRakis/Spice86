namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;

/// <summary>
/// Sets <c>State.InterruptShadowing</c> to true: interrupts are inhibited for one instruction cycle
/// after <c>sti</c>, <c>mov ss</c> or <c>lss</c>.
/// </summary>
public sealed record SetInterruptShadowNode : IVisitableAstNode {
    /// <inheritdoc />
    public T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitSetInterruptShadowNode(this);
    }
}
