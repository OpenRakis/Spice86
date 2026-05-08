namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

/// <summary>PUSHA / PUSHAD</summary>
public class PushaParser : BaseInstructionParser {
    public PushaParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    public CfgInstruction Parse(ParsingContext context) {
        BitWidth bitWidth = GetBitWidth(false, context.HasOperandSize32);
        DataType dataType = _astBuilder.UType(bitWidth);
        DataType addressType = _astBuilder.UType(BitWidth.WORD_16);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);

        string methodName = bitWidth == BitWidth.DWORD_32 ? nameof(Stack.PushAll32) : nameof(Stack.PushAll16);

        MethodCallNode pushAll = new("Stack", methodName,
            _astBuilder.Register.Reg(dataType, RegisterIndex.AxIndex),
            _astBuilder.Register.Reg(dataType, RegisterIndex.CxIndex),
            _astBuilder.Register.Reg(dataType, RegisterIndex.DxIndex),
            _astBuilder.Register.Reg(dataType, RegisterIndex.BxIndex),
            _astBuilder.Register.StackPointer(addressType),
            _astBuilder.Register.Reg(dataType, RegisterIndex.BpIndex),
            _astBuilder.Register.Reg(dataType, RegisterIndex.SiIndex),
            _astBuilder.Register.Reg(dataType, RegisterIndex.DiIndex));

        InstructionNode displayAst = new InstructionNode(bitWidth == BitWidth.DWORD_32 ? InstructionOperation.PUSHAD : InstructionOperation.PUSHA);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr, pushAll);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
