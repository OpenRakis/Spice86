namespace Spice86.Tests.Debugger.Dos;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.DebuggerKnowledgeBase.Dos;
using Spice86.DebuggerKnowledgeBase.Registries;

using Xunit;

public class DosDecoderRegistrationTests {
    [Fact]
    public void RegisterAll_RegistersDecodersForInt20Int21AndInt2F() {
        InterruptDecoderRegistry registry = new InterruptDecoderRegistry();
        DosDecoderRegistration.RegisterAll(registry);
        State state = new State(CpuModel.INTEL_80386);
        IMemory memory = Substitute.For<IMemory>();

        registry.TryDecode(0x20, state, memory, out DecodedCall? int20).Should().BeTrue();
        registry.TryDecode(0x21, state, memory, out DecodedCall? int21).Should().BeTrue();
        registry.TryDecode(0x2F, state, memory, out DecodedCall? int2F).Should().BeTrue();

        int20.Should().NotBeNull();
        int21.Should().NotBeNull();
        int2F.Should().NotBeNull();
        int20?.Subsystem.Should().Be("DOS INT 20h");
        int21?.Subsystem.Should().Be("DOS INT 21h");
        int2F?.Subsystem.Should().Be("DOS INT 2Fh");
    }

    [Fact]
    public void RegisterAll_DoesNotClaimNonDosVectors() {
        InterruptDecoderRegistry registry = new InterruptDecoderRegistry();
        DosDecoderRegistration.RegisterAll(registry);
        State state = new State(CpuModel.INTEL_80386);
        IMemory memory = Substitute.For<IMemory>();

        registry.TryDecode(0x10, state, memory, out _).Should().BeFalse();
        registry.TryDecode(0x33, state, memory, out _).Should().BeFalse();
    }
}
