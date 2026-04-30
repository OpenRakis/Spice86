namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Shared.Emulator.Memory;

/// <summary>POP RM</summary>
public class PopRmParser : OperationModRmParser {
    public PopRmParser(ParsingTools parsingTools) : base(parsingTools, false) {
    }

    protected override void BuildAsts(CfgInstruction instr, DataType dataType, ModRmContext modRmContext) {
        if (modRmContext.RegisterIndex != 0) {
            throw new CpuInvalidOpcodeException(
                $"POP r/m with non-zero modrm reg field ({modRmContext.RegisterIndex}) is invalid");
        }
        ValueNode rmNode = _astBuilder.ModRm.RmToNode(dataType, modRmContext);
        ValueNode popValue = CreatePopValue(dataType);
        List<IVisitableAstNode> statements = new();
        if (rmNode is SegmentedPointerNode or AbsolutePointerNode) {
            VariableDeclarationNode stackCheck = _astBuilder.DeclareVariable(dataType, "popStackCheck", _astBuilder.Stack.Peek(dataType.BitWidth));
            statements.Add(stackCheck);
            ValueNode checkedDestination = ToDestinationCheckNode(dataType, rmNode, modRmContext);
            VariableDeclarationNode destinationCheck = _astBuilder.DeclareVariable(dataType, "popDestinationCheck", checkedDestination);
            statements.Add(destinationCheck);
        }
        VariableDeclarationNode poppedValue = _astBuilder.DeclareVariable(dataType, "poppedValue", popValue);
        statements.Add(poppedValue);
        statements.Add(_astBuilder.Assign(dataType, rmNode, poppedValue.Reference));
        InstructionNode displayAst = new InstructionNode(InstructionOperation.POP, rmNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, statements.ToArray());
        instr.AttachAsts(displayAst, execAst);
    }

    private ValueNode CreatePopValue(DataType dataType) {
        return _astBuilder.Stack.Pop(dataType.BitWidth);
    }

    private static bool UsesStackPointerInDestination(ModRmContext modRmContext) {
        return modRmContext.SibContext?.SibBase == SibBase.ESP;
    }

    private ValueNode ToDestinationCheckNode(DataType dataType, ValueNode rmNode, ModRmContext modRmContext) {
        if (!UsesStackPointerInDestination(modRmContext) || rmNode is not SegmentedPointerNode pointer) {
            return rmNode;
        }
        ValueNode popSize = _astBuilder.Constant.ToNode((ushort)dataType.BitWidth.ToBytes());
        return _astBuilder.Pointer.WithOffsetAdjustment(pointer, popSize);
    }
}
