namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

/// <summary>PUSH/POP segment register</summary>
public class SegRegPushPopParser : BaseInstructionParser {
    public SegRegPushPopParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction ParsePushSReg(ParsingContext context, int segRegIndex) {
        CfgInstruction instr = new(_idAllocator.AllocateId(), context.Address, context.OpcodeField, context.Prefixes, 1);
        ValueNode regNode = _astBuilder.Register.SReg(segRegIndex);
        DataType pushType = context.HasOperandSize32 ? DataType.UINT32 : DataType.UINT16;
        ValueNode pushValue = _astBuilder.TypeConversion.Convert(pushType, regNode);
        MethodCallNode pushBlock = _astBuilder.Stack.Push(pushType, pushValue);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.PUSH, regNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, pushBlock);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    public CfgInstruction ParsePopSReg(ParsingContext context, int segRegIndex) {
        CfgInstruction instr = new(_idAllocator.AllocateId(), context.Address, context.OpcodeField, context.Prefixes, 1);
        ValueNode regNode = _astBuilder.Register.SReg(segRegIndex);
        ushort slotSize = context.HasOperandSize32 ? (ushort)4 : (ushort)2;
        ValueNode popValue = new MethodCallValueNode(DataType.UINT16, "Stack", nameof(Stack.PopSegmentSelector),
            _astBuilder.Constant.ToNode((uint)slotSize));
        ValueNode segIndexNode = _astBuilder.Constant.ToNode((uint)segRegIndex);
        MethodCallNode loadSegment = new MethodCallNode(null, nameof(InstructionExecutionHelper.LoadSegmentRegister), segIndexNode, popValue);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.POP, regNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, loadSegment);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
