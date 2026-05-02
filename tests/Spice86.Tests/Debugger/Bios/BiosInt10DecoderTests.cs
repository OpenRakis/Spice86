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
}
