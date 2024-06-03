namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class Grp4Callback : InstructionWithModRm {
    public Grp4Callback(SegmentedAddress address, InstructionField<byte> opcodeField, List<InstructionPrefix> prefixes,
        ModRmContext modRmContext, InstructionField<byte> callbackNumber) : base(address, opcodeField, prefixes,
        modRmContext) {
        CallbackNumber = callbackNumber;
        FieldsInOrder.Add(callbackNumber);
    }

    public InstructionField<byte> CallbackNumber { get; }

    public override void Execute(InstructionExecutionHelper helper) {
        helper.ModRm.RefreshWithNewModRmContext(ModRmContext);
        helper.CallbackHandler.Run(helper.InstructionFieldValueRetriever.GetFieldValue(CallbackNumber));
        helper.MoveIpAndSetNextNode(this);
    }
}