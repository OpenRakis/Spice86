namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

public class MovSregRm16 : InstructionWithModRm {
    public MovSregRm16(SegmentedAddress address, InstructionField<byte> opcodeField, List<InstructionPrefix> prefixes,
        ModRmContext modRmContext) : base(address, opcodeField, prefixes, modRmContext) {
        if(modRmContext.RegisterIndex == (uint)SegmentRegisterIndex.CsIndex) {
            throw new CpuInvalidOpcodeException("Attempted to write to CS register with MOV instruction");
        }
    }
    
    public override void Execute(InstructionExecutionHelper helper) {
        helper.ModRm.RefreshWithNewModRmContext(ModRmContext);
        helper.State.SegmentRegisters.UInt16[ModRmContext.RegisterIndex] = helper.ModRm.RM16;
        helper.MoveIpAndSetNextNode(this);
    }
}