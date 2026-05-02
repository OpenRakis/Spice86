namespace Spice86.Tests.Debugger.Opl;

using FluentAssertions;

using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.DebuggerKnowledgeBase.Opl;

using Xunit;

public class OplIoPortDecoderTests {
    private readonly OplIoPortDecoder _decoder = new OplIoPortDecoder();

    [Theory]
    [InlineData((ushort)0x388)]
    [InlineData((ushort)0x389)]
    [InlineData((ushort)0x38A)]
    [InlineData((ushort)0x38B)]
    public void CanDecode_ClaimsAdlibPortRange(ushort port) {
        _decoder.CanDecode(port).Should().BeTrue();
    }

    [Theory]
    [InlineData((ushort)0x387)]
    [InlineData((ushort)0x38C)]
    [InlineData((ushort)0x220)]
    [InlineData((ushort)0x300)]
    [InlineData((ushort)0x000)]
    public void CanDecode_RejectsNonAdlibPorts(ushort port) {
        _decoder.CanDecode(port).Should().BeFalse();
    }

    [Fact]
    public void DecodeRead_388_DecodesAsStatus() {
        DecodedCall call = _decoder.DecodeRead(0x388, 0x06, 1);

        call.Subsystem.Should().Be("OPL FM I/O Ports");
        call.FunctionName.Should().Contain("Status Read");
        call.ShortDescription.Should().Contain("in 0x388");
    }

    [Fact]
    public void DecodeWrite_388_DecodesRegisterByName_TimerControl() {
        DecodedCall call = _decoder.DecodeWrite(0x388, 0x04, 1);

        call.FunctionName.Should().Contain("Register Address Write");
        call.Parameters.Should().HaveCount(1);
        call.Parameters[0].FormattedValue.Should().Contain("Timer Control");
    }

    [Fact]
    public void DecodeWrite_388_DecodesPerOperatorRegister() {
        // 0x20 selects "AM/Vibrato/Sustain/KSR/Multiplier" for operator 0.
        DecodedCall call = _decoder.DecodeWrite(0x388, 0x20, 1);
        call.Parameters[0].FormattedValue.Should().Contain("AM/Vibrato/Sustain/KSR/Multiplier");
        call.Parameters[0].FormattedValue.Should().Contain("operator 0");
    }

    [Fact]
    public void DecodeWrite_388_DecodesPerChannelKeyOnRegister() {
        // 0xB3 selects KeyOn/Block/FreqHigh for channel 3.
        DecodedCall call = _decoder.DecodeWrite(0x388, 0xB3, 1);
        call.Parameters[0].FormattedValue.Should().Contain("Key On / Block / Frequency Number High");
        call.Parameters[0].FormattedValue.Should().Contain("channel 3");
    }

    [Fact]
    public void DecodeWrite_388_DecodesRhythmRegister() {
        DecodedCall call = _decoder.DecodeWrite(0x388, 0xBD, 1);
        call.Parameters[0].FormattedValue.Should().Contain("Tremolo / Vibrato / Percussion Mode");
    }

    [Fact]
    public void DecodeWrite_389_IsDescribedAsData() {
        DecodedCall call = _decoder.DecodeWrite(0x389, 0xAB, 1);

        call.FunctionName.Should().Contain("Data Write");
        call.ShortDescription.Should().Contain("OPL register most recently selected");
    }

    [Fact]
    public void DecodeRead_389_IsDescribedAsRead() {
        DecodedCall call = _decoder.DecodeRead(0x389, 0xFF, 1);

        call.FunctionName.Should().Contain("Data Read");
    }

    [Fact]
    public void DecodeWrite_38A_FF_DecodesAsAdlibGoldActivate() {
        DecodedCall call = _decoder.DecodeWrite(0x38A, 0xFF, 1);

        call.FunctionName.Should().Contain("AdLib Gold Control Activate");
        call.Parameters[0].FormattedValue.Should().Contain("Activate AdLib Gold");
    }

    [Fact]
    public void DecodeWrite_38A_FE_DecodesAsAdlibGoldDeactivate() {
        DecodedCall call = _decoder.DecodeWrite(0x38A, 0xFE, 1);

        call.FunctionName.Should().Contain("AdLib Gold Control Deactivate");
    }

    [Fact]
    public void DecodeWrite_38A_GenericByte_ExposesBothModeInterpretations() {
        // 0x18 is Surround Control in AdLib Gold; in OPL3 mode it's Array-1 0x118 (no documented register name -> "Unknown / Reserved").
        DecodedCall call = _decoder.DecodeWrite(0x38A, 0x18, 1);

        call.FunctionName.Should().Contain("OPL3 Array-1 Address / AdLib Gold Control Index Write");
        call.Parameters.Should().HaveCount(2);

        DecodedParameter goldParam = call.Parameters[1];
        goldParam.Name.Should().Be("gold_control");
        goldParam.FormattedValue.Should().Contain("Surround Control");

        DecodedParameter opl3Param = call.Parameters[0];
        opl3Param.Name.Should().Be("opl3_register");
        opl3Param.FormattedValue.Should().Contain("Array-1");
    }

    [Fact]
    public void DecodeWrite_38A_KnownArray1Register_LooksUpName() {
        // 0x05 in Array-1 = "New / OPL3 Enable".
        DecodedCall call = _decoder.DecodeWrite(0x38A, 0x05, 1);

        DecodedParameter opl3Param = call.Parameters[0];
        opl3Param.FormattedValue.Should().Contain("Array-1 New / OPL3 Enable");

        DecodedParameter goldParam = call.Parameters[1];
        goldParam.FormattedValue.Should().Contain("Stereo Volume Right");
    }

    [Fact]
    public void DecodeWrite_38B_DescribedAsArray1OrGoldData() {
        DecodedCall call = _decoder.DecodeWrite(0x38B, 0x42, 1);

        call.FunctionName.Should().Contain("OPL3 Array-1 Data / AdLib Gold Control Data Write");
        call.ShortDescription.Should().Contain("OPL3 mode");
        call.ShortDescription.Should().Contain("OPL3 Gold mode");
    }

    [Fact]
    public void DecodeRead_38A_DocumentsOpl3Status() {
        DecodedCall call = _decoder.DecodeRead(0x38A, 0x00, 1);

        call.FunctionName.Should().Contain("OPL3 Array-1 Status Read");
    }

    [Fact]
    public void Subsystem_IsConsistent() {
        DecodedCall a = _decoder.DecodeWrite(0x388, 0x00, 1);
        DecodedCall b = _decoder.DecodeRead(0x38B, 0xFF, 1);

        a.Subsystem.Should().Be("OPL FM I/O Ports");
        b.Subsystem.Should().Be("OPL FM I/O Ports");
    }
}
