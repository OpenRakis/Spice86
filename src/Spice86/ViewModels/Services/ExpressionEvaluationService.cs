namespace Spice86.ViewModels.Services;

using Iced.Intel;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Core.Emulator.VM.Breakpoint;
using Spice86.ViewModels.TextPresentation;

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
                Register reg = instruction.GetOpRegister(i);
                string? expr = RegisterToExpression(reg);
                if (expr != null) {
                    long value = _compiler.CompileValue(expr)();
                    if (segments.Count > 0) {
                        AddSeparator(segments);
                    }
                    AddRegisterValue(segments, expr.ToUpperInvariant(), value, GetRegisterBitWidth(reg));
                }
            } else if (kind == OpKind.Memory) {
                bool isLea = instruction.Mnemonic == Mnemonic.Lea;
                if (isLea) {
                    string? addressExpr = BuildAddressExpression(instruction);
                    if (addressExpr != null) {
                        long value = _compiler.CompileValue(addressExpr)();
                        if (segments.Count > 0) {
                            AddSeparator(segments);
                        }
                        AddAddressValue(segments, instruction, value, GetRegisterBitWidth(instruction.GetOpRegister(0)));
                    }
                } else {
                    string? expr = BuildMemoryExpression(instruction);
                    if (expr != null) {
                        long value = _compiler.CompileValue(expr)();
                        if (segments.Count > 0) {
                            AddSeparator(segments);
                        }
                        AddMemoryValue(segments, instruction, value, GetMemorySizeBitWidth(instruction.MemorySize));
                    }
                }
            }
            // Immediate and branch operands are skipped (already visible in disassembly text)
        }

        return segments.Count > 0 ? segments : null;
    }

    private static void AddSeparator(List<FormattedTextToken> segments) {
        segments.Add(new FormattedTextToken { Text = "  ", Kind = FormatterTextKind.Text });
    }

    private static void AddRegisterValue(List<FormattedTextToken> segments, string name, long value, int bitWidth) {
        segments.Add(new FormattedTextToken { Text = name, Kind = FormatterTextKind.Register });
        segments.Add(new FormattedTextToken { Text = "=", Kind = FormatterTextKind.Punctuation });
        segments.Add(new FormattedTextToken { Text = FormatHex(value, bitWidth), Kind = FormatterTextKind.Number });
    }

    private static void AddAddressValue(List<FormattedTextToken> segments, Instruction instruction, long value, int bitWidth) {
        segments.Add(new FormattedTextToken { Text = "addr", Kind = FormatterTextKind.Keyword });
        segments.Add(new FormattedTextToken { Text = "=", Kind = FormatterTextKind.Punctuation });
        segments.Add(new FormattedTextToken { Text = FormatHex(value, bitWidth), Kind = FormatterTextKind.Number });
    }

    private static void AddMemoryValue(List<FormattedTextToken> segments, Instruction instruction, long value, int bitWidth) {
        segments.Add(new FormattedTextToken { Text = "[", Kind = FormatterTextKind.Punctuation });

        string? segment = RegisterToExpression(instruction.MemorySegment)?.ToUpperInvariant();
        if (segment != null) {
            segments.Add(new FormattedTextToken { Text = segment, Kind = FormatterTextKind.Register });
            segments.Add(new FormattedTextToken { Text = ":", Kind = FormatterTextKind.Punctuation });
        }

        bool firstPart = true;
        string? baseReg = instruction.MemoryBase != Register.None ? RegisterToExpression(instruction.MemoryBase)?.ToUpperInvariant() : null;
        if (baseReg != null) {
            segments.Add(new FormattedTextToken { Text = baseReg, Kind = FormatterTextKind.Register });
            firstPart = false;
        }

        string? indexReg = instruction.MemoryIndex != Register.None ? RegisterToExpression(instruction.MemoryIndex)?.ToUpperInvariant() : null;
        if (indexReg != null) {
            if (!firstPart) {
                segments.Add(new FormattedTextToken { Text = "+", Kind = FormatterTextKind.Operator });
            }
            segments.Add(new FormattedTextToken { Text = indexReg, Kind = FormatterTextKind.Register });
            firstPart = false;
        }

        uint disp = instruction.MemoryDisplacement32;
        if (disp != 0 || firstPart) {
            if (!firstPart) {
                segments.Add(new FormattedTextToken { Text = "+", Kind = FormatterTextKind.Operator });
            }
            segments.Add(new FormattedTextToken { Text = $"0x{disp:X}", Kind = FormatterTextKind.Number });
        }

        segments.Add(new FormattedTextToken { Text = "]", Kind = FormatterTextKind.Punctuation });
        segments.Add(new FormattedTextToken { Text = "=", Kind = FormatterTextKind.Punctuation });
        segments.Add(new FormattedTextToken { Text = FormatHex(value, bitWidth), Kind = FormatterTextKind.Number });
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
            MemorySize.UInt32 or MemorySize.Int32 => "dword",
            _ => null
        };
    }

    private static int GetMemorySizeBitWidth(MemorySize size) {
        return size switch {
            MemorySize.UInt8 or MemorySize.Int8 => 8,
            MemorySize.UInt16 or MemorySize.Int16 => 16,
            MemorySize.UInt32 or MemorySize.Int32 => 32,
            _ => 16
        };
    }

    private static string? BuildMemoryExpression(Instruction instruction) {
        string? sizePrefix = MemorySizeToPrefix(instruction.MemorySize);
        if (sizePrefix == null) {
            return null;
        }

        string addressExpr = BuildAddressExpressionCore(instruction);

        string? segment = RegisterToExpression(instruction.MemorySegment);
        return segment != null
            ? $"{sizePrefix} ptr {segment}:[{addressExpr}]"
            : $"{sizePrefix} ptr [{addressExpr}]";
    }

    /// <summary>
    /// Builds an arithmetic expression for the effective address (offset only, no memory dereference).
    /// Used by LEA to compute the address without reading memory.
    /// </summary>
    private static string? BuildAddressExpression(Instruction instruction) {
        return BuildAddressExpressionCore(instruction);
    }

    private static string BuildAddressExpressionCore(Instruction instruction) {

        List<string> addressParts = new();

        string? baseReg = instruction.MemoryBase != Register.None ? RegisterToExpression(instruction.MemoryBase) : null;
        if (baseReg != null) {
            addressParts.Add(baseReg);
        }

        string? indexReg = instruction.MemoryIndex != Register.None ? RegisterToExpression(instruction.MemoryIndex) : null;
        if (indexReg != null) {
            addressParts.Add(indexReg);
        }

        uint disp = instruction.MemoryDisplacement32;
        if (disp != 0 || addressParts.Count == 0) {
            addressParts.Add($"0x{disp:X}");
        }

        string addressExpr = string.Join(" + ", addressParts);

        return addressExpr;
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
