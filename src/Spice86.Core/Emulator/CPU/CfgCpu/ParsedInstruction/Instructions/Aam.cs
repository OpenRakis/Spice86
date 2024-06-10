namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Shared.Emulator.Memory;

public class Aam : InstructionWithValueField<byte> {
    public Aam(SegmentedAddress address, InstructionField<byte> opcodeField, InstructionField<byte> valueField) :
        base(address, opcodeField, new List<InstructionPrefix>(), valueField) {
    }

    public override void Execute(InstructionExecutionHelper helper) {
        byte v2 = helper.InstructionFieldValueRetriever.GetFieldValue(ValueField);
        byte v1 = helper.State.AL;
        if (v2 == 0) {
            throw new CpuDivisionErrorException("Division by zero");
        }

        byte result = (byte)(v1 % v2);
        helper.State.AH = (byte)(v1 / v2);
        helper.State.AL = result;
        helper.Alu8.UpdateFlags(result);
        helper.MoveIpAndSetNextNode(this);
    }
}