namespace Spice86.ViewModels.Services;

using Iced.Intel;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.ViewModels.TextPresentation;

using System.Text;

/// <summary>
/// Service for evaluating instruction operands against the current CPU state and memory.
/// Extracts register and memory operands from Iced.Intel instructions, compiles them
/// using the same expression infrastructure as breakpoint conditions, and formats the results
/// as syntax-colored segments reusing the disassembly's <see cref="FormatterTextKind"/> palette.
/// </summary>
public class ExpressionEvaluationService {
    private readonly BreakpointConditionCompiler _compiler;

    public ExpressionEvaluationService(State state, IMemory memory) {
        _compiler = new BreakpointConditionCompiler(state, memory);
    }

    /// <summary>
    /// Evaluates all register and memory operands of an instruction and returns syntax-colored segments.
    /// Immediate and branch operands are skipped since they are already visible in the disassembly text.
    /// </summary>
    /// <param name="instruction">The Iced.Intel instruction to evaluate.</param>
    /// <returns>Syntax-colored segments like [AX(reg) =(punct) 0x1234(num)], or null if no evaluatable operands.</returns>
    public List<FormattedTextToken>? FormatOperandValues(Instruction instruction) {
        if (instruction.Code == Code.INVALID) {
            return null;
        }

        List<FormattedTextToken> segments = new();

        for (int i = 0; i < instruction.OpCount; i++) {
            OpKind kind = instruction.GetOpKind(i);

            if (kind == OpKind.Register) {
                EvaluateRegisterOperand(segments, instruction, i);
            } else if (kind == OpKind.Memory) {
                EvaluateMemoryOperand(segments, instruction);
            }
            // Immediate and branch operands are skipped (already visible in disassembly text)
        }

        if (segments.Count > 0) {
            return segments;
        }
        return null;
    }

    private void EvaluateRegisterOperand(List<FormattedTextToken> segments, Instruction instruction, int operandIndex) {
        Register reg = instruction.GetOpRegister(operandIndex);
        string? expression = RegisterToExpression(reg);
        if (expression == null) {
            return;
        }
        uint value = _compiler.CompileValue(expression)();
        if (segments.Count > 0) {
            AddSeparator(segments);
        }
        AddRegisterValue(segments, expression.ToUpperInvariant(), value, GetRegisterBitWidth(reg));
    }

    private void EvaluateMemoryOperand(List<FormattedTextToken> segments, Instruction instruction) {
        if (instruction.Mnemonic == Mnemonic.Lea) {
            EvaluateLeaOperand(segments, instruction);
        } else {
            EvaluateIndirectMemoryOperand(segments, instruction);
        }
    }

    private void EvaluateLeaOperand(List<FormattedTextToken> segments, Instruction instruction) {
        string addressExpression = BuildAddressExpressionCore(instruction);
        uint value = _compiler.CompileValue(addressExpression)();
        if (segments.Count > 0) {
            AddSeparator(segments);
        }
        AddAddressValue(segments, value, GetRegisterBitWidth(instruction.GetOpRegister(0)));
    }

    private void EvaluateIndirectMemoryOperand(List<FormattedTextToken> segments, Instruction instruction) {
        string? memoryExpression = BuildMemoryExpression(instruction);
        if (memoryExpression == null) {
            return;
        }
        uint value = _compiler.CompileValue(memoryExpression)();
        if (segments.Count > 0) {
            AddSeparator(segments);
        }
        AddMemoryValue(segments, instruction, value, GetMemorySizeBitWidth(instruction.MemorySize));
    }

    private static void AddSeparator(List<FormattedTextToken> segments) {
        segments.Add(new FormattedTextToken { Text = "  ", Kind = FormatterTextKind.Text });
    }

    private static void AddRegisterValue(List<FormattedTextToken> segments, string name, long value, int bitWidth) {
        segments.Add(new FormattedTextToken { Text = name, Kind = FormatterTextKind.Register });
        segments.Add(new FormattedTextToken { Text = "=", Kind = FormatterTextKind.Punctuation });
        segments.Add(new FormattedTextToken { Text = FormatHex(value, bitWidth), Kind = FormatterTextKind.Number });
    }

    private static void AddAddressValue(List<FormattedTextToken> segments, long value, int bitWidth) {
        segments.Add(new FormattedTextToken { Text = "addr", Kind = FormatterTextKind.Keyword });
        segments.Add(new FormattedTextToken { Text = "=", Kind = FormatterTextKind.Punctuation });
        segments.Add(new FormattedTextToken { Text = FormatHex(value, bitWidth), Kind = FormatterTextKind.Number });
    }

    private static void AddMemoryValue(List<FormattedTextToken> segments, Instruction instruction, long value, int bitWidth) {
        segments.Add(new FormattedTextToken { Text = "[", Kind = FormatterTextKind.Punctuation });
        AddMemorySegmentPrefix(segments, instruction);
        bool hasRegisterComponents = AddMemoryRegisterComponents(segments, instruction);
        AddMemoryDisplacementSuffix(segments, instruction, hasRegisterComponents);
        segments.Add(new FormattedTextToken { Text = "]", Kind = FormatterTextKind.Punctuation });
        segments.Add(new FormattedTextToken { Text = "=", Kind = FormatterTextKind.Punctuation });
        segments.Add(new FormattedTextToken { Text = FormatHex(value, bitWidth), Kind = FormatterTextKind.Number });
    }

    private static void AddMemorySegmentPrefix(List<FormattedTextToken> segments, Instruction instruction) {
        string? segmentName = RegisterToExpression(instruction.MemorySegment)?.ToUpperInvariant();
        if (segmentName == null) {
            return;
        }
        segments.Add(new FormattedTextToken { Text = segmentName, Kind = FormatterTextKind.Register });
        segments.Add(new FormattedTextToken { Text = ":", Kind = FormatterTextKind.Punctuation });
    }

    private static bool AddMemoryRegisterComponents(List<FormattedTextToken> segments, Instruction instruction) {
        bool hasRegisterComponents = false;

        if (instruction.MemoryBase != Register.None) {
            string? baseRegExpression = RegisterToExpression(instruction.MemoryBase)?.ToUpperInvariant();
            if (baseRegExpression != null) {
                segments.Add(new FormattedTextToken { Text = baseRegExpression, Kind = FormatterTextKind.Register });
                hasRegisterComponents = true;
            }
        }

        if (instruction.MemoryIndex != Register.None) {
            string? indexRegExpression = RegisterToExpression(instruction.MemoryIndex)?.ToUpperInvariant();
            if (indexRegExpression != null) {
                if (hasRegisterComponents) {
                    segments.Add(new FormattedTextToken { Text = "+", Kind = FormatterTextKind.Operator });
                }
                segments.Add(new FormattedTextToken { Text = indexRegExpression, Kind = FormatterTextKind.Register });
                hasRegisterComponents = true;
            }
        }

        return hasRegisterComponents;
    }

    private static void AddMemoryDisplacementSuffix(List<FormattedTextToken> segments, Instruction instruction, bool hasRegisterComponents) {
        int displacement = GetSignedDisplacement(instruction);

        if (displacement == 0 && hasRegisterComponents) {
            return;
        }

        if (!hasRegisterComponents) {
            // No registers: show raw unsigned hex absolute address
            segments.Add(new FormattedTextToken { Text = $"0x{instruction.MemoryDisplacement32:X}", Kind = FormatterTextKind.Number });
            return;
        }

        if (displacement < 0) {
            segments.Add(new FormattedTextToken { Text = "-", Kind = FormatterTextKind.Operator });
            uint absoluteDisplacement = (uint)(-(long)displacement);
            segments.Add(new FormattedTextToken { Text = $"0x{absoluteDisplacement:X}", Kind = FormatterTextKind.Number });
        } else {
            segments.Add(new FormattedTextToken { Text = "+", Kind = FormatterTextKind.Operator });
            segments.Add(new FormattedTextToken { Text = $"0x{displacement:X}", Kind = FormatterTextKind.Number });
        }
    }

    private static string? RegisterToExpression(Register register) {
        return register switch {
            Register.AL => "al",
            Register.CL => "cl",
            Register.DL => "dl",
            Register.BL => "bl",
            Register.AH => "ah",
            Register.CH => "ch",
            Register.DH => "dh",
            Register.BH => "bh",
            Register.AX => "ax",
            Register.CX => "cx",
            Register.DX => "dx",
            Register.BX => "bx",
            Register.SP => "sp",
            Register.BP => "bp",
            Register.SI => "si",
            Register.DI => "di",
            Register.EAX => "eax",
            Register.ECX => "ecx",
            Register.EDX => "edx",
            Register.EBX => "ebx",
            Register.ESP => "esp",
            Register.EBP => "ebp",
            Register.ESI => "esi",
            Register.EDI => "edi",
            Register.ES => "es",
            Register.CS => "cs",
            Register.SS => "ss",
            Register.DS => "ds",
            Register.FS => "fs",
            Register.GS => "gs",
            _ => null
        };
    }

    private static int GetRegisterBitWidth(Register register) {
        return register switch {
            >= Register.AL and <= Register.BH => 8,
            >= Register.AX and <= Register.DI => 16,
            Register.ES or Register.CS or Register.SS or Register.DS or Register.FS or Register.GS => 16,
            >= Register.EAX and <= Register.EDI => 32,
            _ => 16
        };
    }

    private static string? MemorySizeToPrefix(MemorySize size) {
        return size switch {
            MemorySize.UInt8 or MemorySize.Int8 => "byte",
            MemorySize.UInt16 or MemorySize.Int16 => "word",
            MemorySize.UInt32 or MemorySize.Int32 or MemorySize.SegPtr16 => "dword",
            _ => null
        };
    }

    private static int GetMemorySizeBitWidth(MemorySize size) {
        return size switch {
            MemorySize.UInt8 or MemorySize.Int8 => 8,
            MemorySize.UInt16 or MemorySize.Int16 => 16,
            MemorySize.UInt32 or MemorySize.Int32 or MemorySize.SegPtr16 => 32,
            _ => 16
        };
    }

    private static string? BuildMemoryExpression(Instruction instruction) {
        string? sizePrefix = MemorySizeToPrefix(instruction.MemorySize);
        if (sizePrefix == null) {
            return null;
        }

        string addressExpression = BuildAddressExpressionCore(instruction);
        string? segmentExpression = RegisterToExpression(instruction.MemorySegment);
        if (segmentExpression != null) {
            return $"{sizePrefix} ptr {segmentExpression}:[{addressExpression}]";
        }
        return $"{sizePrefix} ptr [{addressExpression}]";
    }

    private static string BuildAddressExpressionCore(Instruction instruction) {
        StringBuilder expression = new();

        if (instruction.MemoryBase != Register.None) {
            string? baseRegExpression = RegisterToExpression(instruction.MemoryBase);
            if (baseRegExpression != null) {
                expression.Append(baseRegExpression);
            }
        }

        if (instruction.MemoryIndex != Register.None) {
            string? indexRegExpression = RegisterToExpression(instruction.MemoryIndex);
            if (indexRegExpression != null) {
                if (expression.Length > 0) {
                    expression.Append(" + ");
                }
                expression.Append(indexRegExpression);
            }
        }

        int displacement = GetSignedDisplacement(instruction);
        bool hasRegisterComponents = expression.Length > 0;

        if (displacement == 0 && hasRegisterComponents) {
            return expression.ToString();
        }

        if (displacement < 0 && hasRegisterComponents) {
            long displacementMagnitude = -(long)displacement;
            expression.Append($" - 0x{displacementMagnitude:X}");
            return expression.ToString();
        }

        if (expression.Length > 0) {
            expression.Append(" + ");
        }
        expression.Append($"0x{instruction.MemoryDisplacement32:X}");
        return expression.ToString();
    }

    private static int GetSignedDisplacement(Instruction instruction) {
        uint displacement = instruction.MemoryDisplacement32;
        return instruction.MemoryDisplSize switch {
            0 => 0,
            1 => (sbyte)(byte)displacement,
            2 => (short)(ushort)displacement,
            4 => (int)displacement,
            _ => (int)displacement
        };
    }

    private static string FormatHex(long value, int bitWidth) {
        return bitWidth switch {
            8 => $"0x{value & 0xFF:X2}",
            16 => $"0x{value & 0xFFFF:X4}",
            32 => $"0x{value & 0xFFFFFFFF:X8}",
            _ => $"0x{value:X}"
        };
    }
}
