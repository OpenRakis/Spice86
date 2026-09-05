namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;

/// <summary>ENTER</summary>
public class EnterParser : BaseInstructionParser {
    public EnterParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction Parse(ParsingContext context) {
        InstructionField<ushort> storageField = _instructionReader.UInt16.NextField(false);
        InstructionField<byte> levelField = _instructionReader.UInt8.NextField(false);
        CfgInstruction instr = new(_idAllocator.AllocateId(), context.Address, context.OpcodeField, context.Prefixes, 1);
        instr.AddField(storageField);
        instr.AddField(levelField);

        ValueNode storageNode = _astBuilder.InstructionField.ToNode(storageField);
        ValueNode levelNode = _astBuilder.InstructionField.ToNode(levelField);
        ValueNode operandSize32Node = _astBuilder.Constant.ToNode(DataType.BOOL, context.HasOperandSize32 ? 1UL : 0UL);

        // The frame-pointer register width and stack-pointer address width both depend on SS's D/B
        // bit, which - unlike CS's - can legitimately differ between calls to the same code address,
        // so Stack.Enter resolves them fresh every call instead of baking a choice in at parse time.
        MethodCallNode enterCall = new("Stack", nameof(Stack.Enter), storageNode, levelNode, operandSize32Node);

        InstructionNode displayAst = new InstructionNode(context.HasOperandSize32 ? InstructionOperation.ENTERW : InstructionOperation.ENTER, storageNode, levelNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, enterCall);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
