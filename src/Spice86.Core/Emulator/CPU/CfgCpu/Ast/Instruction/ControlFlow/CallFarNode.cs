namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

public class CallFarNode : CfgInstructionNode {
    public CallFarNode(CfgInstruction instruction, SegmentedAddressNode targetAddress, int callSize) : base(instruction) {
        TargetAddress = targetAddress;
        CallSize = callSize;
    }

    public SegmentedAddressNode TargetAddress { get; }
    public int CallSize { get; }

    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitCallFarNode(this);
    }
}