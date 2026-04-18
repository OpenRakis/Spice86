namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

public class JumpFarNode : CfgInstructionNode {
    public JumpFarNode(CfgInstruction instruction, SegmentedAddressNode targetAddress) :
        base(instruction) {
        TargetAddress = targetAddress;
    }

    public SegmentedAddressNode TargetAddress { get; }

    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitJumpFarNode(this);
    }
}