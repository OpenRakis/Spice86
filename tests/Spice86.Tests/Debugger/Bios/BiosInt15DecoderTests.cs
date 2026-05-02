namespace Spice86.Tests.Debugger.Bios;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Bios;
using Spice86.DebuggerKnowledgeBase.Decoding;

using Xunit;

public class BiosInt15DecoderTests {
    private readonly BiosInt15Decoder _decoder = new BiosInt15Decoder();
    private readonly IMemory _memory = Substitute.For<IMemory>();
    private readonly State _state = new State(CpuModel.INTEL_80386);

    [Fact]
    public void Decode_BiosWait_DecodesCxDxMicroseconds() {
        _state.AH = 0x86;
        _state.CX = 0x000F;
        _state.DX = 0x4240;

        DecodedCall call = _decoder.Decode(0x15, _state, _memory);

        call.FunctionName.Should().Contain("Wait");
        call.Parameters.Should().HaveCount(1);
        call.Parameters[0].Source.Should().Be("CX:DX");
        call.Parameters[0].RawValue.Should().Be(0x000F4240); // 1_000_000 us
        call.Parameters[0].FormattedValue.Should().Contain("us");
    }

    [Fact]
    public void Decode_A20Gate_DecodesAlSubFunction() {
        _state.AH = 0x24;
        _state.AL = 0x01;

        DecodedCall call = _decoder.Decode(0x15, _state, _memory);

        call.FunctionName.Should().Contain("A20 Gate");
        call.Parameters[0].Source.Should().Be("AL");
        call.Parameters[0].RawValue.Should().Be(0x01);
    }

    [Fact]
    public void Decode_GetExtendedMemorySize_HasNoParameters() {
        _state.AH = 0x88;

        DecodedCall call = _decoder.Decode(0x15, _state, _memory);

        call.FunctionName.Should().Contain("Extended Memory Size");
        call.Parameters.Should().BeEmpty();
    }
}
