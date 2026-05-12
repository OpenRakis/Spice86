namespace Spice86.Tests.Debugger.Mpu401;

using FluentAssertions;

using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.DebuggerKnowledgeBase.Mpu401;
using Spice86.DebuggerKnowledgeBase.Registries;

using Xunit;

public class Mpu401DecoderRegistrationTests {
    [Theory]
    [InlineData((ushort)0x300)]
    [InlineData((ushort)0x301)]
    [InlineData((ushort)0x310)]
    [InlineData((ushort)0x320)]
    [InlineData((ushort)0x330)]
    [InlineData((ushort)0x331)]
    [InlineData((ushort)0x332)]
    [InlineData((ushort)0x334)]
    [InlineData((ushort)0x336)]
    [InlineData((ushort)0x338)]
    [InlineData((ushort)0x340)]
    [InlineData((ushort)0x350)]
    [InlineData((ushort)0x360)]
    public void RegisterAll_RegistersMpu401PortDecoder_ForKnownPort(ushort port) {
        IoPortDecoderRegistry registry = new IoPortDecoderRegistry();
        Mpu401DecoderRegistration.RegisterAll(registry);

        bool ok = registry.TryDecodeWrite(port, 0, 1, out DecodedCall? call);

        ok.Should().BeTrue();
        call!.Subsystem.Should().Be("MPU-401 (General MIDI / MT-32)");
    }

    [Theory]
    [InlineData((ushort)0x0220)]
    [InlineData((ushort)0x022C)]
    [InlineData((ushort)0x0388)]
    [InlineData((ushort)0x03C0)]
    [InlineData((ushort)0x0060)]
    [InlineData((ushort)0x0302)]
    [InlineData((ushort)0x0342)]
    public void RegisterAll_DoesNotClaim_NonMpu401Ports(ushort port) {
        IoPortDecoderRegistry registry = new IoPortDecoderRegistry();
        Mpu401DecoderRegistration.RegisterAll(registry);

        bool ok = registry.TryDecodeWrite(port, 0, 1, out DecodedCall? call);

        ok.Should().BeFalse();
        call.Should().BeNull();
    }
}
