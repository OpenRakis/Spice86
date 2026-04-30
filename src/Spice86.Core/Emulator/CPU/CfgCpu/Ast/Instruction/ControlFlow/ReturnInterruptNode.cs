namespace Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;

using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

public class ReturnInterruptNode : CfgInstructionNode {
    public ReturnInterruptNode(CfgInstruction instruction, BitWidth operandSize) : base(instruction) {
        OperandSize = operandSize;
    }

    public BitWidth OperandSize { get; }

    public override T Accept<T>(IAstVisitor<T> astVisitor) {
        return astVisitor.VisitReturnInterruptNode(this);
    }
}