namespace Spice86.Tests.Debugger.Bios;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.Emulator.CPU;
using Spice86.Core.Emulator.Memory;
using Spice86.DebuggerKnowledgeBase.Bios;
using Spice86.DebuggerKnowledgeBase.Decoding;

using Xunit;

public class BiosInt13DecoderTests {
    private readonly BiosInt13Decoder _decoder = new BiosInt13Decoder();
    private readonly IMemory _memory = Substitute.For<IMemory>();
    private readonly State _state = new State(CpuModel.INTEL_80386);

    [Fact]
    public void Decode_ReadSectors_DecodesChsAndBuffer() {
        _state.AH = 0x02;
        _state.AL = 8;       // 8 sectors
        _state.CH = 10;      // cylinder
        _state.CL = 1;       // sector
        _state.DH = 0;       // head
        _state.DL = 0x80;    // first hard disk
        _state.ES = 0x1000;
        _state.BX = 0x0200;

        DecodedCall call = _decoder.Decode(0x13, _state, _memory);

        call.FunctionName.Should().Contain("Read Sectors");
        call.Parameters.Should().HaveCount(6);
        call.Parameters[0].FormattedValue.Should().Contain("hard disk 0");
        call.Parameters[1].FormattedValue.Should().Be("8");
        call.Parameters[5].FormattedValue.Should().Be("1000:0200");
    }

    [Fact]
    public void Decode_ResetDiskSystem_DecodesFloppyDrive() {
        _state.AH = 0x00;
        _state.DL = 0x00;

        DecodedCall call = _decoder.Decode(0x13, _state, _memory);

        call.Parameters.Should().HaveCount(1);
        call.Parameters[0].FormattedValue.Should().Contain("floppy 0");
    }
}
