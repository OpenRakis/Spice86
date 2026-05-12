namespace Spice86.Tests.Debugger;

using FluentAssertions;

using NSubstitute;

using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.DebuggerKnowledgeBase.Registries;

using Xunit;

public class IoPortDecoderRegistryTests {
    private static DecodedCall MakeCall(string functionName) {
        return new DecodedCall("OPL3", functionName, "test", [], []);
    }

    [Fact]
    public void TryDecodeRead_NoDecoders_ReturnsFalse() {
        IoPortDecoderRegistry registry = new IoPortDecoderRegistry();

        bool result = registry.TryDecodeRead(0x388, 0xFF, 1, out DecodedCall? call);

        result.Should().BeFalse();
        call.Should().BeNull();
    }

    [Fact]
    public void TryDecodeWrite_DispatchesToClaimingDecoder() {
        DecodedCall expected = MakeCall("Mixer Index Write");
        IIoPortDecoder oplDecoder = Substitute.For<IIoPortDecoder>();
        oplDecoder.CanDecode((ushort)0x388).Returns(true);
        oplDecoder.DecodeWrite(Arg.Any<ushort>(), Arg.Any<uint>(), Arg.Any<int>()).Returns(expected);

        IoPortDecoderRegistry registry = new IoPortDecoderRegistry();
        registry.Register(oplDecoder);

        bool result = registry.TryDecodeWrite(0x388, 0x42, 1, out DecodedCall? call);

        result.Should().BeTrue();
        call.Should().BeSameAs(expected);
    }

    [Fact]
    public void TryDecodeRead_DispatchesToClaimingDecoder() {
        DecodedCall expected = MakeCall("Status Read");
        IIoPortDecoder oplDecoder = Substitute.For<IIoPortDecoder>();
        oplDecoder.CanDecode((ushort)0x388).Returns(true);
        oplDecoder.DecodeRead(Arg.Any<ushort>(), Arg.Any<uint>(), Arg.Any<int>()).Returns(expected);

        IoPortDecoderRegistry registry = new IoPortDecoderRegistry();
        registry.Register(oplDecoder);

        bool result = registry.TryDecodeRead(0x388, 0x06, 1, out DecodedCall? call);

        result.Should().BeTrue();
        call.Should().BeSameAs(expected);
    }

    [Fact]
    public void TryDecode_NonClaimingDecoder_FallsThrough() {
        IIoPortDecoder vgaDecoder = Substitute.For<IIoPortDecoder>();
        vgaDecoder.CanDecode(Arg.Any<ushort>()).Returns(false);

        IoPortDecoderRegistry registry = new IoPortDecoderRegistry();
        registry.Register(vgaDecoder);

        bool readResult = registry.TryDecodeRead(0x388, 0, 1, out DecodedCall? readCall);
        bool writeResult = registry.TryDecodeWrite(0x388, 0, 1, out DecodedCall? writeCall);

        readResult.Should().BeFalse();
        writeResult.Should().BeFalse();
        readCall.Should().BeNull();
        writeCall.Should().BeNull();
    }
}
