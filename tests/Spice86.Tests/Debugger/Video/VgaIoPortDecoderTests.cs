namespace Spice86.Tests.Debugger.Video;

using FluentAssertions;

using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.DebuggerKnowledgeBase.Video;

using Xunit;

public class VgaIoPortDecoderTests {
    private readonly VgaIoPortDecoder _decoder = new VgaIoPortDecoder();

    [Theory]
    [InlineData((ushort)0x3B4)]
    [InlineData((ushort)0x3B5)]
    [InlineData((ushort)0x3BA)]
    [InlineData((ushort)0x3C0)]
    [InlineData((ushort)0x3C2)]
    [InlineData((ushort)0x3C4)]
    [InlineData((ushort)0x3C5)]
    [InlineData((ushort)0x3C7)]
    [InlineData((ushort)0x3C8)]
    [InlineData((ushort)0x3C9)]
    [InlineData((ushort)0x3CE)]
    [InlineData((ushort)0x3CF)]
    [InlineData((ushort)0x3D4)]
    [InlineData((ushort)0x3D5)]
    [InlineData((ushort)0x3D8)]
    [InlineData((ushort)0x3D9)]
    [InlineData((ushort)0x3DA)]
    public void CanDecode_ClaimsKnownVgaPorts(ushort port) {
        _decoder.CanDecode(port).Should().BeTrue();
    }

    [Theory]
    [InlineData((ushort)0x0000)]
    [InlineData((ushort)0x0388)]
    [InlineData((ushort)0x03B0)]
    [InlineData((ushort)0x03BB)]
    [InlineData((ushort)0x03CB)]
    [InlineData((ushort)0x03CD)]
    [InlineData((ushort)0x03DB)]
    public void CanDecode_RejectsNonVgaPorts(ushort port) {
        _decoder.CanDecode(port).Should().BeFalse();
    }

    [Fact]
    public void DecodeWrite_CrtcAddress_ColorGroup_DecodesIndexName() {
        DecodedCall call = _decoder.DecodeWrite(0x3D4, 0x0E, 1);

        call.Subsystem.Should().Be("VGA I/O Ports");
        call.FunctionName.Should().Contain("CRTC Address Write").And.Contain("color");
        call.Parameters.Should().HaveCount(1);
        call.Parameters[0].FormattedValue.Should().Contain("Cursor Location High");
        call.Parameters[0].Source.Should().Be("port 0x3D4");
        call.Parameters[0].Kind.Should().Be(DecodedParameterKind.IoPort);
    }

    [Fact]
    public void DecodeWrite_CrtcAddress_MonoGroup_DecodesIndexName() {
        DecodedCall call = _decoder.DecodeWrite(0x3B4, 0x0F, 1);

        call.FunctionName.Should().Contain("monochrome");
        call.Parameters[0].FormattedValue.Should().Contain("Cursor Location Low");
    }

    [Fact]
    public void DecodeWrite_SequencerAddress_DecodesMapMask() {
        DecodedCall call = _decoder.DecodeWrite(0x3C4, 0x02, 1);

        call.FunctionName.Should().Contain("Sequencer Address Write");
        call.Parameters[0].FormattedValue.Should().Contain("Map Mask");
    }

    [Fact]
    public void DecodeWrite_SequencerData_NoIndexLookup() {
        DecodedCall call = _decoder.DecodeWrite(0x3C5, 0x0F, 1);

        call.FunctionName.Should().Contain("Sequencer Data Write");
        call.Parameters[0].FormattedValue.Should().Be("0x0F");
    }

    [Fact]
    public void DecodeWrite_GraphicsControllerAddress_DecodesBitMask() {
        DecodedCall call = _decoder.DecodeWrite(0x3CE, 0x08, 1);

        call.FunctionName.Should().Contain("Graphics Controller Address Write");
        call.Parameters[0].FormattedValue.Should().Contain("Bit Mask");
    }

    [Fact]
    public void DecodeWrite_GraphicsControllerData_NoIndexLookup() {
        DecodedCall call = _decoder.DecodeWrite(0x3CF, 0xAA, 1);

        call.FunctionName.Should().Contain("Graphics Controller Data Write");
        call.Parameters[0].FormattedValue.Should().Be("0xAA");
    }

    [Fact]
    public void DecodeWrite_AttributeAddressOrData_DecodesIndexAndPasBit() {
        DecodedCall call = _decoder.DecodeWrite(0x3C0, 0x31, 1);

        call.FunctionName.Should().Contain("Attribute Controller Address-or-Data Write");
        call.Parameters[0].FormattedValue.Should().Contain("Overscan");
        call.Parameters[0].Notes.Should().Contain("PAS=1");
    }

    [Fact]
    public void DecodeWrite_AttributeAddressOrData_PasZero_BlanksScreen() {
        DecodedCall call = _decoder.DecodeWrite(0x3C0, 0x10, 1);

        call.Parameters[0].Notes.Should().Contain("PAS=0");
        call.Parameters[0].FormattedValue.Should().Contain("Mode Control");
    }

    [Fact]
    public void DecodeRead_InputStatus1_Color_ResetsAttributeFlipFlop() {
        DecodedCall call = _decoder.DecodeRead(0x3DA, 0x09, 1);

        call.FunctionName.Should().Contain("Input Status #1 Read").And.Contain("color");
        call.ShortDescription.Should().Contain("resets the Attribute Controller flip-flop");
    }

    [Fact]
    public void DecodeRead_InputStatus1_Mono_ResetsAttributeFlipFlop() {
        DecodedCall call = _decoder.DecodeRead(0x3BA, 0x09, 1);

        call.FunctionName.Should().Contain("Input Status #1 Read").And.Contain("mono");
    }

    [Fact]
    public void DecodeWrite_MiscOutput_DecodesClockAndIoMode() {
        // raw=0x67: clock=01 (28 MHz), I/O=color, hsync neg, vsync pos
        DecodedCall call = _decoder.DecodeWrite(0x3C2, 0x67, 1);

        call.FunctionName.Should().Contain("Misc Output Write");
        call.Parameters[0].Notes.Should().Contain("28 MHz").And.Contain("color");
    }

    [Fact]
    public void DecodeWrite_DacWriteIndex_AndData_DescribeTriplet() {
        DecodedCall index = _decoder.DecodeWrite(0x3C8, 0x10, 1);
        DecodedCall data = _decoder.DecodeWrite(0x3C9, 0x3F, 1);

        index.FunctionName.Should().Contain("DAC Write Index");
        index.Parameters[0].FormattedValue.Should().Be("0x10");

        data.FunctionName.Should().Contain("DAC Data Write");
        data.ShortDescription.Should().Contain("R/G/B");
        data.Parameters[0].FormattedValue.Should().Be("0x3F");
    }

    [Fact]
    public void DecodeWrite_DacReadIndex_OnPort3C7() {
        DecodedCall call = _decoder.DecodeWrite(0x3C7, 0x20, 1);
        call.FunctionName.Should().Contain("DAC Read Index Write");
    }

    [Fact]
    public void DecodeRead_DacState_OnPort3C7() {
        DecodedCall call = _decoder.DecodeRead(0x3C7, 0x03, 1);
        call.FunctionName.Should().Contain("DAC State Read");
    }

    [Fact]
    public void DecodeWrite_CgaModeControl_AndColorSelect() {
        DecodedCall mode = _decoder.DecodeWrite(0x3D8, 0x29, 1);
        DecodedCall color = _decoder.DecodeWrite(0x3D9, 0x20, 1);

        mode.FunctionName.Should().Contain("CGA Mode Control");
        color.FunctionName.Should().Contain("CGA Color Select");
    }

    [Fact]
    public void DecodeWrite_DacPelMask_OnPort3C6() {
        DecodedCall call = _decoder.DecodeWrite(0x3C6, 0xFF, 1);
        call.FunctionName.Should().Contain("DAC Pixel Mask");
    }
}
