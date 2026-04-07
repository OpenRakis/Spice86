namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;

public record SelectorNode : IVisitableAstNode {
    public override string ToString() => nameof(SelectorNode);

    public T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitSelectorNode(this);
    }
}