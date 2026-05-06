namespace Spice86.Tests.Debugger.Bios;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Bios;
using Spice86.DebuggerKnowledgeBase.Decoding;

using Xunit;

public class BiosInt1ADecoderTests {
    private readonly BiosInt1ADecoder _decoder = new BiosInt1ADecoder();
    private readonly IMemory _memory = Substitute.For<IMemory>();
    private readonly State _state = new State(CpuModel.INTEL_80386);

    [Fact]
    public void Decode_GetTickCounter_HasNoParameters() {
        _state.AH = 0x00;

        DecodedCall call = _decoder.Decode(0x1A, _state, _memory);

        call.FunctionName.Should().Contain("Get System Tick Counter");
        call.Parameters.Should().BeEmpty();
    }

    [Fact]
    public void Decode_SetTickCounter_DecodesCxDxAsTicks() {
        _state.AH = 0x01;
        _state.CX = 0x0001;
        _state.DX = 0x2345;

        DecodedCall call = _decoder.Decode(0x1A, _state, _memory);

        call.Parameters.Should().HaveCount(1);
        call.Parameters[0].Source.Should().Be("CX:DX");
        call.Parameters[0].RawValue.Should().Be(0x00012345);
    }

    [Fact]
    public void Decode_SetRtcTime_DecodesBcdHoursMinutesSeconds() {
        _state.AH = 0x03;
        _state.CH = 0x14; // 14 (BCD = 14h)
        _state.CL = 0x30;
        _state.DH = 0x00;
        _state.DL = 0x00;

        DecodedCall call = _decoder.Decode(0x1A, _state, _memory);

        call.Parameters.Should().HaveCount(4);
        call.Parameters[0].Name.Should().Contain("hours");
        call.Parameters[0].FormattedValue.Should().Be("0x14");
    }
}
