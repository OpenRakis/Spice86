namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Shared.Emulator.Memory;

public class Cpuid(SegmentedAddress address, InstructionField<ushort> opcodeField, List<InstructionPrefix> prefixes)
    : CfgInstruction(address, opcodeField, prefixes, 1) {
    public override void Execute(InstructionExecutionHelper helper) {
        throw new CpuInvalidOpcodeException("Attempted to call CPUID, which is unsupported on CPUs < 486");
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.CPUID);
    }
}