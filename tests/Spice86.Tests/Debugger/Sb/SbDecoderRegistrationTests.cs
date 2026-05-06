namespace Spice86.Tests.Debugger.Sb;

using FluentAssertions;

using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.DebuggerKnowledgeBase.Registries;
using Spice86.DebuggerKnowledgeBase.Sb;

using Xunit;

public class SbDecoderRegistrationTests {
    [Theory]
    [InlineData((ushort)0x210)]
    [InlineData((ushort)0x220)]
    [InlineData((ushort)0x22C)]
    [InlineData((ushort)0x22E)]
    [InlineData((ushort)0x22F)]
    [InlineData((ushort)0x230)]
    [InlineData((ushort)0x240)]
    [InlineData((ushort)0x250)]
    [InlineData((ushort)0x260)]
    [InlineData((ushort)0x280)]
    public void RegisterAll_RegistersSbPortDecoder_ForKnownPort(ushort port) {
        IoPortDecoderRegistry registry = new IoPortDecoderRegistry();
        SbDecoderRegistration.RegisterAll(registry);

        bool ok = registry.TryDecodeWrite(port, 0, 1, out DecodedCall? call);

        ok.Should().BeTrue();
        call!.Subsystem.Should().Be("Sound Blaster I/O Ports");
    }

    [Theory]
    [InlineData((ushort)0x0388)]
    [InlineData((ushort)0x03C0)]
    [InlineData((ushort)0x0060)]
    [InlineData((ushort)0x0290)]
    [InlineData((ushort)0x020F)]
    public void RegisterAll_DoesNotClaim_NonSbPorts(ushort port) {
        IoPortDecoderRegistry registry = new IoPortDecoderRegistry();
        SbDecoderRegistration.RegisterAll(registry);

        bool ok = registry.TryDecodeWrite(port, 0, 1, out DecodedCall? call);

        ok.Should().BeFalse();
        call.Should().BeNull();
    }
}
