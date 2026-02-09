namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

public class Cbw32 : CfgInstruction {
    public Cbw32(SegmentedAddress address, InstructionField<ushort> opcodeField, List<InstructionPrefix> prefixes) :
        base(address, opcodeField, prefixes, 1) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        // CBW, Convert word to dword
        int shortValue = (short)helper.State.AX;
        helper.State.EAX = (uint)shortValue;
        helper.MoveIpToEndOfInstruction(this);
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.CBWE);
    }

    public override IVisitableAstNode GenerateExecutionAst(AstBuilder builder) {
        // EAX = SignExtendToUnsigned(AX, 16, 32)
        ValueNode eaxNode = builder.Register.Reg32(RegisterIndex.AxIndex);
        ValueNode axNode = builder.Register.Reg16(RegisterIndex.AxIndex);
        ValueNode signExtended = builder.SignExtendToUnsigned(axNode, 16, 32);
        return builder.WithIpAdvancement(this, builder.Assign(DataType.UINT32, eaxNode, signExtended));
    }
}