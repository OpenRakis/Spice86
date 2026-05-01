namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

/// <summary>XLAT</summary>
public class XlatParser : BaseInstructionParser {
    public XlatParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction Parse(ParsingContext context) {
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        int segRegIndex = GetSegmentRegisterOverrideOrDs(context);
        int defaultSegRegIndex = (int)SegmentRegisterIndex.DsIndex;
        DataType addrType = _astBuilder.AddressType(instr);
        ValueNode bxNode = _astBuilder.Register.Reg(addrType, RegisterIndex.BxIndex);
        ValueNode alNode = _astBuilder.Register.Accumulator(DataType.UINT8);
        BinaryOperationNode displayOffset = new BinaryOperationNode(addrType, bxNode, BinaryOperation.PLUS, alNode);
        ValueNode displayPointer = _astBuilder.Pointer.ToSegmentedPointer(DataType.UINT8, segRegIndex, defaultSegRegIndex, displayOffset);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.XLAT, displayPointer);
        ValueNode alWidened = _astBuilder.TypeConversion.Convert(addrType, alNode);
        BinaryOperationNode execOffset = new BinaryOperationNode(addrType, bxNode, BinaryOperation.PLUS, alWidened);
        ValueNode memPointer = _astBuilder.Pointer.ToSegmentedPointer(DataType.UINT8, segRegIndex, defaultSegRegIndex, execOffset);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, _astBuilder.Assign(DataType.UINT8, alNode, memPointer));
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
