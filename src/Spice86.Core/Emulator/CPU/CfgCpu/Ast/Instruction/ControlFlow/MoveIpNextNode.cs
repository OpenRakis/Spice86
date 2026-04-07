namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;

public record MoveIpNextNode(IVisitableAstNode NextIp) : IVisitableAstNode {
    public T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitMoveIpNextNode(this);
    }
}