namespace Spice86.Tests.Debugger;

using FluentAssertions;

using NSubstitute;

using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.DebuggerKnowledgeBase.Registries;
using Spice86.Shared.Emulator.Memory;

using Xunit;

public class AsmRoutineDecoderRegistryTests {
    [Fact]
    public void TryDecode_NoDecoders_ReturnsFalse() {
        AsmRoutineDecoderRegistry registry = new AsmRoutineDecoderRegistry();

        bool result = registry.TryDecode(new SegmentedAddress(0xF000, 0x1000), out DecodedCall? call);

        result.Should().BeFalse();
        call.Should().BeNull();
    }

    [Fact]
    public void TryDecode_DispatchesToClaimingDecoder() {
        SegmentedAddress entry = new SegmentedAddress(0xF000, 0x1000);
        DecodedCall expected = new DecodedCall("Interrupt 21h", "DOS dispatcher", "test", [], []);
        IAsmRoutineDecoder decoder = Substitute.For<IAsmRoutineDecoder>();
        decoder.CanDecode(entry).Returns(true);
        decoder.Decode(entry).Returns(expected);

        AsmRoutineDecoderRegistry registry = new AsmRoutineDecoderRegistry();
        registry.Register(decoder);

        bool result = registry.TryDecode(entry, out DecodedCall? call);

        result.Should().BeTrue();
        call.Should().BeSameAs(expected);
    }
}
