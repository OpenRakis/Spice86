namespace Spice86.Tests.Debugger.Dos;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Decoding;
using Spice86.DebuggerKnowledgeBase.Dos;

using Xunit;

public class DosInt2FDecoderTests {
    private readonly DosInt2FDecoder _decoder = new DosInt2FDecoder();
    private readonly IMemory _memory = Substitute.For<IMemory>();
    private readonly State _state = new State(CpuModel.INTEL_80386);

    [Fact]
    public void CanDecode_OnlyClaims2F() {
        _decoder.CanDecode(0x2F).Should().BeTrue();
        _decoder.CanDecode(0x21).Should().BeFalse();
    }

    [Fact]
    public void Decode_XmsInstallationCheck_NamesXmsAndSubFunction() {
        _state.AH = 0x43;
        _state.AL = 0x00;

        DecodedCall call = _decoder.Decode(0x2F, _state, _memory);

        call.Subsystem.Should().Be("DOS INT 2Fh");
        call.FunctionName.Should().Contain("XMS");
        call.ShortDescription.Should().Contain("Installation Check");
        call.Parameters.Should().HaveCount(2);
        call.Parameters[0].FormattedValue.Should().Contain("XMS");
    }

    [Fact]
    public void Decode_XmsGetEntryPoint_DescribesGetDriverEntryPoint() {
        _state.AH = 0x43;
        _state.AL = 0x10;

        DecodedCall call = _decoder.Decode(0x2F, _state, _memory);

        call.ShortDescription.Should().Contain("Driver Entry Point");
    }

    [Fact]
    public void Decode_HmaQueryFreeSpace_HasDescription() {
        _state.AH = 0x4A;
        _state.AL = 0x01;

        DecodedCall call = _decoder.Decode(0x2F, _state, _memory);

        call.ShortDescription.Should().Contain("Free HMA");
    }

    [Fact]
    public void Decode_UnknownMultiplex_ReportsAh() {
        _state.AH = 0x77;
        _state.AL = 0x00;

        DecodedCall call = _decoder.Decode(0x2F, _state, _memory);

        call.FunctionName.Should().Contain("77h");
    }
}
