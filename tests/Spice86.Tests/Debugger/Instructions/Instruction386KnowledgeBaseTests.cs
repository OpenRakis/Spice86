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

    [Fact]
    public void Count_IsAtLeastReasonableSize() {
        // Sanity check: the knowledge base must cover a meaningful portion of the 386 ISA.
        Instruction386KnowledgeBase.Count.Should().BeGreaterThan(80,
            "the base knowledge set must include the most common 386 mnemonics + their aliases");
    }
}
