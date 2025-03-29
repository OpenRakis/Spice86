﻿namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionRenderer;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.Interfaces;
using Spice86.Shared.Emulator.Memory;

public class NearRetImm : InstructionWithValueField<ushort>, IReturnInstruction {

    public NearRetImm(SegmentedAddress address, InstructionField<ushort> opcodeField, InstructionField<ushort> valueField) : base(address, opcodeField, valueField) {
    }

    public CfgInstruction? CurrentCorrespondingCallInstruction { get; set; }

    public override void Execute(InstructionExecutionHelper helper) {
        helper.HandleNearRet(this, helper.InstructionFieldValueRetriever.GetFieldValue(ValueField));
    }
    
    public override string ToAssemblyString(InstructionRendererHelper helper) {
        return helper.ToAssemblyString("ret near", helper.ToString(ValueField));
    }
}