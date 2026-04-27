namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Parser;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Shared.Emulator.Memory;

/// <summary>
/// Parser for memory-based string operations: MOVS, CMPS, LODS, STOS, SCAS.
/// These share a common structure of pointer setup, core operation, pointer advancement,
/// and rep prefix handling. I/O string operations (INS/OUTS) are handled separately.
/// </summary>
public class MemoryStringOpParser : BaseInstructionParser {
    /// <summary>
    /// Creates a parser for memory string operations.
    /// </summary>
    public MemoryStringOpParser(ParsingTools parsingTools) : base(parsingTools) {
    }

    /// <summary>
    /// Parses a memory-based string operation instruction.
    /// </summary>
    public CfgInstruction Parse(ParsingContext context, MemoryStringOpKind kind) {
        BitWidth bitWidth = GetBitWidth(context.OpcodeField, context.HasOperandSize32);
        DataType dataType = _astBuilder.UType(bitWidth);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, 1);
        DataType addressType = _astBuilder.AddressType(instr);
        bool usesEqualityRep = kind is MemoryStringOpKind.Cmps or MemoryStringOpKind.Scas;
        RepPrefix? repPrefix = _astBuilder.Rep(instr.RepPrefix, usesEqualityRep);

        BlockNode coreOperation;
        InstructionNode displayAst;

        switch (kind) {
            case MemoryStringOpKind.Movs: {
                int segReg = GetSegmentRegisterOverrideOrDs(context);
                ValueNode src = _astBuilder.StringOperation.SourcePointerSi(dataType, addressType,
                    segReg, (int)SegmentRegisterIndex.DsIndex);
                ValueNode dest = _astBuilder.StringOperation.DestPointerDi(dataType, addressType);
                coreOperation = new BlockNode(
                    _astBuilder.Assign(dataType, dest, src),
                    _astBuilder.StringOperation.AdvanceSi(addressType, (int)bitWidth),
                    _astBuilder.StringOperation.AdvanceDi(addressType, (int)bitWidth));
                displayAst = new InstructionNode(repPrefix, InstructionOperation.MOVS,
                    _astBuilder.Pointer.ToSegmentedPointer(dataType, SegmentRegisterIndex.EsIndex,
                        _astBuilder.Register.Reg(addressType, RegisterIndex.DiIndex)),
                    _astBuilder.Pointer.ToSegmentedPointer(dataType, segReg, (int)SegmentRegisterIndex.DsIndex,
                        _astBuilder.Register.Reg(addressType, RegisterIndex.SiIndex)));
                break;
            }
            case MemoryStringOpKind.Cmps: {
                int segReg = GetSegmentRegisterOverrideOrDs(context);
                ValueNode src = _astBuilder.StringOperation.SourcePointerSi(dataType, addressType,
                    segReg, (int)SegmentRegisterIndex.DsIndex);
                ValueNode dest = _astBuilder.StringOperation.DestPointerDi(dataType, addressType);
                MethodCallValueNode aluCall = _astBuilder.AluCall(DataType.UINT16, bitWidth, "Sub", src, dest);
                coreOperation = new BlockNode(
                    aluCall,
                    _astBuilder.StringOperation.AdvanceSi(addressType, (int)bitWidth),
                    _astBuilder.StringOperation.AdvanceDi(addressType, (int)bitWidth));
                displayAst = new InstructionNode(repPrefix, InstructionOperation.CMPS,
                    _astBuilder.Pointer.ToSegmentedPointer(dataType, segReg, (int)SegmentRegisterIndex.DsIndex,
                        _astBuilder.Register.Reg(addressType, RegisterIndex.SiIndex)),
                    _astBuilder.Pointer.ToSegmentedPointer(dataType, SegmentRegisterIndex.EsIndex,
                        _astBuilder.Register.Reg(addressType, RegisterIndex.DiIndex)));
                break;
            }
            case MemoryStringOpKind.Lods: {
                int segReg = GetSegmentRegisterOverrideOrDs(context);
                ValueNode src = _astBuilder.StringOperation.SourcePointerSi(dataType, addressType,
                    segReg, (int)SegmentRegisterIndex.DsIndex);
                ValueNode accumulator = _astBuilder.Register.Accumulator(dataType);
                coreOperation = new BlockNode(
                    _astBuilder.Assign(dataType, accumulator, src),
                    _astBuilder.StringOperation.AdvanceSi(addressType, (int)bitWidth));
                displayAst = new InstructionNode(repPrefix, InstructionOperation.LODS,
                    accumulator,
                    _astBuilder.Pointer.ToSegmentedPointer(dataType, segReg, (int)SegmentRegisterIndex.DsIndex,
                        _astBuilder.Register.Reg(addressType, RegisterIndex.SiIndex)));
                break;
            }
            case MemoryStringOpKind.Stos: {
                ValueNode accumulator = _astBuilder.Register.Accumulator(dataType);
                ValueNode dest = _astBuilder.StringOperation.DestPointerDi(dataType, addressType);
                coreOperation = new BlockNode(
                    _astBuilder.Assign(dataType, dest, accumulator),
                    _astBuilder.StringOperation.AdvanceDi(addressType, (int)bitWidth));
                displayAst = new InstructionNode(repPrefix, InstructionOperation.STOS,
                    _astBuilder.Pointer.ToSegmentedPointer(dataType, SegmentRegisterIndex.EsIndex,
                        _astBuilder.Register.Reg(addressType, RegisterIndex.DiIndex)),
                    accumulator);
                break;
            }
            case MemoryStringOpKind.Scas: {
                ValueNode accumulator = _astBuilder.Register.Accumulator(dataType);
                ValueNode dest = _astBuilder.StringOperation.DestPointerDi(dataType, addressType);
                MethodCallValueNode aluCall = _astBuilder.AluCall(DataType.UINT16, bitWidth, "Sub", accumulator, dest);
                coreOperation = new BlockNode(
                    aluCall,
                    _astBuilder.StringOperation.AdvanceDi(addressType, (int)bitWidth));
                displayAst = new InstructionNode(repPrefix, InstructionOperation.SCAS,
                    accumulator,
                    _astBuilder.Pointer.ToSegmentedPointer(dataType, SegmentRegisterIndex.EsIndex,
                        _astBuilder.Register.Reg(addressType, RegisterIndex.DiIndex)));
                break;
            }
            default:
                throw new InvalidOperationException($"Unknown string operation kind: {kind}");
        }

        IVisitableAstNode execAst = _astBuilder.StringOperation.GenerateExecutionAst(
            instr, usesEqualityRep, coreOperation, _astBuilder);
        instr.AttachAsts(displayAst, execAst);
        return instr;
    }
}
