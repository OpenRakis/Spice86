namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

public class CallNearNode : CfgInstructionNode {
    public CallNearNode(CfgInstruction instruction, IVisitableAstNode targetIp, BitWidth callBitWidth) : base(instruction) {
        TargetIp = targetIp;
        CallBitWidth = callBitWidth;
    }

    public IVisitableAstNode TargetIp { get; }
    public BitWidth CallBitWidth { get; }

    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitCallNearNode(this);
    }
}