namespace Spice86.Core.Emulator.CPU.CfgCpu.Parser.SpecificParsers;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.ModRm;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser;
using Spice86.Core.Emulator.CPU.CfgCpu.Parser.FieldReader;
using Spice86.Core.Emulator.CPU.Exceptions;
using Spice86.Core.Emulator.CPU.Registers;
using Spice86.Core.Emulator.Errors;
using Spice86.Shared.Emulator.Memory;

public class BaseInstructionParser {
    /// <summary>
    /// For some instructions, lsb == 0 when 8bit and 1 when 16/32
    /// </summary>
    protected const byte SizeMask = 0b1;
    protected readonly InstructionReader _instructionReader;
    protected readonly ModRmParser _modRmParser;
    protected readonly State _state;
    protected readonly AstBuilder _astBuilder;

    protected BaseInstructionParser(ParsingTools parsingTools) {
        _instructionReader = parsingTools.InstructionReader;
        _modRmParser = parsingTools.ModRmParser;
        _state = parsingTools.State;
        _astBuilder = parsingTools.AstBuilder;
    }

    protected bool HasOperandSize8(ushort opcode) {
        return (opcode & SizeMask) == 0;
    }
    protected BitWidth GetBitWidth(InstructionField<ushort> opcodeField, bool is32) {
        return HasOperandSize8(opcodeField.Value) ? BitWidth.BYTE_8 : is32 ? BitWidth.DWORD_32 : BitWidth.WORD_16;
    }

    protected BitWidth GetBitWidth(bool is8, bool is32) {
        return is8 ? BitWidth.BYTE_8 : is32 ? BitWidth.DWORD_32 : BitWidth.WORD_16;
    }

    protected int GetSegmentRegisterOverrideOrDs(ParsingContext context) {
        return context.SegmentOverrideFromPrefixes ?? (int)SegmentRegisterIndex.DsIndex;
    }

    protected bool BitIsTrue(uint value, int bitIndex) {
        return ((value >> bitIndex) & 1) == 1;
    }

    protected static UnsupportedBitWidthException CreateUnsupportedBitWidthException(BitWidth bitWidth) {
        return new UnsupportedBitWidthException(bitWidth);
    }

    protected void RegisterModRmFields(CfgInstruction instr, ModRmContext modRmContext) {
        instr.AddFields(modRmContext.FieldsInOrder);
    }

    /// <summary>
    /// Parses a ModRM byte, creates the instruction, and registers ModRM fields.
    /// Does not compute types — callers supply their own.
    /// </summary>
    protected (CfgInstruction instr, ModRmContext modRmContext) ParseModRmBase(ParsingContext context, int maxSuccessors) {
        ModRmContext modRmContext = _modRmParser.ParseNext(context);
        CfgInstruction instr = new(context.Address, context.OpcodeField, context.Prefixes, maxSuccessors);
        RegisterModRmFields(instr, modRmContext);
        return (instr, modRmContext);
    }

    /// <summary>
    /// Parses a ModRM byte and builds the common instruction context with dynamic unsigned type.
    /// </summary>
    protected (CfgInstruction instr, DataType dataType, BitWidth bitWidth, ModRmContext modRmContext) ParseModRm(ParsingContext context, bool has8, int maxSuccessors) {
        (CfgInstruction instr, ModRmContext modRmContext) = ParseModRmBase(context, maxSuccessors);
        BitWidth bitWidth = GetBitWidth(has8 && HasOperandSize8(context.OpcodeField.Value), context.HasOperandSize32);
        DataType dataType = _astBuilder.UType(bitWidth);
        return (instr, dataType, bitWidth, modRmContext);
    }

    protected ValueNode ReadUnsignedImmediate(CfgInstruction instr, BitWidth bitWidth) {
        switch (bitWidth) {
            case BitWidth.BYTE_8: {
                InstructionField<byte> field = _instructionReader.UInt8.NextField(false);
                instr.AddField(field);
                return _astBuilder.InstructionField.ToNode(field);
            }
            case BitWidth.WORD_16: {
                InstructionField<ushort> field = _instructionReader.UInt16.NextField(false);
                instr.AddField(field);
                return _astBuilder.InstructionField.ToNode(field);
            }
            case BitWidth.DWORD_32: {
                InstructionField<uint> field = _instructionReader.UInt32.NextField(false);
                instr.AddField(field);
                return _astBuilder.InstructionField.ToNode(field);
            }
            default:
                throw CreateUnsupportedBitWidthException(bitWidth);
        }
    }

    protected ValueNode ReadSignedImmediate(CfgInstruction instr, BitWidth bitWidth) {
        (int _, FieldWithValue field, ValueNode node) = ReadSignedField(bitWidth, false);
        instr.AddField(field);
        return node;
    }

    protected (int offsetValue, FieldWithValue offsetField) ReadSignedOffset(BitWidth width) {
        (int value, FieldWithValue field, ValueNode _) = ReadSignedField(width, true);
        return (value, field);
    }

    private (int signedValue, FieldWithValue field, ValueNode node) ReadSignedField(BitWidth width, bool isDiscriminant) {
        switch (width) {
            case BitWidth.BYTE_8: {
                InstructionField<sbyte> field = _instructionReader.Int8.NextField(isDiscriminant);
                return (field.Value, field, _astBuilder.InstructionField.ToNode(field));
            }
            case BitWidth.WORD_16: {
                InstructionField<short> field = _instructionReader.Int16.NextField(isDiscriminant);
                return (field.Value, field, _astBuilder.InstructionField.ToNode(field));
            }
            case BitWidth.DWORD_32: {
                InstructionField<int> field = _instructionReader.Int32.NextField(isDiscriminant);
                return (field.Value, field, _astBuilder.InstructionField.ToNode(field));
            }
            default:
                throw CreateUnsupportedBitWidthException(width);
        }
    }

    /// <summary>
    /// Builds the carry flag assignment and optional mutation for bit test instructions (BT/BTS/BTR/BTC).
    /// Returns the setCarry AST node and an optional assignMutated node (null for BT with no mutation).
    /// </summary>
    protected (IVisitableAstNode setCarry, IVisitableAstNode? assignMutated) BuildBitTestCarryAndMutation(
        DataType dataType, ValueNode targetNode, BinaryOperationNode bitInElement, BitTestMutation mutation) {
        BinaryOperationNode carryValue = _astBuilder.Bitwise.IsBitSet(targetNode, bitInElement);
        IVisitableAstNode setCarry = _astBuilder.Assign(DataType.BOOL, _astBuilder.Flag.Carry(), carryValue);
        if (mutation == BitTestMutation.None) {
            return (setCarry, null);
        }
        BinaryOperationNode mutatedValue = mutation switch {
            BitTestMutation.Set => _astBuilder.Bitwise.SetBit(targetNode, bitInElement),
            BitTestMutation.Reset => _astBuilder.Bitwise.ResetBit(targetNode, bitInElement),
            BitTestMutation.Toggle => _astBuilder.Bitwise.ToggleBit(targetNode, bitInElement),
            _ => throw new InvalidOperationException($"Unknown bit test mutation: {mutation}")
        };
        IVisitableAstNode assignMutated = _astBuilder.Assign(dataType, targetNode, mutatedValue);
        return (setCarry, assignMutated);
    }

    /// <summary>
    /// Builds the display and execution ASTs for IO instructions (IN/OUT).
    /// </summary>
    protected (InstructionNode displayAst, IVisitableAstNode execAst) BuildIoAsts(
        CfgInstruction instr, DataType dataType, ValueNode portNode, ValueNode accumulator, bool isInput) {
        InstructionNode displayAst;
        IVisitableAstNode execAst;
        if (isInput) {
            MethodCallValueNode ioRead = _astBuilder.Io.IoRead(dataType, portNode);
            displayAst = new InstructionNode(InstructionOperation.IN, accumulator, portNode);
            execAst = _astBuilder.WithIpAdvancement(instr,
                _astBuilder.Assign(dataType, accumulator, ioRead));
        } else {
            MethodCallNode ioWrite = _astBuilder.Io.IoWrite(dataType, portNode, accumulator);
            displayAst = new InstructionNode(InstructionOperation.OUT, portNode, accumulator);
            execAst = _astBuilder.WithIpAdvancement(instr, ioWrite);
        }
        return (displayAst, execAst);
    }
}