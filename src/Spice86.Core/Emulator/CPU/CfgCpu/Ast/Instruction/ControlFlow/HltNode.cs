namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

public class HltNode : CfgInstructionNode {
    public HltNode(CfgInstruction instruction) : base(instruction) {
    }

    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitHltNode(this);
    }
}