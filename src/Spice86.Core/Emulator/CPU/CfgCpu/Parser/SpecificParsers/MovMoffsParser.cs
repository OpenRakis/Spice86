namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Shared.Emulator.Memory;

/// <summary>MOV ACC, [seg:offset] or MOV [seg:offset], ACC</summary>
public class MovMoffsParser : BaseInstructionParser {
    public MovMoffsParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction Parse(ParsingContext context, bool isLoad) {
        BitWidth bitWidth = GetBitWidth(context.OpcodeField, context.HasOperandSize32);
        DataType dataType = _astBuilder.UType(bitWidth);
        int segmentRegisterIndex = GetSegmentRegisterOverrideOrDs(context);
        InstructionField<ushort> offsetField = _instructionReader.UInt16.NextField(false);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        instr.AddField(offsetField);
        ValueNode accNode = _astBuilder.Register.Accumulator(dataType);
        ValueNode offsetNode = _astBuilder.InstructionField.ToNode(offsetField);
        ValueNode memoryPointer = _astBuilder.Pointer.ToSegmentedPointer(dataType, segmentRegisterIndex, offsetNode);
        ValueNode displayPointer = _astBuilder.Pointer.ToSegmentedPointer(dataType,
            segmentRegisterIndex, (int)Spice86.Core.Emulator.CPU.Registers.SegmentRegisterIndex.DsIndex,
            offsetNode);
        InstructionNode displayAst;
        IVisitableAstNode execAst;
        if (isLoad) {
            displayAst = new InstructionNode(InstructionOperation.MOV, accNode, displayPointer);
            execAst = _astBuilder.WithIpAdvancement(instr,
                _astBuilder.Assign(dataType, accNode, memoryPointer));
        } else {
            displayAst = new InstructionNode(InstructionOperation.MOV, displayPointer, accNode);
            execAst = _astBuilder.WithIpAdvancement(instr,
                _astBuilder.Assign(dataType, memoryPointer, accNode));
        }
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
