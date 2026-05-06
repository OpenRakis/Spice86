namespace Spice86.Tests.Debugger.Ems;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.DebuggerKnowledgeBase.Ems;
using Spice86.DebuggerKnowledgeBase.Registries;

using Xunit;

public class EmsDecoderRegistrationTests {
    [Fact]
    public void RegisterAll_RegistersDecoderForInt67h() {
        InterruptDecoderRegistry registry = new InterruptDecoderRegistry();
        EmsDecoderRegistration.RegisterAll(registry);
        State state = new State(CpuModel.INTEL_80386) { AH = 0x40 };
        IMemory memory = Substitute.For<IMemory>();

        bool decoded = registry.TryDecode(0x67, state, memory, out DecodedCall? call);

        decoded.Should().BeTrue();
        call.Should().NotBeNull();
        call?.Subsystem.Should().Be("EMS INT 67h");
    }

    [Fact]
    public void RegisterAll_DoesNotClaimUnrelatedVectors() {
        InterruptDecoderRegistry registry = new InterruptDecoderRegistry();
        EmsDecoderRegistration.RegisterAll(registry);
        State state = new State(CpuModel.INTEL_80386);
        IMemory memory = Substitute.For<IMemory>();

        registry.TryDecode(0x21, state, memory, out _).Should().BeFalse();
        registry.TryDecode(0x10, state, memory, out _).Should().BeFalse();
        registry.TryDecode(0x66, state, memory, out _).Should().BeFalse();
        registry.TryDecode(0x68, state, memory, out _).Should().BeFalse();
    }
}
