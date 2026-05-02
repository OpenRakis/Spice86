namespace Spice86.Tests.Debugger.Gus;

using FluentAssertions;

using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.DebuggerKnowledgeBase.Gus;
using Spice86.DebuggerKnowledgeBase.Registries;

using Xunit;

public class GusDecoderRegistrationTests {
    [Theory]
    // 0x240 base — the one Spice86's stub uses today.
    [InlineData((ushort)0x240)]
    [InlineData((ushort)0x246)]
    [InlineData((ushort)0x24F)]
    [InlineData((ushort)0x342)]
    [InlineData((ushort)0x347)]
    // 0x220 base — common alternate.
    [InlineData((ushort)0x220)]
    [InlineData((ushort)0x322)]
    // 0x270 base — last documented alternate.
    [InlineData((ushort)0x270)]
    [InlineData((ushort)0x377)]
    public void RegisterAll_ClaimsKnownGusPortsForEveryBase(ushort port) {
        IoPortDecoderRegistry registry = new IoPortDecoderRegistry();
        GusDecoderRegistration.RegisterAll(registry);

        bool ok = registry.TryDecodeWrite(port, 0, 1, out DecodedCall? call);

        ok.Should().BeTrue();
        call!.Subsystem.Should().Be("Gravis Ultrasound I/O Ports");
    }

    [Theory]
    [InlineData((ushort)0x388)] // OPL
    [InlineData((ushort)0x300)] // MPU-401
    [InlineData((ushort)0x000)]
    [InlineData((ushort)0x244)] // Reserved offset within a GUS base window
    public void RegisterAll_DoesNotClaimUnrelatedPorts(ushort port) {
        IoPortDecoderRegistry registry = new IoPortDecoderRegistry();
        GusDecoderRegistration.RegisterAll(registry);

        bool ok = registry.TryDecodeWrite(port, 0, 1, out DecodedCall? call);

        ok.Should().BeFalse();
        call.Should().BeNull();
    }
}
