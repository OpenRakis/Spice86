namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;

public class MoveIpNextNode : IVisitableAstNode {
    public MoveIpNextNode(IVisitableAstNode nextIp) {
        NextIp = nextIp;
    }

    public IVisitableAstNode NextIp { get; }

    public T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitMoveIpNextNode(this);
    }
}