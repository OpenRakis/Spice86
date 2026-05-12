namespace Spice86.Tests.Debugger;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.DebuggerKnowledgeBase.Registries;

using Xunit;

public class InterruptDecoderRegistryTests {
    private static DecodedCall MakeCall(string functionName) {
        return new DecodedCall("DOS INT 21h", functionName, "test", [], []);
    }

    [Fact]
    public void TryDecode_NoDecoders_ReturnsFalse() {
        InterruptDecoderRegistry registry = new InterruptDecoderRegistry();
        State state = new State(CpuModel.INTEL_80386);
        IMemory memory = Substitute.For<IMemory>();

        bool result = registry.TryDecode(0x21, state, memory, out DecodedCall? call);

        result.Should().BeFalse();
        call.Should().BeNull();
    }

    [Fact]
    public void TryDecode_DispatchesToFirstClaimingDecoder() {
        DecodedCall expected = MakeCall("Open File");
        IInterruptDecoder dosDecoder = Substitute.For<IInterruptDecoder>();
        dosDecoder.CanDecode((byte)0x21).Returns(true);
        dosDecoder.Decode(Arg.Any<byte>(), Arg.Any<State>(), Arg.Any<IMemory>()).Returns(expected);

        IInterruptDecoder otherDecoder = Substitute.For<IInterruptDecoder>();
        otherDecoder.CanDecode(Arg.Any<byte>()).Returns(false);

        InterruptDecoderRegistry registry = new InterruptDecoderRegistry();
        registry.Register(otherDecoder);
        registry.Register(dosDecoder);
        State state = new State(CpuModel.INTEL_80386);
        IMemory memory = Substitute.For<IMemory>();

        bool result = registry.TryDecode(0x21, state, memory, out DecodedCall? call);

        result.Should().BeTrue();
        call.Should().BeSameAs(expected);
        otherDecoder.Received().CanDecode((byte)0x21);
    }

    [Fact]
    public void TryDecode_FirstRegisteredWinsOnConflict() {
        DecodedCall first = MakeCall("first");
        DecodedCall second = MakeCall("second");

        IInterruptDecoder firstDecoder = Substitute.For<IInterruptDecoder>();
        firstDecoder.CanDecode(Arg.Any<byte>()).Returns(true);
        firstDecoder.Decode(Arg.Any<byte>(), Arg.Any<State>(), Arg.Any<IMemory>()).Returns(first);

        IInterruptDecoder secondDecoder = Substitute.For<IInterruptDecoder>();
        secondDecoder.CanDecode(Arg.Any<byte>()).Returns(true);
        secondDecoder.Decode(Arg.Any<byte>(), Arg.Any<State>(), Arg.Any<IMemory>()).Returns(second);

        InterruptDecoderRegistry registry = new InterruptDecoderRegistry();
        registry.Register(firstDecoder);
        registry.Register(secondDecoder);
        State state = new State(CpuModel.INTEL_80386);
        IMemory memory = Substitute.For<IMemory>();

        registry.TryDecode(0x21, state, memory, out DecodedCall? call);

        call.Should().BeSameAs(first);
    }
}
