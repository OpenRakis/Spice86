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
        registry.TryDecode(0x22, state, memory, out DecodedCall? int22).Should().BeTrue();
        registry.TryDecode(0x23, state, memory, out DecodedCall? int23).Should().BeTrue();
        registry.TryDecode(0x24, state, memory, out DecodedCall? int24).Should().BeTrue();
        registry.TryDecode(0x25, state, memory, out DecodedCall? int25).Should().BeTrue();
        registry.TryDecode(0x26, state, memory, out DecodedCall? int26).Should().BeTrue();
        registry.TryDecode(0x28, state, memory, out DecodedCall? int28).Should().BeTrue();
        registry.TryDecode(0x2A, state, memory, out DecodedCall? int2A).Should().BeTrue();
        registry.TryDecode(0x2F, state, memory, out DecodedCall? int2F).Should().BeTrue();

        int20?.Subsystem.Should().Be("DOS INT 20h");
        int21?.Subsystem.Should().Be("DOS INT 21h");
        int22?.Subsystem.Should().Be("DOS INT 22h");
        int23?.Subsystem.Should().Be("DOS INT 23h");
        int24?.Subsystem.Should().Be("DOS INT 24h");
        int25?.Subsystem.Should().Be("DOS INT 25h");
        int26?.Subsystem.Should().Be("DOS INT 26h");
        int28?.Subsystem.Should().Be("DOS INT 28h");
        int2A?.Subsystem.Should().Be("DOS INT 2Ah");
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
