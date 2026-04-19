namespace Spice86.Tests.UI;

using Avalonia.Headless.XUnit;

using FluentAssertions;

using Iced.Intel;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.Shared.Emulator.Memory;
using Spice86.ViewModels;
using Spice86.ViewModels.Services;
using Spice86.ViewModels.TextPresentation;
using Spice86.ViewModels.ValueViewModels.Debugging;

using System.Collections.Immutable;

using Xunit;

/// <summary>
/// Integration tests for expression evaluation in the disassembly view.
/// Uses real x86 machine code bytes, decodes them with Iced.Intel,
/// and verifies operand evaluation using the same expression infrastructure as breakpoints.
/// </summary>
public class ExpressionEvaluatorUITests : BreakpointUiTestBase {
    /// <summary>
    /// Decodes a single 16-bit x86 instruction from raw bytes at the given segmented address.
    /// </summary>
    private static (Instruction instruction, byte[] instructionBytes) DecodeInstruction(byte[] machineCode, SegmentedAddress address) {
        ByteArrayCodeReader codeReader = new(machineCode);
        Decoder decoder = Decoder.Create(16, codeReader);
        decoder.IP = address.Offset;
        decoder.Decode(out Instruction instruction);
        byte[] instructionBytes = machineCode[..instruction.Length];
        return (instruction, instructionBytes);
    }

    /// <summary>
    /// Creates a <see cref="DebuggerLineViewModel"/> from raw machine code at the given address.
    /// </summary>
    private static DebuggerLineViewModel CreateDebuggerLine(byte[] machineCode, SegmentedAddress address) {
        (Instruction instruction, byte[] instructionBytes) = DecodeInstruction(machineCode, address);
        EnrichedInstruction enriched = new(instruction) {
            Bytes = instructionBytes,
            SegmentedAddress = address,
            Breakpoints = ImmutableList<BreakpointViewModel>.Empty
        };
        return new DebuggerLineViewModel(enriched);
    }

    /// <summary>
    /// Concatenates segment text for assertion convenience.
    /// </summary>
    private static string SegmentsToText(List<FormattedTextToken> segments) =>
        string.Concat(segments.Select(s => s.Text));

    /// <summary>
    /// Verifies that register operands are evaluated for MOV reg, reg.
    /// ASM: mov ax, bx (opcode 89 D8)
    /// </summary>
    [AvaloniaFact]
    public void EvaluateOperands_MovAxBx_ShowsBothRegisterValues() {
        // Arrange
        State state = CreateState();
        (Memory memory, _, _) = CreateMemory();

        state.AX = 0x1234;
        state.BX = 0x5678;

        // x86 16-bit encoding: mov ax, bx → 89 D8
        byte[] machineCode = [0x89, 0xD8];
        SegmentedAddress address = new(0x1000, 0x0100);
        DebuggerLineViewModel line = CreateDebuggerLine(machineCode, address);

        ExpressionEvaluationService service = new(state, memory);

        // Act
        List<FormattedTextToken>? evaluated = service.FormatOperandValues(line.InstructionInfo);

        // Assert
        evaluated.Should().NotBeNullOrEmpty();
        string text = SegmentsToText(evaluated!);
        text.Should().Contain("AX=0x1234");
        text.Should().Contain("BX=0x5678");

        // Verify syntax coloring: register names get Register kind, values get Number kind
        evaluated.Should().Contain(s => s.Text == "AX" && s.Kind == FormatterTextKind.Register);
        evaluated.Should().Contain(s => s.Text == "0x1234" && s.Kind == FormatterTextKind.Number);
        evaluated.Should().Contain(s => s.Text == "=" && s.Kind == FormatterTextKind.Punctuation);
    }

    /// <summary>
    /// Verifies that memory operands are evaluated for MOV reg, [mem].
    /// ASM: mov ax, [bx] (opcode 8B 07) with DS:BX pointing to 0xABCD.
    /// </summary>
    [AvaloniaFact]
    public void EvaluateOperands_MovAxMemBx_ShowsMemoryValue() {
        // Arrange
        State state = CreateState();
        (Memory memory, _, _) = CreateMemory();

        state.AX = 0x0000;
        state.BX = 0x0050;
        state.DS = 0x2000;

        // Write 0xABCD at physical address DS*16 + BX = 0x20000 + 0x50 = 0x20050
        memory.UInt16[0x20050] = 0xABCD;

        // x86 16-bit encoding: mov ax, word ptr [bx] → 8B 07
        byte[] machineCode = [0x8B, 0x07];
        SegmentedAddress address = new(0x1000, 0x0100);
        DebuggerLineViewModel line = CreateDebuggerLine(machineCode, address);

        ExpressionEvaluationService service = new(state, memory);

        // Act
        List<FormattedTextToken>? evaluated = service.FormatOperandValues(line.InstructionInfo);

        // Assert
        evaluated.Should().NotBeNullOrEmpty();
        string text = SegmentsToText(evaluated!);
        text.Should().Contain("AX=0x0000");
        text.Should().Contain("0xABCD");

        // Verify memory brackets are punctuation-colored
        evaluated.Should().Contain(s => s.Text == "[" && s.Kind == FormatterTextKind.Punctuation);
        evaluated.Should().Contain(s => s.Text == "]" && s.Kind == FormatterTextKind.Punctuation);
    }

    /// <summary>
    /// Verifies that immediates are NOT evaluated (they are already visible in disassembly text).
    /// ASM: mov ax, 0x1234 (opcode B8 34 12) — only AX should appear in evaluation.
    /// </summary>
    [AvaloniaFact]
    public void EvaluateOperands_MovAxImm_OnlyShowsDestRegister() {
        // Arrange
        State state = CreateState();
        (Memory memory, _, _) = CreateMemory();

        state.AX = 0x5555;

        // x86 16-bit encoding: mov ax, 0x1234 → B8 34 12
        byte[] machineCode = [0xB8, 0x34, 0x12];
        SegmentedAddress address = new(0x1000, 0x0100);
        DebuggerLineViewModel line = CreateDebuggerLine(machineCode, address);

        ExpressionEvaluationService service = new(state, memory);

        // Act
        List<FormattedTextToken>? evaluated = service.FormatOperandValues(line.InstructionInfo);

        // Assert
        evaluated.Should().NotBeNullOrEmpty();
        string text = SegmentsToText(evaluated!);
        text.Should().Contain("AX=0x5555");
        // The immediate value 0x1234 should NOT appear in evaluation
        text.Should().NotContain("=0x1234");
    }

    /// <summary>
    /// Verifies that memory with displacement is correctly evaluated.
    /// ASM: mov ax, word ptr [bx+0x10] (opcode 8B 47 10)
    /// </summary>
    [AvaloniaFact]
    public void EvaluateOperands_MovAxMemBxDisp_ShowsMemoryValue() {
        // Arrange
        State state = CreateState();
        (Memory memory, _, _) = CreateMemory();

        state.AX = 0x0000;
        state.BX = 0x0050;
        state.DS = 0x2000;

        // Write 0xBEEF at physical address DS*16 + BX + 0x10 = 0x20000 + 0x50 + 0x10 = 0x20060
        memory.UInt16[0x20060] = 0xBEEF;

        // x86 16-bit encoding: mov ax, word ptr [bx+0x10] → 8B 47 10
        byte[] machineCode = [0x8B, 0x47, 0x10];
        SegmentedAddress address = new(0x1000, 0x0100);
        DebuggerLineViewModel line = CreateDebuggerLine(machineCode, address);

        ExpressionEvaluationService service = new(state, memory);

        // Act
        List<FormattedTextToken>? evaluated = service.FormatOperandValues(line.InstructionInfo);

        // Assert
        evaluated.Should().NotBeNullOrEmpty();
        string text = SegmentsToText(evaluated!);
        text.Should().Contain("AX=0x0000");
        text.Should().Contain("0xBEEF");

        // Verify displacement operator is syntax-colored
        evaluated.Should().Contain(s => s.Text == "+" && s.Kind == FormatterTextKind.Operator);
    }

    /// <summary>
    /// Verifies that LEA computes the effective address instead of reading memory.
    /// ASM: lea ax, [bp-8] (opcode 8D 46 F8) — should show computed address, not memory contents.
    /// </summary>
    [AvaloniaFact]
    public void EvaluateOperands_LeaAxBpMinus8_ShowsEffectiveAddress() {
        // Arrange
        State state = CreateState();
        (Memory memory, _, _) = CreateMemory();

        state.AX = 0x1111;
        state.BP = 0x0100;
        state.SS = 0x2000;

        // Write decoy data at effective address — LEA must NOT read this
        uint physicalAddress = (uint)(state.SS * 16 + state.BP - 8);
        memory.UInt16[physicalAddress] = 0xDEAD;

        // x86 16-bit encoding: lea ax, [bp-8] → 8D 46 F8
        byte[] machineCode = [0x8D, 0x46, 0xF8];
        SegmentedAddress address = new(0x1000, 0x0100);
        DebuggerLineViewModel line = CreateDebuggerLine(machineCode, address);

        ExpressionEvaluationService service = new(state, memory);

        // Act
        List<FormattedTextToken>? evaluated = service.FormatOperandValues(line.InstructionInfo);

        // Assert
        evaluated.Should().NotBeNullOrEmpty();
        string text = SegmentsToText(evaluated!);

        // LEA should show effective address = BP - 8 = 0x0100 - 8 = 0x00F8
        text.Should().Contain("0x00F8");
        // LEA must NOT show the memory contents 0xDEAD
        text.Should().NotContain("DEAD");
        // AX register value should still be shown
        text.Should().Contain("AX=0x1111");
    }

    /// <summary>
    /// Verifies LEA with base+index computes the effective address.
    /// ASM: lea ax, [bx+si] (opcode 8D 00)
    /// </summary>
    [AvaloniaFact]
    public void EvaluateOperands_LeaAxBxSi_ShowsEffectiveAddress() {
        // Arrange
        State state = CreateState();
        (Memory memory, _, _) = CreateMemory();

        state.AX = 0x0000;
        state.BX = 0x0030;
        state.SI = 0x0010;
        state.DS = 0x2000;

        // x86 16-bit encoding: lea ax, [bx+si] → 8D 00
        byte[] machineCode = [0x8D, 0x00];
        SegmentedAddress address = new(0x1000, 0x0100);
        DebuggerLineViewModel line = CreateDebuggerLine(machineCode, address);

        ExpressionEvaluationService service = new(state, memory);

        // Act
        List<FormattedTextToken>? evaluated = service.FormatOperandValues(line.InstructionInfo);

        // Assert
        evaluated.Should().NotBeNullOrEmpty();
        string text = SegmentsToText(evaluated!);
        // Effective address = BX + SI = 0x0030 + 0x0010 = 0x0040
        text.Should().Contain("0x0040");
    }
}
