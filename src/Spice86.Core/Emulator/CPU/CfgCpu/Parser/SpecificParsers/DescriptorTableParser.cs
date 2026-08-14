namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.Exceptions;

/// <summary>
/// Group 0F 01: LGDT, SGDT, LIDT, SIDT, SMSW, LMSW. The ModRM reg field selects the sub-operation.
/// LGDT/SGDT/LIDT/SIDT require a memory r/m operand (a 6-byte pointer: 2-byte limit + 4-byte base);
/// SMSW/LMSW accept a register or memory r/m16 operand.
/// </summary>
public class DescriptorTableParser : BaseGrpOperationParser {
    public DescriptorTableParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    protected override CfgInstruction Parse(ParsingContext context, ModRmContext modRmContext, int groupIndex) {
        return groupIndex switch {
            0 => Build(context, modRmContext, nameof(InstructionExecutionHelper.StoreGdtr), InstructionOperation.SGDT),
            1 => Build(context, modRmContext, nameof(InstructionExecutionHelper.StoreIdtr), InstructionOperation.SIDT),
            2 => Build(context, modRmContext, nameof(InstructionExecutionHelper.LoadGdtr), InstructionOperation.LGDT),
            3 => Build(context, modRmContext, nameof(InstructionExecutionHelper.LoadIdtr), InstructionOperation.LIDT),
            4 => BuildSmsw(context, modRmContext),
            6 => BuildLmsw(context, modRmContext),
            _ => throw new CpuInvalidOpcodeException($"Group 0F 01 /{groupIndex} is not supported")
        };
    }

    private CfgInstruction BuildSmsw(ParsingContext context, ModRmContext modRmContext) {
        CfgInstruction instr = new(_idAllocator.AllocateId(), context.Address, context.OpcodeField, context.Prefixes, 1);
        RegisterModRmFields(instr, modRmContext);
        ValueNode destNode = _astBuilder.ModRm.RmToNode(DataType.UINT16, modRmContext);
        MethodCallValueNode readMsw = new MethodCallValueNode(DataType.UINT16, null, nameof(InstructionExecutionHelper.ReadMachineStatusWord));
        InstructionNode displayAst = new InstructionNode(InstructionOperation.SMSW, destNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, _astBuilder.Assign(DataType.UINT16, destNode, readMsw));
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    private CfgInstruction BuildLmsw(ParsingContext context, ModRmContext modRmContext) {
        CfgInstruction instr = new(_idAllocator.AllocateId(), context.Address, context.OpcodeField, context.Prefixes, 1);
        RegisterModRmFields(instr, modRmContext);
        ValueNode sourceNode = _astBuilder.ModRm.RmToNode(DataType.UINT16, modRmContext);
        MethodCallNode loadMsw = new MethodCallNode(null, nameof(InstructionExecutionHelper.LoadMachineStatusWord), sourceNode);
        InstructionNode displayAst = new InstructionNode(InstructionOperation.LMSW, sourceNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, loadMsw);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    private CfgInstruction Build(ParsingContext context, ModRmContext modRmContext, string methodName, InstructionOperation displayOp) {
        _modRmParser.EnsureNotMode3(modRmContext);
        CfgInstruction instr = new(_idAllocator.AllocateId(), context.Address, context.OpcodeField, context.Prefixes, 1);
        RegisterModRmFields(instr, modRmContext);
        ValueNode segmentNode = new SegmentRegisterNode(modRmContext.SegmentIndex
            ?? throw new CpuInvalidOpcodeException("Memory operand is missing a segment index"));
        ValueNode offsetNode = _astBuilder.TypeConversion.Convert(DataType.UINT32, _astBuilder.ModRm.MemoryOffsetToNode(modRmContext));
        MethodCallNode call = new MethodCallNode(null, methodName, segmentNode, offsetNode);
        InstructionNode displayAst = new InstructionNode(displayOp, _astBuilder.ModRm.RmToNode(DataType.UINT32, modRmContext));
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, call);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
