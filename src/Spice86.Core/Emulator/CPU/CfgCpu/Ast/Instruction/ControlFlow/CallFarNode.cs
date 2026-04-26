namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

public class CallFarNode : CfgInstructionNode {
    public CallFarNode(CfgInstruction instruction, SegmentedAddressNode targetAddress, BitWidth callBitWidth) : base(instruction) {
        TargetAddress = targetAddress;
        CallBitWidth = callBitWidth;
    }

    public SegmentedAddressNode TargetAddress { get; }
    public BitWidth CallBitWidth { get; }

    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitCallFarNode(this);
    }
}