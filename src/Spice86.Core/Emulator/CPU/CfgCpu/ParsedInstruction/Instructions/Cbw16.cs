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

public class Cbw16 : CfgInstruction {
    public Cbw16(SegmentedAddress address, InstructionField<ushort> opcodeField, List<InstructionPrefix> prefixes) :
        base(address, opcodeField, prefixes, 1) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        // CBW, Convert byte to word
        short shortValue = (sbyte)helper.State.AL;
        helper.State.AX = (ushort)shortValue;
        helper.MoveIpToEndOfInstruction(this);
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.CBW);
    }

    public override IVisitableAstNode GenerateExecutionAst(AstBuilder builder) {
        // AX = SignExtendToUnsigned(AL, 8, 16)
        ValueNode axNode = builder.Register.Reg16(RegisterIndex.AxIndex);
        ValueNode alNode = builder.Register.Reg8(RegisterIndex.AxIndex);
        ValueNode signExtended = builder.SignExtendToUnsigned(alNode, 8, 16);
        return builder.WithIpAdvancement(this, builder.Assign(DataType.UINT16, axNode, signExtended));
    }
}