namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

public class JumpFarNode : CfgInstructionNode {
    public JumpFarNode(CfgInstruction instruction, IVisitableAstNode segment, IVisitableAstNode offset) :
        base(instruction) {
        Segment = segment;
        Offset = offset;
    }

    public IVisitableAstNode Segment { get; }
    public IVisitableAstNode Offset { get; }

    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitJumpFarNode(this);
    }
}