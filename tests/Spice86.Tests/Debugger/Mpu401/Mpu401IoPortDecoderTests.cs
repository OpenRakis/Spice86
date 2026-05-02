namespace Spice86.Tests.Debugger.Mpu401;

using FluentAssertions;

using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.DebuggerKnowledgeBase.Mpu401;

using Xunit;

public class Mpu401IoPortDecoderTests {
    private readonly Mpu401IoPortDecoder _decoder = new Mpu401IoPortDecoder();

    [Theory]
    [InlineData((ushort)0x300)]
    [InlineData((ushort)0x301)]
    [InlineData((ushort)0x310)]
    [InlineData((ushort)0x311)]
    [InlineData((ushort)0x320)]
    [InlineData((ushort)0x321)]
    [InlineData((ushort)0x330)]
    [InlineData((ushort)0x331)]
    [InlineData((ushort)0x332)]
    [InlineData((ushort)0x333)]
    [InlineData((ushort)0x334)]
    [InlineData((ushort)0x335)]
    [InlineData((ushort)0x336)]
    [InlineData((ushort)0x337)]
    [InlineData((ushort)0x338)]
    [InlineData((ushort)0x339)]
    [InlineData((ushort)0x340)]
    [InlineData((ushort)0x341)]
    [InlineData((ushort)0x350)]
    [InlineData((ushort)0x351)]
    [InlineData((ushort)0x360)]
    [InlineData((ushort)0x361)]
    public void CanDecode_ClaimsAllStandardMpu401Ports(ushort port) {
        _decoder.CanDecode(port).Should().BeTrue();
    }

    [Theory]
    [InlineData((ushort)0x000)]
    [InlineData((ushort)0x220)]
    [InlineData((ushort)0x22C)]
    [InlineData((ushort)0x302)] // base+2 is not part of the MPU window
    [InlineData((ushort)0x32F)]
    [InlineData((ushort)0x342)]
    [InlineData((ushort)0x388)]
    public void CanDecode_RejectsNonMpu401Ports(ushort port) {
        _decoder.CanDecode(port).Should().BeFalse();
    }

    [Fact]
    public void DecodeWrite_CommandPort_EnterUartMode_DecodesByName() {
        DecodedCall call = _decoder.DecodeWrite(0x331, 0x3F, 1);

        call.Subsystem.Should().Be("MPU-401 (General MIDI / MT-32)");
        call.FunctionName.Should().Be("MPU-401 Command Write (base + 1)");
        call.Parameters.Should().HaveCount(1);
        call.Parameters[0].Name.Should().Be("command");
        call.Parameters[0].FormattedValue.Should().Be("0x3F (Enter UART mode)");
        call.Parameters[0].Source.Should().Be("port 0x331");
        call.Parameters[0].Kind.Should().Be(DecodedParameterKind.IoPort);
    }

    [Fact]
    public void DecodeWrite_CommandPort_Reset_DecodesByName() {
        DecodedCall call = _decoder.DecodeWrite(0x331, 0xFF, 1);

        call.Parameters[0].FormattedValue.Should().Be("0xFF (Reset MPU-401)");
    }

    [Fact]
    public void DecodeWrite_CommandPort_Tempo_DecodesByName() {
        DecodedCall call = _decoder.DecodeWrite(0x331, 0xE0, 1);

        call.Parameters[0].FormattedValue.Should().Contain("Set tempo");
    }

    [Fact]
    public void DecodeWrite_CommandPort_UnknownCommand_FallsBackToUnknown() {
        DecodedCall call = _decoder.DecodeWrite(0x331, 0x77, 1);

        call.Parameters[0].FormattedValue.Should().Be("0x77 (Unknown)");
    }

    [Fact]
    public void DecodeRead_StatusPort_ProducesStatusReadDescription() {
        DecodedCall call = _decoder.DecodeRead(0x331, 0x80, 1);

        call.FunctionName.Should().Be("MPU-401 Status Read (base + 1)");
        call.Parameters.Should().HaveCount(1);
        call.Parameters[0].Name.Should().Be("status");
        call.Parameters[0].Notes.Should().Contain("DSR");
        call.Parameters[0].Notes.Should().Contain("DRR");
    }

    [Fact]
    public void DecodeWrite_DataPort_NoteOn_DecodesMidiStatusWithChannel() {
        // 0x90 = Note On on channel 1 (lower nibble 0 -> channel 1)
        DecodedCall call = _decoder.DecodeWrite(0x330, 0x90, 1);

        call.FunctionName.Should().Be("MPU-401 Data Write (base + 0)");
        call.Parameters[0].FormattedValue.Should().Be("0x90 (Note On (channel 1))");
    }

    [Fact]
    public void DecodeWrite_DataPort_ControlChange_DecodesMidiStatusWithChannel() {
        // 0xB5 = Control Change on channel 6
        DecodedCall call = _decoder.DecodeWrite(0x330, 0xB5, 1);

        call.Parameters[0].FormattedValue.Should().Be("0xB5 (Control Change (channel 6))");
    }

    [Fact]
    public void DecodeWrite_DataPort_SysExStart_DecodesAsSystemExclusive() {
        DecodedCall call = _decoder.DecodeWrite(0x330, 0xF0, 1);

        call.Parameters[0].FormattedValue.Should().Contain("System Exclusive");
    }

    [Fact]
    public void DecodeWrite_DataPort_DataByte_DescribedAsDataByte() {
        DecodedCall call = _decoder.DecodeWrite(0x330, 0x40, 1);

        call.Parameters[0].FormattedValue.Should().Be("0x40 (data byte)");
    }

    [Fact]
    public void DecodeRead_DataPort_ProducesDataReadDescription() {
        DecodedCall call = _decoder.DecodeRead(0x330, 0xFE, 1);

        call.FunctionName.Should().Be("MPU-401 Data Read (base + 0)");
        call.Parameters.Should().HaveCount(1);
        call.Parameters[0].Name.Should().Be("value");
        call.Parameters[0].FormattedValue.Should().Be("0xFE");
    }

    [Theory]
    [InlineData((ushort)0x300)]
    [InlineData((ushort)0x340)]
    [InlineData((ushort)0x360)]
    public void DecodeWrite_NonDefaultBase_ReportsBaseInNote(ushort baseAddress) {
        ushort commandPort = (ushort)(baseAddress + 1);
        DecodedCall call = _decoder.DecodeWrite(commandPort, 0x3F, 1);

        call.Parameters[0].Notes.Should().Be($"base=0x{baseAddress:X3}");
    }
}
