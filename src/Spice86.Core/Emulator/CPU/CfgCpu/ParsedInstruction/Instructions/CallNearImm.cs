namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions.CommonGrammar;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Prefix;
using Spice86.Shared.Emulator.Memory;

public class CallNearImm : InstructionWithOffsetField<short> {
    private readonly ushort _targetIp;
    public CallNearImm(SegmentedAddress address, InstructionField<ushort> opcodeField,
        List<InstructionPrefix> prefixes, InstructionField<short> offsetField) : base(address, opcodeField, prefixes,
        offsetField, null) {
        _targetIp = (ushort)(NextInMemoryAddress.Offset + offsetField.Value);
    }

    public override void Execute(InstructionExecutionHelper helper) {
        helper.NearCallWithReturnIpNextInstruction(this, _targetIp);
    }

    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.CALL_NEAR, builder.Constant.ToNode(_targetIp));
    }
}