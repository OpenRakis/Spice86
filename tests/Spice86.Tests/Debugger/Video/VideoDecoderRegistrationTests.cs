namespace Spice86.Tests.Debugger.Video;

using FluentAssertions;

using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.DebuggerKnowledgeBase.Registries;
using Spice86.DebuggerKnowledgeBase.Video;

using Xunit;

public class VideoDecoderRegistrationTests {
    [Theory]
    [InlineData((ushort)0x3B4)]
    [InlineData((ushort)0x3C0)]
    [InlineData((ushort)0x3C9)]
    [InlineData((ushort)0x3CE)]
    [InlineData((ushort)0x3D4)]
    [InlineData((ushort)0x3DA)]
    public void RegisterAll_RegistersVgaPortDecoder_ForKnownPort(ushort port) {
        IoPortDecoderRegistry registry = new IoPortDecoderRegistry();
        VideoDecoderRegistration.RegisterAll(registry);

        bool ok = registry.TryDecodeWrite(port, 0, 1, out DecodedCall? call);

        ok.Should().BeTrue();
        call!.Subsystem.Should().Be("VGA I/O Ports");
    }

    [Theory]
    [InlineData((ushort)0x0388)]
    [InlineData((ushort)0x0220)]
    [InlineData((ushort)0x0060)]
    public void RegisterAll_DoesNotClaim_NonVgaPorts(ushort port) {
        IoPortDecoderRegistry registry = new IoPortDecoderRegistry();
        VideoDecoderRegistration.RegisterAll(registry);

        bool ok = registry.TryDecodeWrite(port, 0, 1, out DecodedCall? call);

        ok.Should().BeFalse();
        call.Should().BeNull();
    }
}
