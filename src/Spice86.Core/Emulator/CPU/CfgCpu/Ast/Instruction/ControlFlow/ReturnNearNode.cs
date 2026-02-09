namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

public class ReturnNearNode : CfgInstructionNode {
    public ReturnNearNode(CfgInstruction instruction, IVisitableAstNode bytesToPop, int retSize) : base(instruction) {
        BytesToPop = bytesToPop;
        RetSize = retSize;
    }

    public IVisitableAstNode BytesToPop { get; }
    public int RetSize { get; }

    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitReturnNearNode(this);
    }
}