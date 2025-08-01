namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Shared.Emulator.Memory;

public class InvalidInstruction : CfgInstruction {
    private readonly CpuException _cpuException;
    public InvalidInstruction(SegmentedAddress address, InstructionField<ushort> opcodeField,
        List<InstructionPrefix> prefixes, CpuException cpuException) : base(address, opcodeField, prefixes, null) {
        _cpuException = cpuException;
    }

    public override void Execute(InstructionExecutionHelper helper) {
        helper.HandleCpuException(this, _cpuException);
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.INVALID);
    }
}