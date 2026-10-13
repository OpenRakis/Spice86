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
/// Group 0F 00: SLDT, STR, LLDT, LTR, VERR, VERW. The ModRM reg field selects the sub-operation; the
/// r/m field is a 16-bit selector, in a register or in memory.
/// </summary>
public class SystemSegmentParser : BaseGrpOperationParser {
    public SystemSegmentParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    protected override CfgInstruction Parse(ParsingContext context, ModRmContext modRmContext, int groupIndex) {
        return groupIndex switch {
            0 => BuildStore(context, modRmContext, nameof(InstructionExecutionHelper.StoreLdtr), InstructionOperation.SLDT),
            1 => BuildStore(context, modRmContext, nameof(InstructionExecutionHelper.StoreTr), InstructionOperation.STR),
            2 => BuildLoad(context, modRmContext, nameof(InstructionExecutionHelper.LoadLdtr), InstructionOperation.LLDT),
            3 => BuildLoad(context, modRmContext, nameof(InstructionExecutionHelper.LoadTr), InstructionOperation.LTR),
            4 => BuildVerify(context, modRmContext, nameof(InstructionExecutionHelper.VerifyReadable), InstructionOperation.VERR),
            5 => BuildVerify(context, modRmContext, nameof(InstructionExecutionHelper.VerifyWritable), InstructionOperation.VERW),
            _ => throw new CpuInvalidOpcodeException($"Group 0F 00 /{groupIndex} is not supported")
        };
    }

    private CfgInstruction BuildStore(ParsingContext context, ModRmContext modRmContext, string methodName, InstructionOperation displayOp) {
        CfgInstruction instr = new(_idAllocator.AllocateId(), context.Address, context.OpcodeField, context.Prefixes, 1);
        RegisterModRmFields(instr, modRmContext);
        ValueNode destNode = _astBuilder.ModRm.RmToNode(DataType.UINT16, modRmContext);
        MethodCallValueNode storeCall = new MethodCallValueNode(DataType.UINT16, null, methodName);
        InstructionNode displayAst = new InstructionNode(displayOp, destNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, _astBuilder.Assign(DataType.UINT16, destNode, storeCall));
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    private CfgInstruction BuildLoad(ParsingContext context, ModRmContext modRmContext, string methodName, InstructionOperation displayOp) {
        CfgInstruction instr = new(_idAllocator.AllocateId(), context.Address, context.OpcodeField, context.Prefixes, 1);
        RegisterModRmFields(instr, modRmContext);
        ValueNode sourceNode = _astBuilder.ModRm.RmToNode(DataType.UINT16, modRmContext);
        MethodCallNode loadCall = new MethodCallNode(null, methodName, sourceNode);
        InstructionNode displayAst = new InstructionNode(displayOp, sourceNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, loadCall);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }

    private CfgInstruction BuildVerify(ParsingContext context, ModRmContext modRmContext, string methodName, InstructionOperation displayOp) {
        CfgInstruction instr = new(_idAllocator.AllocateId(), context.Address, context.OpcodeField, context.Prefixes, 1);
        RegisterModRmFields(instr, modRmContext);
        ValueNode selectorNode = _astBuilder.ModRm.RmToNode(DataType.UINT16, modRmContext);
        MethodCallValueNode verifyCall = new MethodCallValueNode(DataType.BOOL, null, methodName, selectorNode);
        InstructionNode displayAst = new InstructionNode(displayOp, selectorNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr,
            _astBuilder.Assign(DataType.BOOL, _astBuilder.Flag.Zero(), verifyCall));
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
