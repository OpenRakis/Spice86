﻿namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Shared.Emulator.Memory;

public class Nop : CfgInstruction {
    public Nop(SegmentedAddress address, InstructionField<ushort> opcodeField) : base(address, opcodeField) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        // Well nothing to do :)
        helper.MoveIpAndSetNextNode(this);
    }

    public override string ToAssemblyString(InstructionRendererHelper helper) {
        return helper.ToAssemblyString("nop");
    }
}