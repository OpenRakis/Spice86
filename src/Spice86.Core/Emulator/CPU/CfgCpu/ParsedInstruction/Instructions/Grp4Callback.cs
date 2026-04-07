namespace Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.Instructions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
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
    public override InstructionNode ToInstructionAst(AstBuilder builder) {
        return new InstructionNode(InstructionOperation.CALLBACK, builder.InstructionField.ToNode(CallbackNumber)!);
    }

    protected override IVisitableAstNode BuildExecutionAst(AstBuilder builder) {
        return new CallbackNode(this, builder.Constant.ToNode(CallbackNumber.Value));
    }
}