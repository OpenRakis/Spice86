namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

public class JumpNearNode : CfgInstructionNode {
    public JumpNearNode(CfgInstruction instruction, IVisitableAstNode ip) : base(instruction) {
        Ip = ip;
    }

    public IVisitableAstNode Ip { get; }

    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitJumpNearNode(this);
    }
}