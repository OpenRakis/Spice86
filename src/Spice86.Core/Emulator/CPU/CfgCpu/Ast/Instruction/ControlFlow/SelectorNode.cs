namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;

public class SelectorNode : IVisitableAstNode {
    public override string ToString() {
        return nameof(SelectorNode);
    }

    public T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitSelectorNode(this);
    }
}