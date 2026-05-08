namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Parser for GRP2 instructions: shift/rotate operations with ModRM.
/// 7 operations: Rol, Ror, Rcl, Rcr, Shl, Shr, Sar.
/// 3 forms: count=1, count=CL, count=IMM8.
/// </summary>
public class Grp2Parser : BaseGrpOperationParser {
    private static readonly (string Operation, InstructionOperation DisplayOp)[] Operations = {
        ("Rol", InstructionOperation.ROL),
        ("Ror", InstructionOperation.ROR),
        ("Rcl", InstructionOperation.RCL),
        ("Rcr", InstructionOperation.RCR),
        ("Shl", InstructionOperation.SHL),
        ("Shr", InstructionOperation.SHR),
        // groupIndex 6 is an undocumented alias of SHL/SAL (groupIndex 4) on
        // 80186/286/386. The real CPU executes it identically. Tests in the
        // SingleStep suite include this encoding, so we map /6 -> SHL.
        ("Shl", InstructionOperation.SHL),
        ("Sar", InstructionOperation.SAR),
    };

    private readonly Grp2CountSource _countSource;

    public Grp2Parser(ParsingTools parsingTools, Grp2CountSource countSource) : base(parsingTools) {
        _countSource = countSource;
    }

    protected override CfgInstruction Parse(ParsingContext context, ModRmContext modRmContext, int groupIndex) {
        if (groupIndex < 0 || groupIndex > 7) {
            throw new InvalidGroupIndexException(_state, groupIndex);
        }
        (string operation, InstructionOperation displayOp) = Operations[groupIndex];
        BitWidth bitWidth = GetBitWidth(context.OpcodeField, context.HasOperandSize32);
        DataType dataType = _astBuilder.UType(bitWidth);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        RegisterModRmFields(instr, modRmContext);
        ValueNode countNode;
        ValueNode displayCountNode;
        switch (_countSource) {
            case Grp2CountSource.Immediate: {
                InstructionField<byte> immField = _instructionReader.UInt8.NextField(false);
                instr.AddField(immField);
                ValueNode immNode = _astBuilder.InstructionField.ToNode(immField);
                countNode = immNode;
                displayCountNode = immNode;
                break;
            }
            case Grp2CountSource.Cl: {
                ValueNode clNode = _astBuilder.Register.Reg8(RegisterIndex.CxIndex);
                countNode = clNode;
                displayCountNode = clNode;
                break;
            }
            default: {
                countNode = _astBuilder.Constant.ToNode(1);
                displayCountNode = _astBuilder.Constant.ToNode(_astBuilder.UType(BitWidth.QUIBBLE_5), 1UL);
                break;
            }
        }
        ValueNode rmNode = _astBuilder.ModRm.RmToNode(dataType, modRmContext);
        MethodCallValueNode shiftCall = _astBuilder.AluCall(dataType, bitWidth, operation, rmNode, countNode);
        InstructionNode displayAst = new InstructionNode(displayOp, rmNode, displayCountNode);
        IVisitableAstNode execAst = _astBuilder.WithIpAdvancement(instr,
            _astBuilder.Assign(dataType, rmNode, shiftCall));
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
