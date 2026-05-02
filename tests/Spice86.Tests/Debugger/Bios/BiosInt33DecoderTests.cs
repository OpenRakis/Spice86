namespace Spice86.Tests.Debugger.Bios;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Bios;
using Spice86.DebuggerKnowledgeBase.Decoding;

using Xunit;

public class BiosInt33DecoderTests {
    private readonly BiosInt33Decoder _decoder = new BiosInt33Decoder();
    private readonly IMemory _memory = Substitute.For<IMemory>();
    private readonly State _state = new State(CpuModel.INTEL_80386);

    [Fact]
    public void Decode_InstallationCheck_HasNoParameters() {
        _state.AX = 0x0000;

        DecodedCall call = _decoder.Decode(0x33, _state, _memory);

        call.Subsystem.Should().Be("Mouse INT 33h");
        call.FunctionName.Should().Contain("Mouse Installation Check");
        call.Parameters.Should().BeEmpty();
    }

    [Fact]
    public void Decode_SetCursorPosition_DecodesCxAndDx() {
        _state.AX = 0x0004;
        _state.CX = 320;
        _state.DX = 200;

        DecodedCall call = _decoder.Decode(0x33, _state, _memory);

        call.Parameters.Should().HaveCount(2);
        call.Parameters[0].FormattedValue.Should().Be("320");
        call.Parameters[1].FormattedValue.Should().Be("200");
    }

    [Fact]
    public void Decode_SetUserCallback_DecodesEventMaskAndEsDx() {
        _state.AX = 0x000C;
        _state.CX = 0x001F;
        _state.ES = 0x2000;
        _state.DX = 0x0123;

        DecodedCall call = _decoder.Decode(0x33, _state, _memory);

        call.Parameters.Should().HaveCount(2);
        call.Parameters[1].Source.Should().Be("ES:DX");
        call.Parameters[1].FormattedValue.Should().Be("2000:0123");
    }

    [Fact]
    public void Decode_UnknownFunction_ReportsAxAsUnknown() {
        _state.AX = 0x00FE;

        DecodedCall call = _decoder.Decode(0x33, _state, _memory);

        call.FunctionName.Should().Contain("00FEh");
        call.ShortDescription.Should().Contain("Unknown");
    }
}
