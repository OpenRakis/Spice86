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

        // Check if the callback changed CS:IP to a different address.
        // If CS:IP is still at the callback instruction, the callback returned normally
        // and we need to advance past it. But if CS:IP was changed (e.g., EXEC loading
        // a child program, or QuitWithExitCode returning to parent), we should NOT
        // add the instruction length - the callback has set up the correct target.
        if (helper.State.IpSegmentedAddress != Address) {
            // Callback changed CS:IP - it wants to jump somewhere else (e.g., EXEC or terminate)
            // Just set next node based on current CS:IP without adjustment
            helper.SetNextNodeToSuccessorAtCsIp(this);
        } else {
            // Normal case - callback returned, advance IP past this instruction
            helper.MoveIpAndSetNextNode(this);
        }
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.CALLBACK);
    }
}