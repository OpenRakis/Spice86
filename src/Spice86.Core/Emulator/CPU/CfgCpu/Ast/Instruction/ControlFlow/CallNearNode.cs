namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

public class CallNearNode : CfgInstructionNode {
    public CallNearNode(CfgInstruction instruction, IVisitableAstNode targetIp, int callSize) : base(instruction) {
        TargetIp = targetIp;
        CallSize = callSize;
    }

    public IVisitableAstNode TargetIp { get; }
    public int CallSize { get; }

    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitCallNearNode(this);
    }
}