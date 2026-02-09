namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

public class ReturnInterruptNode : CfgInstructionNode {
    public ReturnInterruptNode(CfgInstruction instruction) : base(instruction) {
    }

    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitReturnInterruptNode(this);
    }
}