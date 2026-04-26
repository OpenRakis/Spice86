namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

/// <summary>POPA / POPAD</summary>
public class PopaParser : BaseInstructionParser {
    public PopaParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction Parse(ParsingContext context) {
        BitWidth bitWidth = GetBitWidth(false, context.HasOperandSize32);
        DataType dataType = _astBuilder.UType(bitWidth);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);

        List<IVisitableAstNode> statements = new();

        _astBuilder.Stack.PopValues(statements, dataType,
            _astBuilder.Register.Reg(dataType, RegisterIndex.DiIndex),
            _astBuilder.Register.Reg(dataType, RegisterIndex.SiIndex),
            _astBuilder.Register.Reg(dataType, RegisterIndex.BpIndex),
            null,
            _astBuilder.Register.Reg(dataType, RegisterIndex.BxIndex),
            _astBuilder.Register.Reg(dataType, RegisterIndex.DxIndex),
            _astBuilder.Register.Reg(dataType, RegisterIndex.CxIndex),
            _astBuilder.Register.Reg(dataType, RegisterIndex.AxIndex));

        InstructionNode displayAst = new InstructionNode(bitWidth == BitWidth.DWORD_32 ? InstructionOperation.POPAD : InstructionOperation.POPA);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, statements.ToArray());
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
