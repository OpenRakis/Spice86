namespace Spice86.Tests.Debugger.Instructions;

using FluentAssertions;

using Spice86.DebuggerKnowledgeBase.Instructions;

using Xunit;

/// <summary>
/// Tests for the static 386 instruction knowledge base.
/// </summary>
public class Instruction386KnowledgeBaseTests {
    [Theory]
    [InlineData("MOV", "Move")]
    [InlineData("mov", "Move")]
    [InlineData("LEA", "Load Effective Address")]
    [InlineData("PUSH", "Push onto Stack")]
    [InlineData("POP", "Pop off Stack")]
    [InlineData("CALL", "Call Procedure")]
    [InlineData("RET", "Return from Near Procedure")]
    [InlineData("RETN", "Return from Near Procedure")]
    [InlineData("RETF", "Return from Far Procedure")]
    [InlineData("IRET", "Interrupt Return")]
    [InlineData("INT", "Software Interrupt")]
    [InlineData("IN", "Input from I/O Port")]
    [InlineData("OUT", "Output to I/O Port")]
    [InlineData("CMP", "Compare")]
    [InlineData("TEST", "Logical Compare")]
    [InlineData("XOR", "Bitwise Exclusive-OR")]
    [InlineData("MOVSB", "Move String")]
    [InlineData("MOVSD", "Move String")]
    [InlineData("REP", "Repeat String Operation")]
    [InlineData("REPE", "Repeat String Operation")]
    [InlineData("PUSHA", "Push All General-Purpose Registers")]
    [InlineData("PUSHAD", "Push All General-Purpose Registers")]
    public void TryGet_KnownMnemonics_ReturnsHighLevelInfo(string mnemonic, string expectedName) {
        bool found = Instruction386KnowledgeBase.TryGet(mnemonic, out InstructionInfo? info);

        found.Should().BeTrue();
        info.Should().NotBeNull();
        info!.Name.Should().Be(expectedName);
        info.Summary.Should().NotBeNullOrWhiteSpace();
        info.Uses.Should().NotBeNullOrWhiteSpace();
        info.Purpose.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("JE", "JE")]
    [InlineData("JZ", "JE")]
    [InlineData("JA", "JA")]
    [InlineData("JNBE", "JA")]
    [InlineData("JNC", "JAE")]
    [InlineData("JC", "JB")]
    public void TryGet_ConditionalJumpAliases_ResolveToCanonicalEntry(string alias, string expectedCanonical) {
        bool found = Instruction386KnowledgeBase.TryGet(alias, out InstructionInfo? info);

        found.Should().BeTrue();
        info.Should().NotBeNull();
        info!.Mnemonic.Should().Be(expectedCanonical);
    }

    [Theory]
    [InlineData("CPUID")]    // 486+ feature, intentionally out of scope of the 386 base set
    [InlineData("RDTSC")]    // Pentium+
    [InlineData("XYZZY")]    // not a real mnemonic
    [InlineData("")]         // empty
    public void TryGet_UnknownMnemonic_ReturnsFalseAndNull(string mnemonic) {
        bool found = Instruction386KnowledgeBase.TryGet(mnemonic, out InstructionInfo? info);

        found.Should().BeFalse();
        info.Should().BeNull();
    }

    [Theory]
    [InlineData("AAA", "ASCII Adjust After Addition")]
    [InlineData("AAS", "ASCII Adjust After Subtraction")]
    [InlineData("AAM", "ASCII Adjust After Multiplication")]
    [InlineData("AAD", "ASCII Adjust Before Division")]
    [InlineData("DAA", "Decimal Adjust After Addition")]
    [InlineData("DAS", "Decimal Adjust After Subtraction")]
    [InlineData("BOUND", "Check Array Index Against Bounds")]
    public void TryGet_BcdAndBounds_AreCovered(string mnemonic, string expectedName) {
        bool found = Instruction386KnowledgeBase.TryGet(mnemonic, out InstructionInfo? info);
        found.Should().BeTrue();
        info!.Name.Should().Be(expectedName);
    }

    [Theory]
    [InlineData("LGDT")]
    [InlineData("SGDT")]
    [InlineData("LIDT")]
    [InlineData("SIDT")]
    [InlineData("LLDT")]
    [InlineData("SLDT")]
    [InlineData("LTR")]
    [InlineData("STR")]
    [InlineData("LMSW")]
    [InlineData("SMSW")]
    [InlineData("ARPL")]
    [InlineData("CLTS")]
    [InlineData("LAR")]
    [InlineData("LSL")]
    [InlineData("VERR")]
    [InlineData("VERW")]
    public void TryGet_ProtectedModeSystemInstructions_AreCovered(string mnemonic) {
        bool found = Instruction386KnowledgeBase.TryGet(mnemonic, out InstructionInfo? info);
        found.Should().BeTrue();
        info!.Summary.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("FLD", "Load Floating-Point Value")]
    [InlineData("FST", "Store Floating-Point Value")]
    [InlineData("FSTP", "Store Floating-Point and Pop")]
    [InlineData("FILD", "Load Integer")]
    [InlineData("FISTP", "Store Integer and Pop")]
    [InlineData("FADD", "Floating-Point Add")]
    [InlineData("FADDP", "Floating-Point Add and Pop")]
    [InlineData("FSUB", "Floating-Point Subtract")]
    [InlineData("FMUL", "Floating-Point Multiply")]
    [InlineData("FDIV", "Floating-Point Divide")]
    [InlineData("FDIVR", "Floating-Point Reverse Divide")]
    [InlineData("FCHS", "Floating-Point Change Sign")]
    [InlineData("FABS", "Floating-Point Absolute Value")]
    [InlineData("FSQRT", "Floating-Point Square Root")]
    [InlineData("FCOM", "Floating-Point Compare")]
    [InlineData("FCOMPP", "Floating-Point Compare and Pop Twice")]
    [InlineData("FSIN", "Floating-Point Sine")]
    [InlineData("FCOS", "Floating-Point Cosine")]
    [InlineData("FPATAN", "Partial Arctangent")]
    [InlineData("F2XM1", "2^x - 1")]
    [InlineData("FYL2X", "ST(1) * log2(ST(0))")]
    [InlineData("FLDPI", "Load Pi")]
    [InlineData("FLDZ", "Load +0.0")]
    [InlineData("FLD1", "Load +1.0")]
    [InlineData("FNSTSW", "Store Status Word")]
    [InlineData("FLDCW", "Load Control Word")]
    [InlineData("FNINIT", "Initialize FPU")]
    [InlineData("FNCLEX", "Clear FPU Exceptions")]
    public void TryGet_FpuInstructions_AreCovered(string mnemonic, string expectedName) {
        bool found = Instruction386KnowledgeBase.TryGet(mnemonic, out InstructionInfo? info);
        found.Should().BeTrue();
        info!.Name.Should().Be(expectedName);
    }

    [Fact]
    public void Count_CoversFull386PlusFpu() {
        // The full-386-expert pass covers integer + 186/286/386 system + 387 FPU.
        Instruction386KnowledgeBase.Count.Should().BeGreaterThan(180,
            "the expanded knowledge base must include the BCD/system/FPU additions on top of the integer base set");
    }
}
