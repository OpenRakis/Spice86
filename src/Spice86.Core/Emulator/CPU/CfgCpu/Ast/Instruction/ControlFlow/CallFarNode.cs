namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

public class CallFarNode : CfgInstructionNode {
    public CallFarNode(CfgInstruction instruction, IVisitableAstNode targetSegment, IVisitableAstNode targetOffset,
        int callSize) : base(instruction) {
        TargetSegment = targetSegment;
        TargetOffset = targetOffset;
        CallSize = callSize;
    }

    public IVisitableAstNode TargetSegment { get; }
    public IVisitableAstNode TargetOffset { get; }
    public int CallSize { get; }

    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitCallFarNode(this);
    }
}