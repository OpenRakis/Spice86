namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

/// <summary>INS (input from DX to [ES:DI]) or OUTS (output [DS:SI] to DX)</summary>
public class IoStringParser : BaseInstructionParser {
    public IoStringParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    /// <summary>
    /// Parses an I/O string operation instruction.
    /// </summary>
    public CfgInstruction Parse(ParsingContext context, bool isInput) {
        BitWidth bitWidth = GetBitWidth(context.OpcodeField, context.HasOperandSize32);
        DataType dataType = _astBuilder.UType(bitWidth);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        DataType addressType = _astBuilder.AddressType(instr);
        ValueNode dx = _astBuilder.Register.Reg16(RegisterIndex.DxIndex);
        BlockNode coreOperation;
        RepPrefix? repPrefix = _astBuilder.Rep(instr.RepPrefix, false);
        InstructionNode displayAst;
        if (isInput) {
            MethodCallValueNode ioRead = new MethodCallValueNode(dataType, null, $"In{(int)bitWidth}", dx);
            ValueNode destPointer = _astBuilder.StringOperation.DestPointerDi(dataType, addressType);
            BinaryOperationNode storeOperation = _astBuilder.Assign(dataType, destPointer, ioRead);
            BinaryOperationNode advanceDi = _astBuilder.StringOperation.AdvanceDi(addressType, (int)bitWidth);
            coreOperation = new BlockNode(storeOperation, advanceDi);
            displayAst = new InstructionNode(repPrefix, InstructionOperation.INS,
                _astBuilder.Pointer.ToSegmentedPointer(dataType, SegmentRegisterIndex.EsIndex,
                    _astBuilder.Register.Reg(addressType, RegisterIndex.DiIndex)));
        } else {
            int segmentRegisterIndex = GetSegmentRegisterOverrideOrDs(context);
            ValueNode sourcePointer = _astBuilder.StringOperation.SourcePointerSi(dataType, addressType,
                segmentRegisterIndex, (int)SegmentRegisterIndex.DsIndex);
            MethodCallNode ioWrite = new MethodCallNode(null, $"Out{(int)bitWidth}", dx, sourcePointer);
            BinaryOperationNode advanceSi = _astBuilder.StringOperation.AdvanceSi(addressType, (int)bitWidth);
            coreOperation = new BlockNode(ioWrite, advanceSi);
            displayAst = new InstructionNode(repPrefix, InstructionOperation.OUTS,
                _astBuilder.Pointer.ToSegmentedPointer(dataType, segmentRegisterIndex, (int)SegmentRegisterIndex.DsIndex,
                    _astBuilder.Register.Reg(addressType, RegisterIndex.SiIndex)));
        }
        IVisitableAstNode execAst = _astBuilder.StringOperation.GenerateExecutionAst(instr, false, coreOperation, _astBuilder);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
