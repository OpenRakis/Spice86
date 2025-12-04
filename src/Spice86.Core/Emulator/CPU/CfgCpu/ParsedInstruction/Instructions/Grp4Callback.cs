namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class Grp4Callback : InstructionWithModRm {
    public Grp4Callback(SegmentedAddress address, InstructionField<ushort> opcodeField, List<InstructionPrefix> prefixes,
        ModRmContext modRmContext, InstructionField<ushort> callbackNumber) : base(address, opcodeField, prefixes,
        modRmContext, null) {
        CallbackNumber = callbackNumber;
        AddField(callbackNumber);
    }

    public InstructionField<ushort> CallbackNumber { get; }

    public override void Execute(InstructionExecutionHelper helper) {
        helper.ModRm.RefreshWithNewModRmContext(ModRmContext);

        helper.CallbackHandler.Run(helper.InstructionFieldValueRetriever.GetFieldValue(CallbackNumber));

        // Check if IP changed during callback execution, if so it means callback code did a jump.
        if (helper.State.IpSegmentedAddress != Address) {
            helper.SetNextNodeToSuccessorAtCsIp(this);
        } else {
            helper.MoveIpAndSetNextNode(this);
        }
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.CALLBACK);
    }
}