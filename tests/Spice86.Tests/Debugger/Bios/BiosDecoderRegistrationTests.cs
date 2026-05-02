namespace Spice86.Tests.Debugger.Bios;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Bios;
using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.DebuggerKnowledgeBase.Registries;

using Xunit;

public class BiosDecoderRegistrationTests {
    [Theory]
    [InlineData((byte)0x08, "BIOS INT 08h")]
    [InlineData((byte)0x09, "BIOS INT 09h")]
    [InlineData((byte)0x10, "BIOS INT 10h")]
    [InlineData((byte)0x11, "BIOS INT 11h")]
    [InlineData((byte)0x12, "BIOS INT 12h")]
    [InlineData((byte)0x13, "BIOS INT 13h")]
    [InlineData((byte)0x15, "BIOS INT 15h")]
    [InlineData((byte)0x16, "BIOS INT 16h")]
    [InlineData((byte)0x1A, "BIOS INT 1Ah")]
    [InlineData((byte)0x1C, "BIOS INT 1Ch")]
    [InlineData((byte)0x33, "Mouse INT 33h")]
    [InlineData((byte)0x70, "BIOS INT 70h")]
    public void RegisterAll_RegistersDecoderForVector(byte vector, string expectedSubsystem) {
        InterruptDecoderRegistry registry = new InterruptDecoderRegistry();
        BiosDecoderRegistration.RegisterAll(registry);
        State state = new State(CpuModel.INTEL_80386);
        IMemory memory = Substitute.For<IMemory>();

        bool decoded = registry.TryDecode(vector, state, memory, out DecodedCall? call);

        decoded.Should().BeTrue();
        call.Should().NotBeNull();
        call?.Subsystem.Should().Be(expectedSubsystem);
    }

    [Fact]
    public void RegisterAll_DoesNotClaimDosVectors() {
        InterruptDecoderRegistry registry = new InterruptDecoderRegistry();
        BiosDecoderRegistration.RegisterAll(registry);
        State state = new State(CpuModel.INTEL_80386);
        IMemory memory = Substitute.For<IMemory>();

        registry.TryDecode(0x21, state, memory, out _).Should().BeFalse();
        registry.TryDecode(0x2F, state, memory, out _).Should().BeFalse();
        registry.TryDecode(0x20, state, memory, out _).Should().BeFalse();
    }
}
