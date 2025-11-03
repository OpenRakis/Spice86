namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Utils;

public class Cpuid(SegmentedAddress address, InstructionField<ushort> opcodeField, List<InstructionPrefix> prefixes)
    : CfgInstruction(address, opcodeField, prefixes, 1) {
    public override void Execute(InstructionExecutionHelper helper) {
        // 80386-class CPUs do not support CPUID; raise invalid opcode like the interpreter
        string message = $"opcode={ConvertUtils.ToHex(OpcodeField.Value)}";
        helper.HandleCpuException(this, new CpuInvalidOpcodeException(message));
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.CPUID);
    }
}