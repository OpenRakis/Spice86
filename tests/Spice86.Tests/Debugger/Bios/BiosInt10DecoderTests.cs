namespace Spice86.Tests.Debugger.Bios;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Bios;
using Spice86.DebuggerKnowledgeBase.Decoding;

using Xunit;

public class BiosInt10DecoderTests {
    private readonly BiosInt10Decoder _decoder = new BiosInt10Decoder();
    private readonly IMemory _memory = Substitute.For<IMemory>();
    private readonly State _state = new State(CpuModel.INTEL_80386);

    [Fact]
    public void CanDecode_OnlyClaims10() {
        _decoder.CanDecode(0x10).Should().BeTrue();
        _decoder.CanDecode(0x21).Should().BeFalse();
    }

    [Fact]
    public void Decode_SetVideoMode_DecodesKnownMode13h() {
        _state.AH = 0x00;
        _state.AL = 0x13;

        DecodedCall call = _decoder.Decode(0x10, _state, _memory);

        call.Subsystem.Should().Be("BIOS INT 10h");
        call.FunctionName.Should().Contain("Set Video Mode");
        call.Parameters.Should().HaveCount(1);
        call.Parameters[0].FormattedValue.Should().Contain("256-color");
    }

    [Fact]
    public void Decode_SetCursorPosition_DecodesPageRowCol() {
        _state.AH = 0x02;
        _state.BH = 1;
        _state.DH = 5;
        _state.DL = 12;

        DecodedCall call = _decoder.Decode(0x10, _state, _memory);

        call.Parameters.Should().HaveCount(3);
        call.Parameters[0].Source.Should().Be("BH");
        call.Parameters[1].Source.Should().Be("DH");
        call.Parameters[1].FormattedValue.Should().Be("5");
        call.Parameters[2].FormattedValue.Should().Be("12");
    }

    [Fact]
    public void Decode_TeletypeOutput_DecodesAsciiCharacter() {
        _state.AH = 0x0E;
        _state.AL = (byte)'X';
        _state.BH = 0;
        _state.BL = 0x07;

        DecodedCall call = _decoder.Decode(0x10, _state, _memory);

        call.FunctionName.Should().Contain("Teletype Output");
        call.Parameters[0].FormattedValue.Should().Contain("'X'");
        call.Parameters[2].Source.Should().Be("BL");
    }

    [Fact]
    public void Decode_WriteString_DecodesEsBpPointer() {
        _state.AH = 0x13;
        _state.AL = 1;
        _state.BH = 0;
        _state.BL = 0x07;
        _state.CX = 12;
        _state.DH = 10;
        _state.DL = 0;
        _state.ES = 0x9000;
        _state.BP = 0x0100;

        DecodedCall call = _decoder.Decode(0x10, _state, _memory);

        call.Parameters.Should().HaveCount(7);
        call.Parameters[6].Source.Should().Be("ES:BP");
        call.Parameters[6].FormattedValue.Should().Be("9000:0100");
    }

    [Fact]
    public void Decode_UnknownAh_ReturnsGenericFunction() {
        _state.AH = 0xCC;

        DecodedCall call = _decoder.Decode(0x10, _state, _memory);

        call.FunctionName.Should().Contain("CCh");
        call.ShortDescription.Should().Contain("Unknown");
    }

    [Fact]
    public void Decode_Ah0B_DecodesBhFunctionMnemonic() {
        _state.AH = 0x0B;
        _state.BH = 0x01;
        _state.BL = 0x05;

        DecodedCall call = _decoder.Decode(0x10, _state, _memory);

        call.Parameters.Should().HaveCount(2);
        call.Parameters[0].Source.Should().Be("BH");
        call.Parameters[0].FormattedValue.Should().Contain("CGA palette");
        call.Parameters[1].Source.Should().Be("BL");
    }

    [Fact]
    public void Decode_Ah10_Al02_DecodesEsDxPaletteTable() {
        _state.AH = 0x10;
        _state.AL = 0x02;
        _state.ES = 0x1234;
        _state.DX = 0x5678;

        DecodedCall call = _decoder.Decode(0x10, _state, _memory);

        call.Parameters[0].FormattedValue.Should().Contain("Set All Palette Registers");
        call.Parameters[1].Source.Should().Be("ES:DX");
        call.Parameters[1].FormattedValue.Should().Be("1234:5678");
    }

    [Fact]
    public void Decode_Ah10_Al10_DecodesDacRgb() {
        _state.AH = 0x10;
        _state.AL = 0x10;
        _state.BX = 0x0007;
        _state.DH = 0x3F;
        _state.CH = 0x20;
        _state.CL = 0x10;

        DecodedCall call = _decoder.Decode(0x10, _state, _memory);

        call.Parameters[0].FormattedValue.Should().Contain("Set Individual DAC");
        call.Parameters[1].Source.Should().Be("BX");
        call.Parameters[2].Source.Should().Be("DH");
        call.Parameters[3].Source.Should().Be("CH");
        call.Parameters[4].Source.Should().Be("CL");
    }

    [Fact]
    public void Decode_Ah11_Al10_DecodesFontLoadActivate() {
        _state.AH = 0x11;
        _state.AL = 0x10;
        _state.BH = 16;
        _state.BL = 0;
        _state.CX = 256;
        _state.DX = 0;
        _state.ES = 0xC000;
        _state.BP = 0x1234;

        DecodedCall call = _decoder.Decode(0x10, _state, _memory);

        call.Parameters[0].FormattedValue.Should().Contain("Load and Activate User Font");
        call.Parameters.Should().HaveCount(6);
        call.Parameters[5].Source.Should().Be("ES:BP");
    }

    [Fact]
    public void Decode_Ah11_Al30_DecodesFontInfoBhMnemonic() {
        _state.AH = 0x11;
        _state.AL = 0x30;
        _state.BH = 0x06;

        DecodedCall call = _decoder.Decode(0x10, _state, _memory);

        call.Parameters[0].FormattedValue.Should().Contain("Get Font Information");
        call.Parameters[1].Source.Should().Be("BH");
        call.Parameters[1].FormattedValue.Should().Contain("8x16");
    }

    [Fact]
    public void Decode_Ah12_Bl30_DecodesVerticalResolution() {
        _state.AH = 0x12;
        _state.BL = 0x30;

        DecodedCall call = _decoder.Decode(0x10, _state, _memory);

        call.Parameters.Should().HaveCount(1);
        call.Parameters[0].Source.Should().Be("BL");
        call.Parameters[0].FormattedValue.Should().Contain("Vertical Resolution");
    }

    [Fact]
    public void Decode_Ah1C_Al01_DecodesSaveStateWithBuffer() {
        _state.AH = 0x1C;
        _state.AL = 0x01;
        _state.CX = 0x0007;
        _state.ES = 0x9000;
        _state.BX = 0x0100;

        DecodedCall call = _decoder.Decode(0x10, _state, _memory);

        call.FunctionName.Should().Contain("Video Save/Restore Area");
        call.Parameters.Should().HaveCount(3);
        call.Parameters[0].FormattedValue.Should().Contain("Save Video State");
        call.Parameters[2].Source.Should().Be("ES:BX");
        call.Parameters[2].FormattedValue.Should().Be("9000:0100");
    }

    [Fact]
    public void Decode_Ah4F_Al01_DecodesGetModeInfo() {
        _state.AH = 0x4F;
        _state.AL = 0x01;
        _state.CX = 0x0103;
        _state.ES = 0x2000;
        _state.DI = 0x0000;

        DecodedCall call = _decoder.Decode(0x10, _state, _memory);

        call.FunctionName.Should().Contain("VESA");
        call.Parameters[0].FormattedValue.Should().Contain("Get SVGA Mode Information");
        call.Parameters[1].Source.Should().Be("CX");
        call.Parameters[2].Source.Should().Be("ES:DI");
    }

    [Fact]
    public void Decode_AhF1_DecodesEgaRilWriteRegister() {
        _state.AH = 0xF1;
        _state.BL = 0x10;
        _state.BH = 0x55;
        _state.DX = 0x03C0;

        DecodedCall call = _decoder.Decode(0x10, _state, _memory);

        call.FunctionName.Should().Contain("Write One Register");
        call.Parameters.Should().HaveCount(3);
        call.Parameters[0].Source.Should().Be("BL");
        call.Parameters[1].Source.Should().Be("BH");
        call.Parameters[2].Source.Should().Be("DX");
    }

    [Fact]
    public void Decode_AhFF_NoParams() {
        _state.AH = 0xFF;

        DecodedCall call = _decoder.Decode(0x10, _state, _memory);

        call.FunctionName.Should().Contain("Update Whole Screen");
        call.Parameters.Should().BeEmpty();
    }
}
