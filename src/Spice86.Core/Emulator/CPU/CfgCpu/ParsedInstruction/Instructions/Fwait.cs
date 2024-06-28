namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Do nothing, this is to wait for the FPU which is not implemented
/// </summary>
public class Fwait : Nop {

    public Fwait(SegmentedAddress address, InstructionField<ushort> opcodeField) : base(address, opcodeField) {
    }
}