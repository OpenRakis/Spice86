namespace Spice86.Tests.Debugger.Sb;

using FluentAssertions;

using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.DebuggerKnowledgeBase.Sb;

using Xunit;

public class SbIoPortDecoderTests {
    private readonly SbIoPortDecoder _decoder = new SbIoPortDecoder();

    [Theory]
    [InlineData((ushort)0x220)]
    [InlineData((ushort)0x224)]
    [InlineData((ushort)0x225)]
    [InlineData((ushort)0x226)]
    [InlineData((ushort)0x22A)]
    [InlineData((ushort)0x22C)]
    [InlineData((ushort)0x22E)]
    [InlineData((ushort)0x22F)]
    [InlineData((ushort)0x210)]
    [InlineData((ushort)0x21F)]
    [InlineData((ushort)0x230)]
    [InlineData((ushort)0x240)]
    [InlineData((ushort)0x250)]
    [InlineData((ushort)0x260)]
    [InlineData((ushort)0x280)]
    [InlineData((ushort)0x28F)]
    public void CanDecode_ClaimsAllSbPortsForEveryStandardBase(ushort port) {
        _decoder.CanDecode(port).Should().BeTrue();
    }

    [Theory]
    [InlineData((ushort)0x0000)]
    [InlineData((ushort)0x0060)]
    [InlineData((ushort)0x020F)]
    [InlineData((ushort)0x0270)]
    [InlineData((ushort)0x0290)]
    [InlineData((ushort)0x0388)]
    [InlineData((ushort)0x03C0)]
    public void CanDecode_RejectsNonSbPorts(ushort port) {
        _decoder.CanDecode(port).Should().BeFalse();
    }

    [Fact]
    public void DecodeWrite_DspWritePort_DecodesCommandMnemonic() {
        DecodedCall call = _decoder.DecodeWrite(0x22C, 0xE1, 1);

        call.Subsystem.Should().Be("Sound Blaster I/O Ports");
        call.FunctionName.Should().Contain("DSP Write Command/Data");
        call.Parameters.Should().HaveCount(1);
        call.Parameters[0].FormattedValue.Should().Contain("Get DSP Version");
        call.Parameters[0].Source.Should().Be("port 0x22C");
        call.Parameters[0].Kind.Should().Be(DecodedParameterKind.IoPort);
    }

    [Fact]
    public void DecodeWrite_DspWritePort_UnknownCommand_FallsBackToUnknown() {
        DecodedCall call = _decoder.DecodeWrite(0x22C, 0xAB, 1);

        call.Parameters[0].FormattedValue.Should().Contain("Unknown");
    }

    [Fact]
    public void DecodeRead_DspReadDataPort_LabelsAsReadFifo() {
        DecodedCall call = _decoder.DecodeRead(0x22A, 0xAA, 1);

        call.FunctionName.Should().Contain("DSP Read Data");
        call.Parameters[0].FormattedValue.Should().Be("0xAA");
    }

    [Fact]
    public void DecodeRead_DspReadStatusPort_DescribesIrqAck() {
        DecodedCall call = _decoder.DecodeRead(0x22E, 0xFF, 1);

        call.FunctionName.Should().Contain("DSP Read Buffer Status");
        call.FunctionName.Should().Contain("8-bit IRQ Ack");
        call.Parameters[0].Notes.Should().Contain("acks 8-bit IRQ");
    }

    [Fact]
    public void DecodeRead_DspAck16BitPort_DescribesSb16Ack() {
        DecodedCall call = _decoder.DecodeRead(0x22F, 0xFF, 1);

        call.FunctionName.Should().Contain("DSP 16-bit IRQ Ack");
        call.Parameters[0].Notes.Should().Contain("ack 16-bit IRQ");
    }

    [Fact]
    public void DecodeWrite_DspResetPort_AnnotatesAssertedAndDeasserted() {
        DecodedCall asserted = _decoder.DecodeWrite(0x226, 0x01, 1);
        DecodedCall deasserted = _decoder.DecodeWrite(0x226, 0x00, 1);

        asserted.FunctionName.Should().Contain("DSP Reset");
        asserted.Parameters[0].Notes.Should().Contain("reset asserted");
        deasserted.Parameters[0].Notes.Should().Contain("reset deasserted");
    }

    [Fact]
    public void DecodeWrite_MixerIndexPort_DecodesRegisterName() {
        DecodedCall call = _decoder.DecodeWrite(0x224, 0x22, 1);

        call.FunctionName.Should().Contain("Mixer Index Write");
        call.Parameters[0].FormattedValue.Should().Contain("Master Volume (SB Pro)");
    }

    [Fact]
    public void DecodeWrite_MixerDataPort_HasNoIndexLookup() {
        DecodedCall call = _decoder.DecodeWrite(0x225, 0x55, 1);

        call.FunctionName.Should().Contain("Mixer Data Write");
        call.Parameters[0].FormattedValue.Should().Be("0x55");
    }

    [Fact]
    public void DecodeRead_MixerIndexPort_LabelsAsRead() {
        DecodedCall call = _decoder.DecodeRead(0x224, 0x30, 1);

        call.FunctionName.Should().Contain("Mixer Index Read");
        call.Parameters[0].FormattedValue.Should().Be("0x30");
    }

    [Fact]
    public void DecodeWrite_AdlibLeftRightOffsets_DescribeStereoSplit() {
        DecodedCall left = _decoder.DecodeWrite(0x220, 0x20, 1);
        DecodedCall right = _decoder.DecodeWrite(0x222, 0x20, 1);

        left.FunctionName.Should().Contain("AdLib FM left register/index");
        right.FunctionName.Should().Contain("AdLib FM right register/index");
    }

    [Fact]
    public void DecodeWrite_OplWindowOffsets_DescribePassthrough() {
        DecodedCall call = _decoder.DecodeWrite(0x228, 0x20, 1);

        call.FunctionName.Should().Contain("OPL FM at SB Window");
    }

    [Theory]
    [InlineData((ushort)0x227, 0x07)]
    [InlineData((ushort)0x22B, 0x0B)]
    [InlineData((ushort)0x22D, 0x0D)]
    public void DecodeWrite_ReservedOffsets_AreLabelledReserved(ushort port, int expectedOffset) {
        DecodedCall call = _decoder.DecodeWrite(port, 0x00, 1);

        call.FunctionName.Should().Contain("Reserved SB Port");
        call.FunctionName.Should().Contain($"0x{expectedOffset:X2}");
    }

    [Fact]
    public void DecodeWrite_AcrossDifferentBases_LabelsBaseInNotes() {
        DecodedCall call240 = _decoder.DecodeWrite(0x24C, 0xD1, 1);
        DecodedCall call260 = _decoder.DecodeWrite(0x26C, 0xD1, 1);

        call240.Parameters[0].Notes.Should().Contain("base=0x240");
        call260.Parameters[0].Notes.Should().Contain("base=0x260");
        call240.Parameters[0].FormattedValue.Should().Contain("Enable Speaker");
        call260.Parameters[0].FormattedValue.Should().Contain("Enable Speaker");
    }
}
