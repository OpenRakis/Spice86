namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Shared.Emulator.Memory;

/// <summary>XCHG R, RM</summary>
public class XchgRmParser : OperationModRmParser {
    public XchgRmParser(ParsingTools parsingTools) : base(parsingTools, true) {
    }

    protected override void BuildAsts(CfgInstruction instr, DataType dataType, ModRmContext modRmContext) {
        ValueNode rNode = _astBuilder.ModRm.RToNode(dataType, modRmContext);
        List<IVisitableAstNode> nodes = new();
        ValueNode rmNode;
        if (modRmContext.MemoryAddressType != MemoryAddressType.NONE) {
            DataType addrType = _astBuilder.AddressType(instr);
            (VariableDeclarationNode cachedOffset, rmNode) =
                _astBuilder.ModRm.ToMemoryAddressNodeWithCachedOffset(dataType, addrType, modRmContext, "xchgOffset");
            nodes.Add(cachedOffset);
        } else {
            rmNode = _astBuilder.ModRm.RmToNode(dataType, modRmContext);
        }
        VariableDeclarationNode tempDecl = _astBuilder.DeclareVariable(dataType, "temp", rNode);
        nodes.Add(tempDecl);
        nodes.Add(_astBuilder.Assign(dataType, rNode, rmNode));
        nodes.Add(_astBuilder.Assign(dataType, rmNode, tempDecl.Reference));
        InstructionNode displayAst = new InstructionNode(InstructionOperation.XCHG,
            _astBuilder.ModRm.RToNode(dataType, modRmContext),
            _astBuilder.ModRm.RmToNode(dataType, modRmContext));
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, nodes.ToArray());
        instr.AttachAsts(displayAst, execAst);
    }
}
