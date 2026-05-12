namespace Spice86.Tests.Debugger.Joystick;

using FluentAssertions;

using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.DebuggerKnowledgeBase.Joystick;
using Spice86.DebuggerKnowledgeBase.Registries;

using Xunit;

public class JoystickDecoderRegistrationTests {
    [Theory]
    [InlineData((ushort)0x200)]
    [InlineData((ushort)0x201)] // canonical
    [InlineData((ushort)0x207)] // top of decode window
    public void RegisterAll_ClaimsGameportWindow(ushort port) {
        IoPortDecoderRegistry registry = new IoPortDecoderRegistry();
        JoystickDecoderRegistration.RegisterAll(registry);

        bool ok = registry.TryDecodeWrite(port, 0, 1, out DecodedCall? call);

        ok.Should().BeTrue();
        call!.Subsystem.Should().Be("Joystick Gameport I/O Ports");
    }

    [Theory]
    [InlineData((ushort)0x1FF)]
    [InlineData((ushort)0x208)]
    [InlineData((ushort)0x210)] // SB / GUS
    [InlineData((ushort)0x300)] // MPU-401
    [InlineData((ushort)0x388)] // OPL
    public void RegisterAll_DoesNotClaimUnrelatedPorts(ushort port) {
        IoPortDecoderRegistry registry = new IoPortDecoderRegistry();
        JoystickDecoderRegistration.RegisterAll(registry);

        bool ok = registry.TryDecodeWrite(port, 0, 1, out DecodedCall? call);

        ok.Should().BeFalse();
        call.Should().BeNull();
    }
}
