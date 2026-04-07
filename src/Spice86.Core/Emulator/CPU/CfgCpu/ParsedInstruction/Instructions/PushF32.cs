namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class PushF32 : CfgInstruction {
    public PushF32(SegmentedAddress address, InstructionField<ushort> opcodeField, List<InstructionPrefix> prefixes) :
        base(address, opcodeField, prefixes, 1) {
    }
    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.PUSHFD);
    }

    protected override IVisitableAstNode BuildExecutionAst(AstBuilder builder) {
        // PUSHFD: Push 32-bit FLAGS onto stack (masked with 0x00FCFFFF)
        ValueNode flagsRegister = builder.Flag.FlagsRegister(DataType.UINT32);
        ValueNode maskedFlags = new BinaryOperationNode(builder.UType(32), flagsRegister, BinaryOperation.BITWISE_AND, builder.Constant.ToNode(0x00FCFFFFu));
        IVisitableAstNode pushNode = builder.Stack.Push32((ValueNode)maskedFlags);
        return builder.WithIpAdvancement(this, pushNode);
    }
}