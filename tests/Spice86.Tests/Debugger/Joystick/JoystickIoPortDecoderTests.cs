namespace Spice86.Tests.Debugger.Joystick;

using FluentAssertions;

using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.DebuggerKnowledgeBase.Joystick;

using Xunit;

public class JoystickIoPortDecoderTests {
    private readonly JoystickIoPortDecoder _decoder = new JoystickIoPortDecoder();

    [Theory]
    [InlineData((ushort)0x200)]
    [InlineData((ushort)0x201)] // canonical gameport
    [InlineData((ushort)0x202)]
    [InlineData((ushort)0x203)]
    [InlineData((ushort)0x204)]
    [InlineData((ushort)0x205)]
    [InlineData((ushort)0x206)]
    [InlineData((ushort)0x207)]
    public void CanDecode_ClaimsGameportWindow(ushort port) {
        _decoder.CanDecode(port).Should().BeTrue();
    }

    [Theory]
    [InlineData((ushort)0x000)]
    [InlineData((ushort)0x1FF)]
    [InlineData((ushort)0x208)]
    [InlineData((ushort)0x210)] // SB / GUS territory
    [InlineData((ushort)0x220)]
    [InlineData((ushort)0x300)] // MPU-401
    [InlineData((ushort)0x388)] // OPL
    public void CanDecode_RejectsNonGameportPorts(ushort port) {
        _decoder.CanDecode(port).Should().BeFalse();
    }

    [Fact]
    public void DecodeWrite_TriggersAxisTimers() {
        DecodedCall call = _decoder.DecodeWrite(0x201, 0x42, 1);

        call.Subsystem.Should().Be("Joystick Gameport I/O Ports");
        call.FunctionName.Should().Contain("Trigger Axis Timers");
        call.ShortDescription.Should().Contain("out 0x201");
        call.ShortDescription.ToLowerInvariant().Should().Contain("558");
        call.Parameters.Should().HaveCount(1);
        call.Parameters[0].Name.Should().Be("trigger");
    }

    [Fact]
    public void DecodeRead_AllReleasedAndSettled() {
        // Buttons released = bits 4..7 set; axes settled = bits 0..3 clear.
        DecodedCall call = _decoder.DecodeRead(0x201, 0xF0, 1);

        call.FunctionName.Should().Contain("Read Status");
        call.ShortDescription.Should().Contain("in 0x201");
        call.Parameters.Should().HaveCount(1);
        call.Parameters[0].FormattedValue.Should().Contain("all buttons released");
    }

    [Fact]
    public void DecodeRead_ButtonJ1APressed() {
        // Joy-1 button A pressed: bit 4 cleared (= 0xE0 buttons + 0x0 axes).
        DecodedCall call = _decoder.DecodeRead(0x201, 0xE0, 1);

        call.Parameters[0].FormattedValue.Should().Contain("J1A pressed");
    }

    [Fact]
    public void DecodeRead_AxisJ2YStillTiming() {
        // All buttons released (0xF0) + bit 3 set (Joy-2 Y still timing).
        DecodedCall call = _decoder.DecodeRead(0x201, 0xF8, 1);

        call.Parameters[0].FormattedValue.Should().Contain("J2Y timing");
    }

    [Fact]
    public void DecodeRead_AllButtonsPressedAllAxesTiming() {
        DecodedCall call = _decoder.DecodeRead(0x201, 0x0F, 1);

        string formatted = call.Parameters[0].FormattedValue;
        formatted.Should().Contain("J1A pressed");
        formatted.Should().Contain("J1B pressed");
        formatted.Should().Contain("J2A pressed");
        formatted.Should().Contain("J2B pressed");
        formatted.Should().Contain("J1X timing");
        formatted.Should().Contain("J1Y timing");
        formatted.Should().Contain("J2X timing");
        formatted.Should().Contain("J2Y timing");
    }
}
