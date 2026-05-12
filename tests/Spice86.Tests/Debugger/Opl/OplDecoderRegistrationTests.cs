namespace Spice86.Tests.Debugger.Opl;

using FluentAssertions;

using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.DebuggerKnowledgeBase.Opl;
using Spice86.DebuggerKnowledgeBase.Registries;

using Xunit;

public class OplDecoderRegistrationTests {
    [Theory]
    [InlineData((ushort)0x388)]
    [InlineData((ushort)0x389)]
    [InlineData((ushort)0x38A)]
    [InlineData((ushort)0x38B)]
    public void RegisterAll_RegistersOplPortDecoder_ForAdlibPorts(ushort port) {
        IoPortDecoderRegistry registry = new IoPortDecoderRegistry();
        OplDecoderRegistration.RegisterAll(registry);

        bool ok = registry.TryDecodeWrite(port, 0, 1, out DecodedCall? call);

        ok.Should().BeTrue();
        call!.Subsystem.Should().Be("OPL FM I/O Ports");
    }

    [Theory]
    [InlineData((ushort)0x387)]
    [InlineData((ushort)0x38C)]
    [InlineData((ushort)0x220)]
    [InlineData((ushort)0x300)]
    public void RegisterAll_DoesNotClaim_NonAdlibPorts(ushort port) {
        IoPortDecoderRegistry registry = new IoPortDecoderRegistry();
        OplDecoderRegistration.RegisterAll(registry);

        bool ok = registry.TryDecodeWrite(port, 0, 1, out DecodedCall? call);

        ok.Should().BeFalse();
        call.Should().BeNull();
    }
}
