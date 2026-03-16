namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

public class InterruptCallNode : CfgInstructionNode {
    public InterruptCallNode(CfgInstruction instruction, IVisitableAstNode vectorNumber) : base(instruction) {
        VectorNumber = vectorNumber;
    }

    public IVisitableAstNode VectorNumber { get; }

    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitInterruptCallNode(this);
    }
}